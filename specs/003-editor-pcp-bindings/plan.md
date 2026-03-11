# Implementation Plan: Editor-Integrated PCP Bindings

**Branch**: `003-editor-pcp-bindings` | **Date**: 2026-03-11 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-editor-pcp-bindings/spec.md`

## Summary

Move PCP metric binding configuration from external TOML files into the Godot editor inspector, so scene authors can add, edit, and validate bindings directly on nodes. Uses Godot Custom Resources (`PcpBindingResource`) for type-safe storage and serialization, with an `EditorInspectorPlugin` layered on top for metric browsing and validation display. The existing SceneBinder runtime is adapted to read bindings from scene node properties instead of TOML files.

## Technical Context

**Language/Version**: C# (.NET 8.0 LTS) for bridge nodes + resources; GDScript for scene controller
**Primary Dependencies**: Godot 4.4+ (Godot.NET.Sdk), existing PcpClient + PcpGodotBridge libraries
**Storage**: Godot Custom Resources serialized inline in `.tscn` scene files
**Testing**: xUnit for pure .NET validation logic; manual Godot editor testing for inspector UI
**Target Platform**: Linux primary (runtime), macOS dev; editor UI is cross-platform via Godot
**Project Type**: Godot editor addon (plugin) + runtime bridge nodes
**Performance Goals**: Metric discovery < 3s for 1,000 metrics; validation feedback < 1s (SC-002, SC-004)
**Constraints**: Editor must be useful offline (without pmproxy); no Godot dependencies in PcpGodotBridge library
**Scale/Scope**: 2 demo scenes to migrate; ~8 new/modified C# files; ~2 new/modified GDScript files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Prototype-First | **PASS** | This feature builds on proven spike work (002-editor-launch-config). The binding model, PropertyVocabulary, and SceneBinder are already production code with tests. The new inspector UI is the only unproven element — mitigated by using Godot's built-in Resource array editor as the baseline (zero custom UI risk). |
| II. TDD | **PASS** | Validation logic lives in pure .NET (PcpGodotBridge) — fully xUnit testable. PcpBindingResource conversion is testable. Editor UI is Godot-dependent (manual testing). |
| III. Code Quality | **PASS** | PcpBindingResource is a simple data class. PcpBindable is a thin script. Conversion methods are single-purpose. |
| IV. UX Consistency | **PASS** | Reuses existing PropertyVocabulary. Inspector controls follow Godot conventions. |
| V. Performance | **PASS** | No runtime overhead vs TOML — binding data is read once at scene load. Editor metric browsing uses existing PcpClient async methods. |

### Post-Design Gate

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Prototype-First | **PASS** | No new unproven technology. Custom Resources and EditorInspectorPlugin are well-documented Godot patterns. |
| II. TDD | **PASS** | New validation rules test-driven in PcpGodotBridge. Conversion methods test-driven. SceneBinder adaptation test-driven (mock binding data). |
| III. Code Quality | **PASS** | Clean separation: Resource (data) → Conversion (mapping) → Validation (rules) → Inspector Plugin (UI). Each layer has one job. |
| IV. UX Consistency | **PASS** | Same property vocabulary, same value mapping math. Inspector follows Godot conventions. |
| V. Performance | **PASS** | Scene tree walk is O(nodes) at load time only. Metric browser uses async PcpClient. No runtime regression. |

## Project Structure

### Documentation (this feature)

```text
specs/003-editor-pcp-bindings/
├── plan.md              # This file
├── research.md          # Phase 0: storage mechanism research + decisions
├── data-model.md        # Phase 1: entity definitions + relationships
├── quickstart.md        # Phase 1: usage guide
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
# Pure .NET libraries (no Godot dependency, xUnit tested)
src/pcp-godot-bridge/
├── src/PcpGodotBridge/
│   ├── MetricBinding.cs              # existing — add InitialValue field
│   ├── BindingConfigLoader.cs        # existing — validation logic reused
│   ├── BindingValidator.cs           # NEW — extracted validation rules (offline tier)
│   └── PropertyVocabulary.cs         # existing — reused as-is
└── tests/PcpGodotBridge.Tests/
    ├── BindingConfigLoaderTests.cs    # existing
    └── BindingValidatorTests.cs      # NEW — offline validation tests

# Godot addon (bridge nodes + editor plugin)
godot-project/addons/pmview-bridge/
├── PcpBindingResource.cs             # NEW — Godot Resource for binding data
├── PcpBindable.cs                    # NEW — [Tool] script exposing PcpBindings array
├── PcpBindingInspectorPlugin.cs      # NEW — EditorInspectorPlugin for validation + browse
├── MetricBrowserDialog.cs            # NEW — Window dialog for metric discovery
├── SceneBinder.cs                    # MODIFIED — add BindFromSceneProperties()
├── MetricPoller.cs                   # existing — no changes
├── MetricBrowser.cs                  # existing — reused by MetricBrowserDialog
└── PmviewBridgePlugin.cs            # MODIFIED — register inspector plugin

# Scenes + controller
godot-project/
├── scenes/test_bars.tscn             # MODIFIED — embed bindings on nodes
├── scenes/disk_io_panel.tscn         # MODIFIED — embed bindings on nodes
├── bindings/                          # REMOVED — TOML files obsoleted after migration
└── scripts/scenes/
    └── metric_scene_controller.gd    # MODIFIED — use BindFromSceneProperties()
```

**Structure Decision**: Extends the existing four-layer architecture. No new projects or libraries — new code lives in the Godot addon layer where Godot dependencies are already present. Validation logic extracted into `BindingValidator` in the pure .NET layer for maximum test coverage.

## Complexity Tracking

| Aspect | Decision | Why Not Simpler |
|--------|----------|-----------------|
| Separate PcpBindingResource + MetricBinding | Two representations of the same data (Godot Resource vs pure .NET record) | MetricBinding lives in Godot-free library (xUnit testable). PcpBindingResource needs Godot's Resource base class for serialization. Thin conversion method bridges them. The alternative (single class) would force Godot dependency into PcpGodotBridge, violating the architectural boundary. |
| EditorInspectorPlugin for validation | Custom UI layer on top of built-in array editor | Without it, validation feedback (FR-010) and metric browsing (FR-004) have no home. The built-in array editor shows fields but can't display warnings or launch browse dialogs. |
| BindingValidator extraction | New class extracted from BindingConfigLoader | BindingConfigLoader mixes TOML parsing with validation. Extracting validation rules lets us test them independently and reuse them from both the TOML path (legacy) and the Resource path (new). |
