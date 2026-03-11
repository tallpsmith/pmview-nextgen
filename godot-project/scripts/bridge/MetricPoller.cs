using Godot;
using PcpClient;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Polls a PcpClient connection on a configurable interval and emits
/// metric values as Godot signals. Bridges C# async world to GDScript.
/// Owns the PcpClientConnection lifecycle.
/// </summary>
public partial class MetricPoller : Node
{
	[Signal]
	public delegate void MetricsUpdatedEventHandler(
		Godot.Collections.Dictionary metrics);

	[Signal]
	public delegate void ConnectionStateChangedEventHandler(string state);

	[Signal]
	public delegate void ErrorOccurredEventHandler(string message);

	[Export] public string Endpoint { get; set; } = "http://localhost:44322";
	[Export] public int PollIntervalMs { get; set; } = 1000;
	[Export] public string[] MetricNames { get; set; } = [];

	private readonly System.Net.Http.HttpClient _sharedHttpClient = new();
	private PcpClientConnection? _client;
	private MetricRateConverter? _rateConverter;
	private Godot.Timer? _pollTimer;
	private bool _polling;

	public ConnectionState CurrentState => _client?.State ?? ConnectionState.Disconnected;

	public override void _Ready()
	{
		if (MetricNames.Length > 0)
			CallDeferred(nameof(StartPolling));
	}

	public async void StartPolling()
	{
		try
		{
			await ConnectToEndpoint();
			StartPollTimer();
		}
		catch (Exception ex)
		{
			EmitConnectionState("Failed");
			EmitSignal(SignalName.ErrorOccurred, $"StartPolling failed: {ex.Message}");
		}
	}

	public void StopPolling()
	{
		_pollTimer?.Stop();
		_polling = false;
	}

	public void UpdateMetricNames(string[] metricNames)
	{
		MetricNames = metricNames;
	}

	public void UpdateEndpoint(string endpoint, int pollIntervalMs)
	{
		StopPolling();
		_client?.Dispose();
		_client = null;

		Endpoint = endpoint;
		PollIntervalMs = pollIntervalMs;

		if (MetricNames.Length > 0)
			StartPolling();
	}

	private async Task ConnectToEndpoint()
	{
		try
		{
			_client?.Dispose();
			_client = new PcpClientConnection(new Uri(Endpoint), _sharedHttpClient);
			EmitConnectionState("Connecting");

			await _client.ConnectAsync();
			await InitialiseRateConverter();
			EmitConnectionState("Connected");
		}
		catch (PcpConnectionException ex)
		{
			EmitConnectionState("Failed");
			EmitSignal(SignalName.ErrorOccurred, ex.Message);
		}
	}

	private async Task InitialiseRateConverter()
	{
		if (_client == null || MetricNames.Length == 0)
			return;

		try
		{
			var descriptors = await _client.DescribeMetricsAsync(MetricNames);
			_rateConverter = new MetricRateConverter(descriptors);
			var counterNames = descriptors
				.Where(d => d.Semantics == MetricSemantics.Counter)
				.Select(d => d.Name)
				.ToList();
			if (counterNames.Count > 0)
				GD.Print($"[MetricPoller] Rate-converting counters: {string.Join(", ", counterNames)}");
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[MetricPoller] Could not describe metrics for rate conversion: {ex.Message}");
			_rateConverter = null;
		}
	}

	private void StartPollTimer()
	{
		_pollTimer?.QueueFree();
		_pollTimer = new Godot.Timer();
		_pollTimer.WaitTime = PollIntervalMs / 1000.0;
		_pollTimer.Autostart = true;
		_pollTimer.Timeout += OnPollTimerTimeout;
		AddChild(_pollTimer);
		_polling = true;
	}

	private async void OnPollTimerTimeout()
	{
		if (!_polling || _client == null || MetricNames.Length == 0)
			return;

		try
		{
			var values = await _client.FetchAsync(MetricNames);
			var converted = _rateConverter != null
				? _rateConverter.Convert(values)
				: values;
			if (converted.Count > 0)
			{
				var dict = MarshalMetricValues(converted);
				EmitSignal(SignalName.MetricsUpdated, dict);
			}
		}
		catch (PcpConnectionException ex)
		{
			EmitConnectionState("Reconnecting");
			EmitSignal(SignalName.ErrorOccurred, ex.Message);
			await TryReconnect();
		}
		catch (PcpContextExpiredException)
		{
			EmitConnectionState("Failed");
		}
		catch (Exception ex)
		{
			EmitSignal(SignalName.ErrorOccurred, $"Fetch error: {ex.Message}");
		}
	}

	private async Task TryReconnect()
	{
		try
		{
			await ConnectToEndpoint();
		}
		catch
		{
			EmitConnectionState("Failed");
		}
	}

	/// <summary>
	/// Marshals C# MetricValue list into a GDScript-friendly Dictionary.
	/// Singular metrics use key -1 (matching PCP wire protocol convention).
	/// </summary>
	private static Godot.Collections.Dictionary MarshalMetricValues(
		IReadOnlyList<MetricValue> values)
	{
		var dict = new Godot.Collections.Dictionary();

		foreach (var metric in values)
		{
			var instances = new Godot.Collections.Dictionary();
			foreach (var iv in metric.InstanceValues)
			{
				var key = iv.InstanceId ?? -1;
				instances[key] = iv.Value;
			}

			var metricDict = new Godot.Collections.Dictionary
			{
				["timestamp"] = metric.Timestamp,
				["instances"] = instances
			};

			dict[metric.Name] = metricDict;
		}

		return dict;
	}

	private void EmitConnectionState(string state)
	{
		EmitSignal(SignalName.ConnectionStateChanged, state);
	}

	public override void _ExitTree()
	{
		StopPolling();
		_client?.Dispose();
		_client = null;
		_sharedHttpClient.Dispose();
	}
}
