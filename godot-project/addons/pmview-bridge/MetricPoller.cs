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

	[Signal]
	public delegate void PlaybackPositionChangedEventHandler(string position, string mode);

	private readonly System.Net.Http.HttpClient _sharedHttpClient = new();
	private PcpClientConnection? _client;
	private MetricRateConverter? _rateConverter;
	private Godot.Timer? _pollTimer;
	private bool _polling;
	private readonly TimeCursor _timeCursor = new();
	private DateTime _lastPollTime = DateTime.UtcNow;
	private double _archiveSamplingIntervalSeconds;
	private readonly Dictionary<string, double> _lastEmittedTimestamp = new();
	private readonly Dictionary<string, Dictionary<string, int>> _seriesInstanceMap = new();
	private Godot.Collections.Dictionary? _lastEmittedMetrics;

	public ConnectionState CurrentState => _client?.State ?? ConnectionState.Disconnected;
	internal PcpClientConnection? Client => _client;

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

	public TimeCursor TimeCursor => _timeCursor;

	public async void StartPlayback(string startTimeIso)
	{
		if (DateTime.TryParse(startTimeIso, null,
			System.Globalization.DateTimeStyles.AdjustToUniversal, out var startTime))
		{
			_lastEmittedTimestamp.Clear();
			_seriesInstanceMap.Clear();
			await DiscoverArchiveMetadata();
			_timeCursor.StartPlayback(startTime);
			EmitSignal(SignalName.PlaybackPositionChanged,
				startTime.ToString("o"), "Playback");
		}
		else
		{
			EmitSignal(SignalName.ErrorOccurred,
				$"Invalid start time format: {startTimeIso}");
		}
	}

	public void PausePlayback()
	{
		try
		{
			_timeCursor.Pause();
			EmitSignal(SignalName.PlaybackPositionChanged,
				_timeCursor.Position.ToString("o"), "Paused");
		}
		catch (InvalidOperationException ex)
		{
			EmitSignal(SignalName.ErrorOccurred, ex.Message);
		}
	}

	public void ResumePlayback()
	{
		try
		{
			_timeCursor.Resume();
			EmitSignal(SignalName.PlaybackPositionChanged,
				_timeCursor.Position.ToString("o"), "Playback");
		}
		catch (InvalidOperationException ex)
		{
			EmitSignal(SignalName.ErrorOccurred, ex.Message);
		}
	}

	public void ResetToLive()
	{
		_timeCursor.ResetToLive();
		_lastEmittedTimestamp.Clear();
		EmitSignal(SignalName.PlaybackPositionChanged, "", "Live");
	}

	public void SetPlaybackSpeed(double speed)
	{
		_timeCursor.PlaybackSpeed = speed;
	}

	public void SetLoop(bool loop)
	{
		_timeCursor.Loop = loop;
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

		// Advance cursor position in playback mode
		var now = DateTime.UtcNow;
		var elapsed = now - _lastPollTime;
		_lastPollTime = now;

		if (_timeCursor.Mode == CursorMode.Playback)
		{
			_timeCursor.AdvanceBy(elapsed);
			EmitSignal(SignalName.PlaybackPositionChanged,
				_timeCursor.Position.ToString("o"), "Playback");
		}

		// Paused mode — don't fetch new data
		if (_timeCursor.Mode == CursorMode.Paused)
			return;

		try
		{
			if (_timeCursor.Mode == CursorMode.Live)
			{
				// Live mode — fetch current values via /pmapi/fetch
				await FetchLiveMetrics();
			}
			else
			{
				// Playback mode — query historical values via /series/*
				await FetchHistoricalMetrics();
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

	/// <summary>
	/// Re-emits the last known metric values without fetching.
	/// Call after scene switch so new bindings get values immediately.
	/// </summary>
	public void ReplayLastMetrics()
	{
		_lastEmittedTimestamp.Clear();

		if (_lastEmittedMetrics != null && _lastEmittedMetrics.Count > 0)
		{
			GD.Print($"[MetricPoller] Replaying {_lastEmittedMetrics.Count} cached metrics for new scene");
			EmitSignal(SignalName.MetricsUpdated, _lastEmittedMetrics);
		}
	}

	private async Task FetchLiveMetrics()
	{
		var values = await _client!.FetchAsync(MetricNames);
		var converted = _rateConverter != null
			? _rateConverter.Convert(values)
			: values;
		if (converted.Count > 0)
		{
			var dict = MarshalMetricValues(converted);
			_lastEmittedMetrics = dict;
			EmitSignal(SignalName.MetricsUpdated, dict);
		}
	}

	/// <summary>
	/// Queries a sample of series values to infer the archive sampling
	/// interval and time bounds. Logs all discovered values for diagnostics.
	/// </summary>
	private async Task DiscoverArchiveMetadata()
	{
		var endpointUri = new Uri(Endpoint);
		_archiveSamplingIntervalSeconds = ArchiveDiscovery.DefaultSamplingIntervalSeconds;

		if (MetricNames.Length == 0)
			return;

		try
		{
			var probeMetric = MetricNames[0];
			var queryUrl = PcpSeriesQuery.BuildQueryUrl(endpointUri, probeMetric);
			var queryResponse = await _sharedHttpClient.GetAsync(queryUrl);
			if (!queryResponse.IsSuccessStatusCode)
				return;

			var queryJson = await queryResponse.Content.ReadAsStringAsync();
			var seriesIds = PcpSeriesQuery.ParseQueryResponse(queryJson);
			if (seriesIds.Count == 0)
				return;

			// Fetch a small sample to infer interval
			var sampleUrl = PcpSeriesQuery.BuildValuesUrl(endpointUri, seriesIds);
			var sampleUrlWithCount = new Uri($"{sampleUrl}&count=10");
			var sampleResponse = await _sharedHttpClient.GetAsync(sampleUrlWithCount);
			if (!sampleResponse.IsSuccessStatusCode)
				return;

			var sampleJson = await sampleResponse.Content.ReadAsStringAsync();
			var sampleValues = PcpSeriesQuery.ParseValuesResponse(sampleJson);

			if (sampleValues.Count > 0)
			{
				var timestamps = sampleValues.Select(v => v.Timestamp).ToArray();
				_archiveSamplingIntervalSeconds =
					ArchiveDiscovery.InferSamplingIntervalSeconds(timestamps);

				var bounds = ArchiveDiscovery.DetectTimeBounds(sampleValues);
				if (bounds.HasValue)
				{
					_timeCursor.EndBound = bounds.Value.End;
					GD.Print($"[MetricPoller] Archive discovery: " +
						$"sampling interval = {_archiveSamplingIntervalSeconds:F1}s, " +
						$"sample bounds = {bounds.Value.Start:o} to {bounds.Value.End:o}");
				}
			}

			GD.Print($"[MetricPoller] Archive query window: " +
				$"{_archiveSamplingIntervalSeconds * 1.5:F1}s " +
				$"(1.5x detected {_archiveSamplingIntervalSeconds:F1}s interval)");
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[MetricPoller] Archive discovery failed, " +
				$"using default {_archiveSamplingIntervalSeconds}s: {ex.Message}");
		}
	}

	private async Task FetchHistoricalMetrics()
	{
		var dict = new Godot.Collections.Dictionary();
		var endpointUri = new Uri(Endpoint);
		var cursorPosition = _timeCursor.Position;

		GD.Print($"[MetricPoller] Historical fetch at cursor: {cursorPosition:o}");

		foreach (var metricName in MetricNames)
		{
			try
			{
				var queryUrl = PcpSeriesQuery.BuildQueryUrl(
					endpointUri, metricName);

				var queryResponse = await _sharedHttpClient.GetAsync(queryUrl);
				if (!queryResponse.IsSuccessStatusCode)
				{
					var errorBody = await queryResponse.Content.ReadAsStringAsync();
					GD.PrintErr($"[MetricPoller] Series query failed ({queryResponse.StatusCode}): {errorBody}");
					continue;
				}

				var queryJson = await queryResponse.Content.ReadAsStringAsync();
				var seriesIds = PcpSeriesQuery.ParseQueryResponse(queryJson);

				if (seriesIds.Count == 0)
					continue;

				// Resolve series IDs to PCP instance IDs (cached per metric)
				await EnsureSeriesInstanceMapping(endpointUri, metricName, seriesIds);

				// Counters need 2 samples for rate conversion — widen the window
				var isCounter = _rateConverter?.IsCounter(metricName) == true;
				var windowSeconds = isCounter
					? _archiveSamplingIntervalSeconds * 2.5
					: _archiveSamplingIntervalSeconds * 1.5;

				var valuesUrl = PcpSeriesQuery.BuildValuesUrlWithTimeWindow(
					endpointUri, seriesIds, cursorPosition,
					windowSeconds: windowSeconds);

				var valuesResponse = await _sharedHttpClient.GetAsync(valuesUrl);
				if (!valuesResponse.IsSuccessStatusCode)
				{
					var errorBody = await valuesResponse.Content.ReadAsStringAsync();
					GD.PrintErr($"[MetricPoller] Values query failed ({valuesResponse.StatusCode}): {errorBody}");
					continue;
				}

				var valuesJson = await valuesResponse.Content.ReadAsStringAsync();
				var seriesValues = PcpSeriesQuery.ParseValuesResponse(valuesJson);

				if (seriesValues.Count == 0)
				{
					GD.Print($"[MetricPoller] No values in window for {metricName} " +
						$"(window: {windowSeconds:F1}s before {cursorPosition:o})");
					continue;
				}

				// Log raw samples received per series
				var bySeries = seriesValues.GroupBy(v => v.SeriesId);
				foreach (var group in bySeries)
				{
					var seriesLabel = group.Key[..8];
					var vals = group.OrderBy(v => v.Timestamp).ToList();
					var valStrs = vals.Select(v =>
						$"{v.NumericValue:F2}@{DateTimeOffset.FromUnixTimeMilliseconds((long)v.Timestamp).UtcDateTime:HH:mm:ss}");
					GD.Print($"[MetricPoller] {metricName} series {seriesLabel}: " +
						$"{vals.Count} samples [{string.Join(", ", valStrs)}]");
				}

				// Sample-and-hold: skip if we already emitted this timestamp
				// Timestamps are epoch milliseconds — 1000ms = 1 second tolerance
				var latestTimestamp = seriesValues.Max(v => v.Timestamp);
				if (_lastEmittedTimestamp.TryGetValue(metricName, out var lastTs)
					&& Math.Abs(latestTimestamp - lastTs) < 1000.0)
				{
					continue;  // same data point, scene holds current state
				}
				_lastEmittedTimestamp[metricName] = latestTimestamp;

				GD.Print($"[MetricPoller] {metricName}: NEW sample detected " +
					$"(ts={latestTimestamp:F3}, prev={lastTs:F3})");

				// Counters: compute per-second rates from consecutive samples
				// Instant/discrete: take latest timestamp's values directly
				IReadOnlyList<SeriesValue> resolvedValues;
				if (isCounter)
				{
					resolvedValues = PcpSeriesQuery.ComputeRatesFromSeriesValues(seriesValues);
					foreach (var rv in resolvedValues)
					{
						var seriesLabel = rv.SeriesId[..8];
						GD.Print($"[MetricPoller] {metricName} rate: series {seriesLabel} " +
							$"= {rv.NumericValue:F4}/s");
					}
					if (resolvedValues.Count == 0)
						GD.Print($"[MetricPoller] {metricName}: rate computation returned 0 values " +
							$"(need >=2 samples per series, got {seriesValues.Count} total)");
				}
				else
				{
					resolvedValues = seriesValues
						.Where(v => Math.Abs(v.Timestamp - latestTimestamp) < 1.0
							&& !v.IsString)
						.ToList();
				}

				var instanceMap = _seriesInstanceMap.GetValueOrDefault(metricName);
				var instances = new Godot.Collections.Dictionary();
				foreach (var sv in resolvedValues)
				{
					// Map series ID back to PCP instance ID for binding lookup
					int instanceKey = instanceMap != null
						&& instanceMap.TryGetValue(sv.SeriesId, out var pcpId)
						? pcpId
						: -1;  // singular metric fallback
					instances[instanceKey] = sv.NumericValue;
					GD.Print($"[MetricPoller] {metricName}: instance {instanceKey} " +
						$"= {sv.NumericValue:F4}");
				}

				if (instances.Count > 0)
				{
					dict[metricName] = new Godot.Collections.Dictionary
					{
						["timestamp"] = cursorPosition
							.Subtract(DateTime.UnixEpoch).TotalSeconds,
						["instances"] = instances
					};
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr(
					$"[MetricPoller] Series query error for {metricName}: {ex.Message}");
			}
		}

		if (dict.Count > 0)
		{
			GD.Print($"[MetricPoller] Historical update: {dict.Count} metrics with new data");
			_lastEmittedMetrics = dict;
			EmitSignal(SignalName.MetricsUpdated, dict);
		}
	}

	/// <summary>
	/// Fetches and caches the series-ID-to-PCP-instance-ID mapping for a metric.
	/// Only makes the HTTP call on the first invocation per metric per playback session.
	/// </summary>
	private async Task EnsureSeriesInstanceMapping(Uri endpointUri, string metricName,
		IReadOnlyList<string> seriesIds)
	{
		if (_seriesInstanceMap.ContainsKey(metricName))
			return;

		try
		{
			var instancesUrl = PcpSeriesQuery.BuildInstancesUrl(endpointUri, seriesIds);
			var response = await _sharedHttpClient.GetAsync(instancesUrl);

			if (!response.IsSuccessStatusCode)
			{
				GD.PushWarning($"[MetricPoller] /series/instances failed for {metricName} " +
					$"({response.StatusCode}) — falling back to singular mapping");
				_seriesInstanceMap[metricName] = new Dictionary<string, int>();
				return;
			}

			var json = await response.Content.ReadAsStringAsync();
			var mapping = PcpSeriesQuery.ParseInstancesResponse(json);

			_seriesInstanceMap[metricName] = mapping;

			if (mapping.Count > 0)
			{
				var pairs = mapping.Select(kv => $"{kv.Key[..8]}..→{kv.Value}");
				GD.Print($"[MetricPoller] Instance mapping for {metricName}: " +
					$"{string.Join(", ", pairs)}");
			}
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[MetricPoller] Instance mapping failed for {metricName}: " +
				$"{ex.Message}");
			_seriesInstanceMap[metricName] = new Dictionary<string, int>();
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
