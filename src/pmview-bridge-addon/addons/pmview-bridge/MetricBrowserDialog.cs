#if TOOLS
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Microsoft.Extensions.Logging;
using PcpClient;
using PmviewApp;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Editor dialog for browsing PCP metric namespaces from pmproxy.
/// Supports live mode (lazy namespace traversal via /pmapi/) and
/// archive mode (full tree population via /series/ endpoints).
/// Writes selected metric + instance back to a PcpBindingResource.
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

	private OptionButton? _hostDropdown;
	private ArchiveMetricDiscoverer? _discoverer;
	private bool _isArchiveMode;

	private ILogger? _log;
	private ILogger Log => _log ??= PmviewLogger.GetLogger("MetricBrowser");

	public override void _Ready()
	{
		Title = "Browse PCP Metrics";
		Size = new Vector2I(600, 500);
		Exclusive = true;

		var vbox = new VBoxContainer();
		AddChild(vbox);
		vbox.AnchorRight = 1;
		vbox.AnchorBottom = 1;
		vbox.OffsetLeft = 8;
		vbox.OffsetTop = 8;
		vbox.OffsetRight = -8;
		vbox.OffsetBottom = -8;

		_statusLabel = new Label { Text = "Connecting..." };
		vbox.AddChild(_statusLabel);

		_retryButton = new Button { Text = "Retry", Visible = false };
		_retryButton.Pressed += OnRetryPressed;
		vbox.AddChild(_retryButton);

		_hostDropdown = new OptionButton();
		_hostDropdown.Visible = false;
		_hostDropdown.ItemSelected += OnHostSelected;
		vbox.AddChild(_hostDropdown);

		var split = new HSplitContainer();
		split.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
#pragma warning disable CS0618 // SplitOffset deprecated but SplitOffsets API not yet available
		split.SplitOffset = 250;
#pragma warning restore CS0618
		vbox.AddChild(split);

		_tree = new Tree();
		_tree.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_tree.CustomMinimumSize = new Vector2(200, 0);
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

		_isArchiveMode = ProjectSettings.GetSetting("pmview/mode", 0).AsInt32() == 0;
		Log.LogInformation("Opening — endpoint={Endpoint}, mode={Mode}", endpoint, _isArchiveMode ? "archive" : "live");

		if (string.IsNullOrEmpty(endpoint))
		{
			ShowError("Configure pmproxy endpoint in Project Settings > PCP");
			PopupCentered();
			return;
		}

		PopupCentered();

		if (_isArchiveMode)
			await ConnectAndLoadHosts(endpoint);
		else
			await ConnectAndLoadRoot(endpoint);
	}

	private async Task ConnectAndLoadRoot(string endpoint)
	{
		try
		{
			Log.LogInformation("Live: connecting to {Endpoint}...", endpoint);
			_statusLabel!.Text = "Connecting...";
			_retryButton!.Visible = false;

			_httpClient?.Dispose();
			_client?.Dispose();
			_httpClient = new System.Net.Http.HttpClient();
			_client = new PcpClientConnection(new Uri(endpoint), _httpClient);
			await _client.ConnectAsync();

			Log.LogInformation("Live: connected, loading root namespace...");
			_statusLabel.Text = $"Connected to {endpoint}";
			await LoadChildren("");
		}
		catch (PcpConnectionException ex)
		{
			Log.LogWarning("Live: connection failed — {Message}", ex.Message);
			ShowError($"Connection failed: {ex.Message}");
		}
		catch (Exception ex)
		{
			Log.LogWarning("Live: error — {Message}", ex.Message);
			ShowError($"Error: {ex.Message}");
		}
	}

	private async Task ConnectAndLoadHosts(string endpoint)
	{
		try
		{
			Log.LogInformation("Archive: discovering hosts at {Endpoint}...", endpoint);
			_statusLabel!.Text = "Discovering hosts...";
			_retryButton!.Visible = false;
			_hostDropdown!.Visible = true;
			_hostDropdown.Clear();

			_httpClient?.Dispose();
			_client?.Dispose();
			_httpClient = new System.Net.Http.HttpClient();
			_discoverer = new ArchiveMetricDiscoverer(new Uri(endpoint), _httpClient);

			var hosts = await _discoverer.GetHostnamesAsync();
			Log.LogInformation("Archive: found {HostCount} hosts: {Hosts}", hosts.Count, string.Join(", ", hosts));

			if (hosts.Count == 0)
			{
				ShowError("No archived hosts found");
				return;
			}

			foreach (var host in hosts)
				_hostDropdown.AddItem(host);

			_statusLabel.Text = $"Archive: select a host ({hosts.Count} available)";
		}
		catch (Exception ex)
		{
			Log.LogWarning("Archive: host discovery failed — {Message}", ex.Message);
			ShowError($"Host discovery failed: {ex.Message}");
		}
	}

	private async void OnHostSelected(long index)
	{
		var hostname = _hostDropdown!.GetItemText((int)index);
		Log.LogInformation("Archive: host selected — {Hostname}", hostname);
		_statusLabel!.Text = $"Archive: {hostname} — loading metrics...";
		_tree!.Clear();

		try
		{
			var metricNames = await _discoverer!.DiscoverMetricsForHostAsync(hostname);
			Log.LogInformation("Archive: discovered {MetricCount} metrics for {Hostname}", metricNames.Count, hostname);

			if (metricNames.Count == 0)
			{
				_statusLabel.Text = $"Archive: {hostname} — no metrics found";
				return;
			}

			var tree = NamespaceTreeBuilder.BuildTree(metricNames);
			var root = _tree.CreateItem();
			root.SetText(0, hostname);

			PopulateTreeFromNamespace(root, tree);

			_statusLabel.Text = $"Archive: {hostname} — {metricNames.Count} metrics";
		}
		catch (Exception ex)
		{
			Log.LogWarning("Archive: metric discovery failed — {Message}", ex.Message);
			ShowError($"Metric discovery failed: {ex.Message}");
		}
	}

	private void PopulateTreeFromNamespace(TreeItem parent, IReadOnlyList<NamespaceNode> nodes)
	{
		foreach (var node in nodes)
		{
			var item = _tree!.CreateItem(parent);
			item.SetText(0, node.Name);
			item.SetMetadata(0, node.FullPath);

			if (node.IsLeaf)
			{
				item.SetCustomColor(0, new Color(0.4f, 1.0f, 0.4f));
			}
			else
			{
				PopulateTreeFromNamespace(item, node.Children);
			}
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
			Log.LogInformation("Live: loading children for prefix='{Prefix}'", prefix);
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
		if (_isArchiveMode) return; // archive tree is fully populated

		var selected = _tree?.GetSelected();
		if (selected == null) return;

		var path = selected.GetMetadata(0).AsString();

		var firstChild = selected.GetFirstChild();
		if (firstChild != null && firstChild.GetText(0) == "Loading...")
		{
			firstChild.Free();
			try
			{
				await LoadChildren(path);
			}
			catch (Exception ex)
			{
				ShowError($"Browse error: {ex.Message}");
			}
		}
	}

	private async void OnTreeItemSelected()
	{
		var selected = _tree?.GetSelected();
		if (selected == null) return;

		var path = selected.GetMetadata(0).AsString();
		if (string.IsNullOrEmpty(path)) return;

		// Only describe leaf nodes (no children in either mode)
		if (selected.GetFirstChild() != null) return;

		_selectedMetric = path;
		_confirmButton!.Disabled = false;
		Log.LogInformation("Leaf selected: {Path}", path);

		if (_isArchiveMode)
		{
			await DescribeMetricFromArchive(path);
		}
		else
		{
			await DescribeMetricFromLive(path);
		}
	}

	private async Task DescribeMetricFromArchive(string metricName)
	{
		if (_discoverer == null || _hostDropdown == null || _hostDropdown.Selected < 0) return;

		var hostname = _hostDropdown.GetItemText(_hostDropdown.Selected);

		try
		{
			var detail = await _discoverer.DescribeMetricAsync(metricName, hostname);
			Log.LogInformation("Archive describe: {MetricName} — type={Type}, semantics={Semantics}, instances={InstanceCount}", detail.Name, detail.Type, detail.Semantics, detail.Instances.Count);

			_descriptionLabel!.Text = $"{detail.Name}";
			if (detail.Semantics != null)
				_descriptionLabel.Text += $"\n\nSemantics: {detail.Semantics}";
			if (detail.Type != null)
				_descriptionLabel.Text += $"\nType: {detail.Type}";
			if (detail.Units != null)
				_descriptionLabel.Text += $"\nUnits: {detail.Units}";

			_instanceList!.Clear();
			if (detail.Instances.Count > 0)
			{
				foreach (var inst in detail.Instances)
					_instanceList.AddItem($"{inst.Name} (id: {inst.PcpInstanceId})");
			}
		}
		catch (Exception ex)
		{
			_descriptionLabel!.Text = $"Error: {ex.Message}";
		}
	}

	private async Task DescribeMetricFromLive(string metricName)
	{
		if (_client == null) return;

		try
		{
			var descriptors = await _client.DescribeMetricsAsync(new[] { metricName });
			Log.LogInformation("Live describe: got {DescriptorCount} descriptors for {MetricName}", descriptors.Count, metricName);
			if (descriptors.Count > 0)
			{
				var desc = descriptors[0];
				_descriptionLabel!.Text = $"{desc.Name}\n\n{desc.OneLineHelp}";
				if (!string.IsNullOrEmpty(desc.LongHelp))
					_descriptionLabel.Text += $"\n\n{desc.LongHelp}";

				_instanceList!.Clear();
				try
				{
					var indom = await _client.GetInstanceDomainAsync(metricName);
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
			_descriptionLabel!.Text = $"Metric not found: {metricName}";
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
			Log.LogInformation("Confirmed: metric={Metric}", _selectedMetric);
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
		_discoverer = null; // doesn't own HttpClient
		_httpClient?.Dispose();
		_httpClient = null;
		if (_hostDropdown != null)
			_hostDropdown.Visible = false;
		Hide();
	}

	private async void OnRetryPressed()
	{
		var endpoint = ProjectSettings.GetSetting(
			"pmview/endpoint", "http://localhost:44322").AsString();
		if (_isArchiveMode)
			await ConnectAndLoadHosts(endpoint);
		else
			await ConnectAndLoadRoot(endpoint);
	}

	public override void _ExitTree()
	{
		_client?.Dispose();
		_client = null;
		_discoverer = null;
		_httpClient?.Dispose();
		_httpClient = null;
		if (_hostDropdown != null)
			_hostDropdown.Visible = false;
	}
}
#endif
