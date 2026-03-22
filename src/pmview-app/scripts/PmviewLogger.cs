using System;
using Godot;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
///   var logger = PmviewLogger.GetLogger&lt;MyClass&gt;();
///   logger.LogInformation("something happened");
/// </summary>
public partial class PmviewLogger : Node
{
    private static PmviewLoggerProvider? _provider;
    private static ILoggerFactory? _factory;

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
            LogFilePath = ProjectSettings.GlobalizePath("res://pmview.log")
        };

        _factory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(_provider);
            builder.SetMinimumLevel(LogLevel.Trace);
        });
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
               ?? NullLogger<T>.Instance;
    }

    /// <summary>Get a named ILogger for C# classes.</summary>
    public static ILogger GetLogger(string categoryName)
    {
        return _factory?.CreateLogger(categoryName)
               ?? NullLoggerFactory.Instance.CreateLogger(categoryName);
    }

    // --- GDScript-callable methods (lowercase to follow GDScript conventions) ---

    /// <summary>Verbose log — only emitted when verbose toggle is on.</summary>
    public void log(string component, string message)
    {
        GetLogger(component).LogInformation("{Message}", message);
    }

    /// <summary>Warning — always emitted.</summary>
    public void warn(string component, string message)
    {
        GetLogger(component).LogWarning("{Message}", message);
    }

    /// <summary>Error — always emitted.</summary>
    public void error(string component, string message)
    {
        GetLogger(component).LogError("{Message}", message);
    }
}
