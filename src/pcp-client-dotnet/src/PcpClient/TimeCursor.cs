namespace PcpClient;

/// <summary>
/// Controls what "now" means for metric queries.
/// Manages transitions between live, playback, and paused modes
/// with speed-adjusted position advancement and IN/OUT range looping.
/// </summary>
public class TimeCursor
{
    private double _playbackSpeed = 1.0;

    public CursorMode Mode { get; private set; } = CursorMode.Live;

    public DateTime Position { get; private set; }

    public DateTime? StartTime { get; private set; }

    public DateTime? EndBound { get; set; }

    public bool Loop { get; set; }

    public DateTime? InPoint { get; private set; }

    public DateTime? OutPoint { get; private set; }

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
        EndBound = null;
        Loop = false;
        InPoint = null;
        OutPoint = null;
    }

    public void AdvanceBy(TimeSpan elapsed)
    {
        if (Mode != CursorMode.Playback)
            return;

        var scaledTicks = (long)(elapsed.Ticks * _playbackSpeed);
        var newPosition = Position.Add(TimeSpan.FromTicks(scaledTicks));

        if (Loop && EndBound.HasValue && newPosition > EndBound.Value)
        {
            // Loop back to InPoint if set, otherwise StartTime
            var loopTarget = InPoint ?? StartTime;
            if (loopTarget.HasValue)
                Position = loopTarget.Value;
            else
                Position = newPosition;
        }
        else
        {
            Position = newPosition;
        }
    }

    /// <summary>
    /// Sets the IN/OUT range for loop playback.
    /// Enables looping and sets EndBound to the OUT point.
    /// </summary>
    public void SetInOutRange(DateTime inPoint, DateTime outPoint)
    {
        InPoint = inPoint;
        OutPoint = outPoint;
        EndBound = outPoint;
        Loop = true;
    }

    /// <summary>
    /// Clears the IN/OUT range and disables looping.
    /// </summary>
    public void ClearInOutRange()
    {
        InPoint = null;
        OutPoint = null;
        EndBound = null;
        Loop = false;
    }

    /// <summary>
    /// Steps the position by a fixed interval. Used for frame-by-frame
    /// scrubbing (arrow keys). Works in Playback or Paused mode.
    /// Direction: +1 = forward, -1 = backward.
    /// </summary>
    public void StepByInterval(double intervalSeconds, int direction)
    {
        if (Mode == CursorMode.Live)
            return;

        Position = Position.AddSeconds(intervalSeconds * direction);
    }

    /// <summary>
    /// Jumps the position to a specific timestamp.
    /// Works in Playback or Paused mode.
    /// </summary>
    public void JumpTo(DateTime target)
    {
        if (Mode == CursorMode.Live)
            return;

        Position = target;
    }
}

public enum CursorMode
{
    Live,
    Playback,
    Paused
}
