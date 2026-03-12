using Godot;
using GdUnit4;
using PmviewNextgen.Bridge;
using static GdUnit4.Assertions;

namespace PmviewNextgen.Tests;

/// <summary>
/// Tests for MetricBrowser: null/invalid poller connections and
/// disconnected discovery operations. These validate defensive
/// behaviour without requiring a real PCP connection.
/// </summary>
[TestSuite]
public partial class MetricBrowserTests
{
	[TestCase]
	[RequireGodotRuntime]
	public async Task ConnectToPoller_WithNull_NoCrash()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var browser = new MetricBrowser();
		runner.Scene().AddChild(browser);

		// Passing null should not throw — just log a warning
		browser.ConnectToPoller(null!);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task ConnectToPoller_WithNonPollerNode_NoCrash()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var browser = new MetricBrowser();
		var notAPoller = new Node3D();
		runner.Scene().AddChild(browser);
		runner.Scene().AddChild(notAPoller);

		// Should warn, not crash
		browser.ConnectToPoller(notAPoller);
		await runner.AwaitIdleFrame();
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task BrowseChildren_WhenNotConnected_EmitsDiscoveryError()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var browser = new MetricBrowser();
		runner.Scene().AddChild(browser);

		var errors = new List<string>();
		browser.DiscoveryError += msg => errors.Add(msg);

		browser.BrowseChildren("kernel");
		await Task.Delay(100);

		AssertThat(errors).HasSize(1);
		AssertThat(errors[0]).Contains("Not connected");
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task DescribeMetric_WhenNotConnected_EmitsDiscoveryError()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var browser = new MetricBrowser();
		runner.Scene().AddChild(browser);

		var errors = new List<string>();
		browser.DiscoveryError += msg => errors.Add(msg);

		browser.DescribeMetric("kernel.all.load");
		await Task.Delay(100);

		AssertThat(errors).HasSize(1);
		AssertThat(errors[0]).Contains("Not connected");
	}

	[TestCase]
	[RequireGodotRuntime]
	public async Task LoadInstanceDomain_WhenNotConnected_EmitsDiscoveryError()
	{
		var runner = ISceneRunner.Load("res://test/scenes/test_node3d.tscn");
		var browser = new MetricBrowser();
		runner.Scene().AddChild(browser);

		var errors = new List<string>();
		browser.DiscoveryError += msg => errors.Add(msg);

		browser.LoadInstanceDomain("kernel.all.load");
		await Task.Delay(100);

		AssertThat(errors).HasSize(1);
		AssertThat(errors[0]).Contains("Not connected");
	}
}
