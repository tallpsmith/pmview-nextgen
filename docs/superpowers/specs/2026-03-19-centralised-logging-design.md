# Centralised Logging via ILogger

**Date:** 2026-03-19
**Status:** Approved

## Problem

Logging is scattered across the codebase using raw `GD.Print`, `GD.PrintErr`,
and `GD.PushWarning` calls. Some components (MetricPoller, SceneBinder) have
per-component `VerboseLogging` toggles; others log unconditionally. There's no
way for users to control logging at runtime, and no file output for post-mortem
debugging when running as an exported app.

## Design

### Single logging pipeline via `Microsoft.Extensions.Logging.ILogger`

All logging — from pure .NET libraries (PcpClient, PcpGodotBridge), C# Godot
code, and GDScript — flows through one `ILoggerFactory` backed by a custom
`PmviewLoggerProvider`.

```
GDScript code  ──→  PmviewLogger (autoload, thin wrapper)  ──→  ILoggerFactory
C# Godot code  ──→  ILogger<T>  ──────────────────────────────→  ILoggerFactory
PcpClient      ──→  ILogger<T>  ──────────────────────────────→  ILoggerFactory
PcpGodotBridge ──→  ILogger<T>  ──────────────────────────────→  ILoggerFactory
                                                                       │
                                                              PmviewLoggerProvider
                                                                  │          │
                                                          user://pmview.log  stdout
```

### Two sinks, hardcoded

- **File:** `user://pmview.log` — truncated on each app launch (one session per file)
- **Stdout:** console output for terminal debugging

No configurable sink framework — YAGNI until someone needs syslog or remote.

### Log format

```
[2026-03-19T07:33:01] [INFO] [MetricPoller] Rate-converting counters: kernel.all.cpu.idle
[2026-03-19T07:33:01] [WARN] [SceneBinder] Missing instance value for disk.dev.read
[2026-03-19T07:33:01] [ERROR] [LoadingPipeline] Pipeline failed: Connection refused
```

### Verbose toggle

- `LogLevel.Warning` and above always log (errors and warnings are never silenced)
- `LogLevel.Information` and below only log when verbose is enabled
- One global toggle — no per-component switches

### Components

#### `PmviewLoggerProvider` / `PmviewLogWriter` (C#)

- Implements `ILoggerProvider` / `ILogger`
- Writes to both sinks with the format above
- `static bool Verbose` property controls the LogLevel gate
- File opened lazily on first write, flushed after each line, closed on exit
- Lives in `src/pmview-app/scripts/` (app-level concern)

#### `PmviewLogger` autoload node (C#)

- Godot Node registered as autoload in `project.godot`
- Exposes `Log()`, `Warn()`, `Error()` methods callable from GDScript
- Internally delegates to an `ILogger` instance from the shared factory
- Owns the `ILoggerFactory` instance and the `Verbose` toggle
- Lives in `src/pmview-app/scripts/`

#### ILogger injection into .NET libraries

- PcpClient and PcpGodotBridge accept `ILogger<T>` or `ILoggerFactory` via
  constructor injection (standard .NET pattern)
- The app layer passes the factory when constructing these types
- Libraries that don't currently log can adopt `ILogger` incrementally

### UI

- `CheckButton` on the main menu, labelled "VERBOSE LOGGING"
- Off by default
- Positioned below the mode buttons, above the separator before LAUNCH
- Value passed through `connection_config["verbose_logging"]`
- `LoadingPipeline` reads the flag and sets `PmviewLogger.Verbose`

### Migration

- Replace all `GD.Print` diagnostic calls with `ILogger.LogInformation()` or
  `PmviewLogger.Log()` (from GDScript)
- Replace all `GD.PushWarning` with `ILogger.LogWarning()` / `PmviewLogger.Warn()`
- Replace all `GD.PrintErr` with `ILogger.LogError()` / `PmviewLogger.Error()`
- Remove `[Export] public bool VerboseLogging` from MetricPoller and SceneBinder
- Remove archive-mode auto-enable of verbose logging in RuntimeSceneBuilder

## Out of scope

- Configurable sink destinations (file path, remote, syslog)
- Log rotation or size limits (single session file, truncated on launch)
- Structured/JSON logging
- Per-component log level configuration
