using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PcpClient;
using PmviewProjectionCore.Discovery;
using PmviewProjectionCore.Layout;
using PmviewProjectionCore.Models;
using PmviewProjectionCore.Profiles;

namespace PmviewApp;

/// <summary>
/// Runs the async discovery → layout → build pipeline and emits signals
/// for GDScript to react to as each phase completes.
/// </summary>
public partial class LoadingPipeline : Node
{
	[Signal]
	public delegate void PhaseCompletedEventHandler(int phaseIndex, string phaseName);

	[Signal]
	public delegate void PipelineCompletedEventHandler();

	[Signal]
	public delegate void PipelineErrorEventHandler(int phaseIndex, string error);

	/// <summary>The fully-built scene graph, available after phase 5 completes.</summary>
	public Node3D? BuiltScene { get; private set; }

	/// <summary>
	/// Minimum milliseconds between phase completion signals.
	/// Configurable from the editor or GDScript to let the loading
	/// animation breathe. Set to 0 for no artificial delay.
	/// </summary>
	[Export] public int MinPhaseDelayMs { get; set; } = 500;

	public async void StartPipeline(string endpoint, string mode = "live",
		string hostname = "", string startTime = "")
	{
		var currentPhase = 0;
		PcpClientConnection? client = null;

		try
		{
			// Phase 0: CONNECTING
			currentPhase = 0;
			var phaseStart = DateTime.UtcNow;
			client = new PcpClientConnection(new Uri(endpoint));
			await client.ConnectAsync();
			await EnforceMinPhaseDelay(phaseStart);
			EmitSignal(SignalName.PhaseCompleted, 0, "CONNECTING");

			// Phase 1: TOPOLOGY
			// Both live and archive modes use the same /pmapi discovery for now.
			// pmproxy exposes the same endpoints for archived hosts. The critical
			// live/archive difference is in MetricPoller playback, not topology.
			currentPhase = 1;
			phaseStart = DateTime.UtcNow;
			var topology = await MetricDiscovery.DiscoverAsync(client);
			await EnforceMinPhaseDelay(phaseStart);
			EmitSignal(SignalName.PhaseCompleted, 1, "TOPOLOGY");

			// Phase 2: INSTANCES (already resolved as part of discovery)
			currentPhase = 2;
			await EnforceMinPhaseDelay(DateTime.UtcNow);
			EmitSignal(SignalName.PhaseCompleted, 2, "INSTANCES");

			// Phase 3: PROFILE
			currentPhase = 3;
			phaseStart = DateTime.UtcNow;
			var profileProvider = new HostProfileProvider();
			var zones = profileProvider.GetProfile(topology.Os);
			await EnforceMinPhaseDelay(phaseStart);
			EmitSignal(SignalName.PhaseCompleted, 3, "PROFILE");

			// Phase 4: LAYOUT
			currentPhase = 4;
			phaseStart = DateTime.UtcNow;
			var layout = LayoutCalculator.Calculate(zones, topology);
			await EnforceMinPhaseDelay(phaseStart);
			EmitSignal(SignalName.PhaseCompleted, 4, "LAYOUT");

			// Phase 5: BUILDING
			currentPhase = 5;
			phaseStart = DateTime.UtcNow;
			BuiltScene = RuntimeSceneBuilder.Build(layout, endpoint, mode);
			await EnforceMinPhaseDelay(phaseStart);
			EmitSignal(SignalName.PhaseCompleted, 5, "BUILDING");

			// Disconnect — the MetricPoller in the built scene manages its own connection
			await client.DisposeAsync();

			EmitSignal(SignalName.PipelineCompleted);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Pipeline failed at phase {currentPhase}: {ex.Message}");
			EmitSignal(SignalName.PipelineError, currentPhase, ex.Message);

			if (client != null)
			{
				try { await client.DisposeAsync(); }
				catch { /* swallow cleanup errors */ }
			}
		}
	}

	private async Task EnforceMinPhaseDelay(DateTime phaseStart)
	{
		if (MinPhaseDelayMs <= 0) return;

		var elapsed = (int)(DateTime.UtcNow - phaseStart).TotalMilliseconds;
		var remaining = MinPhaseDelayMs - elapsed;
		if (remaining > 0)
			await Task.Delay(remaining);
	}
}
