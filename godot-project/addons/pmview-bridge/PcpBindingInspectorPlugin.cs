#if TOOLS
using Godot;
using PcpClient;
using PcpGodotBridge;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Editor inspector plugin that enhances nodes with PcpBindable children.
/// Adds Browse Metrics button, offline validation display, and connected
/// validation against pmproxy.
/// </summary>
[Tool]
public partial class PcpBindingInspectorPlugin : EditorInspectorPlugin
{
	private MetricBrowserDialog? _browserDialog;

	public override bool _CanHandle(GodotObject @object)
	{
		if (@object is not Node node) return false;

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

		// Browse Metrics button
		var browseButton = new Button { Text = "Browse Metrics" };
		browseButton.Pressed += () => OnBrowsePressed(bindable);
		AddCustomControl(browseButton);

		// Offline validation display
		var validationContainer = new VBoxContainer();
		AddCustomControl(validationContainer);
		RunOfflineValidation(bindable, node, validationContainer);

		// Re-validate when bindings change
		foreach (var binding in bindable.PcpBindings)
		{
			binding?.Changed += () => RunOfflineValidation(bindable, node, validationContainer);
		}

		// Connected validation button
		var validateButton = new Button { Text = "Validate Against pmproxy" };
		validateButton.Pressed += () => OnValidatePressed(bindable, node, validationContainer);
		AddCustomControl(validateButton);
	}

	private void RunOfflineValidation(
		PcpBindable bindable, Node ownerNode, VBoxContainer container)
	{
		// Clear previous validation labels
		foreach (var child in container.GetChildren())
			child.QueueFree();

		if (bindable.PcpBindings.Count == 0)
		{
			AddValidationLabel(container, "No bindings configured", Colors.Gray);
			return;
		}

		var metricBindings = new List<MetricBinding>();
		foreach (var res in bindable.PcpBindings)
		{
			if (res == null) continue;
			var parentNode = bindable.GetParent();
			var nodeName = parentNode?.Name ?? ownerNode.Name;
			metricBindings.Add(res.ToMetricBinding(nodeName));
		}

		var messages = BindingValidator.ValidateBindingSet(metricBindings);

		var hasErrors = false;
		var hasWarnings = false;

		foreach (var msg in messages)
		{
			var color = msg.Severity switch
			{
				ValidationSeverity.Error => Colors.Red,
				ValidationSeverity.Warning => Colors.Yellow,
				_ => Colors.Gray,
			};

			if (msg.Severity == ValidationSeverity.Error) hasErrors = true;
			if (msg.Severity == ValidationSeverity.Warning) hasWarnings = true;

			// Skip info messages in the display (too noisy for inspector)
			if (msg.Severity == ValidationSeverity.Info) continue;

			AddValidationLabel(container, msg.Message, color);
		}

		if (!hasErrors && !hasWarnings)
		{
			AddValidationLabel(container, "Bindings valid (offline check)", Colors.Green);
		}
	}

	private async void OnValidatePressed(
		PcpBindable bindable, Node ownerNode, VBoxContainer container)
	{
		// Clear and show connecting status
		foreach (var child in container.GetChildren())
			child.QueueFree();

		AddValidationLabel(container, "Connecting to pmproxy...", Colors.Yellow);

		var endpoint = ProjectSettings.GetSetting(
			"pmview/endpoint", "http://localhost:44322").AsString();

		if (string.IsNullOrEmpty(endpoint))
		{
			foreach (var child in container.GetChildren())
				child.QueueFree();
			AddValidationLabel(container,
				"Configure pmproxy endpoint in Project Settings > PCP", Colors.Red);
			return;
		}

		System.Net.Http.HttpClient? httpClient = null;
		PcpClientConnection? client = null;
		try
		{
			httpClient = new System.Net.Http.HttpClient();
			client = new PcpClientConnection(new Uri(endpoint), httpClient);
			await client.ConnectAsync();

			foreach (var child in container.GetChildren())
				child.QueueFree();

			var allValid = true;

			foreach (var res in bindable.PcpBindings)
			{
				if (res == null || string.IsNullOrEmpty(res.MetricName)) continue;

				try
				{
					var descriptors = await client.DescribeMetricsAsync(
						new[] { res.MetricName });

					if (descriptors.Count == 0)
					{
						AddValidationLabel(container,
							$"Metric not found: {res.MetricName}", Colors.Red);
						allValid = false;
						continue;
					}

					// Check instance if specified
					if (!string.IsNullOrEmpty(res.InstanceName))
					{
						try
						{
							var indom = await client.GetInstanceDomainAsync(res.MetricName);
							var found = false;
							if (indom?.Instances != null)
							{
								foreach (var inst in indom.Instances)
								{
									if (inst.Name == res.InstanceName)
									{
										found = true;
										break;
									}
								}
							}

							if (!found)
							{
								AddValidationLabel(container,
									$"Instance '{res.InstanceName}' not found for {res.MetricName}",
									Colors.Yellow);
								allValid = false;
							}
						}
						catch (PcpException)
						{
							// No instance domain — singular metric but instance specified
							AddValidationLabel(container,
								$"No instances for {res.MetricName} but InstanceName set",
								Colors.Yellow);
							allValid = false;
						}
					}
				}
				catch (PcpMetricNotFoundException)
				{
					AddValidationLabel(container,
						$"Metric not found: {res.MetricName}", Colors.Red);
					allValid = false;
				}
				catch (PcpException ex)
				{
					AddValidationLabel(container,
						$"Error checking {res.MetricName}: {ex.Message}", Colors.Red);
					allValid = false;
				}
			}

			if (allValid)
			{
				AddValidationLabel(container,
					"All bindings validated against pmproxy", Colors.Green);
			}
		}
		catch (PcpConnectionException ex)
		{
			foreach (var child in container.GetChildren())
				child.QueueFree();
			AddValidationLabel(container,
				$"pmproxy unreachable: {ex.Message}", Colors.Red);

			// Re-run offline validation so we still show those results
			RunOfflineValidation(bindable, ownerNode, container);
		}
		catch (Exception ex)
		{
			foreach (var child in container.GetChildren())
				child.QueueFree();
			AddValidationLabel(container, $"Error: {ex.Message}", Colors.Red);
		}
		finally
		{
			client?.Dispose();
			httpClient?.Dispose();
		}
	}

	private static void AddValidationLabel(VBoxContainer container, string text, Color color)
	{
		var label = new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.Word,
		};
		label.AddThemeColorOverride("font_color", color);
		container.AddChild(label);
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

		var lastBinding = bindable.PcpBindings[^1];
		if (lastBinding != null)
			_browserDialog.OpenForBinding(lastBinding);
	}
}
#endif
