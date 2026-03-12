#if TOOLS
using Godot;
using PcpClient;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Editor dialog for browsing PCP metric namespaces from pmproxy.
/// Creates its own PcpClientConnection from ProjectSettings endpoint.
/// Navigates namespace hierarchy, shows descriptions and instances,
/// and writes selected metric + instance back to a PcpBindingResource.
/// </summary>
[Tool]
public partial class MetricBrowserDialog : Window
{
	private Tree? _tree;
	private Label? _descriptionLabel;
	private ItemList? _instanceList;
	private Button? _confirmButton;
	private Button? _cancelButton;
	private Button? _retryButton;
	private Label? _statusLabel;

	private System.Net.Http.HttpClient? _httpClient;
	private PcpClientConnection? _client;
	private PcpBindingResource? _targetBinding;
	private string _selectedMetric = "";

	public override void _Ready()
	{
		Title = "Browse PCP Metrics";
		Size = new Vector2I(600, 500);
		Exclusive = true;

		var vbox = new VBoxContainer();
		AddChild(vbox);

		_statusLabel = new Label { Text = "Connecting..." };
		vbox.AddChild(_statusLabel);

		_retryButton = new Button { Text = "Retry", Visible = false };
		_retryButton.Pressed += OnRetryPressed;
		vbox.AddChild(_retryButton);

		var split = new HSplitContainer();
		split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		vbox.AddChild(split);

		_tree = new Tree();
		_tree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_tree.ItemActivated += OnTreeItemActivated;
		_tree.ItemSelected += OnTreeItemSelected;
		split.AddChild(_tree);

		var rightPanel = new VBoxContainer();
		rightPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		split.AddChild(rightPanel);

		_descriptionLabel = new Label
		{
			Text = "Select a metric",
			AutowrapMode = TextServer.AutowrapMode.Word
		};
		rightPanel.AddChild(_descriptionLabel);

		rightPanel.AddChild(new Label { Text = "Instances:" });
		_instanceList = new ItemList();
		_instanceList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		rightPanel.AddChild(_instanceList);

		var buttonBar = new HBoxContainer();
		vbox.AddChild(buttonBar);

		_confirmButton = new Button { Text = "Confirm", Disabled = true };
		_confirmButton.Pressed += OnConfirmPressed;
		buttonBar.AddChild(_confirmButton);

		_cancelButton = new Button { Text = "Cancel" };
		_cancelButton.Pressed += OnCancelPressed;
		buttonBar.AddChild(_cancelButton);

		CloseRequested += OnCancelPressed;
	}

	public async void OpenForBinding(PcpBindingResource binding)
	{
		_targetBinding = binding;
		_selectedMetric = "";
		_confirmButton!.Disabled = true;
		_instanceList!.Clear();
		_tree!.Clear();
		_descriptionLabel!.Text = "Select a metric";

		var endpoint = ProjectSettings.GetSetting(
			"pmview/endpoint", "http://localhost:44322").AsString();

		if (string.IsNullOrEmpty(endpoint))
		{
			ShowError("Configure pmproxy endpoint in Project Settings > PCP");
			PopupCentered();
			return;
		}

		PopupCentered();
		await ConnectAndLoadRoot(endpoint);
	}

	private async Task ConnectAndLoadRoot(string endpoint)
	{
		try
		{
			_statusLabel!.Text = "Connecting...";
			_retryButton!.Visible = false;

			_httpClient?.Dispose();
			_client?.Dispose();
			_httpClient = new System.Net.Http.HttpClient();
			_client = new PcpClientConnection(new Uri(endpoint), _httpClient);
			await _client.ConnectAsync();

			_statusLabel.Text = $"Connected to {endpoint}";
			await LoadChildren("");
		}
		catch (PcpConnectionException ex)
		{
			ShowError($"Connection failed: {ex.Message}");
		}
		catch (Exception ex)
		{
			ShowError($"Error: {ex.Message}");
		}
	}

	private void ShowError(string message)
	{
		_statusLabel!.Text = message;
		_retryButton!.Visible = true;
	}

	private async Task LoadChildren(string prefix)
	{
		if (_client == null) return;

		try
		{
			var ns = await _client.GetChildrenAsync(prefix);

			TreeItem parent;
			if (string.IsNullOrEmpty(prefix))
			{
				_tree!.Clear();
				parent = _tree.CreateItem();
				parent.SetText(0, "Metrics");
			}
			else
			{
				parent = _tree!.GetSelected()!;
			}

			foreach (var nonLeaf in ns.NonLeafNames)
			{
				var item = _tree!.CreateItem(parent);
				item.SetText(0, nonLeaf);
				var fullPath = string.IsNullOrEmpty(prefix)
					? nonLeaf : $"{prefix}.{nonLeaf}";
				item.SetMetadata(0, fullPath);
				// Dummy child so it shows as expandable
				var dummy = _tree.CreateItem(item);
				dummy.SetText(0, "Loading...");
			}

			foreach (var leaf in ns.LeafNames)
			{
				var item = _tree!.CreateItem(parent);
				item.SetText(0, leaf);
				var fullPath = string.IsNullOrEmpty(prefix)
					? leaf : $"{prefix}.{leaf}";
				item.SetMetadata(0, fullPath);
				item.SetCustomColor(0, new Color(0.4f, 1.0f, 0.4f));
			}
		}
		catch (PcpException ex)
		{
			ShowError($"Browse error: {ex.Message}");
		}
	}

	private async void OnTreeItemActivated()
	{
		var selected = _tree?.GetSelected();
		if (selected == null) return;

		var path = selected.GetMetadata(0).AsString();

		// Expand namespace nodes with a "Loading..." dummy child
		var firstChild = selected.GetFirstChild();
		if (firstChild != null && firstChild.GetText(0) == "Loading...")
		{
			firstChild.Free();
			await LoadChildren(path);
		}
	}

	private async void OnTreeItemSelected()
	{
		var selected = _tree?.GetSelected();
		if (selected == null) return;

		var path = selected.GetMetadata(0).AsString();
		if (string.IsNullOrEmpty(path)) return;

		// Only describe leaf nodes (no children)
		var firstChild = selected.GetFirstChild();
		if (firstChild != null) return;

		_selectedMetric = path;
		_confirmButton!.Disabled = false;

		if (_client == null) return;

		try
		{
			var descriptors = await _client.DescribeMetricsAsync(new[] { path });
			if (descriptors.Count > 0)
			{
				var desc = descriptors[0];
				_descriptionLabel!.Text = $"{desc.Name}\n\n{desc.OneLineHelp}";
				if (!string.IsNullOrEmpty(desc.LongHelp))
					_descriptionLabel.Text += $"\n\n{desc.LongHelp}";

				// Load instances
				_instanceList!.Clear();
				try
				{
					var indom = await _client.GetInstanceDomainAsync(path);
					if (indom?.Instances != null)
					{
						foreach (var inst in indom.Instances)
							_instanceList.AddItem($"{inst.Name} (id: {inst.Id})");
					}
				}
				catch (PcpException)
				{
					// No instance domain — singular metric
				}
			}
		}
		catch (PcpMetricNotFoundException)
		{
			_descriptionLabel!.Text = $"Metric not found: {path}";
		}
		catch (PcpException ex)
		{
			_descriptionLabel!.Text = $"Error: {ex.Message}";
		}
	}

	private void OnConfirmPressed()
	{
		if (_targetBinding != null && !string.IsNullOrEmpty(_selectedMetric))
		{
			_targetBinding.MetricName = _selectedMetric;

			var selectedIdx = _instanceList?.GetSelectedItems();
			if (selectedIdx != null && selectedIdx.Length > 0)
			{
				var text = _instanceList!.GetItemText(selectedIdx[0]);
				var nameEnd = text.IndexOf(" (id:");
				if (nameEnd > 0)
					_targetBinding.InstanceName = text[..nameEnd];
			}

			_targetBinding.EmitChanged();
		}

		CleanupAndClose();
	}

	private void OnCancelPressed()
	{
		CleanupAndClose();
	}

	private void CleanupAndClose()
	{
		_client?.Dispose();
		_client = null;
		_httpClient?.Dispose();
		_httpClient = null;
		Hide();
	}

	private async void OnRetryPressed()
	{
		var endpoint = ProjectSettings.GetSetting(
			"pmview/endpoint", "http://localhost:44322").AsString();
		await ConnectAndLoadRoot(endpoint);
	}

	public override void _ExitTree()
	{
		_client?.Dispose();
		_client = null;
		_httpClient?.Dispose();
		_httpClient = null;
	}
}
#endif
