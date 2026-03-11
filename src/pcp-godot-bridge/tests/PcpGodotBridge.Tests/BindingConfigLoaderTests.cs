using Xunit;

namespace PcpGodotBridge.Tests;

/// <summary>
/// Tests for BindingConfigLoader: TOML parsing, structural validation,
/// property classification, and error reporting.
/// Per binding-config-schema.md contract and design spec 2026-03-10.
/// </summary>
public class BindingConfigLoaderTests
{
    // ── Happy Path: Valid Config ──

    private const string ValidMinimalConfig = """
        [meta]
        scene = "res://scenes/test.tscn"

        [[bindings]]
        scene_node = "Bar"
        metric = "kernel.all.load"
        property = "height"
        source_range = [0.0, 10.0]
        target_range = [0.0, 5.0]
        """;

    [Fact]
    public void Load_ValidMinimalConfig_ReturnsValidResult()
    {
        var result = BindingConfigLoader.Load(ValidMinimalConfig);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Config);
        Assert.Equal("res://scenes/test.tscn", result.Config!.ScenePath);
        Assert.Equal(1000, result.Config.PollIntervalMs); // default
        Assert.Null(result.Config.Endpoint);
        Assert.Null(result.Config.Description);
        Assert.Single(result.Config.Bindings);
    }

    [Fact]
    public void Load_ValidFullConfig_ParsesAllMetaFields()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"
            endpoint = "http://monitoring:44322"
            poll_interval_ms = 2000
            description = "Full test config"

            [[bindings]]
            scene_node = "Bar"
            metric = "kernel.all.load"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid);
        Assert.Equal("http://monitoring:44322", result.Config!.Endpoint);
        Assert.Equal(2000, result.Config.PollIntervalMs);
        Assert.Equal("Full test config", result.Config.Description);
    }

    [Fact]
    public void Load_ValidBinding_ParsesAllBindingFields()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Gauges/DiskIO"
            metric = "disk.dev.read"
            property = "rotation_speed"
            source_range = [0.0, 5000.0]
            target_range = [0.0, 360.0]
            instance_filter = "sd*"
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid);
        var binding = result.Config!.Bindings[0];
        Assert.Equal("Gauges/DiskIO", binding.SceneNode);
        Assert.Equal("disk.dev.read", binding.Metric);
        Assert.Equal("rotation_speed", binding.Property);
        Assert.Equal(0.0, binding.SourceRangeMin);
        Assert.Equal(5000.0, binding.SourceRangeMax);
        Assert.Equal(0.0, binding.TargetRangeMin);
        Assert.Equal(360.0, binding.TargetRangeMax);
        Assert.Equal("sd*", binding.InstanceFilter);
        Assert.Null(binding.InstanceId);
    }

    [Fact]
    public void Load_BindingWithInstanceId_ParsesCorrectly()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "kernel.all.load"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            instance_id = 1
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid);
        Assert.Equal(1, result.Config!.Bindings[0].InstanceId);
        Assert.Null(result.Config.Bindings[0].InstanceFilter);
    }

    [Fact]
    public void Load_MultipleBindings_ParsesAll()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar1"
            metric = "kernel.all.load"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]

            [[bindings]]
            scene_node = "Bar2"
            metric = "disk.dev.read"
            property = "width"
            source_range = [0.0, 1000.0]
            target_range = [0.0, 3.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Config!.Bindings.Count);
    }

    // ── Structured Logging: Info Messages ──

    [Fact]
    public void Load_ValidConfig_EmitsInfoMessagesForEachBinding()
    {
        var result = BindingConfigLoader.Load(ValidMinimalConfig);

        var infoMessages = result.Messages
            .Where(m => m.Severity == ValidationSeverity.Info).ToList();
        Assert.True(infoMessages.Count >= 2); // at least binding info + summary
    }

    [Fact]
    public void Load_CustomProperty_EmitsInfoAboutPassThrough()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "River"
            metric = "network.interface.total.bytes"
            property = "river_flow_speed"
            source_range = [0.0, 1000000.0]
            target_range = [0.0, 10.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid);
        var passThrough = result.Messages.FirstOrDefault(m =>
            m.Severity == ValidationSeverity.Info &&
            m.Message.Contains("custom", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(passThrough);
    }

    [Fact]
    public void Load_ValidConfig_EmitsSummaryMessage()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar1"
            metric = "m1"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]

            [[bindings]]
            scene_node = "Bar2"
            metric = "m2"
            property = "width"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        var summary = result.Messages.LastOrDefault(m =>
            m.Severity == ValidationSeverity.Info &&
            m.Message.Contains("2 active", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(summary);
    }

    // ── TOML Parse Errors ──

    [Fact]
    public void Load_InvalidToml_ReturnsError()
    {
        var result = BindingConfigLoader.Load("this is not valid toml [[[");

        Assert.False(result.IsValid);
        Assert.Null(result.Config);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("TOML parse error"));
    }

    // ── Missing [meta] Section ──

    [Fact]
    public void Load_MissingMetaSection_ReturnsError()
    {
        var toml = """
            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("[meta]"));
    }

    // ── Missing [meta].scene ──

    [Fact]
    public void Load_MissingScene_ReturnsError()
    {
        var toml = """
            [meta]
            poll_interval_ms = 1000

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("scene"));
    }

    // ── Invalid Scene Path ──

    [Theory]
    [InlineData("scenes/test.tscn")]
    [InlineData("res://scenes/test.json")]
    [InlineData("res://scenes/test")]
    public void Load_InvalidScenePath_ReturnsError(string scenePath)
    {
        var toml = $"""
            [meta]
            scene = "{scenePath}"

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("res://"));
    }

    // ── Missing [[bindings]] ──

    [Fact]
    public void Load_MissingBindingsSection_ReturnsError()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("bindings"));
    }

    // ── Missing Required Binding Fields ──

    [Theory]
    [InlineData("scene_node")]
    [InlineData("metric")]
    [InlineData("property")]
    public void Load_MissingRequiredBindingField_SkipsBinding(string missingField)
    {
        var fields = new Dictionary<string, string>
        {
            ["scene_node"] = "scene_node = \"Bar\"",
            ["metric"] = "metric = \"kernel.all.load\"",
            ["property"] = "property = \"height\"",
        };
        fields.Remove(missingField);

        var toml = $"""
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            {string.Join("\n", fields.Values)}
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid); // config still valid, binding skipped
        Assert.Empty(result.Config!.Bindings);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains(missingField));
    }

    // ── Range Validation ──

    [Fact]
    public void Load_SourceRangeMinEqualsMax_SkipsBinding()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [5.0, 5.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid);
        Assert.Empty(result.Config!.Bindings);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("source_range"));
    }

    [Fact]
    public void Load_SourceRangeReversed_SkipsBinding()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [10.0, 0.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.Empty(result.Config!.Bindings);
    }

    [Fact]
    public void Load_TargetRangeReversed_SkipsBinding()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [5.0, 0.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.Empty(result.Config!.Bindings);
    }

    [Fact]
    public void Load_RangeWrongElementCount_SkipsBinding()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [0.0, 5.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.Empty(result.Config!.Bindings);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("2 elements"));
    }

    // ── Mutual Exclusion: instance_filter + instance_id ──

    [Fact]
    public void Load_BothInstanceFilterAndId_SkipsBinding()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            instance_filter = "sd*"
            instance_id = 1
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.Empty(result.Config!.Bindings);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("mutually exclusive"));
    }

    // ── Duplicate Node+Property ──

    [Fact]
    public void Load_DuplicateNodeProperty_KeepsFirstSkipsDuplicate()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "kernel.all.load"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]

            [[bindings]]
            scene_node = "Bar"
            metric = "disk.dev.read"
            property = "height"
            source_range = [0.0, 1000.0]
            target_range = [0.0, 3.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.Single(result.Config!.Bindings);
        Assert.Equal("kernel.all.load", result.Config.Bindings[0].Metric);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Load_SameNodeDifferentProperty_AllowsBoth()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "Bar"
            metric = "kernel.all.load"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]

            [[bindings]]
            scene_node = "Bar"
            metric = "kernel.all.load"
            property = "color_temperature"
            source_range = [0.0, 10.0]
            target_range = [0.0, 1.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.Equal(2, result.Config!.Bindings.Count);
    }

    // ── Poll Interval Validation ──

    [Fact]
    public void Load_PollIntervalTooLow_UsesDefault()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"
            poll_interval_ms = 50

            [[bindings]]
            scene_node = "Bar"
            metric = "m"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.Equal(1000, result.Config!.PollIntervalMs);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Warning &&
            m.Message.Contains("poll_interval_ms"));
    }

    // ── Mixed Valid and Invalid Bindings ──

    [Fact]
    public void Load_MixedValidAndInvalid_SkipsBadKeepsGood()
    {
        var toml = """
            [meta]
            scene = "res://scenes/test.tscn"

            [[bindings]]
            scene_node = "GoodBar"
            metric = "kernel.all.load"
            property = "height"
            source_range = [0.0, 10.0]
            target_range = [0.0, 5.0]

            [[bindings]]
            scene_node = "BadBar"
            metric = "m"
            property = "height"
            source_range = [10.0, 0.0]
            target_range = [0.0, 5.0]

            [[bindings]]
            scene_node = "AlsoGood"
            metric = "disk.dev.read"
            property = "width"
            source_range = [0.0, 1000.0]
            target_range = [0.0, 3.0]
            """;

        var result = BindingConfigLoader.Load(toml);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Config!.Bindings.Count);
        Assert.Equal("GoodBar", result.Config.Bindings[0].SceneNode);
        Assert.Equal("AlsoGood", result.Config.Bindings[1].SceneNode);
    }

    // ── File Loading ──

    [Fact]
    public void LoadFromFile_NonexistentFile_ReturnsError()
    {
        var result = BindingConfigLoader.LoadFromFile("/nonexistent/path.toml");

        Assert.False(result.IsValid);
        Assert.Contains(result.Messages, m =>
            m.Severity == ValidationSeverity.Error &&
            m.Message.Contains("Cannot read file"));
    }
}
