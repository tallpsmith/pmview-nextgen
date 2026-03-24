using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.Logging;
using PcpClient;
using PmviewApp;

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
		string hostname, Godot.Collections.Dictionary metrics);

	[Signal]
	public delegate void ConnectionStateChangedEventHandler(string state);

	[Signal]
	public delegate void ErrorOccurredEventHandler(string message);

	[Export] public string Endpoint { get; set; } = "http://localhost:44322";
	[Export] public int PollIntervalMs { get; set; } = 1000;
	[Export] public string[] MetricNames { get; set; } = [];
	[Export] public string Hostname { get; set; } = "";

	[Signal]
	public delegate void PlaybackPositionChangedEventHandler(string position, string mode);

	[Signal]
	public delegate void ShardPollCompletedEventHandler();

	private ILogger? _log;
	private ILogger Log => _log ??= PmviewLogger.GetLogger("MetricPoller");

	private readonly System.Net.Http.HttpClient _sharedHttpClient = new();
	private IPcpClient? _client;
	private MetricRateConverter? _rateConverter;
	private Godot.Timer? _pollTimer;
	private bool _polling;
	private readonly TimeCursor _timeCursor = new();
	private DateTime _lastPollTime = DateTime.UtcNow;
	private double _archiveSamplingIntervalSeconds;
	private readonly Dictionary<string, double> _lastEmittedTimestamp = new();
	private readonly Dictionary<string, Dictionary<string, SeriesInstanceInfo>> _seriesInstanceMap = new();
	private readonly Dictionary<string, Dictionary<string, int>> _liveInstanceNames = new();
	private readonly HashSet<string> _liveInstanceNamesPopulated = new();
	private Godot.Collections.Dictionary? _lastEmittedMetrics;
	private bool _skipNextAdvance;

	public ConnectionState CurrentState => _client?.State ?? ConnectionState.Disconnected;
	internal IPcpClient? Client => _client;

	/// <summary>
	/// Factory method for creating PCP client instances.
	/// Override in tests to inject a mock client.
	/// </summary>
	protected virtual IPcpClient CreateClient(Uri endpoint, System.Net.Http.HttpClient http)
	{
		return new PcpClientConnection(endpoint, http);
	}

	public override void _Ready()
	{
		if (MetricNames.Length > 0)
			CallDeferred(nameof(StartPolling));
	}

	private bool _startPollingCalled;

	public async void StartPolling()
	{
		if (_startPollingCalled)
			return;
		_startPollingCalled = true;

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

	private IReadOnlyDictionary<string, string>? _cachedSeriesIdToHostname;
	private IReadOnlyDictionary<string, IReadOnlyList<string>>? _cachedSeriesIdsPerMetric;

	/// <summary>
	/// Whether this poller has a pre-cached series map from FleetMetricPoller
	/// discovery. When true, uses FetchSeriesMetricsForHosts instead of the
	/// standard live/historical fetch paths.
	/// </summary>
	public bool HasCachedSeriesMap => _cachedSeriesIdToHostname != null;

	/// <summary>
	/// Initialise with a pre-resolved series map from centralised discovery.
	/// Caller must pass independent immutable copies — no shared state between shards.
	/// Skips per-shard discovery; shard goes straight to polling with cached data.
	/// </summary>
	public void InitialiseWithCachedSeriesMap(
		IReadOnlyDictionary<string, string> seriesIdToHostname,
		IReadOnlyDictionary<string, IReadOnlyList<string>> seriesIdsPerMetric)
	{
		_cachedSeriesIdToHostname = seriesIdToHostname;
		_cachedSeriesIdsPerMetric = seriesIdsPerMetric;
	}

	public TimeCursor TimeCursor => _timeCursor;

	public async void StartPlayback(string startTimeIso)
	{
		GD.Print($"[MetricPoller] StartPlayback called: '{startTimeIso}'");
		if (DateTime.TryParse(startTimeIso, null,
			DateTimeStyles.AdjustToUniversal, out var startTime))
		{
			GD.Print($"[MetricPoller] Parsed start time: {startTime:o}");
			_lastEmittedTimestamp.Clear();
			_seriesInstanceMap.Clear();
			_liveInstanceNames.Clear();
			_liveInstanceNamesPopulated.Clear();
			await DiscoverArchiveMetadata();
			GD.Print($"[MetricPoller] Archive metadata discovered, starting playback at {startTime:o}");
			_timeCursor.StartPlayback(startTime);
			EmitSignal(SignalName.PlaybackPositionChanged,
				startTime.ToString("o"), "Playback");
		}
		else
		{
			GD.Print($"[MetricPoller] FAILED to parse start time: '{startTimeIso}'");
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
			// Reset poll time so AdvanceBy doesn't jump by the time
			// spent paused (e.g. in the TimeControl panel)
			_lastPollTime = DateTime.UtcNow;
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
		_seriesInstanceMap.Clear();
		_liveInstanceNames.Clear();
		_liveInstanceNamesPopulated.Clear();
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

	/// <summary>
	/// Returns true if currently in Playback mode (not paused, not live).
	/// GDScript-callable — TimeCursor is not visible to GDScript.
	/// </summary>
	public bool IsPlaying()
	{
		return _timeCursor.Mode == CursorMode.Playback;
	}

	/// <summary>
	/// Toggles between Playback and Paused modes.
	/// No-op in Live mode. GDScript-callable.
	/// </summary>
	public void TogglePlayback()
	{
		if (_timeCursor.Mode == CursorMode.Playback)
			PausePlayback();
		else if (_timeCursor.Mode == CursorMode.Paused)
			ResumePlayback();
	}

	/// <summary>
	/// Steps the playback position by a fixed interval.
	/// Direction: +1 = forward, -1 = backward.
	/// Pauses playback if currently playing, then fetches data at new position.
	/// </summary>
	public async void StepPlayback(double intervalSeconds, int direction)
	{
		if (_timeCursor.Mode == CursorMode.Live)
			return;

		// Step without pausing — playback continues from the new position.
		// If paused, stay paused but update the position.
		_timeCursor.StepByInterval(intervalSeconds, direction);
		_lastEmittedTimestamp.Clear();
		_skipNextAdvance = true;
		EmitSignal(SignalName.PlaybackPositionChanged,
			_timeCursor.Position.ToString("o"), "Paused");

		try
		{
			await FetchHistoricalMetrics();
		}
		catch (Exception ex)
		{
			EmitSignal(SignalName.ErrorOccurred, $"Step fetch error: {ex.Message}");
		}
	}

	/// <summary>
	/// Jumps the playback position to a specific timestamp.
	/// Pauses playback if currently playing, then fetches data at new position.
	/// </summary>
	public async void JumpToTimestamp(string isoTimestamp)
	{
		if (DateTime.TryParse(isoTimestamp, null,
			DateTimeStyles.AdjustToUniversal, out var target))
		{
			if (_timeCursor.Mode == CursorMode.Live)
				return;

			if (_timeCursor.Mode == CursorMode.Playback)
				_timeCursor.Pause();

			_timeCursor.JumpTo(target);
			_lastEmittedTimestamp.Clear();
			_skipNextAdvance = true;
			EmitSignal(SignalName.PlaybackPositionChanged,
				target.ToString("o"), "Paused");

			try
			{
				await FetchHistoricalMetrics();
			}
			catch (Exception ex)
			{
				EmitSignal(SignalName.ErrorOccurred, $"Jump fetch error: {ex.Message}");
			}
		}
		else
		{
			EmitSignal(SignalName.ErrorOccurred,
				$"Invalid timestamp format: {isoTimestamp}");
		}
	}

	/// <summary>
	/// Sets the IN/OUT range for loop playback.
	/// Resumes playback if currently paused.
	/// </summary>
	public void SetInOutRange(string inPointIso, string outPointIso)
	{
		if (DateTime.TryParse(inPointIso, null,
				DateTimeStyles.AdjustToUniversal, out var inPoint)
			&& DateTime.TryParse(outPointIso, null,
				DateTimeStyles.AdjustToUniversal, out var outPoint))
		{
			_timeCursor.SetInOutRange(inPoint, outPoint);

			// If playhead is outside the new range, jump to IN point
			if (_timeCursor.Position < inPoint || _timeCursor.Position > outPoint)
			{
				_timeCursor.JumpTo(inPoint);
				_lastEmittedTimestamp.Clear();
				_skipNextAdvance = true;
			}

			if (_timeCursor.Mode == CursorMode.Paused)
				_timeCursor.Resume();

			EmitSignal(SignalName.PlaybackPositionChanged,
				_timeCursor.Position.ToString("o"), "Playback");
		}
		else
		{
			EmitSignal(SignalName.ErrorOccurred,
				$"Invalid range format: {inPointIso} → {outPointIso}");
		}
	}

	/// <summary>
	/// Clears the IN/OUT range and resumes playback.
	/// </summary>
	public void ClearRange()
	{
		_timeCursor.ClearInOutRange();

		if (_timeCursor.Mode == CursorMode.Paused)
			_timeCursor.Resume();

		EmitSignal(SignalName.PlaybackPositionChanged,
			_timeCursor.Position.ToString("o"), "Playback");
	}

	public void UpdateEndpoint(string endpoint, int pollIntervalMs)
	{
		StopPolling();
		_liveInstanceNames.Clear();
		_liveInstanceNamesPopulated.Clear();
		_seriesInstanceMap.Clear();
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
			_client = CreateClient(new Uri(Endpoint), _sharedHttpClient);
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
			var realMetrics = MetricNames
				.Where(m => !m.StartsWith("pmview.meta.", StringComparison.Ordinal))
				.ToArray();

			// Describe metrics individually — some may not exist on the live pmcd
			// (archive metrics can differ from the local host's metrics).
			var descriptors = new List<MetricDescriptor>();
			foreach (var metric in realMetrics)
			{
				try
				{
					var descs = await _client.DescribeMetricsAsync(new[] { metric });
					descriptors.AddRange(descs);
				}
				catch
				{
					// Metric not available on live pmcd — skip silently
				}
			}

			_rateConverter = new MetricRateConverter(descriptors);
			var counterNames = descriptors
				.Where(d => d.Semantics == MetricSemantics.Counter)
				.Select(d => d.Name)
				.ToList();
			if (counterNames.Count > 0)
				Log.LogInformation("Rate-converting counters: {Counters}", string.Join(", ", counterNames));
		}
		catch (Exception ex)
		{
			Log.LogWarning("Could not describe metrics for rate conversion: {Message}", ex.Message);
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
			if (_skipNextAdvance)
			{
				// After a JumpTo, fetch at the current position without advancing
				_skipNextAdvance = false;
			}
			else
			{
				// In archive playback, advance by the archive sampling interval
				// so each poll tick lands on the next sample.
				var advanceAmount = _archiveSamplingIntervalSeconds > 0
					? TimeSpan.FromSeconds(_archiveSamplingIntervalSeconds)
					: elapsed;
				_timeCursor.AdvanceBy(advanceAmount);
			}
			EmitSignal(SignalName.PlaybackPositionChanged,
				_timeCursor.Position.ToString("o"), "Playback");
		}

		// Paused mode — don't fetch new data
		if (_timeCursor.Mode == CursorMode.Paused)
			return;

		var scrapeWatch = System.Diagnostics.Stopwatch.StartNew();
		try
		{
			if (HasCachedSeriesMap)
			{
				Log.LogWarning("[Fleet] Shard {Name}: using FetchSeriesMetricsForHosts ({MetricCount} metrics, {SeriesCount} series IDs)",
					Name, _cachedSeriesIdsPerMetric?.Count ?? 0, _cachedSeriesIdToHostname?.Count ?? 0);
				await FetchSeriesMetricsForHosts();
			}
			else if (_timeCursor.Mode == CursorMode.Live)
			{
				Log.LogWarning("[Fleet] Shard {Name}: NO cached series map — falling back to FetchLiveMetrics", Name);
				await FetchLiveMetrics();
			}
			else
			{
				// Playback mode — query historical values via /series/*
				await FetchHistoricalMetrics();
			}

			scrapeWatch.Stop();
			var scrapeMs = scrapeWatch.ElapsedMilliseconds;
			var spareMs = PollIntervalMs - scrapeMs;
			if (spareMs < 0)
				Log.LogWarning("Poll scrape took {ScrapeMs}ms, {OverrunMs}ms over budget (interval={IntervalMs}ms)",
					scrapeMs, -spareMs, PollIntervalMs);
			else
				Log.LogDebug("Poll scrape took {ScrapeMs}ms, {SpareMs}ms spare (interval={IntervalMs}ms)",
					scrapeMs, spareMs, PollIntervalMs);
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
			Log.LogInformation("Replaying {Count} cached metrics for new scene", _lastEmittedMetrics.Count);
			var dict = _lastEmittedMetrics.Duplicate(true);   // deep copy — inner dicts are independent
			InjectVirtualMetrics(dict);
			EmitSignal(SignalName.MetricsUpdated, Hostname ?? "", dict);
		}
	}

	private async Task FetchLiveMetrics()
	{
		var realMetrics = MetricNames
			.Where(m => !m.StartsWith("pmview.meta.", StringComparison.Ordinal))
			.ToArray();

		var values = await _client!.FetchAsync(realMetrics);
		var converted = _rateConverter != null
			? _rateConverter.Convert(values)
			: values;

		foreach (var metricName in realMetrics)
		{
			if (_liveInstanceNamesPopulated.Contains(metricName))
				continue;
			_liveInstanceNamesPopulated.Add(metricName);
			try
			{
				var indom = await _client!.GetInstanceDomainAsync(metricName);
				if (indom != null)
				{
					var nameMap = new Dictionary<string, int>();
					foreach (var inst in indom.Instances)
						nameMap[inst.Name] = inst.Id;
					_liveInstanceNames[metricName] = nameMap;
					Log.LogInformation("Live instance names for {Metric}: {Names}",
						metricName,
						string.Join(", ", nameMap.Select(kv => $"{kv.Key}→{kv.Value}")));
				}
			}
			catch (Exception ex)
			{
				Log.LogWarning("Instance domain lookup failed for {Metric}: {Message}", metricName, ex.Message);
			}
		}

		if (converted.Count > 0)
		{
			var dict = MarshalMetricValues(converted);
			InjectVirtualMetrics(dict);
			_lastEmittedMetrics = dict;
			EmitSignal(SignalName.MetricsUpdated, Hostname ?? "", dict);
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
					Log.LogInformation(
						"Archive discovery: sampling interval = {Interval:F1}s, sample bounds = {Start:o} to {End:o}",
						_archiveSamplingIntervalSeconds,
						bounds.Value.Start,
						bounds.Value.End);
				}
			}

			Log.LogInformation(
				"Archive query window: {Window:F1}s (1.5x detected {Interval:F1}s interval)",
				_archiveSamplingIntervalSeconds * 1.5,
				_archiveSamplingIntervalSeconds);
		}
		catch (Exception ex)
		{
			Log.LogWarning(
				"Archive discovery failed, using default {Default}s: {Message}",
				_archiveSamplingIntervalSeconds,
				ex.Message);
		}
	}

	private async Task FetchHistoricalMetrics()
	{
		var dict = new Godot.Collections.Dictionary();
		var endpointUri = new Uri(Endpoint);
		var cursorPosition = _timeCursor.Position;
		var realMetrics = MetricNames
			.Where(m => !m.StartsWith("pmview.meta.", StringComparison.Ordinal))
			.ToArray();

		Log.LogInformation("Historical fetch at cursor: {Position:o}", cursorPosition);

		foreach (var metricName in realMetrics)
		{
			try
			{
				var queryUrl = PcpSeriesQuery.BuildQueryUrl(
					endpointUri, metricName);

				var queryResponse = await _sharedHttpClient.GetAsync(queryUrl);
				if (!queryResponse.IsSuccessStatusCode)
				{
					var errorBody = await queryResponse.Content.ReadAsStringAsync();
					Log.LogError("Series query failed ({StatusCode}): {Body}", queryResponse.StatusCode, errorBody);
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
					Log.LogError("Values query failed ({StatusCode}): {Body}", valuesResponse.StatusCode, errorBody);
					continue;
				}

				var valuesJson = await valuesResponse.Content.ReadAsStringAsync();
				var seriesValues = PcpSeriesQuery.ParseValuesResponse(valuesJson);

				if (seriesValues.Count == 0)
				{
					Log.LogInformation(
						"No values in window for {Metric} (window: {Window:F1}s before {Position:o})",
						metricName, windowSeconds, cursorPosition);
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
					Log.LogInformation(
						"{Metric} series {Series}: {Count} samples [{Samples}]",
						metricName, seriesLabel, vals.Count, string.Join(", ", valStrs));
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

				Log.LogInformation(
					"{Metric}: NEW sample detected (ts={Timestamp:F3}, prev={Prev:F3})",
					metricName, latestTimestamp, lastTs);

				// Counters: compute per-second rates from consecutive samples
				// Instant/discrete: take latest timestamp's values directly
				IReadOnlyList<SeriesValue> resolvedValues;
				if (isCounter)
				{
					resolvedValues = PcpSeriesQuery.ComputeRatesFromSeriesValues(seriesValues);
					foreach (var rv in resolvedValues)
					{
						var seriesLabel = rv.SeriesId[..8];
						Log.LogInformation(
							"{Metric} rate: series {Series} = {Rate:F4}/s",
							metricName, seriesLabel, rv.NumericValue);
					}
					if (resolvedValues.Count == 0)
						Log.LogInformation(
							"{Metric}: rate computation returned 0 values (need >=2 samples per series, got {Count} total)",
							metricName, seriesValues.Count);
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
				// Track which instance map entries were actually matched to values,
				// so we only build nameToId from entries with real data (not from
				// other archive sources with different PCP instance ID schemes).
				var matchedInstanceInfos = new List<SeriesInstanceInfo>();
				foreach (var sv in resolvedValues)
				{
					// Instance map is keyed by instance hash (unique per instance).
					// Try instance hash first, fall back to series hash for singular metrics.
					var lookupKey = sv.InstanceId ?? sv.SeriesId;
					SeriesInstanceInfo? matched = null;
					int instanceKey = instanceMap != null
						&& instanceMap.TryGetValue(lookupKey, out matched)
						? matched.PcpInstanceId
						: -1;  // singular metric fallback
					instances[instanceKey] = sv.NumericValue;
					if (matched != null)
						matchedInstanceInfos.Add(matched);
					Log.LogInformation(
						"{Metric}: instance {InstanceKey} = {Value:F4}",
						metricName, instanceKey, sv.NumericValue);
				}

				// Build name→id from only the instance entries that matched actual values.
				var nameToId = new Godot.Collections.Dictionary();
				foreach (var matched in matchedInstanceInfos)
					nameToId[matched.Name] = matched.PcpInstanceId;

				if (instances.Count > 0)
				{
					dict[metricName] = new Godot.Collections.Dictionary
					{
						["timestamp"] = cursorPosition
							.Subtract(DateTime.UnixEpoch).TotalSeconds,
						["instances"] = instances,
						["name_to_id"] = nameToId
					};
				}
			}
			catch (Exception ex)
			{
				Log.LogError("Series query error for {Metric}: {Message}", metricName, ex.Message);
			}
		}

		// Always inject virtual metrics (timestamp, hostname) so the 3D
		// display stays in sync with the cursor position, even when no
		// new sample data is available (sample-and-hold between intervals).
		InjectVirtualMetrics(dict);

		if (dict.Count > 0)
		{
			Log.LogInformation("Historical update: {Count} metrics with new data", dict.Count);
			_lastEmittedMetrics = dict;
			EmitSignal(SignalName.MetricsUpdated, Hostname ?? "", dict);
		}
	}

	/// <summary>
	/// Fetches metric values using the cached series map from centralised discovery.
	/// Queries one /series/values call per metric with all cached series IDs,
	/// partitions results by hostname, and fires MetricsUpdated per host.
	/// Handles counter metrics via rate conversion (needs 2+ samples).
	/// Used for both fleet live mode (near-now window) and fleet playback.
	/// </summary>
	private async Task FetchSeriesMetricsForHosts()
	{
		if (_cachedSeriesIdToHostname == null || _cachedSeriesIdsPerMetric == null)
		{
			GD.Print($"[MetricPoller] FetchSeriesMetricsForHosts: cached map is null — bailing");
			return;
		}

		var endpointUri = new Uri(Endpoint);
		var isPlayback = _timeCursor.Mode == CursorMode.Playback;

		// Collect values per hostname across all metrics
		var hostMetrics = new Dictionary<string, Godot.Collections.Dictionary>();

		foreach (var (metricName, seriesIds) in _cachedSeriesIdsPerMetric)
		{
			try
			{
				var isCounter = _rateConverter?.IsCounter(metricName) == true;

				Uri valuesUrl;
				if (isPlayback)
				{
					// Archive playback — query at cursor position with time window.
					// Counters need 2 samples for rate, so widen the window.
					var windowSeconds = isCounter
						? _archiveSamplingIntervalSeconds * 2.5
						: _archiveSamplingIntervalSeconds * 1.5;
					if (windowSeconds < 1.0) windowSeconds = isCounter ? 120.0 : 60.0;
					valuesUrl = PcpSeriesQuery.BuildValuesUrlWithTimeWindow(
						endpointUri, seriesIds, _timeCursor.Position,
						windowSeconds: windowSeconds);
				}
				else
				{
					// Live mode — get latest values, no time window needed.
					var baseValuesUrl = PcpSeriesQuery.BuildValuesUrl(
						endpointUri, seriesIds);
					valuesUrl = new Uri($"{baseValuesUrl}&count=5");
				}

				var response = await _sharedHttpClient.GetAsync(valuesUrl);
				if (!response.IsSuccessStatusCode)
				{
					Log.LogError("Series values query failed for {Metric}: {StatusCode}",
						metricName, response.StatusCode);
					continue;
				}

				var json = await response.Content.ReadAsStringAsync();
				var seriesValues = PcpSeriesQuery.ParseValuesResponse(json);

				Log.LogWarning(
					"[Fleet] {Metric}: {Count} raw series values from API (isCounter={IsCounter}, mode={Mode})",
					metricName, seriesValues.Count, isCounter, isPlayback ? "playback" : "live");

				if (seriesValues.Count == 0)
					continue;

				// Counters: compute per-second rates from consecutive samples
				// Instant/discrete: take latest timestamp's values directly
				IReadOnlyList<SeriesValue> resolvedValues;
				if (isCounter)
				{
					resolvedValues = PcpSeriesQuery.ComputeRatesFromSeriesValues(
						seriesValues);
					Log.LogWarning(
						"[Fleet] {Metric}: {RawCount} raw → {RateCount} rate values after ComputeRates",
						metricName, seriesValues.Count, resolvedValues.Count);
					if (resolvedValues.Count == 0)
						continue;
				}
				else
				{
					var latestTimestamp = seriesValues.Max(v => v.Timestamp);
					resolvedValues = seriesValues
						.Where(v => Math.Abs(v.Timestamp - latestTimestamp) < 1.0
							&& !v.IsString)
						.ToList();
				}

				// Partition by hostname using cached series_id → hostname map.
				// Sum values per hostname/metric for multi-instance metrics
				// (e.g. network.interface.in.bytes has one series per interface).
				var hostSums = new Dictionary<string, double>();
				var unmappedCount = 0;
				foreach (var sv in resolvedValues)
				{
					var lookupKey = sv.InstanceId ?? sv.SeriesId;
					// Try instance hash first, then series hash
					if (!_cachedSeriesIdToHostname.TryGetValue(lookupKey, out var hostname)
						&& !_cachedSeriesIdToHostname.TryGetValue(sv.SeriesId, out hostname))
					{
						unmappedCount++;
						continue; // Unknown series — skip
					}

					if (hostSums.TryGetValue(hostname, out var existing))
						hostSums[hostname] = existing + sv.NumericValue;
					else
						hostSums[hostname] = sv.NumericValue;
				}

				if (unmappedCount > 0)
					Log.LogWarning(
						"[Fleet] {Metric}: {Unmapped} series values had no hostname mapping (of {Total} resolved)",
						metricName, unmappedCount, resolvedValues.Count);

				// Log per-host sums for this metric
				foreach (var (h, v) in hostSums)
					Log.LogWarning("[Fleet] {Metric}: {Host} = {Value:F4}", metricName, h, v);

				// Build the metric dict entries per hostname
				foreach (var (hostname, summedValue) in hostSums)
				{
					if (!hostMetrics.TryGetValue(hostname, out var hostDict))
					{
						hostDict = new Godot.Collections.Dictionary();
						hostMetrics[hostname] = hostDict;
					}

					// Use the latest timestamp from the actual data
					var dataTimestamp = resolvedValues.Max(v => v.Timestamp);
					hostDict[metricName] = new Godot.Collections.Dictionary
					{
						["timestamp"] = dataTimestamp / 1000.0,
						["instances"] = new Godot.Collections.Dictionary
						{
							[-1] = summedValue
						},
						["name_to_id"] = new Godot.Collections.Dictionary()
					};
				}
			}
			catch (Exception ex)
			{
				Log.LogError("Series fetch error for {Metric}: {Message}",
					metricName, ex.Message);
			}
		}

		// Emit per-host signals — do NOT set _lastEmittedMetrics here;
		// it is a single-host concept and ReplayLastMetrics makes no
		// sense for fleet shards (which serve multiple hosts).
		foreach (var (hostname, metrics) in hostMetrics)
		{
			InjectVirtualMetricsForHost(metrics, hostname);
			EmitSignal(SignalName.MetricsUpdated, hostname, metrics);
		}

		EmitSignal(SignalName.ShardPollCompleted);
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
				Log.LogWarning(
					"/series/instances failed for {Metric} ({StatusCode}) — falling back to singular mapping",
					metricName, response.StatusCode);
				_seriesInstanceMap[metricName] = new Dictionary<string, SeriesInstanceInfo>();
				return;
			}

			var json = await response.Content.ReadAsStringAsync();
			var mapping = PcpSeriesQuery.ParseInstancesResponse(json);

			_seriesInstanceMap[metricName] = mapping;

			if (mapping.Count > 0)
			{
				var pairs = mapping.Select(kv => $"{kv.Key[..8]}..→{kv.Value.PcpInstanceId} ({kv.Value.Name})");
				Log.LogInformation("Instance mapping for {Metric}: {Mapping}", metricName, string.Join(", ", pairs));
			}
		}
		catch (Exception ex)
		{
			Log.LogWarning("Instance mapping failed for {Metric}: {Message}", metricName, ex.Message);
			_seriesInstanceMap[metricName] = new Dictionary<string, SeriesInstanceInfo>();
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
	/// Includes name_to_id mapping for instance name resolution in SceneBinder.
	/// </summary>
	private Godot.Collections.Dictionary MarshalMetricValues(
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

			var nameToId = new Godot.Collections.Dictionary();
			if (_liveInstanceNames.TryGetValue(metric.Name, out var nameMap))
			{
				foreach (var kv in nameMap)
					nameToId[kv.Key] = kv.Value;
			}

			var metricDict = new Godot.Collections.Dictionary
			{
				["timestamp"] = metric.Timestamp,
				["instances"] = instances,
				["name_to_id"] = nameToId
			};

			dict[metric.Name] = metricDict;
		}

		return dict;
	}

	/// <summary>
	/// Adds synthetic pmview.meta.* keys to the outgoing metric dictionary.
	/// These are derived from local poller state, not fetched from pmproxy.
	/// Delegates to InjectVirtualMetricsForHost using the Hostname property.
	/// </summary>
	internal void InjectVirtualMetrics(Godot.Collections.Dictionary dict)
	{
		InjectVirtualMetricsForHost(dict, Hostname);
	}

	/// <summary>
	/// Variant of InjectVirtualMetrics that takes an explicit hostname
	/// parameter instead of reading the Hostname property. Used by the fleet
	/// series path where each signal carries a different host's data.
	/// </summary>
	internal void InjectVirtualMetricsForHost(
		Godot.Collections.Dictionary dict, string hostname)
	{
		var now = _timeCursor.Mode == CursorMode.Playback
			? _timeCursor.Position
			: DateTime.UtcNow;

		dict["pmview.meta.timestamp"] = new Godot.Collections.Dictionary
		{
			["text_value"] = now.ToString("yyyy-MM-dd · HH:mm:ss")
		};

		if (!string.IsNullOrEmpty(hostname))
		{
			dict["pmview.meta.hostname"] = new Godot.Collections.Dictionary
			{
				["text_value"] = hostname
			};
		}

		dict["pmview.meta.endpoint"] = new Godot.Collections.Dictionary
		{
			["text_value"] = Endpoint
		};
	}

	private void EmitConnectionState(string state)
	{
		EmitSignal(SignalName.ConnectionStateChanged, state);
	}

	public override void _ExitTree()
	{
		StopPolling();
		// Allow re-start if node re-enters a tree (e.g. fleet → HostView handoff).
		// Without this, StartPolling() returns early on re-entry because the
		// guard flag is still set from the previous tree.
		_startPollingCalled = false;
	}
}
