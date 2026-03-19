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
