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

    // ── Loop property defaults ──

    [Fact]
    public void NewTimeCursor_Loop_DefaultsToFalse()
    {
        var cursor = new TimeCursor();

        Assert.False(cursor.Loop);
    }

    [Fact]
    public void NewTimeCursor_EndBound_DefaultsToNull()
    {
        var cursor = new TimeCursor();

        Assert.Null(cursor.EndBound);
    }

    // ── Loop wrap-around behaviour ──

    [Fact]
    public void AdvanceBy_LoopTrue_PastEndBound_WrapsToStartTime()
    {
        var cursor = new TimeCursor();
        var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 10, 14, 1, 0, DateTimeKind.Utc);

        cursor.StartPlayback(start);
        cursor.EndBound = end;
        cursor.Loop = true;

        // Advance 90 seconds at 1x — well past the 60-second window
        cursor.AdvanceBy(TimeSpan.FromSeconds(90));

        Assert.Equal(start, cursor.Position);
    }

    [Fact]
    public void AdvanceBy_LoopFalse_PastEndBound_ContinuesAdvancing()
    {
        var cursor = new TimeCursor();
        var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 3, 10, 14, 1, 0, DateTimeKind.Utc);

        cursor.StartPlayback(start);
        cursor.EndBound = end;
        cursor.Loop = false;

        cursor.AdvanceBy(TimeSpan.FromSeconds(90));

        // Should be 90 seconds past start — no wrapping
        var expected = start.AddSeconds(90);
        Assert.Equal(expected, cursor.Position);
    }

    [Fact]
    public void AdvanceBy_LoopTrue_NoEndBound_DoesNotWrap()
    {
        var cursor = new TimeCursor();
        var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);

        cursor.StartPlayback(start);
        cursor.Loop = true;
        // No EndBound set — can't know where to wrap

        cursor.AdvanceBy(TimeSpan.FromSeconds(90));

        // Should advance normally — no wrapping without known bounds
        var expected = start.AddSeconds(90);
        Assert.Equal(expected, cursor.Position);
    }

    // ── IN/OUT point properties ──

    [Fact]
    public void NewTimeCursor_InPoint_DefaultsToNull()
    {
        var cursor = new TimeCursor();
        Assert.Null(cursor.InPoint);
    }

    [Fact]
    public void NewTimeCursor_OutPoint_DefaultsToNull()
    {
        var cursor = new TimeCursor();
        Assert.Null(cursor.OutPoint);
    }

    [Fact]
    public void SetInOutRange_SetsInPointAndOutPoint()
    {
        var cursor = new TimeCursor();
        var inPoint = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc);
        var outPoint = new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc);

        cursor.SetInOutRange(inPoint, outPoint);

        Assert.Equal(inPoint, cursor.InPoint);
        Assert.Equal(outPoint, cursor.OutPoint);
        Assert.Equal(outPoint, cursor.EndBound);
        Assert.True(cursor.Loop);
    }

    [Fact]
    public void ClearInOutRange_ResetsToNull()
    {
        var cursor = new TimeCursor();
        cursor.SetInOutRange(
            new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc));

        cursor.ClearInOutRange();

        Assert.Null(cursor.InPoint);
        Assert.Null(cursor.OutPoint);
        Assert.Null(cursor.EndBound);
        Assert.False(cursor.Loop);
    }

    [Fact]
    public void AdvanceBy_WithInPoint_LoopsBackToInPoint()
    {
        var cursor = new TimeCursor();
        var start = new DateTime(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);
        var inPoint = new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc);
        var outPoint = new DateTime(2026, 3, 14, 0, 1, 0, DateTimeKind.Utc);

        cursor.StartPlayback(start);
        cursor.SetInOutRange(inPoint, outPoint);

        // Jump near out point, then advance past it
        cursor.JumpTo(outPoint.AddSeconds(-5));
        cursor.AdvanceBy(TimeSpan.FromSeconds(30));

        // Should loop back to IN point, not StartTime
        Assert.Equal(inPoint, cursor.Position);
    }

    [Fact]
    public void ResetToLive_ClearsInOutPoints()
    {
        var cursor = new TimeCursor();
        cursor.StartPlayback(DateTime.UtcNow);
        cursor.SetInOutRange(
            new DateTime(2026, 3, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc));

        cursor.ResetToLive();

        Assert.Null(cursor.InPoint);
        Assert.Null(cursor.OutPoint);
    }

    // ── StepByInterval ──

    [Fact]
    public void StepByInterval_Forward_AdvancesPosition()
    {
        var cursor = new TimeCursor();
        var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(start);
        cursor.Pause();

        cursor.StepByInterval(60.0, 1);

        Assert.Equal(start.AddSeconds(60), cursor.Position);
    }

    [Fact]
    public void StepByInterval_Backward_RewindsPosition()
    {
        var cursor = new TimeCursor();
        var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(start);
        cursor.AdvanceBy(TimeSpan.FromMinutes(5));
        cursor.Pause();

        cursor.StepByInterval(60.0, -1);

        Assert.Equal(start.AddMinutes(5).AddSeconds(-60), cursor.Position);
    }

    [Fact]
    public void StepByInterval_InLiveMode_DoesNothing()
    {
        var cursor = new TimeCursor();

        cursor.StepByInterval(60.0, 1);

        Assert.Equal(CursorMode.Live, cursor.Mode);
    }

    // ── JumpTo ──

    [Fact]
    public void JumpTo_SetsPositionDirectly()
    {
        var cursor = new TimeCursor();
        var start = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc);
        cursor.StartPlayback(start);

        var target = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc);
        cursor.JumpTo(target);

        Assert.Equal(target, cursor.Position);
    }

    [Fact]
    public void JumpTo_InLiveMode_DoesNothing()
    {
        var cursor = new TimeCursor();

        cursor.JumpTo(new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc));

        // Should remain in Live mode, position is not tracked
        Assert.Equal(CursorMode.Live, cursor.Mode);
    }
}
