# Quickstart: Editor-Integrated PCP Bindings

**Feature Branch**: `003-editor-pcp-bindings`

## Prerequisites

- Godot 4.4+ with .NET 8.0
- pmview-bridge addon enabled in project
- pmproxy endpoint configured in Project Settings (`pmview/endpoint`)

## Adding a PCP Binding to a Node

1. Select any Node3D in your scene
2. In the Inspector, click "Add Script" → select `PcpBindable.cs` from `addons/pmview-bridge/`
3. The "Pcp Bindings" array property appears in the inspector
4. Click the array → "Add Element" → "New PcpBindingResource"
5. Expand the new binding and fill in:
   - **Metric Name**: e.g., `kernel.all.load` (or use "Browse Metrics" button)
   - **Target Property**: e.g., `height` (built-in) or a custom `@export` var name
   - **Source Range Min/Max**: expected metric value range
   - **Target Range Min/Max**: desired visual property range
   - **Instance Name** or **Instance Id**: for instanced metrics (leave defaults for singular)
   - **Initial Value**: value applied before metric data arrives (default: 0)
6. Save the scene — bindings persist in the `.tscn` file

## Browsing Available Metrics

1. With a binding selected, click the "Browse Metrics" button in the inspector
2. The metric browser connects to the pmproxy endpoint from project settings
3. Navigate the metric namespace tree (click to expand namespaces)
4. Select a metric to see its description and instance information
5. For instanced metrics, select the desired instance
6. Confirm to populate the binding fields

## Validation

- **Red indicators**: errors that must be fixed (invalid ranges, duplicate property targets)
- **Yellow indicators**: warnings to review (metric not found on pmproxy, missing instance selection)
- **Green indicator**: binding configuration is valid
- Offline validation runs always; connected validation requires pmproxy access

## Runtime

Bindings are automatically picked up by the SceneBinder at runtime. No additional configuration needed — just press Play.
