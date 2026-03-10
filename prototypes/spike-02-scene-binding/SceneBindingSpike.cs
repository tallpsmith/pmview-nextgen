using Godot;
using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Spike 2: Prove a real metric value can drive a 3D object property in real-time.
/// THROWAWAY CODE — do not promote to production.
/// </summary>
public partial class SceneBindingSpike : Node3D
{
    private static readonly HttpClient Http = new();
    private const string PmproxyUrl = "http://localhost:44322";

    [Export] public NodePath TargetBarPath { get; set; } = "Bar";

    private int _contextId;
    private Node3D? _targetBar;
    private double _pollTimer;
    private const double PollIntervalSeconds = 1.0;

    public override async void _Ready()
    {
        _targetBar = GetNode<Node3D>(TargetBarPath);
        GD.Print("[Spike-02] Starting scene binding proof...");

        try
        {
            _contextId = await CreateContext();
            GD.Print($"[Spike-02] Connected with context: {_contextId}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Spike-02] Connection failed: {ex.Message}");
        }
    }

    public override void _Process(double delta)
    {
        _pollTimer += delta;
        if (_pollTimer >= PollIntervalSeconds)
        {
            _pollTimer = 0;
            _ = PollAndUpdate();
        }
    }

    private async Task PollAndUpdate()
    {
        try
        {
            var loadValue = await FetchLoadValue();
            var normalised = Lerp(loadValue, 0.0, 10.0, 0.1, 5.0);

            if (_targetBar != null)
            {
                var scale = _targetBar.Scale;
                scale.Y = (float)normalised;
                _targetBar.Scale = scale;
            }

            GD.Print($"[Spike-02] Load: {loadValue:F2} -> Scale.Y: {normalised:F2}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Spike-02] Poll failed: {ex.Message}");
        }
    }

    private async Task<double> FetchLoadValue()
    {
        var response = await Http.GetStringAsync(
            $"{PmproxyUrl}/pmapi/fetch?names=kernel.all.load&context={_contextId}");
        using var doc = JsonDocument.Parse(response);

        var values = doc.RootElement.GetProperty("values");
        var firstMetric = values[0];
        var instances = firstMetric.GetProperty("instances");
        var firstInstance = instances[0];
        return firstInstance.GetProperty("value").GetDouble();
    }

    private async Task<int> CreateContext()
    {
        var response = await Http.GetStringAsync($"{PmproxyUrl}/pmapi/context?polltimeout=60");
        using var doc = JsonDocument.Parse(response);
        return doc.RootElement.GetProperty("context").GetInt32();
    }

    private static double Lerp(double value, double srcMin, double srcMax, double tgtMin, double tgtMax)
    {
        var t = Math.Clamp((value - srcMin) / (srcMax - srcMin), 0.0, 1.0);
        return tgtMin + t * (tgtMax - tgtMin);
    }
}
