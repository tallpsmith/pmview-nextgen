#if TOOLS
using Godot;
using PcpGodotBridge;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Editor inspector plugin that enhances nodes with PcpBindable children.
/// Adds a "Browse Metrics" button that opens MetricBrowserDialog.
/// </summary>
[Tool]
public partial class PcpBindingInspectorPlugin : EditorInspectorPlugin
{
	private MetricBrowserDialog? _browserDialog;

	public override bool _CanHandle(GodotObject @object)
	{
		if (@object is not Node node) return false;

		// Handle nodes that have a PcpBindable child
		foreach (var child in node.GetChildren())
		{
			if (child is PcpBindable)
				return true;
		}

		return @object is PcpBindable;
	}

	public override void _ParseEnd(GodotObject @object)
	{
		if (@object is not Node node) return;

		// Find the PcpBindable (could be the object itself or a child)
		PcpBindable? bindable = node as PcpBindable;
		if (bindable == null)
		{
			foreach (var child in node.GetChildren())
			{
				if (child is PcpBindable b)
				{
					bindable = b;
					break;
				}
			}
		}

		if (bindable == null) return;

		// Add Browse Metrics button
		var browseButton = new Button { Text = "Browse Metrics" };
		browseButton.Pressed += () => OnBrowsePressed(bindable);
		AddCustomControl(browseButton);
	}

	private void OnBrowsePressed(PcpBindable bindable)
	{
		if (bindable.PcpBindings.Count == 0)
		{
			GD.PushWarning(
				"[PcpBindingInspectorPlugin] Add a binding first before browsing metrics");
			return;
		}

		if (_browserDialog == null)
		{
			_browserDialog = new MetricBrowserDialog();
			EditorInterface.Singleton.GetBaseControl().AddChild(_browserDialog);
		}

		// Open for the last binding in the array (most recently added)
		var lastBinding = bindable.PcpBindings[^1];
		if (lastBinding != null)
			_browserDialog.OpenForBinding(lastBinding);
	}
}
#endif
