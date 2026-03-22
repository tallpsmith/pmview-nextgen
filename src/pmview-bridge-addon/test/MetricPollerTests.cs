using Godot;
using GdUnit4;
using PcpClient;
using PmviewNextgen.Bridge;
using static GdUnit4.Assertions;

namespace PmviewNextgen.Tests;

/// <summary>
/// Tests for MetricPoller: state machine transitions, live polling with
/// mock IPcpClient, playback controls, configuration, and replay.
/// Uses a TestableMetricPoller subclass to inject mock clients via the
/// virtual CreateClient factory method.
/// </summary>
[TestSuite]
public partial class MetricPollerTests
{
	// ── Test infrastructure ─────────────────────────────────────────────

	/// <summary>
	/// Subclass that overrides the factory method to inject a mock client.
	/// </summary>
	private partial class TestableMetricPoller : MetricPoller
	{
		private readonly IPcpClient _mockClient;

		public TestableMetricPoller(IPcpClient mockClient)
		{
			_mockClient = mockClient;
		}

		protected override IPcpClient CreateClient(Uri endpoint, System.Net.Http.HttpClient http)
		{
			return _mockClient;
		}
	}

	/// <summary>
	/// Minimal mock IPcpClient for unit testing.
	/// Records calls and returns configured results.
	/// </summary>
	private class MockPcpClient : IPcpClient
	{
		public ConnectionState State { get; set; } = ConnectionState.Disconnected;
		public Uri BaseUrl { get; } = new("http://localhost:44322");

		public bool ConnectAsyncCalled { get; private set; }
		public bool ShouldThrowOnConnect { get; set; }
		public string? ConnectErrorMessage { get; set; }
		public IReadOnlyList<MetricValue> FetchResult { get; set; } = [];
		public IReadOnlyList<MetricDescriptor> DescribeResult { get; set; } = [];

		public Task<int> ConnectAsync(int pollTimeoutSeconds = 60,
			CancellationToken cancellationToken = default)
		{
			ConnectAsyncCalled = true;
			if (ShouldThrowOnConnect)
				throw new PcpConnectionException(
					ConnectErrorMessage ?? "Connection refused");
			State = ConnectionState.Connected;
			return Task.FromResult(1);
		}

		public Task DisconnectAsync(CancellationToken cancellationToken = default)
		{
			State = ConnectionState.Disconnected;
			return Task.CompletedTask;
		}

		public Task<MetricNamespace> GetChildrenAsync(string prefix = "",
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new MetricNamespace(prefix, [], []));
		}

		public Task<IReadOnlyList<MetricDescriptor>> DescribeMetricsAsync(
			IEnumerable<string> metricNames,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(DescribeResult);
		}

		public Task<InstanceDomain> GetInstanceDomainAsync(string metricName,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new InstanceDomain("", []));
		}

		public Task<IReadOnlyList<MetricValue>> FetchAsync(
			IEnumerable<string> metricNames,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(FetchResult);
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
		public void Dispose() { }
	}

	// ── State machine transitions ───────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task InitialState_IsDisconnected()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		AssertThat(poller.CurrentState).IsEqual(ConnectionState.Disconnected);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task StartPolling_EmitsConnectingThenConnected()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		poller.MetricNames = ["kernel.all.load"];
		runner.Scene().AddChild(poller);

		var states = new List<string>();
		poller.ConnectionStateChanged += state => states.Add(state);

		poller.StartPolling();
		// Allow async operations to complete
		await Task.Delay(200);

		AssertThat(states).Contains("Connecting");
		AssertThat(states).Contains("Connected");
		AssertThat(mock.ConnectAsyncCalled).IsTrue();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task StartPolling_ConnectionFails_EmitsFailedAndError()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient
		{
			ShouldThrowOnConnect = true,
			ConnectErrorMessage = "Connection refused"
		};
		var poller = new TestableMetricPoller(mock);
		poller.MetricNames = ["kernel.all.load"];
		runner.Scene().AddChild(poller);

		var states = new List<string>();
		var errors = new List<string>();
		poller.ConnectionStateChanged += state => states.Add(state);
		poller.ErrorOccurred += msg => errors.Add(msg);

		poller.StartPolling();
		await Task.Delay(200);

		AssertThat(states).Contains("Failed");
		AssertThat(errors.Count).IsGreater(0);
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task StopPolling_AfterStart_NoError()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		poller.MetricNames = ["kernel.all.load"];
		runner.Scene().AddChild(poller);

		poller.StartPolling();
		await Task.Delay(200);

		// Should not throw or error
		poller.StopPolling();
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task StopPolling_WhenAlreadyStopped_IsNoOp()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		// Should not throw when no polling is active
		poller.StopPolling();
		poller.StopPolling(); // double stop
		await runner.AwaitIdleFrame();
	}

	// ── Playback controls ───────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task SetPlaybackSpeed_BelowMinimum_ClampsToPointOne()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		poller.SetPlaybackSpeed(0.05);
		AssertThat(poller.TimeCursor.PlaybackSpeed).IsEqual(0.1);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task SetPlaybackSpeed_AboveMaximum_ClampsToHundred()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		poller.SetPlaybackSpeed(200);
		AssertThat(poller.TimeCursor.PlaybackSpeed).IsEqual(100.0);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task SetPlaybackSpeed_ValidValue_AcceptedAsIs()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		poller.SetPlaybackSpeed(5.0);
		AssertThat(poller.TimeCursor.PlaybackSpeed).IsEqual(5.0);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task SetLoop_TogglesWithoutError()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		poller.SetLoop(true);
		AssertThat(poller.TimeCursor.Loop).IsTrue();

		poller.SetLoop(false);
		AssertThat(poller.TimeCursor.Loop).IsFalse();
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ResetToLive_EmitsLiveMode()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		var positions = new List<(string pos, string mode)>();
		poller.PlaybackPositionChanged += (pos, mode) => positions.Add((pos, mode));

		poller.ResetToLive();
		await runner.AwaitIdleFrame();

		AssertThat(positions.Count).IsGreater(0);
		AssertThat(positions[^1].mode).IsEqual("Live");
	}

	// ── Configuration ───────────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task UpdateMetricNames_UpdatesInternalList()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		poller.UpdateMetricNames(["kernel.all.load", "mem.util.used"]);
		AssertThat(poller.MetricNames).HasSize(2);
		AssertThat(poller.MetricNames).Contains("kernel.all.load");
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task DefaultExportedProperties_HaveExpectedValues()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		AssertThat(poller.Endpoint).IsEqual("http://localhost:44322");
		AssertThat(poller.PollIntervalMs).IsEqual(1000);
		AssertThat(poller.MetricNames).IsEmpty();
		await runner.AwaitIdleFrame();
	}

	// ── ReplayLastMetrics ───────────────────────────────────────────────

	[TestCase]
	[RequireGodotRuntime]
	public async Task ReplayLastMetrics_WithNoData_DoesNotEmit()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var mock = new MockPcpClient();
		var poller = new TestableMetricPoller(mock);
		runner.Scene().AddChild(poller);

		var emitted = false;
		poller.MetricsUpdated += (_, _) => emitted = true;

		poller.ReplayLastMetrics();
		await Task.Delay(100);

		AssertThat(emitted).IsFalse();
	}
}
