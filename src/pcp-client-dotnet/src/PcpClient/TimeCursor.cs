namespace PcpClient;

/// <summary>
/// Controls what "now" means for metric queries.
/// Manages transitions between live, playback, and paused modes
/// with speed-adjusted position advancement.
/// </summary>
public class TimeCursor
{
    private double _playbackSpeed = 1.0;

    public CursorMode Mode { get; private set; } = CursorMode.Live;

    public DateTime Position { get; private set; }

    public DateTime? StartTime { get; private set; }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set => _playbackSpeed = Math.Clamp(value, 0.1, 100.0);
    }

    public void StartPlayback(DateTime startTime)
    {
        Mode = CursorMode.Playback;
        StartTime = startTime;
        Position = startTime;
    }

    public void Pause()
    {
        if (Mode != CursorMode.Playback)
            throw new InvalidOperationException(
                $"Cannot pause from {Mode} mode. Must be in Playback mode.");

        Mode = CursorMode.Paused;
    }

    public void Resume()
    {
        if (Mode != CursorMode.Paused)
            throw new InvalidOperationException(
                $"Cannot resume from {Mode} mode. Must be in Paused mode.");

        Mode = CursorMode.Playback;
    }

    public void ResetToLive()
    {
        Mode = CursorMode.Live;
        StartTime = null;
    }

    public void AdvanceBy(TimeSpan elapsed)
    {
        if (Mode != CursorMode.Playback)
            return;

        var scaledTicks = (long)(elapsed.Ticks * _playbackSpeed);
        Position = Position.Add(TimeSpan.FromTicks(scaledTicks));
    }
}

public enum CursorMode
{
    Live,
    Playback,
    Paused
}
