using Xunit;

namespace PcpClient.Tests;

/// <summary>
/// Tests for TimeCursor state machine: mode transitions, speed clamping,
/// and position advancement. Covers task T043.
/// </summary>
public class TimeCursorTests
{
    // ── Default state ──

    [Fact]
    public void NewTimeCursor_DefaultsToLiveMode()
    {
        var cursor = new TimeCursor();

        Assert.Equal(CursorMode.Live, cursor.Mode);
    }

    [Fact]
    public void NewTimeCursor_DefaultSpeed_IsOne()
    {
        var cursor = new TimeCursor();

        Assert.Equal(1.0, cursor.PlaybackSpeed);
    }

    [Fact]
    public void NewTimeCursor_StartTime_IsNull()
    {
        var cursor = new TimeCursor();

        Assert.Null(cursor.StartTime);
    }

    // ── Live → Playback transition ──

    [Fact]
    public void StartPlayback_SetsPlaybackModeAndStartTime()
    {
        var cursor = new TimeCursor();
        var startTime = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);

        cursor.StartPlayback(startTime);

        Assert.Equal(CursorMode.Playback, cursor.Mode);
        Assert.Equal(startTime, cursor.StartTime);
        Assert.Equal(startTime, cursor.Position);
    }

    // ── Playback → Paused transition ──

    [Fact]
    public void Pause_FromPlayback_SetsPausedMode()
    {
        var cursor = new TimeCursor();
        var startTime = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(startTime);

        cursor.Pause();

        Assert.Equal(CursorMode.Paused, cursor.Mode);
    }

    [Fact]
    public void Pause_PreservesCurrentPosition()
    {
        var cursor = new TimeCursor();
        var startTime = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(startTime);
        cursor.AdvanceBy(TimeSpan.FromSeconds(30));

        var positionBeforePause = cursor.Position;
        cursor.Pause();

        Assert.Equal(positionBeforePause, cursor.Position);
    }

    // ── Paused → Playback transition ──

    [Fact]
    public void Resume_FromPaused_SetsPlaybackMode()
    {
        var cursor = new TimeCursor();
        cursor.StartPlayback(DateTime.UtcNow);
        cursor.Pause();

        cursor.Resume();

        Assert.Equal(CursorMode.Playback, cursor.Mode);
    }

    // ── Playback/Paused → Live transition ──

    [Fact]
    public void ResetToLive_FromPlayback_SetsLiveMode()
    {
        var cursor = new TimeCursor();
        cursor.StartPlayback(DateTime.UtcNow.AddHours(-1));

        cursor.ResetToLive();

        Assert.Equal(CursorMode.Live, cursor.Mode);
        Assert.Null(cursor.StartTime);
    }

    [Fact]
    public void ResetToLive_FromPaused_SetsLiveMode()
    {
        var cursor = new TimeCursor();
        cursor.StartPlayback(DateTime.UtcNow.AddHours(-1));
        cursor.Pause();

        cursor.ResetToLive();

        Assert.Equal(CursorMode.Live, cursor.Mode);
        Assert.Null(cursor.StartTime);
    }

    // ── Invalid transitions ──

    [Fact]
    public void Pause_FromLive_ThrowsInvalidOperation()
    {
        var cursor = new TimeCursor();

        Assert.Throws<InvalidOperationException>(() => cursor.Pause());
    }

    [Fact]
    public void Resume_FromLive_ThrowsInvalidOperation()
    {
        var cursor = new TimeCursor();

        Assert.Throws<InvalidOperationException>(() => cursor.Resume());
    }

    [Fact]
    public void Resume_FromPlayback_ThrowsInvalidOperation()
    {
        var cursor = new TimeCursor();
        cursor.StartPlayback(DateTime.UtcNow);

        // Already playing — nothing to resume
        Assert.Throws<InvalidOperationException>(() => cursor.Resume());
    }

    // ── PlaybackSpeed clamping ──

    [Fact]
    public void PlaybackSpeed_BelowMinimum_ClampsToPointOne()
    {
        var cursor = new TimeCursor();

        cursor.PlaybackSpeed = 0.01;

        Assert.Equal(0.1, cursor.PlaybackSpeed);
    }

    [Fact]
    public void PlaybackSpeed_AboveMaximum_ClampsToHundred()
    {
        var cursor = new TimeCursor();

        cursor.PlaybackSpeed = 500.0;

        Assert.Equal(100.0, cursor.PlaybackSpeed);
    }

    [Fact]
    public void PlaybackSpeed_WithinRange_SetsExactValue()
    {
        var cursor = new TimeCursor();

        cursor.PlaybackSpeed = 2.5;

        Assert.Equal(2.5, cursor.PlaybackSpeed);
    }

    [Fact]
    public void PlaybackSpeed_AtBoundaries_Accepted()
    {
        var cursor = new TimeCursor();

        cursor.PlaybackSpeed = 0.1;
        Assert.Equal(0.1, cursor.PlaybackSpeed);

        cursor.PlaybackSpeed = 100.0;
        Assert.Equal(100.0, cursor.PlaybackSpeed);
    }

    // ── Position advancement ──

    [Fact]
    public void AdvanceBy_InPlayback_AdvancesPositionBySpeedMultiplier()
    {
        var cursor = new TimeCursor();
        var startTime = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(startTime);
        cursor.PlaybackSpeed = 2.0;

        cursor.AdvanceBy(TimeSpan.FromSeconds(10));

        // 10 seconds of real time at 2x speed = 20 seconds of playback time
        var expected = startTime.AddSeconds(20);
        Assert.Equal(expected, cursor.Position);
    }

    [Fact]
    public void AdvanceBy_AtDefaultSpeed_AdvancesOneToOne()
    {
        var cursor = new TimeCursor();
        var startTime = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(startTime);

        cursor.AdvanceBy(TimeSpan.FromSeconds(5));

        var expected = startTime.AddSeconds(5);
        Assert.Equal(expected, cursor.Position);
    }

    [Fact]
    public void AdvanceBy_WhenPaused_DoesNotAdvance()
    {
        var cursor = new TimeCursor();
        var startTime = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(startTime);
        cursor.Pause();

        cursor.AdvanceBy(TimeSpan.FromSeconds(10));

        Assert.Equal(startTime, cursor.Position);
    }

    [Fact]
    public void AdvanceBy_WhenLive_DoesNotAdvance()
    {
        var cursor = new TimeCursor();

        // Should be a no-op in Live mode — position is always "now"
        cursor.AdvanceBy(TimeSpan.FromSeconds(10));

        // No exception, position is not tracked in Live mode
        Assert.Equal(CursorMode.Live, cursor.Mode);
    }
}
