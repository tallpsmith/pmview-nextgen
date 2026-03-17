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

    public async void StartPipeline(string endpoint)
    {
        var currentPhase = 0;
        PcpClientConnection? client = null;

        try
        {
            // Phase 0: CONNECTING
            currentPhase = 0;
            client = new PcpClientConnection(new Uri(endpoint));
            await client.ConnectAsync();
            EmitSignal(SignalName.PhaseCompleted, 0, "CONNECTING");

            // Phase 1: TOPOLOGY
            currentPhase = 1;
            var topology = await MetricDiscovery.DiscoverAsync(client);
            EmitSignal(SignalName.PhaseCompleted, 1, "TOPOLOGY");

            // Phase 2: INSTANCES (already resolved as part of discovery)
            currentPhase = 2;
            EmitSignal(SignalName.PhaseCompleted, 2, "INSTANCES");

            // Phase 3: PROFILE
            currentPhase = 3;
            var profileProvider = new HostProfileProvider();
            var zones = profileProvider.GetProfile(topology.Os);
            EmitSignal(SignalName.PhaseCompleted, 3, "PROFILE");

            // Phase 4: LAYOUT
            currentPhase = 4;
            var layout = LayoutCalculator.Calculate(zones, topology);
            EmitSignal(SignalName.PhaseCompleted, 4, "LAYOUT");

            // Phase 5: BUILDING
            currentPhase = 5;
            BuiltScene = RuntimeSceneBuilder.Build(layout, endpoint);
            EmitSignal(SignalName.PhaseCompleted, 5, "BUILDING");

            // Disconnect — the MetricPoller in the built scene manages its own connection
            await client.DisconnectAsync();
            client.Dispose();

            EmitSignal(SignalName.PipelineCompleted);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Pipeline failed at phase {currentPhase}: {ex.Message}");
            EmitSignal(SignalName.PipelineError, currentPhase, ex.Message);

            if (client != null)
            {
                try { client.Dispose(); }
                catch { /* swallow cleanup errors */ }
            }
        }
    }
}
