# Godot Bridge Layer + Scene Player Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the PcpGodotBridge binding config library (pure .NET, TDD) and the Godot bridge nodes + test scenes that prove live metric-driven 3D visualisation and scene swapping work end-to-end.

**Architecture:** Four layers — PcpClient (exists, pure .NET) → PcpGodotBridge (new, pure .NET, binding config model + validation) → Godot bridge nodes (new, thin C# in Godot) → GDScript scenes. The binding config library is fully xUnit testable. Godot bridge nodes are kept thin. See `docs/superpowers/specs/2026-03-10-godot-bridge-and-scene-player-design.md`.

**Tech Stack:** .NET 8.0, xUnit, Tomlyn (TOML parser), Godot 4.4+ (Godot.NET.Sdk), GDScript

**Environment:** `dotnet` is at `/opt/homebrew/opt/dotnet@8/bin/dotnet`. Always set `export PATH="/opt/homebrew/opt/dotnet@8/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"` and `export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"` before running commands. Godot is NOT installed — write scene/script files directly, do not attempt to launch Godot.

**Existing code:** PcpClient library at `src/pcp-client-dotnet/` with 74 passing tests. Key types: `IPcpClient`, `PcpClientConnection`, `MetricValue`, `InstanceValue`, `ConnectionState`. Tests use `MockHttpHandler` pattern in `PcpContextTests.cs`.

---

## Chunk 1: PcpGodotBridge Library — Project Setup + Core Types

### Task 1: Create PcpGodotBridge .NET project structure

**Files:**
- Create: `src/pcp-godot-bridge/src/PcpGodotBridge/PcpGodotBridge.csproj`
- Create: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/PcpGodotBridge.Tests.csproj`
- Create: `src/pcp-godot-bridge/PcpGodotBridge.sln`

- [ ] **Step 1: Create project directory structure**

```bash
mkdir -p src/pcp-godot-bridge/src/PcpGodotBridge
mkdir -p src/pcp-godot-bridge/tests/PcpGodotBridge.Tests
```

- [ ] **Step 2: Create PcpGodotBridge.csproj**

Create `src/pcp-godot-bridge/src/PcpGodotBridge/PcpGodotBridge.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>PcpGodotBridge</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Tomlyn" Version="0.17.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\..\pcp-client-dotnet\src\PcpClient\PcpClient.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create PcpGodotBridge.Tests.csproj**

Create `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/PcpGodotBridge.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>PcpGodotBridge.Tests</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PcpGodotBridge\PcpGodotBridge.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 4: Create PcpGodotBridge.sln**

```bash
cd src/pcp-godot-bridge
dotnet new sln --name PcpGodotBridge
dotnet sln add src/PcpGodotBridge/PcpGodotBridge.csproj
dotnet sln add tests/PcpGodotBridge.Tests/PcpGodotBridge.Tests.csproj
```

- [ ] **Step 5: Verify build succeeds**

```bash
dotnet build src/pcp-godot-bridge/PcpGodotBridge.sln
```

Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/pcp-godot-bridge/
git commit -m "add PcpGodotBridge library skeleton with test project

Binding config model + TOML validation layer, sits between
PcpClient (pure .NET) and Godot bridge nodes."
```

### Task 2: Create core model types (BindingConfig, MetricBinding, ValidationMessage)

**Files:**
- Create: `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfig.cs`
- Create: `src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs`
- Create: `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigResult.cs`
- Create: `src/pcp-godot-bridge/src/PcpGodotBridge/PropertyVocabulary.cs`
- Test: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/ModelTests.cs`

- [ ] **Step 1: Write failing tests for core model types**

Create `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/ModelTests.cs`:

```csharp
using Xunit;

namespace PcpGodotBridge.Tests;

/// <summary>
/// Tests for binding config model records: BindingConfig, MetricBinding,
/// ValidationMessage, BindingConfigResult, PropertyVocabulary.
/// Per design spec 2026-03-10.
/// </summary>
public class ModelTests
{
    // ── MetricBinding Record ──

    [Fact]
    public void MetricBinding_StoresAllFields()
    {
        var binding = new MetricBinding(
            SceneNode: "CpuLoadBar",
            Metric: "kernel.all.load",
            Property: "height",
            SourceRangeMin: 0.0,
            SourceRangeMax: 10.0,
            TargetRangeMin: 0.0,
            TargetRangeMax: 5.0,
            InstanceFilter: null,
            InstanceId: null);

        Assert.Equal("CpuLoadBar", binding.SceneNode);
        Assert.Equal("kernel.all.load", binding.Metric);
        Assert.Equal("height", binding.Property);
        Assert.Equal(0.0, binding.SourceRangeMin);
        Assert.Equal(10.0, binding.SourceRangeMax);
        Assert.Equal(0.0, binding.TargetRangeMin);
        Assert.Equal(5.0, binding.TargetRangeMax);
        Assert.Null(binding.InstanceFilter);
        Assert.Null(binding.InstanceId);
    }

    [Fact]
    public void MetricBinding_WithInstanceFilter()
    {
        var binding = new MetricBinding("Node", "metric", "height",
            0, 10, 0, 5, InstanceFilter: "sd*", InstanceId: null);

        Assert.Equal("sd*", binding.InstanceFilter);
        Assert.Null(binding.InstanceId);
    }

    [Fact]
    public void MetricBinding_WithInstanceId()
    {
        var binding = new MetricBinding("Node", "metric", "height",
            0, 10, 0, 5, InstanceFilter: null, InstanceId: 42);

        Assert.Null(binding.InstanceFilter);
        Assert.Equal(42, binding.InstanceId);
    }

    [Fact]
    public void MetricBinding_ValueEquality()
    {
        var a = new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null);
        var b = new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null);

        Assert.Equal(a, b);
    }

    // ── BindingConfig Record ──

    [Fact]
    public void BindingConfig_StoresAllFields()
    {
        var bindings = new[] {
            new MetricBinding("Bar", "kernel.all.load", "height",
                0, 10, 0, 5, null, null)
        };

        var config = new BindingConfig(
            ScenePath: "res://scenes/test.tscn",
            Endpoint: "http://localhost:44322",
            PollIntervalMs: 1000,
            Description: "Test config",
            Bindings: bindings);

        Assert.Equal("res://scenes/test.tscn", config.ScenePath);
        Assert.Equal("http://localhost:44322", config.Endpoint);
        Assert.Equal(1000, config.PollIntervalMs);
        Assert.Equal("Test config", config.Description);
        Assert.Single(config.Bindings);
    }

    [Fact]
    public void BindingConfig_OptionalFieldsNullable()
    {
        var config = new BindingConfig("res://s.tscn", null, 1000, null,
            Array.Empty<MetricBinding>());

        Assert.Null(config.Endpoint);
        Assert.Null(config.Description);
    }

    // ── ValidationMessage ──

    [Fact]
    public void ValidationMessage_StoresSeverityAndMessage()
    {
        var msg = new ValidationMessage(
            ValidationSeverity.Error,
            "Missing required field: metric",
            "bindings[2]");

        Assert.Equal(ValidationSeverity.Error, msg.Severity);
        Assert.Equal("Missing required field: metric", msg.Message);
        Assert.Equal("bindings[2]", msg.BindingContext);
    }

    [Fact]
    public void ValidationMessage_NullContextAllowed()
    {
        var msg = new ValidationMessage(ValidationSeverity.Info,
            "Config loaded", null);

        Assert.Null(msg.BindingContext);
    }

    // ── ValidationSeverity Enum ──

    [Theory]
    [InlineData(ValidationSeverity.Info)]
    [InlineData(ValidationSeverity.Warning)]
    [InlineData(ValidationSeverity.Error)]
    public void ValidationSeverity_HasExpectedValues(ValidationSeverity severity)
    {
        Assert.True(Enum.IsDefined(severity));
    }

    // ── BindingConfigResult ──

    [Fact]
    public void BindingConfigResult_ValidConfig_IsValid()
    {
        var config = new BindingConfig("res://s.tscn", null, 1000, null,
            new[] { new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null) });
        var result = new BindingConfigResult(config,
            new[] { new ValidationMessage(ValidationSeverity.Info, "ok", null) });

        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
        Assert.NotNull(result.Config);
    }

    [Fact]
    public void BindingConfigResult_WithErrors_IsNotValid()
    {
        var result = new BindingConfigResult(null,
            new[] { new ValidationMessage(ValidationSeverity.Error, "bad", null) });

        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
        Assert.Null(result.Config);
    }

    [Fact]
    public void BindingConfigResult_WarningsOnly_IsStillValid()
    {
        var config = new BindingConfig("res://s.tscn", null, 1000, null,
            Array.Empty<MetricBinding>());
        var result = new BindingConfigResult(config,
            new[] { new ValidationMessage(ValidationSeverity.Warning, "eh", null) });

        Assert.True(result.IsValid);
        Assert.False(result.HasErrors);
    }

    // ── PropertyVocabulary ──

    [Theory]
    [InlineData("height")]
    [InlineData("width")]
    [InlineData("depth")]
    [InlineData("scale")]
    [InlineData("rotation_speed")]
    [InlineData("position_y")]
    [InlineData("color_temperature")]
    [InlineData("opacity")]
    public void PropertyVocabulary_RecognisesBuiltInProperties(string property)
    {
        Assert.True(PropertyVocabulary.IsBuiltIn(property));
    }

    [Theory]
    [InlineData("river_flow_speed")]
    [InlineData("wind_intensity")]
    [InlineData("fire_brightness")]
    [InlineData("nonexistent")]
    public void PropertyVocabulary_DoesNotRecogniseCustomProperties(string property)
    {
        Assert.False(PropertyVocabulary.IsBuiltIn(property));
    }

    [Fact]
    public void PropertyVocabulary_ClassifiesBuiltInAsBuiltIn()
    {
        var kind = PropertyVocabulary.Classify("height");

        Assert.Equal(PropertyKind.BuiltIn, kind);
    }

    [Fact]
    public void PropertyVocabulary_ClassifiesUnknownAsCustom()
    {
        var kind = PropertyVocabulary.Classify("river_flow_speed");

        Assert.Equal(PropertyKind.Custom, kind);
    }

    [Fact]
    public void PropertyVocabulary_ResolvesBuiltInToGodotProperty()
    {
        Assert.Equal("scale:y", PropertyVocabulary.ResolveGodotProperty("height"));
        Assert.Equal("scale:x", PropertyVocabulary.ResolveGodotProperty("width"));
        Assert.Equal("scale:z", PropertyVocabulary.ResolveGodotProperty("depth"));
        Assert.Equal("scale", PropertyVocabulary.ResolveGodotProperty("scale"));
        Assert.Equal("rotation:y", PropertyVocabulary.ResolveGodotProperty("rotation_speed"));
        Assert.Equal("position:y", PropertyVocabulary.ResolveGodotProperty("position_y"));
    }

    [Fact]
    public void PropertyVocabulary_ResolvesCustomToSameName()
    {
        Assert.Equal("river_flow_speed",
            PropertyVocabulary.ResolveGodotProperty("river_flow_speed"));
    }

    // ── ResolvedBinding ──

    [Fact]
    public void ResolvedBinding_StoresBindingAndResolution()
    {
        var binding = new MetricBinding("N", "m", "height", 0, 10, 0, 5, null, null);
        var resolved = new ResolvedBinding(binding, PropertyKind.BuiltIn, "scale:y");

        Assert.Equal(binding, resolved.Binding);
        Assert.Equal(PropertyKind.BuiltIn, resolved.Kind);
        Assert.Equal("scale:y", resolved.GodotPropertyName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln --verbosity quiet
```

Expected: FAIL — types don't exist yet.

- [ ] **Step 3: Implement MetricBinding record**

Create `src/pcp-godot-bridge/src/PcpGodotBridge/MetricBinding.cs`:

```csharp
namespace PcpGodotBridge;

/// <summary>
/// Maps a single scene node property to a PCP metric value.
/// Parsed from a [[bindings]] entry in the TOML config.
/// </summary>
public record MetricBinding(
    string SceneNode,
    string Metric,
    string Property,
    double SourceRangeMin,
    double SourceRangeMax,
    double TargetRangeMin,
    double TargetRangeMax,
    string? InstanceFilter,
    int? InstanceId);
```

- [ ] **Step 4: Implement BindingConfig record**

Create `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfig.cs`:

```csharp
namespace PcpGodotBridge;

/// <summary>
/// Top-level binding configuration loaded from a TOML file.
/// Maps a Godot scene to PCP metrics via bindings.
/// </summary>
public record BindingConfig(
    string ScenePath,
    string? Endpoint,
    int PollIntervalMs,
    string? Description,
    IReadOnlyList<MetricBinding> Bindings);
```

- [ ] **Step 5: Implement BindingConfigResult and ValidationMessage**

Create `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigResult.cs`:

```csharp
namespace PcpGodotBridge;

public enum ValidationSeverity { Info, Warning, Error }

/// <summary>
/// A single validation message from config loading.
/// BindingContext identifies which binding entry (e.g. "bindings[2]") if applicable.
/// </summary>
public record ValidationMessage(
    ValidationSeverity Severity,
    string Message,
    string? BindingContext);

/// <summary>
/// Result of loading and validating a binding config.
/// Contains the parsed config (if valid) and all validation messages.
/// </summary>
public record BindingConfigResult(
    BindingConfig? Config,
    IReadOnlyList<ValidationMessage> Messages)
{
    public bool HasErrors => Messages.Any(m => m.Severity == ValidationSeverity.Error);
    public bool IsValid => Config != null && !HasErrors;
}
```

- [ ] **Step 6: Implement PropertyVocabulary**

Create `src/pcp-godot-bridge/src/PcpGodotBridge/PropertyVocabulary.cs`:

```csharp
namespace PcpGodotBridge;

public enum PropertyKind { BuiltIn, Custom }

/// <summary>
/// Resolved binding with property classification and Godot property name.
/// </summary>
public record ResolvedBinding(
    MetricBinding Binding,
    PropertyKind Kind,
    string GodotPropertyName);

/// <summary>
/// Maps binding config property names to Godot node properties.
/// Built-in properties map to specific Godot properties (e.g. "height" → "scale:y").
/// Custom properties pass through as-is for @export vars on scene nodes.
/// </summary>
public static class PropertyVocabulary
{
    private static readonly Dictionary<string, string> BuiltInMappings = new()
    {
        ["height"] = "scale:y",
        ["width"] = "scale:x",
        ["depth"] = "scale:z",
        ["scale"] = "scale",
        ["rotation_speed"] = "rotation:y",
        ["position_y"] = "position:y",
        ["color_temperature"] = "albedo_color",
        ["opacity"] = "albedo_color:a",
    };

    public static bool IsBuiltIn(string property) =>
        BuiltInMappings.ContainsKey(property);

    public static PropertyKind Classify(string property) =>
        IsBuiltIn(property) ? PropertyKind.BuiltIn : PropertyKind.Custom;

    public static string ResolveGodotProperty(string property) =>
        BuiltInMappings.TryGetValue(property, out var godotProp)
            ? godotProp
            : property;

    public static ResolvedBinding Resolve(MetricBinding binding) =>
        new(binding, Classify(binding.Property),
            ResolveGodotProperty(binding.Property));
}
```

- [ ] **Step 7: Run tests to verify they pass**

```bash
dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln --verbosity quiet
```

Expected: All tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/pcp-godot-bridge/
git commit -m "add binding config model types with property vocabulary

Records for BindingConfig, MetricBinding, ValidationMessage,
ResolvedBinding. Two-tier property system: built-in vocabulary
maps to Godot properties, custom pass-through for @export vars."
```

---

## Chunk 2: PcpGodotBridge Library — BindingConfigLoader + Validation (TDD)

### Task 3: Write failing tests for TOML parsing (happy path)

**Files:**
- Create: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingConfigLoaderTests.cs`

- [ ] **Step 1: Write failing tests for happy-path TOML loading**

Create `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingConfigLoaderTests.cs`:

```csharp
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
        Assert.True(infoMessages.Count >= 2); // at least load + binding summary
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
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln --verbosity quiet
```

Expected: FAIL — `BindingConfigLoader` doesn't exist.

### Task 4: Implement BindingConfigLoader happy path

**Files:**
- Create: `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigLoader.cs`

- [ ] **Step 1: Implement BindingConfigLoader with TOML parsing**

Create `src/pcp-godot-bridge/src/PcpGodotBridge/BindingConfigLoader.cs`:

```csharp
using Tomlyn;
using Tomlyn.Model;

namespace PcpGodotBridge;

/// <summary>
/// Parses TOML binding config files and validates structure.
/// Returns a BindingConfigResult with the parsed config and validation messages.
/// All validation that can be done without Godot happens here.
/// </summary>
public static class BindingConfigLoader
{
    private const int DefaultPollIntervalMs = 1000;
    private const int MinPollIntervalMs = 100;

    public static BindingConfigResult Load(string toml)
    {
        var messages = new List<ValidationMessage>();

        // Parse TOML
        TomlTable table;
        try
        {
            table = Toml.ToModel(toml);
        }
        catch (TomlException ex)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"TOML parse error: {ex.Message}", null));
            return new BindingConfigResult(null, messages);
        }

        // Validate [meta] section
        if (!table.TryGetValue("meta", out var metaObj) || metaObj is not TomlTable meta)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required [meta] section", null));
            return new BindingConfigResult(null, messages);
        }

        var scenePath = ValidateScenePath(meta, messages);
        if (scenePath == null)
            return new BindingConfigResult(null, messages);

        var endpoint = meta.TryGetValue("endpoint", out var ep) ? ep?.ToString() : null;
        var pollIntervalMs = ValidatePollInterval(meta, messages);
        var description = meta.TryGetValue("description", out var desc) ? desc?.ToString() : null;

        // Parse [[bindings]]
        if (!table.TryGetValue("bindings", out var bindingsObj) || bindingsObj is not TomlTableArray bindingsArray)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required [[bindings]] section", null));
            return new BindingConfigResult(null, messages);
        }

        var bindings = new List<MetricBinding>();
        var seenNodeProperties = new HashSet<string>();

        for (var i = 0; i < bindingsArray.Count; i++)
        {
            var bindingTable = bindingsArray[i];
            var context = FormatBindingContext(i, bindingTable);
            var binding = ValidateBinding(bindingTable, i, context, seenNodeProperties, messages);
            if (binding != null)
            {
                bindings.Add(binding);
                LogBindingInfo(binding, i, messages, context);
            }
        }

        var config = new BindingConfig(scenePath, endpoint, pollIntervalMs, description, bindings);
        var skipped = bindingsArray.Count - bindings.Count;

        messages.Add(new ValidationMessage(ValidationSeverity.Info,
            $"Config loaded: {bindings.Count} active binding{(bindings.Count == 1 ? "" : "s")}" +
            (skipped > 0 ? $", {skipped} skipped" : ""),
            null));

        return new BindingConfigResult(config, messages);
    }

    public static BindingConfigResult LoadFromFile(string filePath)
    {
        try
        {
            var toml = File.ReadAllText(filePath);
            return Load(toml);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new BindingConfigResult(null, new[]
            {
                new ValidationMessage(ValidationSeverity.Error,
                    $"Cannot read file: {ex.Message}", null)
            });
        }
    }

    private static string? ValidateScenePath(TomlTable meta, List<ValidationMessage> messages)
    {
        if (!meta.TryGetValue("scene", out var sceneObj) || sceneObj is not string scene
            || string.IsNullOrWhiteSpace(scene))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "Missing required field: [meta].scene", null));
            return null;
        }

        if (!scene.StartsWith("res://") || !scene.EndsWith(".tscn"))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"[meta].scene must start with 'res://' and end with '.tscn', got: '{scene}'",
                null));
            return null;
        }

        return scene;
    }

    private static int ValidatePollInterval(TomlTable meta, List<ValidationMessage> messages)
    {
        if (!meta.TryGetValue("poll_interval_ms", out var pollObj))
            return DefaultPollIntervalMs;

        var pollValue = Convert.ToInt32(pollObj);
        if (pollValue < MinPollIntervalMs)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"poll_interval_ms must be >= {MinPollIntervalMs}, got {pollValue}. Using default {DefaultPollIntervalMs}.",
                null));
            return DefaultPollIntervalMs;
        }

        return pollValue;
    }

    private static MetricBinding? ValidateBinding(TomlTable binding, int index, string context,
        HashSet<string> seenNodeProperties, List<ValidationMessage> messages)
    {
        // Required fields
        var sceneNode = GetRequiredString(binding, "scene_node", context, messages);
        var metric = GetRequiredString(binding, "metric", context, messages);
        var property = GetRequiredString(binding, "property", context, messages);

        if (sceneNode == null || metric == null || property == null)
            return null;

        // Range validation
        var sourceRange = ValidateRange(binding, "source_range", context, messages);
        var targetRange = ValidateRange(binding, "target_range", context, messages);

        if (sourceRange == null || targetRange == null)
            return null;

        // Property vocabulary check (info for custom, not an error)
        if (!PropertyVocabulary.IsBuiltIn(property))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Info,
                $"Property '{property}' is a custom pass-through — will validate against scene node at load time",
                context));
        }

        // Mutual exclusion: instance_filter vs instance_id
        var hasFilter = binding.TryGetValue("instance_filter", out var filterObj);
        var hasId = binding.TryGetValue("instance_id", out var idObj);

        if (hasFilter && hasId)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                "instance_filter and instance_id are mutually exclusive",
                context));
            return null;
        }

        var instanceFilter = hasFilter ? filterObj?.ToString() : null;
        int? instanceId = hasId ? Convert.ToInt32(idObj) : null;

        // Duplicate node+property detection
        var nodePropertyKey = $"{sceneNode}+{property}";
        if (!seenNodeProperties.Add(nodePropertyKey))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"Duplicate binding for {sceneNode}+{property} — keeping first, skipping this one",
                context));
            return null;
        }

        return new MetricBinding(sceneNode, metric, property,
            sourceRange.Value.min, sourceRange.Value.max,
            targetRange.Value.min, targetRange.Value.max,
            instanceFilter, instanceId);
    }

    private static string? GetRequiredString(TomlTable table, string key, string context,
        List<ValidationMessage> messages)
    {
        if (!table.TryGetValue(key, out var value) || value is not string str
            || string.IsNullOrWhiteSpace(str))
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"Missing required field: {key}", context));
            return null;
        }
        return str;
    }

    private static (double min, double max)? ValidateRange(TomlTable table, string key,
        string context, List<ValidationMessage> messages)
    {
        if (!table.TryGetValue(key, out var rangeObj) || rangeObj is not TomlArray rangeArray)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"Missing required field: {key}", context));
            return null;
        }

        if (rangeArray.Count != 2)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"{key} must have exactly 2 elements, got {rangeArray.Count}", context));
            return null;
        }

        var min = Convert.ToDouble(rangeArray[0]);
        var max = Convert.ToDouble(rangeArray[1]);

        if (min >= max)
        {
            messages.Add(new ValidationMessage(ValidationSeverity.Error,
                $"{key}[0] must be less than {key}[1]: got [{min}, {max}]", context));
            return null;
        }

        return (min, max);
    }

    private static void LogBindingInfo(MetricBinding binding, int index,
        List<ValidationMessage> messages, string context)
    {
        var instanceInfo = binding.InstanceFilter != null
            ? $" (filter: {binding.InstanceFilter})"
            : binding.InstanceId != null
                ? $" (instance: {binding.InstanceId})"
                : "";

        messages.Add(new ValidationMessage(ValidationSeverity.Info,
            $"{binding.SceneNode}.{binding.Property} <- {binding.Metric} " +
            $"[{binding.SourceRangeMin}-{binding.SourceRangeMax}] -> " +
            $"[{binding.TargetRangeMin}-{binding.TargetRangeMax}]{instanceInfo}",
            context));
    }

    private static string FormatBindingContext(int index, TomlTable binding)
    {
        var node = binding.TryGetValue("scene_node", out var n) ? n?.ToString() : "?";
        var metric = binding.TryGetValue("metric", out var m) ? m?.ToString() : "?";
        return $"bindings[{index}] (scene_node='{node}', metric='{metric}')";
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

```bash
dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln --verbosity quiet
```

Expected: All happy-path tests PASS.

- [ ] **Step 3: Commit**

```bash
git add src/pcp-godot-bridge/
git commit -m "add BindingConfigLoader with TOML parsing and structured logging

Parses [meta] and [[bindings]] sections via Tomlyn. Validates
required fields, range values, property vocabulary. Returns
BindingConfigResult with full audit trail of info/warning/error
messages."
```

### Task 5: Write failing tests for validation error cases

**Files:**
- Modify: `src/pcp-godot-bridge/tests/PcpGodotBridge.Tests/BindingConfigLoaderTests.cs`

- [ ] **Step 1: Add validation error tests**

Append to `BindingConfigLoaderTests.cs`:

```csharp
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
    [InlineData("scenes/test.tscn")]          // missing res://
    [InlineData("res://scenes/test.json")]     // wrong extension
    [InlineData("res://scenes/test")]          // no extension
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
        Assert.Equal("kernel.all.load", result.Config.Bindings[0].Metric); // first kept
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
            m.Severity == ValidationSeverity.Error &&
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
```

- [ ] **Step 2: Run tests to verify they pass**

```bash
dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln --verbosity quiet
```

Expected: All tests PASS (implementation already handles these cases).

- [ ] **Step 3: Commit**

```bash
git add src/pcp-godot-bridge/tests/
git commit -m "add comprehensive validation tests for BindingConfigLoader

Covers: TOML parse errors, missing meta/scene/bindings, invalid
scene paths, missing required fields, range validation, mutual
exclusion, duplicate detection, poll interval limits, mixed
valid/invalid bindings, file loading errors."
```

---

## Chunk 3: Godot Project Scaffolding

### Task 6: Create godot-project directory structure and project files

**Files:**
- Create: `godot-project/project.godot`
- Create: `godot-project/pmview-nextgen.csproj`
- Create: `godot-project/pmview-nextgen.sln`

- [ ] **Step 1: Create directory structure**

```bash
mkdir -p godot-project/scripts/bridge
mkdir -p godot-project/scripts/scenes
mkdir -p godot-project/scenes
mkdir -p godot-project/bindings
```

- [ ] **Step 2: Create project.godot**

Create `godot-project/project.godot`:

```ini
; Engine configuration file.
; It's best edited using the editor UI and not directly,
; but it can also be edited via text for version control.

[application]

config/name="pmview-nextgen"
config/description="PCP performance metrics as living 3D environments"
run/main_scene="res://scenes/test_bars.tscn"
config/features=PackedStringArray("4.4", "C#", "Forward Plus")

[dotnet]

project/assembly_name="pmview-nextgen"
```

- [ ] **Step 3: Create pmview-nextgen.csproj**

Create `godot-project/pmview-nextgen.csproj`:

```xml
<Project Sdk="Godot.NET.Sdk/4.4.0">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>PmviewNextgen</RootNamespace>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\src\pcp-client-dotnet\src\PcpClient\PcpClient.csproj" />
    <ProjectReference Include="..\src\pcp-godot-bridge\src\PcpGodotBridge\PcpGodotBridge.csproj" />
  </ItemGroup>
</Project>
```

Note: This will NOT build in the VM (no Godot.NET.Sdk). That's expected — the Godot project builds in the user's host environment. We can still verify the file is well-formed.

- [ ] **Step 4: Create pmview-nextgen.sln**

Create `godot-project/pmview-nextgen.sln` — this is the top-level solution the user opens in their IDE, containing all projects:

```
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "pmview-nextgen", "pmview-nextgen.csproj", "{D1E2F3A4-B5C6-7890-DEF0-123456789ABC}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "PcpClient", "..\src\pcp-client-dotnet\src\PcpClient\PcpClient.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "PcpClient.Tests", "..\src\pcp-client-dotnet\tests\PcpClient.Tests\PcpClient.Tests.csproj", "{B2C3D4E5-F6A7-8901-BCDE-F12345678901}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "PcpGodotBridge", "..\src\pcp-godot-bridge\src\PcpGodotBridge\PcpGodotBridge.csproj", "{C3D4E5F6-A7B8-9012-CDEF-234567890ABC}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "PcpGodotBridge.Tests", "..\src\pcp-godot-bridge\tests\PcpGodotBridge.Tests\PcpGodotBridge.Tests.csproj", "{D4E5F6A7-B8C9-0123-DEFA-34567890ABCD}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{D1E2F3A4-B5C6-7890-DEF0-123456789ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{D1E2F3A4-B5C6-7890-DEF0-123456789ABC}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{D1E2F3A4-B5C6-7890-DEF0-123456789ABC}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{D1E2F3A4-B5C6-7890-DEF0-123456789ABC}.Release|Any CPU.Build.0 = Release|Any CPU
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}.Release|Any CPU.Build.0 = Release|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{B2C3D4E5-F6A7-8901-BCDE-F12345678901}.Release|Any CPU.Build.0 = Release|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-234567890ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-234567890ABC}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-234567890ABC}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{C3D4E5F6-A7B8-9012-CDEF-234567890ABC}.Release|Any CPU.Build.0 = Release|Any CPU
		{D4E5F6A7-B8C9-0123-DEFA-34567890ABCD}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{D4E5F6A7-B8C9-0123-DEFA-34567890ABCD}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{D4E5F6A7-B8C9-0123-DEFA-34567890ABCD}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{D4E5F6A7-B8C9-0123-DEFA-34567890ABCD}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
EndGlobal
```

- [ ] **Step 5: Commit**

```bash
git add godot-project/
git commit -m "add Godot 4.4 project scaffold with solution references

project.godot + pmview-nextgen.csproj (Godot.NET.Sdk) referencing
PcpClient and PcpGodotBridge. Solution includes all projects and
test projects."
```

---

## Chunk 4: Godot Bridge Nodes (C#)

### Task 7: Create MetricPoller bridge node

**Files:**
- Create: `godot-project/scripts/bridge/MetricPoller.cs`

- [ ] **Step 1: Create MetricPoller.cs**

Create `godot-project/scripts/bridge/MetricPoller.cs`:

```csharp
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

    private PcpClientConnection? _client;
    private Timer? _pollTimer;
    private bool _polling;

    public ConnectionState CurrentState => _client?.State ?? ConnectionState.Disconnected;

    public override void _Ready()
    {
        if (MetricNames.Length > 0)
            CallDeferred(nameof(StartPolling));
    }

    public async void StartPolling()
    {
        await ConnectToEndpoint();
        StartPollTimer();
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
            _client = new PcpClientConnection(new Uri(Endpoint));
            EmitConnectionState("Connecting");

            await _client.ConnectAsync();
            EmitConnectionState("Connected");
        }
        catch (PcpConnectionException ex)
        {
            EmitConnectionState("Failed");
            EmitSignal(SignalName.ErrorOccurred, ex.Message);
        }
    }

    private void StartPollTimer()
    {
        _pollTimer?.QueueFree();
        _pollTimer = new Timer();
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
            var dict = MarshalMetricValues(values);
            EmitSignal(SignalName.MetricsUpdated, dict);
        }
        catch (PcpConnectionException ex)
        {
            EmitConnectionState("Reconnecting");
            EmitSignal(SignalName.ErrorOccurred, ex.Message);
            await TryReconnect();
        }
        catch (PcpContextExpiredException)
        {
            // PcpClientConnection handles auto-reconnect internally
            // If we get here, reconnect failed
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
                instances[key] = iv.Value is double d ? d : Convert.ToDouble(iv.Value);
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
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add godot-project/scripts/bridge/MetricPoller.cs
git commit -m "add MetricPoller bridge node: polls PcpClient, emits Godot signals

Timer-driven polling with GDScript-friendly Dictionary signal format.
Owns PcpClientConnection lifecycle. Handles reconnection on failure."
```

### Task 8: Create SceneBinder bridge node

**Files:**
- Create: `godot-project/scripts/bridge/SceneBinder.cs`

- [ ] **Step 1: Create SceneBinder.cs**

Create `godot-project/scripts/bridge/SceneBinder.cs`:

```csharp
using Godot;
using PcpGodotBridge;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Loads scene+binding config pairs and applies metric values to scene nodes.
/// Validates properties against real nodes at scene load time.
/// Supports scene swapping at runtime (US03).
/// </summary>
public partial class SceneBinder : Node
{
    [Signal]
    public delegate void SceneLoadedEventHandler(string scenePath, string configPath);

    [Signal]
    public delegate void BindingErrorEventHandler(string message);

    private Node? _currentScene;
    private string? _currentConfigPath;
    private BindingConfig? _currentConfig;
    private readonly List<ActiveBinding> _activeBindings = new();

    /// <summary>
    /// A validated, resolved binding with a cached node reference.
    /// Only created for bindings that passed both config and scene validation.
    /// </summary>
    private record ActiveBinding(
        ResolvedBinding Resolved,
        Node TargetNode);

    public BindingConfig? CurrentConfig => _currentConfig;
    public int ActiveBindingCount => _activeBindings.Count;

    /// <summary>
    /// Load a scene and its binding config. Replaces any currently loaded scene.
    /// Returns the list of metric names needed for polling.
    /// </summary>
    public string[] LoadSceneWithBindings(string configPath)
    {
        UnloadCurrentScene();

        // Phase 1: Config validation (pure .NET)
        var configResult = BindingConfigLoader.LoadFromFile(configPath);
        LogConfigResult(configResult);

        if (!configResult.IsValid)
        {
            EmitSignal(SignalName.BindingError, "Config validation failed — see log");
            return [];
        }

        _currentConfig = configResult.Config!;
        _currentConfigPath = configPath;

        // Phase 2: Scene load + node/property validation (Godot runtime)
        var packedScene = GD.Load<PackedScene>(_currentConfig.ScenePath);
        if (packedScene == null)
        {
            EmitSignal(SignalName.BindingError,
                $"Cannot load scene: {_currentConfig.ScenePath}");
            return [];
        }

        _currentScene = packedScene.Instantiate();
        AddChild(_currentScene);

        // Phase 2 validation: resolve nodes and check properties
        ResolveAndValidateBindings();

        EmitSignal(SignalName.SceneLoaded, _currentConfig.ScenePath, configPath);

        // Return unique metric names for MetricPoller
        return _currentConfig.Bindings
            .Select(b => b.Metric)
            .Distinct()
            .ToArray();
    }

    /// <summary>
    /// Apply metric values from a MetricPoller signal to the active bindings.
    /// Called every poll cycle. Hot path — no validation, just cached lookups.
    /// </summary>
    public void ApplyMetrics(Godot.Collections.Dictionary metrics)
    {
        foreach (var active in _activeBindings)
        {
            var binding = active.Resolved.Binding;

            if (!metrics.ContainsKey(binding.Metric))
                continue;

            var metricData = metrics[binding.Metric].AsGodotDictionary();
            var instances = metricData["instances"].AsGodotDictionary();

            double? rawValue = ExtractValue(binding, instances);
            if (rawValue == null)
                continue;

            var normalised = Normalise(rawValue.Value,
                binding.SourceRangeMin, binding.SourceRangeMax,
                binding.TargetRangeMin, binding.TargetRangeMax);

            ApplyProperty(active, (float)normalised);
        }
    }

    public void UnloadCurrentScene()
    {
        _activeBindings.Clear();

        if (_currentScene != null)
        {
            _currentScene.QueueFree();
            _currentScene = null;
        }

        _currentConfig = null;
        _currentConfigPath = null;
    }

    private void ResolveAndValidateBindings()
    {
        _activeBindings.Clear();

        foreach (var binding in _currentConfig!.Bindings)
        {
            var resolved = PropertyVocabulary.Resolve(binding);

            // Resolve node in scene tree
            var node = _currentScene!.GetNodeOrNull(binding.SceneNode);
            if (node == null)
            {
                GD.PushWarning(
                    $"[SceneBinder] Node not found: '{binding.SceneNode}' — skipping binding");
                EmitSignal(SignalName.BindingError,
                    $"Node not found: '{binding.SceneNode}'");
                continue;
            }

            // Validate property exists on node
            if (!ValidatePropertyExists(node, resolved))
            {
                continue;
            }

            _activeBindings.Add(new ActiveBinding(resolved, node));
            GD.Print($"[SceneBinder] Bound: {binding.SceneNode}.{binding.Property} " +
                     $"<- {binding.Metric}");
        }

        GD.Print($"[SceneBinder] {_activeBindings.Count} active bindings " +
                 $"({_currentConfig.Bindings.Count - _activeBindings.Count} skipped)");
    }

    private bool ValidatePropertyExists(Node node, ResolvedBinding resolved)
    {
        var godotProperty = resolved.GodotPropertyName;

        // For built-in properties, check the mapped Godot property
        // For custom properties, check the @export var name directly
        var propertyList = node.GetPropertyList();
        var propertyName = godotProperty.Contains(':')
            ? godotProperty.Split(':')[0]  // "scale:y" → check "scale" exists
            : godotProperty;

        foreach (var prop in propertyList)
        {
            if (prop["name"].AsString() == propertyName)
                return true;
        }

        // Build available properties list for helpful error message
        var available = new List<string>();
        foreach (var prop in propertyList)
        {
            var name = prop["name"].AsString();
            var usage = prop["usage"].AsInt32();
            // Filter to script/exported properties (usage flags include PROPERTY_USAGE_SCRIPT_VARIABLE)
            if ((usage & (int)PropertyUsageFlags.ScriptVariable) != 0)
                available.Add(name);
        }

        var availableStr = available.Count > 0
            ? $" Available script properties: {string.Join(", ", available)}"
            : " No script properties found on this node.";

        GD.PushWarning(
            $"[SceneBinder] Property '{resolved.GodotPropertyName}' not found on " +
            $"node '{resolved.Binding.SceneNode}'.{availableStr}");
        EmitSignal(SignalName.BindingError,
            $"Property '{resolved.GodotPropertyName}' not found on " +
            $"'{resolved.Binding.SceneNode}'");

        return false;
    }

    private static double? ExtractValue(MetricBinding binding,
        Godot.Collections.Dictionary instances)
    {
        if (binding.InstanceId != null)
        {
            return instances.ContainsKey(binding.InstanceId.Value)
                ? instances[binding.InstanceId.Value].AsDouble()
                : null;
        }

        if (binding.InstanceFilter != null)
        {
            // For instance_filter, take first matching instance
            // Full glob matching is a future enhancement
            foreach (var key in instances.Keys)
            {
                return instances[key].AsDouble();
            }
            return null;
        }

        // No filter — singular metric (instance -1) or first available
        if (instances.ContainsKey(-1))
            return instances[-1].AsDouble();

        // Take first instance if available
        foreach (var key in instances.Keys)
            return instances[key].AsDouble();

        return null;
    }

    private static void ApplyProperty(ActiveBinding active, float value)
    {
        var node = active.TargetNode;
        var resolved = active.Resolved;

        switch (resolved.Kind)
        {
            case PropertyKind.BuiltIn:
                ApplyBuiltInProperty(node, resolved.Binding.Property, value);
                break;

            case PropertyKind.Custom:
                node.Set(resolved.GodotPropertyName, value);
                break;
        }
    }

    private static void ApplyBuiltInProperty(Node node, string property, float value)
    {
        if (node is not Node3D node3D)
        {
            GD.PushWarning($"[SceneBinder] Built-in property '{property}' requires " +
                           $"Node3D but got {node.GetClass()}");
            return;
        }

        switch (property)
        {
            case "height":
                node3D.Scale = new Vector3(node3D.Scale.X, value, node3D.Scale.Z);
                break;
            case "width":
                node3D.Scale = new Vector3(value, node3D.Scale.Y, node3D.Scale.Z);
                break;
            case "depth":
                node3D.Scale = new Vector3(node3D.Scale.X, node3D.Scale.Y, value);
                break;
            case "scale":
                node3D.Scale = new Vector3(value, value, value);
                break;
            case "rotation_speed":
                // Degrees per second applied as delta per frame
                node3D.RotateY(Mathf.DegToRad(value) * (float)GetProcessDeltaTime());
                break;
            case "position_y":
                node3D.Position = new Vector3(node3D.Position.X, value, node3D.Position.Z);
                break;
            case "color_temperature":
                ApplyColorTemperature(node3D, value);
                break;
            case "opacity":
                ApplyOpacity(node3D, value);
                break;
        }
    }

    private static void ApplyColorTemperature(Node3D node, float value)
    {
        if (node is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D mat)
        {
            // 0 = blue (cold), 1 = red (hot)
            var hue = Mathf.Lerp(0.66f, 0.0f, Mathf.Clamp(value, 0f, 1f));
            mat.AlbedoColor = Color.FromHsv(hue, 0.8f, 0.9f);
        }
    }

    private static void ApplyOpacity(Node3D node, float value)
    {
        if (node is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D mat)
        {
            var color = mat.AlbedoColor;
            mat.AlbedoColor = new Color(color.R, color.G, color.B, Mathf.Clamp(value, 0f, 1f));
        }
    }

    private static double Normalise(double value,
        double srcMin, double srcMax, double tgtMin, double tgtMax)
    {
        var clamped = Math.Clamp(value, srcMin, srcMax);
        var ratio = (clamped - srcMin) / (srcMax - srcMin);
        return tgtMin + ratio * (tgtMax - tgtMin);
    }

    private static double GetProcessDeltaTime()
    {
        return Engine.GetProcessFrames() > 0
            ? 1.0 / Engine.GetFramesPerSecond()
            : 1.0 / 60.0;
    }

    private void LogConfigResult(BindingConfigResult result)
    {
        foreach (var msg in result.Messages)
        {
            var prefix = msg.BindingContext != null ? $"[{msg.BindingContext}] " : "";
            var text = $"[SceneBinder] {prefix}{msg.Message}";

            switch (msg.Severity)
            {
                case ValidationSeverity.Error:
                    GD.PushError(text);
                    break;
                case ValidationSeverity.Warning:
                    GD.PushWarning(text);
                    break;
                case ValidationSeverity.Info:
                    GD.Print(text);
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add godot-project/scripts/bridge/SceneBinder.cs
git commit -m "add SceneBinder: loads scenes+configs, validates properties, applies metrics

Two-phase validation: config-level (pure .NET) then scene-level
(node existence, property existence via GetPropertyList). Two-tier
property application: built-in vocabulary for Node3D primitives,
custom pass-through for @export vars. Scene swapping support."
```

---

## Chunk 5: GDScript Controllers + Test Scenes + Binding Configs

### Task 9: Create GDScript scene controller

**Files:**
- Create: `godot-project/scripts/scenes/metric_scene_controller.gd`

- [ ] **Step 1: Create metric_scene_controller.gd**

Create `godot-project/scripts/scenes/metric_scene_controller.gd`:

```gdscript
extends Control

## Main scene controller: wires MetricPoller signals to SceneBinder.
## Displays connection status overlay. Intentionally thin.

@onready var metric_poller: Node = $MetricPoller
@onready var scene_binder: Node = $SceneBinder
@onready var status_label: Label = $StatusOverlay/StatusLabel

var _connection_state: String = "Disconnected"

func _ready() -> void:
	metric_poller.connect("MetricsUpdated", _on_metrics_updated)
	metric_poller.connect("ConnectionStateChanged", _on_connection_state_changed)
	metric_poller.connect("ErrorOccurred", _on_error_occurred)
	scene_binder.connect("SceneLoaded", _on_scene_loaded)
	scene_binder.connect("BindingError", _on_binding_error)

	_update_status_display()

func load_config(config_path: String) -> void:
	var metric_names = scene_binder.call("LoadSceneWithBindings", config_path)
	if metric_names.size() > 0:
		metric_poller.set("MetricNames", metric_names)
		metric_poller.call("StartPolling")

func _on_metrics_updated(metrics: Dictionary) -> void:
	scene_binder.call("ApplyMetrics", metrics)

func _on_connection_state_changed(state: String) -> void:
	_connection_state = state
	_update_status_display()

func _on_error_occurred(message: String) -> void:
	push_warning("[MetricSceneController] Error: %s" % message)
	_update_status_display()

func _on_scene_loaded(scene_path: String, config_path: String) -> void:
	print("[MetricSceneController] Scene loaded: %s with %s" % [scene_path, config_path])

func _on_binding_error(message: String) -> void:
	push_warning("[MetricSceneController] Binding error: %s" % message)

func _update_status_display() -> void:
	if status_label:
		status_label.text = "Connection: %s" % _connection_state
		match _connection_state:
			"Connected":
				status_label.add_theme_color_override("font_color", Color.GREEN)
			"Reconnecting", "Connecting":
				status_label.add_theme_color_override("font_color", Color.YELLOW)
			_:
				status_label.add_theme_color_override("font_color", Color.RED)
```

- [ ] **Step 2: Commit**

```bash
git add godot-project/scripts/scenes/metric_scene_controller.gd
git commit -m "add GDScript scene controller: wires MetricPoller to SceneBinder

Thin controller that connects signals, forwards metrics, and
displays connection status overlay with colour-coded state."
```

### Task 10: Create config selector UI (US03)

**Files:**
- Create: `godot-project/scripts/scenes/config_selector.gd`

- [ ] **Step 1: Create config_selector.gd**

Create `godot-project/scripts/scenes/config_selector.gd`:

```gdscript
extends Control

## Config selector UI: scans bindings/ for available configs,
## displays them in a list, triggers scene swapping on selection.

signal config_selected(config_path: String)

@onready var config_list: ItemList = $ConfigList
@onready var description_label: Label = $DescriptionLabel

var _config_paths: Array[String] = []

func _ready() -> void:
	config_list.item_selected.connect(_on_config_selected)
	scan_configs()

func scan_configs() -> void:
	config_list.clear()
	_config_paths.clear()

	var dir = DirAccess.open("res://bindings")
	if dir == null:
		push_warning("[ConfigSelector] Cannot open res://bindings/")
		return

	dir.list_dir_begin()
	var file_name = dir.get_next()

	while file_name != "":
		if file_name.ends_with(".toml"):
			var full_path = "res://bindings/%s" % file_name
			_config_paths.append(full_path)

			# Read description from TOML meta section (basic parse)
			var display_name = _read_config_description(full_path, file_name)
			config_list.add_item(display_name)
		file_name = dir.get_next()

	dir.list_dir_end()

func _read_config_description(path: String, fallback: String) -> String:
	var file = FileAccess.open(path, FileAccess.READ)
	if file == null:
		return fallback

	# Simple line-by-line scan for description field in [meta]
	var in_meta = false
	while not file.eof_reached():
		var line = file.get_line().strip_edges()
		if line == "[meta]":
			in_meta = true
		elif line.begins_with("[") and line != "[meta]":
			in_meta = false
		elif in_meta and line.begins_with("description"):
			var parts = line.split("=", true, 1)
			if parts.size() == 2:
				return parts[1].strip_edges().trim_prefix('"').trim_suffix('"')

	return fallback

func _on_config_selected(index: int) -> void:
	if index >= 0 and index < _config_paths.size():
		var path = _config_paths[index]
		description_label.text = "Loading: %s" % path
		config_selected.emit(path)
```

- [ ] **Step 2: Commit**

```bash
git add godot-project/scripts/scenes/config_selector.gd
git commit -m "add config selector UI: scans bindings/ and triggers scene swapping

Reads TOML description from [meta] for display. Emits
config_selected signal for the scene controller to handle."
```

### Task 11: Create test scenes and binding configs

**Files:**
- Create: `godot-project/scenes/test_bars.tscn`
- Create: `godot-project/scenes/disk_io_panel.tscn`
- Create: `godot-project/bindings/test_bars.toml`
- Create: `godot-project/bindings/disk_io_panel.toml`

- [ ] **Step 1: Create test_bars.tscn**

Create `godot-project/scenes/test_bars.tscn`:

```
[gd_scene load_steps=2 format=3 uid="uid://test_bars_scene"]

[sub_resource type="BoxMesh" id="BoxMesh_bar"]
size = Vector3(0.8, 1, 0.8)

[node name="TestBars" type="Node3D"]

[node name="Camera3D" type="Camera3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 0.866, 0.5, 0, -0.5, 0.866, 0, 3, 5)

[node name="DirectionalLight3D" type="DirectionalLight3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 0.707, -0.707, 0, 0.707, 0.707, 0, 5, 5)

[node name="LoadBar1Min" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, -2, 0.5, 0)
mesh = SubResource("BoxMesh_bar")

[node name="LoadBar5Min" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0.5, 0)
mesh = SubResource("BoxMesh_bar")

[node name="LoadBar15Min" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 2, 0.5, 0)
mesh = SubResource("BoxMesh_bar")
```

- [ ] **Step 2: Create disk_io_panel.tscn**

Create `godot-project/scenes/disk_io_panel.tscn`:

```
[gd_scene load_steps=3 format=3 uid="uid://disk_io_panel_scene"]

[sub_resource type="BoxMesh" id="BoxMesh_flat"]
size = Vector3(1.5, 0.5, 1.5)

[sub_resource type="CylinderMesh" id="CylinderMesh_spinner"]
top_radius = 0.4
bottom_radius = 0.4
height = 0.3

[node name="DiskIOPanel" type="Node3D"]

[node name="Camera3D" type="Camera3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 0.866, 0.5, 0, -0.5, 0.866, 0, 4, 6)

[node name="DirectionalLight3D" type="DirectionalLight3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 0.707, -0.707, 0, 0.707, 0.707, 0, 5, 5)

[node name="ReadSpinner" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, -1.5, 0.5, 0)
mesh = SubResource("CylinderMesh_spinner")

[node name="WriteBar" type="MeshInstance3D" parent="."]
transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, 1.5, 0.25, 0)
mesh = SubResource("BoxMesh_flat")
```

- [ ] **Step 3: Create test_bars.toml binding config**

Create `godot-project/bindings/test_bars.toml`:

```toml
# CPU load bar visualisation
# Maps kernel.all.load instances to vertical bar height

[meta]
scene = "res://scenes/test_bars.tscn"
poll_interval_ms = 1000
description = "CPU load averages as vertical bars"

[[bindings]]
scene_node = "LoadBar1Min"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.2, 5.0]
instance_id = 0

[[bindings]]
scene_node = "LoadBar5Min"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.2, 5.0]
instance_id = 1

[[bindings]]
scene_node = "LoadBar15Min"
metric = "kernel.all.load"
property = "height"
source_range = [0.0, 10.0]
target_range = [0.2, 5.0]
instance_id = 2
```

- [ ] **Step 4: Create disk_io_panel.toml binding config**

Create `godot-project/bindings/disk_io_panel.toml`:

```toml
# Disk I/O panel visualisation
# Different visual layout from test_bars — proves scene swapping

[meta]
scene = "res://scenes/disk_io_panel.tscn"
poll_interval_ms = 1000
description = "Disk I/O with spinner and bar"

[[bindings]]
scene_node = "ReadSpinner"
metric = "disk.dev.read"
property = "rotation_speed"
source_range = [0.0, 5000.0]
target_range = [0.0, 360.0]
instance_filter = "sd*"

[[bindings]]
scene_node = "WriteBar"
metric = "disk.dev.write"
property = "height"
source_range = [0.0, 5000.0]
target_range = [0.1, 3.0]
instance_filter = "sd*"
```

- [ ] **Step 5: Commit**

```bash
git add godot-project/scenes/ godot-project/bindings/
git commit -m "add test scenes and binding configs for CPU load and disk I/O

Two scene+config pairs prove scene swapping works:
- test_bars.tscn: 3 vertical bars for kernel.all.load averages
- disk_io_panel.tscn: spinner + bar for disk read/write"
```

---

## Chunk 6: README + Final Verification

### Task 12: Update README.md with setup and usage instructions

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update README.md**

Add sections covering: prerequisites (dotnet 8, Godot 4.4+ with .NET), dev environment setup, building, running tests, project structure overview, and how to create custom scenes with binding configs.

Key content to include:

- Prerequisites: .NET 8.0 SDK, Godot 4.4+ with .NET support
- Quick start: clone, `dotnet build`, `dotnet test`, open in Godot
- Project structure diagram showing the four layers
- How binding configs work (TOML format, property vocabulary, custom properties)
- Dev environment: podman compose for pmproxy + synthetic data
- Link to design docs

- [ ] **Step 2: Commit**

```bash
git add README.md
git commit -m "update README with setup instructions and architecture overview

Covers prerequisites, building, testing, project structure,
binding config format, and custom scene development."
```

### Task 13: Update tasks.md checkboxes and run final verification

**Files:**
- Modify: `specs/001-pcp-godot-bridge/tasks.md`

- [ ] **Step 1: Run all tests**

```bash
dotnet test src/pcp-client-dotnet/PcpClient.sln --verbosity quiet
dotnet test src/pcp-godot-bridge/PcpGodotBridge.sln --verbosity quiet
```

Expected: All tests PASS in both solutions.

- [ ] **Step 2: Mark completed tasks in tasks.md**

Update tasks T023-T035 to `[x]` in `specs/001-pcp-godot-bridge/tasks.md`.

- [ ] **Step 3: Commit**

```bash
git add specs/001-pcp-godot-bridge/tasks.md
git commit -m "mark Phase 4 (T023-T028) and Phase 5 (T029-T035) tasks complete"
```

---

## Execution Notes

**Task dependencies:**
- Tasks 1-2 (model types) must complete before Tasks 3-5 (loader + validation)
- Task 6 (Godot scaffold) is independent of Tasks 1-5, can run in parallel
- Tasks 7-8 (bridge nodes) depend on Tasks 1-5 (need PcpGodotBridge types)
- Tasks 9-11 (GDScript + scenes) depend on Task 6 (need godot-project/)
- Task 12 (README) can be done at any point
- Task 13 (verification) must be last

**What we can build and test in the VM:**
- PcpGodotBridge library: full build + test (Tasks 1-5)
- Godot bridge nodes: write files only, no build (Tasks 7-8) — needs Godot.NET.Sdk
- GDScript + scenes: write files only (Tasks 9-11)

**What the user verifies in their host environment:**
- `dotnet build godot-project/pmview-nextgen.sln` compiles the Godot project
- Open Godot editor, run test_bars scene with dev-environment pmproxy
- Switch to disk_io_panel config at runtime
