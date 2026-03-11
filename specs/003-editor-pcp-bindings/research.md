# Research: Editor-Integrated PCP Bindings

**Feature Branch**: `003-editor-pcp-bindings` | **Date**: 2026-03-11

## Decision 1: Binding Data Storage Mechanism

**Decision**: Custom Resources (`Resource` subclass with `[Export]` array on nodes)

**Rationale**:
- Idiomatic Godot 4 approach for structured data that persists in `.tscn` files
- Full type safety — each field is strongly typed C#
- Free inspector editing out of the box (expandable arrays, proper editors per field)
- Automatic serialization as inline `[sub_resource]` blocks in `.tscn`
- Clean alignment with existing `PcpGodotBridge.MetricBinding` model
- Resources can optionally be saved as standalone `.tres` files and shared

**Alternatives considered**:

| Approach | Rejected Because |
|----------|-----------------|
| Node metadata (`SetMeta`) | No type safety (Variant blobs), terrible inspector UX for structured data, known persistence bugs in inherited scenes (godotengine/godot#97596) |
| `_GetPropertyList()` override | Enormous boilerplate, known C# persistence bugs on rebuild (forum reports), reimplements what `[Export] Array<Resource>` gives for free |
| Keep TOML sidecar files | Defeats the feature's entire purpose — moving config into the editor |

**Risks**:
- Known bugs with C# typed arrays in Godot inspector (godotengine/godot#75066) — assignment failures, null values. Mitigated by testing early and reporting upstream if hit.
- Array reordering in inspector is clunky (no drag-to-reorder). Acceptable for v1; can enhance later with custom EditorProperty.

## Decision 2: Inspector Enhancement Strategy

**Decision**: Two-phase approach:
1. **Phase 1 (v1)**: Custom Resources provide baseline inspector editing for free
2. **Phase 2 (v1)**: EditorInspectorPlugin adds "Browse Metrics" button, validation display, and enhanced UX

**Rationale**:
- Custom Resources alone give a fully functional editing experience with zero custom UI code
- EditorInspectorPlugin layers on top without replacing the storage mechanism
- Separation means the data model works even if the inspector plugin has issues

**Implementation pattern**:
- `PcpBindingInspectorPlugin : EditorInspectorPlugin` registered from existing `PmviewBridgePlugin`
- `_CanHandle()` checks for nodes with a `PcpBindings` array property
- `_ParseProperty()` intercepts the `PcpBindings` property to add validation indicators
- `_ParseEnd()` adds "Browse Metrics" button that opens a `Window` dialog

**Alternatives considered**:

| Approach | Rejected Because |
|----------|-----------------|
| Full custom EditorProperty replacing array editor | Enormous effort for v1; built-in array editor is serviceable |
| No inspector plugin at all | Lose metric browsing and validation display — core spec requirements |

## Decision 3: Binding Resource Architecture

**Decision**: `PcpBindingResource` lives in the Godot addon (`addons/pmview-bridge/`), NOT in the pure .NET `PcpGodotBridge` library.

**Rationale**:
- `Resource` subclass requires Godot dependency (`GodotSharp`)
- The existing `PcpGodotBridge` library is deliberately Godot-free (pure .NET, xUnit testable)
- The addon already contains the bridge nodes (`MetricPoller`, `SceneBinder`) that depend on Godot
- A conversion method maps between `PcpBindingResource` (Godot) and `MetricBinding` (pure .NET) for validation

**Data flow**:
```
PcpBindingResource (Godot Resource, inspector editing)
  → ToMetricBinding() conversion
    → MetricBinding (pure .NET, validation via PcpGodotBridge)
      → SceneBinder.ApplyMetrics() (runtime binding application)
```

## Decision 4: SceneBinder Adaptation

**Decision**: SceneBinder reads bindings from node properties at runtime, replacing TOML file loading.

**Rationale**:
- Existing `LoadSceneWithBindings(configPath)` loads TOML → parses → validates → creates ActiveBindings
- New flow: scene loads normally, SceneBinder walks the scene tree finding nodes with `PcpBindings`, creates ActiveBindings directly
- Validation logic in `PcpGodotBridge` is reused via the conversion layer
- The TOML loading path (`BindingConfigLoader`) is preserved in the library but no longer called at runtime

**Migration path**:
1. Add new `BindFromSceneProperties()` method to SceneBinder
2. Update `metric_scene_controller.gd` to call new method instead of `LoadSceneWithBindings(configPath)`
3. Migrate demo scene `.tscn` files to embed binding data
4. Remove TOML loading from runtime path (keep library code for potential future use)

## Decision 5: Metric Browser in Editor

**Decision**: Reuse existing `MetricBrowser` bridge node's PcpClient integration, wrapped in an editor `Window` dialog.

**Rationale**:
- `MetricBrowser.cs` already wraps all PcpClient discovery methods (BrowseChildren, DescribeMetric, LoadInstanceDomain)
- Editor plugin creates a temporary `PcpClientConnection` to the configured endpoint for browsing
- Tree-based UI in a popup Window, navigating the metric namespace hierarchy
- Selection populates the binding's `MetricName` field

**Key consideration**: The editor `PcpClientConnection` is separate from the runtime `MetricPoller`'s connection. This is intentional — editor browsing works independently of runtime polling, and the editor connection is short-lived.

## Decision 6: Validation Architecture

**Decision**: Two-tier validation matching existing `PcpGodotBridge` pattern.

**Tier 1 — Offline (always active)**:
- Reuse `BindingConfigLoader` validation logic via `MetricBinding` conversion
- Invalid ranges (min >= max for source/target)
- Missing required fields (metric name, target property)
- Duplicate property targets on same node
- Property existence check against `PropertyVocabulary` + node's `@export` vars

**Tier 2 — Connected (when pmproxy reachable)**:
- Metric existence check via `PcpClientConnection.DescribeMetricsAsync()`
- Instance availability for instanced metrics
- Missing instance selection when metric has instances
- Uses editor's `PcpClientConnection` (same as metric browser)

**Display**: Validation messages shown as colored labels (red=error, yellow=warning, green=valid) in the inspector via the `EditorInspectorPlugin`.

## Decision 7: Node Script Strategy

**Decision**: A reusable `PcpBindable` C# script that can be attached to any Node3D to give it binding capabilities.

**Rationale**:
- Nodes need a C# script to have the `[Export] Array<PcpBindingResource>` property
- Rather than requiring scene authors to write custom scripts, provide a ready-made `PcpBindable` component
- `[Tool]` attribute ensures the exported properties appear in the editor
- Scene authors attach `PcpBindable.cs` to any Node3D → bindings section appears in inspector

**Alternative**: Require every bound node to have a custom script inheriting from a base class. Rejected because it's invasive — scene authors shouldn't need to change their node scripts just to add metric bindings.
