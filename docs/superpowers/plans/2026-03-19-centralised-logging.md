# Centralised Logging Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all scattered GD.Print/PrintErr/PushWarning calls with a centralised ILogger-based pipeline, controlled by a UI toggle on the main menu, writing to both stdout and `user://pmview.log`.

**Architecture:** A custom `PmviewLoggerProvider` implements `ILoggerProvider` with two sinks (stdout + file). A `PmviewLogger` Godot autoload node owns the `ILoggerFactory` and exposes `Log()`/`Warn()`/`Error()` to GDScript. All C# code uses `ILogger<T>` from the shared factory. One global `Verbose` toggle controls whether `LogLevel.Information` and below are emitted.

**Tech Stack:** Microsoft.Extensions.Logging.Abstractions (NuGet), Godot autoload, C#

**Spec:** `docs/superpowers/specs/2026-03-19-centralised-logging-design.md`

---

## File Map

### New files
- `src/pmview-app/scripts/PmviewLoggerProvider.cs` — `ILoggerProvider` + `ILogger` implementation with stdout + file sinks
- `src/pmview-app/scripts/PmviewLogger.cs` — Godot autoload Node, owns `ILoggerFactory`, exposes methods to GDScript

### Modified files
- `src/pmview-app/pmview-app.csproj` — add `Microsoft.Extensions.Logging` NuGet
- `src/pmview-app/project.godot` — register `PmviewLogger` autoload
- `src/pmview-app/scenes/main_menu.tscn` — add verbose logging CheckButton
- `src/pmview-app/scripts/MainMenuController.gd` — wire checkbox into connection_config
- `src/pmview-app/scripts/LoadingPipeline.cs` — read verbose flag, set PmviewLogger.Verbose, migrate logging
- `src/pmview-app/scripts/RuntimeSceneBuilder.cs` — migrate all GD.Print/PrintErr/PushWarning to ILogger
- `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs` — remove VerboseLogging export, migrate to ILogger
- `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs` — remove VerboseLogging export, migrate to ILogger
- `src/pmview-bridge-addon/addons/pmview-bridge/MetricBrowserDialog.cs` — migrate to ILogger
- `src/pmview-bridge-addon/pmview-nextgen.csproj` — add `Microsoft.Extensions.Logging.Abstractions` NuGet

### Not modified (out of scope)
- `PcpClient.csproj` / PcpClient classes — ILogger injection deferred (no logging calls currently)
- `PcpGodotBridge.csproj` / PcpGodotBridge classes — all static, ILogger injection deferred
- `PcpBindingInspectorPlugin.cs` — editor-only tool, leave as-is
- E2E test files — test skip messages, not diagnostic logging

---

## Chunk 1: Logger Infrastructure

### Task 1: Add NuGet dependencies

**Files:**
- Modify: `src/pmview-app/pmview-app.csproj`
- Modify: `src/pmview-bridge-addon/pmview-nextgen.csproj`

- [ ] **Step 1: Add Microsoft.Extensions.Logging to pmview-app.csproj**

Add to the existing `<ItemGroup>` with `<Reference>` elements, or create a new `<ItemGroup>`:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Logging" Version="8.*" />
</ItemGroup>
```

Note: Version 8.x matches the net8.0 target framework.

- [ ] **Step 2: Add Microsoft.Extensions.Logging.Abstractions to addon csproj**

The addon's C# files (MetricPoller, SceneBinder, MetricBrowserDialog) need `ILogger<T>`. Add to `src/pmview-bridge-addon/pmview-nextgen.csproj`:

```xml
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.*" />
```

Add this alongside the existing test package references.

- [ ] **Step 3: Verify build**

Run:
```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-bridge-addon/pmview-nextgen.csproj
dotnet build src/pmview-app/pmview-app.csproj
```
Expected: both build successfully.

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/pmview-app.csproj src/pmview-bridge-addon/pmview-nextgen.csproj
git commit -m "Add Microsoft.Extensions.Logging NuGet dependencies"
```

---

### Task 2: Implement PmviewLoggerProvider

**Files:**
- Create: `src/pmview-app/scripts/PmviewLoggerProvider.cs`

- [ ] **Step 1: Create PmviewLoggerProvider**

This is the `ILoggerProvider` implementation with two sinks: stdout and file.

```csharp
using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace PmviewApp;

/// <summary>
/// Custom ILoggerProvider that writes to both stdout and a log file.
/// All logging in pmview routes through this provider.
/// </summary>
public sealed class PmviewLoggerProvider : ILoggerProvider
{
    private readonly object _lock = new();
    private StreamWriter? _fileWriter;
    private bool _disposed;

    /// <summary>
    /// When false, only Warning and above are emitted.
    /// When true, all levels including Information and Debug are emitted.
    /// </summary>
    public static bool Verbose { get; set; }

    /// <summary>
    /// Absolute file path resolved from Godot's user:// — set once at startup.
    /// </summary>
    public string? LogFilePath { get; set; }

    public ILogger CreateLogger(string categoryName)
    {
        return new PmviewLog(categoryName, this);
    }

    internal void WriteEntry(LogLevel level, string category, string message)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        var levelTag = level switch
        {
            LogLevel.Error or LogLevel.Critical => "ERROR",
            LogLevel.Warning => "WARN",
            LogLevel.Information => "INFO",
            LogLevel.Debug => "DEBUG",
            LogLevel.Trace => "TRACE",
            _ => "INFO"
        };

        var line = $"[{timestamp}] [{levelTag}] [{category}] {message}";

        lock (_lock)
        {
            Console.WriteLine(line);
            EnsureFileWriter();
            _fileWriter?.WriteLine(line);
            _fileWriter?.Flush();
        }
    }

    private void EnsureFileWriter()
    {
        if (_fileWriter != null || LogFilePath == null)
            return;

        var dir = Path.GetDirectoryName(LogFilePath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _fileWriter = new StreamWriter(LogFilePath, append: false);
    }

    public void Close()
    {
        lock (_lock)
        {
            _fileWriter?.Flush();
            _fileWriter?.Dispose();
            _fileWriter = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Close();
    }
}

/// <summary>
/// Individual logger instance that gates on Verbose and delegates to the provider.
/// </summary>
internal sealed class PmviewLog : ILogger
{
    private readonly string _category;
    private readonly PmviewLoggerProvider _provider;

    public PmviewLog(string category, PmviewLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        if (PmviewLoggerProvider.Verbose)
            return logLevel >= LogLevel.Debug;

        return logLevel >= LogLevel.Warning;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (exception != null)
            message += Environment.NewLine + exception;

        _provider.WriteEntry(logLevel, _category, message);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        => null;
}
```

- [ ] **Step 2: Verify build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-app/pmview-app.csproj
```
Expected: PASS

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scripts/PmviewLoggerProvider.cs
git commit -m "Add PmviewLoggerProvider — dual-sink ILogger with verbose gate"
```

---

### Task 3: Implement PmviewLogger autoload node

**Files:**
- Create: `src/pmview-app/scripts/PmviewLogger.cs`
- Modify: `src/pmview-app/project.godot`

- [ ] **Step 1: Create PmviewLogger autoload**

```csharp
using Godot;
using Microsoft.Extensions.Logging;

namespace PmviewApp;

/// <summary>
/// Godot autoload node that owns the ILoggerFactory and provides logging
/// to both C# and GDScript code. Registered as "PmviewLogger" in project.godot.
///
/// GDScript usage:
///   PmviewLogger.log("MyScript", "something happened")
///   PmviewLogger.warn("MyScript", "this looks wrong")
///   PmviewLogger.error("MyScript", "this is broken")
///
/// C# usage:
///   var logger = PmviewLogger.GetLogger<MyClass>();
///   logger.LogInformation("something happened");
/// </summary>
public partial class PmviewLogger : Node
{
    private static PmviewLoggerProvider? _provider;
    private static ILoggerFactory? _factory;
    private static ILogger? _gdScriptLogger;

    /// <summary>Global verbose toggle — set from UI before pipeline starts.</summary>
    public static bool Verbose
    {
        get => PmviewLoggerProvider.Verbose;
        set => PmviewLoggerProvider.Verbose = value;
    }

    public override void _Ready()
    {
        _provider = new PmviewLoggerProvider
        {
            LogFilePath = ProjectSettings.GlobalizePath("user://pmview.log")
        };

        _factory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(_provider);
            builder.SetMinimumLevel(LogLevel.Trace);
        });

        _gdScriptLogger = _factory.CreateLogger("GDScript");
    }

    public override void _ExitTree()
    {
        _provider?.Close();
        _factory?.Dispose();
    }

    /// <summary>Get a typed ILogger for C# classes.</summary>
    public static ILogger<T> GetLogger<T>()
    {
        return _factory?.CreateLogger<T>()
               ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<T>.Instance;
    }

    /// <summary>Get a named ILogger for C# classes.</summary>
    public static ILogger GetLogger(string categoryName)
    {
        return _factory?.CreateLogger(categoryName)
               ?? Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance.CreateLogger(categoryName);
    }

    // --- GDScript-callable methods ---

    /// <summary>Verbose log — only emitted when verbose toggle is on.</summary>
    public void log(string component, string message)
    {
        _factory?.CreateLogger(component).LogInformation("{Message}", message);
    }

    /// <summary>Warning — always emitted.</summary>
    public void warn(string component, string message)
    {
        _factory?.CreateLogger(component).LogWarning("{Message}", message);
    }

    /// <summary>Error — always emitted.</summary>
    public void error(string component, string message)
    {
        _factory?.CreateLogger(component).LogError("{Message}", message);
    }
}
```

- [ ] **Step 2: Register autoload in project.godot**

Find the `[autoload]` section in `src/pmview-app/project.godot` and add PmviewLogger **before** SceneManager (logger should be available first):

```ini
[autoload]

PmviewLogger="*res://scripts/PmviewLogger.cs"
SceneManager="*res://scripts/SceneManager.gd"
```

- [ ] **Step 3: Verify build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-app/pmview-app.csproj
```
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/PmviewLogger.cs src/pmview-app/project.godot
git commit -m "Add PmviewLogger autoload — ILoggerFactory bridge for C# and GDScript"
```

---

## Chunk 2: UI Toggle and Pipeline Wiring

### Task 4: Add verbose logging checkbox to main menu

**Files:**
- Modify: `src/pmview-app/scenes/main_menu.tscn`
- Modify: `src/pmview-app/scripts/MainMenuController.gd`

- [ ] **Step 1: Add CheckButton to main_menu.tscn**

Add after the `HSeparator2` node (line 327-328) and before the `LaunchPanel` node (line 330). Insert these lines:

```ini
[node name="VerboseCheck" type="CheckButton" parent="CanvasLayer/Control/UIPanel/CenterContainer/VBoxContainer"]
unique_name_in_owner = true
layout_mode = 2
theme_override_font_sizes/font_size = 10
text = "VERBOSE LOGGING"
```

- [ ] **Step 2: Wire checkbox in MainMenuController.gd**

Add the `@onready` reference near the other UI references (around line 28):

```gdscript
@onready var verbose_check: CheckButton = %VerboseCheck
```

Then update the `_launch()` method. In the archive branch (around line 281), add `"verbose_logging"`:

```gdscript
		SceneManager.go_to_loading({
			"endpoint": url,
			"mode": "archive",
			"hostname": hostname,
			"start_time": start_time,
			"archive_start_epoch": _archive_start_epoch,
			"archive_end_epoch": _archive_end_epoch,
			"verbose_logging": verbose_check.button_pressed,
		})
```

And in the live branch (around line 290):

```gdscript
		SceneManager.go_to_loading({
			"endpoint": url,
			"mode": "live",
			"verbose_logging": verbose_check.button_pressed,
		})
```

- [ ] **Step 3: Commit**

```bash
git add src/pmview-app/scenes/main_menu.tscn src/pmview-app/scripts/MainMenuController.gd
git commit -m "Add verbose logging checkbox to main menu"
```

---

### Task 5: Wire verbose flag through LoadingPipeline

**Files:**
- Modify: `src/pmview-app/scripts/LoadingPipeline.cs`

- [ ] **Step 1: Read the verbose flag and set PmviewLogger.Verbose**

In `LoadingPipeline.cs`, find where connection_config is read (the method that starts the pipeline). Add early in the pipeline startup:

```csharp
var verbose = config.ContainsKey("verbose_logging") && (bool)config["verbose_logging"];
PmviewLogger.Verbose = verbose;
```

- [ ] **Step 2: Migrate LoadingPipeline's own logging**

Replace line 110:
```csharp
// Before:
GD.PrintErr($"Pipeline failed at phase {currentPhase}: {ex.Message}");
// After:
PmviewLogger.GetLogger("LoadingPipeline").LogError("Pipeline failed at phase {Phase}: {Error}", currentPhase, ex.Message);
```

Add at top of file:
```csharp
using Microsoft.Extensions.Logging;
```

- [ ] **Step 3: Verify build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-app/pmview-app.csproj
```
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/pmview-app/scripts/LoadingPipeline.cs
git commit -m "Wire verbose toggle through LoadingPipeline to PmviewLogger"
```

---

## Chunk 3: Migrate App-Layer Logging

### Task 6: Migrate RuntimeSceneBuilder to ILogger

**Files:**
- Modify: `src/pmview-app/scripts/RuntimeSceneBuilder.cs`

- [ ] **Step 1: Add logger field and using directive**

Add at top:
```csharp
using Microsoft.Extensions.Logging;
```

Add field in the class:
```csharp
private static readonly ILogger _log = PmviewLogger.GetLogger("RuntimeSceneBuilder");
```

- [ ] **Step 2: Replace all GD.Print calls with _log.LogInformation**

Replace each `GD.Print(...)` call with `_log.LogInformation(...)`. Examples:

```csharp
// Line 44: GD.Print("[RuntimeSceneBuilder] Build starting...");
_log.LogInformation("Build starting...");

// Line 51: GD.Print($"[RuntimeSceneBuilder] Building {layout.Zones.Count} zones...");
_log.LogInformation("Building {ZoneCount} zones...", layout.Zones.Count);

// Line 68: GD.Print($"[RuntimeSceneBuilder] Build complete. Root children: {root.GetChildCount()}");
_log.LogInformation("Build complete. Root children: {ChildCount}", root.GetChildCount());
```

Apply the same pattern to all `GD.Print` calls (lines 44, 51, 68, 81, 84, 96, 111).

- [ ] **Step 3: Replace all GD.PrintErr calls with _log.LogError**

```csharp
// Line 79: GD.PrintErr($"[RuntimeSceneBuilder] FAILED to load controller script: {ControllerScriptPath}");
_log.LogError("FAILED to load controller script: {Path}", ControllerScriptPath);
```

Apply to lines 79, 94, 109, 202.

- [ ] **Step 4: Replace all GD.PushWarning calls with _log.LogWarning**

```csharp
// Line 368: GD.PushWarning("[RuntimeSceneBuilder] RangeTuningPanel scene not found");
_log.LogWarning("RangeTuningPanel scene not found");
```

Apply to lines 368, 401, 409.

- [ ] **Step 5: Remove archive-mode auto-enable of verbose logging**

Find line 48 where `verboseLogging: mode == "archive"` is passed. Remove this parameter — verbose is now controlled globally via the UI toggle.

- [ ] **Step 6: Verify build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-app/pmview-app.csproj
```
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/pmview-app/scripts/RuntimeSceneBuilder.cs
git commit -m "Migrate RuntimeSceneBuilder logging to ILogger"
```

---

## Chunk 4: Migrate Addon Logging

### Task 7: Migrate MetricPoller to ILogger

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs`

- [ ] **Step 1: Add using directive and logger field**

Add at top:
```csharp
using Microsoft.Extensions.Logging;
```

Add field (MetricPoller is a Godot Node, so use lazy initialisation since the autoload may not be ready at field init time):

```csharp
private ILogger? _log;
private ILogger Log => _log ??= PmviewLogger.GetLogger("MetricPoller");
```

Note: `PmviewLogger` here refers to the autoload's static methods, which the addon can call because it's loaded in the same Godot app assembly context.

- [ ] **Step 2: Remove VerboseLogging export property**

Delete:
```csharp
[Export] public bool VerboseLogging { get; set; } = false;
```

- [ ] **Step 3: Replace all conditional verbose checks**

All places that check `if (VerboseLogging)` now just use `_log.LogInformation(...)` — the log level gate in PmviewLoggerProvider handles the filtering.

- [ ] **Step 4: Replace all GD.Print calls with Log.LogInformation**

Apply to all ~15 `GD.Print` calls. Pattern:
```csharp
// Before: GD.Print($"[MetricPoller] Rate-converting counters: {string.Join(", ", counterNames)}");
// After:  Log.LogInformation("Rate-converting counters: {Counters}", string.Join(", ", counterNames));
```

- [ ] **Step 5: Replace all GD.PushWarning calls with Log.LogWarning**

Apply to lines 381, 509, 581, 782, 803.

- [ ] **Step 6: Replace all GD.PrintErr calls with Log.LogError**

Apply to lines 609, 636, 746.

- [ ] **Step 7: Verify build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-bridge-addon/pmview-nextgen.csproj
```
Expected: PASS

- [ ] **Step 8: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricPoller.cs
git commit -m "Migrate MetricPoller logging to ILogger, remove per-component toggle"
```

---

### Task 8: Migrate SceneBinder to ILogger

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs`

- [ ] **Step 1: Add using directive and logger field**

Same pattern as MetricPoller:
```csharp
using Microsoft.Extensions.Logging;

private ILogger? _log;
private ILogger Log => _log ??= PmviewLogger.GetLogger("SceneBinder");
```

- [ ] **Step 2: Remove VerboseLogging export property**

Delete:
```csharp
[Export] public bool VerboseLogging { get; set; } = false;
```

- [ ] **Step 3: Replace all conditional verbose checks and GD.Print/PushWarning calls**

Apply same pattern as MetricPoller to all 13 logging calls. Remove `if (VerboseLogging)` guards — the log level gate handles it.

- [ ] **Step 4: Verify build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-bridge-addon/pmview-nextgen.csproj
```
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/SceneBinder.cs
git commit -m "Migrate SceneBinder logging to ILogger, remove per-component toggle"
```

---

### Task 9: Migrate MetricBrowserDialog to ILogger

**Files:**
- Modify: `src/pmview-bridge-addon/addons/pmview-bridge/MetricBrowserDialog.cs`

- [ ] **Step 1: Add using directive and logger field**

```csharp
using Microsoft.Extensions.Logging;

private ILogger? _log;
private ILogger Log => _log ??= PmviewLogger.GetLogger("MetricBrowser");
```

- [ ] **Step 2: Replace all 15 GD.Print/PushWarning calls**

Apply same pattern. All `GD.Print` → `Log.LogInformation`, all `GD.PushWarning` → `Log.LogWarning`.

- [ ] **Step 3: Verify build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet build src/pmview-bridge-addon/pmview-nextgen.csproj
```
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/pmview-bridge-addon/addons/pmview-bridge/MetricBrowserDialog.cs
git commit -m "Migrate MetricBrowserDialog logging to ILogger"
```

---

## Chunk 5: Final Verification

### Task 10: Full build and verify no remaining GD.Print calls

- [ ] **Step 1: Full CI build**

```bash
export PATH="/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
dotnet test pmview-nextgen.ci.slnf --filter "FullyQualifiedName!~Integration"
```
Expected: all tests pass.

- [ ] **Step 2: Grep for remaining GD.Print calls**

Verify no stray `GD.Print` calls remain in production code (E2E tests and editor plugins are OK):

```bash
grep -rn "GD\.Print\|GD\.PrintErr\|GD\.PushWarning" \
  src/pmview-app/scripts/ \
  src/pmview-bridge-addon/addons/pmview-bridge/ \
  --include="*.cs" \
  | grep -v "Test" | grep -v "InspectorPlugin"
```
Expected: no output (all migrated).

- [ ] **Step 3: Commit any remaining fixes**

- [ ] **Step 4: Push and monitor CI**

```bash
git push origin main
```
Then monitor the GitHub Actions CI run.
