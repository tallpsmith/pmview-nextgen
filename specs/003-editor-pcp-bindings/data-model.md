# Data Model: Editor-Integrated PCP Bindings

**Feature Branch**: `003-editor-pcp-bindings` | **Date**: 2026-03-11

## Entities

### PcpBindingResource (Godot Resource — new)

A single metric-to-property mapping stored as a Godot Resource on a scene node. Mirrors the existing `PcpGodotBridge.MetricBinding` record but as a serializable Godot type.

**Location**: `godot-project/addons/pmview-bridge/PcpBindingResource.cs`

| Field | Type | Default | Validation | Notes |
|-------|------|---------|-----------|-------|
| MetricName | string | "" | Required, non-empty | PCP metric FQDN (e.g., "kernel.all.load") |
| TargetProperty | string | "" | Required, must exist in PropertyVocabulary or as @export on node | Built-in name or custom property path |
| SourceRangeMin | float | 0.0f | Must be < SourceRangeMax | Expected metric value lower bound |
| SourceRangeMax | float | 1.0f | Must be > SourceRangeMin | Expected metric value upper bound |
| TargetRangeMin | float | 0.0f | Must be < TargetRangeMax | Mapped visual lower bound |
| TargetRangeMax | float | 1.0f | Must be > TargetRangeMin | Mapped visual upper bound |
| InstanceName | string | "" | Mutually exclusive with InstanceId | Human-readable instance (e.g., "1 minute") |
| InstanceId | int | -1 | Mutually exclusive with InstanceName; -1 = not set | Numeric instance identifier |
| InitialValue | float | 0.0f | None (intentionally unclamped) | Applied to target before metric data arrives |

**Serialization**: Inline `[sub_resource]` in `.tscn` file:
```
[sub_resource type="PcpBindingResource" id="PcpBindingResource_abc12"]
MetricName = "kernel.all.load"
TargetProperty = "height"
SourceRangeMin = 0.6
SourceRangeMax = 0.7
TargetRangeMin = 0.2
TargetRangeMax = 5.0
InstanceName = "1 minute"
InstanceId = -1
InitialValue = 0.0
```

**Relationships**:
- Owned by: Node via `PcpBindable.PcpBindings` array
- Converts to: `PcpGodotBridge.MetricBinding` for validation and runtime use

---

### PcpBindable (Godot Script Component — new)

A `[Tool]` C# script attached to any Node3D to enable PCP metric binding.

**Location**: `godot-project/addons/pmview-bridge/PcpBindable.cs`

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| PcpBindings | Array\<PcpBindingResource\> | empty | Zero or more binding definitions |

**Constraints**:
- No two bindings in the array may target the same `TargetProperty` (validation error)
- Node must be a Node3D (or subtype)

**Inspector appearance**: The `[Export]` array shows as an expandable list in the inspector with "Add Element" / remove controls. Each element expands to show all PcpBindingResource fields.

---

### MetricCatalogEntry (Editor-only, transient — new)

Represents a node in the PCP metric namespace tree, used by the metric browser dialog. Not persisted.

| Field | Type | Notes |
|-------|------|-------|
| Name | string | Metric or namespace segment name |
| FullPath | string | Complete dotted path (e.g., "kernel.all.load") |
| IsLeaf | bool | True if this is a concrete metric, false if namespace |
| Description | string | Help text from PCP (empty for namespaces) |
| HasInstances | bool | Whether metric has an instance domain |
| Instances | List\<InstanceInfo\> | Populated on demand when selected |

---

### BindingValidationResult (Editor-only, transient — new)

Result of validating a single PcpBindingResource.

| Field | Type | Notes |
|-------|------|-------|
| Binding | PcpBindingResource | The binding that was validated |
| Tier | ValidationTier | Offline or Connected |
| Severity | ValidationSeverity | Error, Warning, or Valid |
| Message | string | Human-readable explanation |

**Enums**:
- `ValidationTier`: Offline, Connected
- `ValidationSeverity`: Error, Warning, Valid

---

## State Transitions

### Binding Lifecycle

```
[Empty Node]
  → Attach PcpBindable script
    → [Bindable, No Bindings]
      → Add PcpBindingResource to array
        → [Binding Draft] (incomplete fields)
          → Fill required fields
            → [Binding Complete] (offline validation passes)
              → Connected validation passes
                → [Binding Validated]

Scene Save → All bindings persisted in .tscn
Scene Load → All bindings restored from .tscn
Runtime    → SceneBinder reads PcpBindings, creates ActiveBindings
```

### Editor Metric Browser

```
[Closed]
  → User clicks "Browse Metrics" on a binding
    → [Connecting] (create PcpClientConnection to configured endpoint)
      → Success → [Connected, Root View] (show top-level namespaces)
        → Navigate into namespace → [Browsing] (show children)
        → Select leaf metric → [Metric Selected] (show description + instances)
          → Select instance (if applicable) → [Selection Complete]
            → Confirm → populate MetricName + InstanceName on binding → [Closed]
      → Failure → [Connection Error] (display message, allow retry/close)
```

## Relationship to Existing Models

### PcpGodotBridge.MetricBinding (existing, pure .NET)

The existing `MetricBinding` record in the Godot-free library:
```csharp
public record MetricBinding(
    string SceneNode, string Metric, string Property,
    double SourceRangeMin, double SourceRangeMax,
    double TargetRangeMin, double TargetRangeMax,
    int? InstanceId, string? InstanceName
);
```

**Conversion**: `PcpBindingResource.ToMetricBinding(string nodeName)` creates a `MetricBinding` for validation. The `SceneNode` field comes from the owning node's name (not stored on the binding itself, since the binding travels with its node).

### SceneBinder.ActiveBinding (existing, Godot-dependent)

The runtime representation with cached node references. Currently created from TOML-parsed `MetricBinding` records. Will be created from `PcpBindingResource` arrays instead.

**Change**: `SceneBinder` gains a `BindFromSceneProperties(Node sceneRoot)` method that:
1. Walks the scene tree finding nodes with `PcpBindable` scripts
2. Reads their `PcpBindings` arrays
3. Converts to `ActiveBinding` records (same as current TOML path)
4. Applies `InitialValue` to target properties immediately
