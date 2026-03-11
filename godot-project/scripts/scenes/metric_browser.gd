extends Control

## Metric namespace browser: tree view of PCP metric namespace with
## metric details panel and instance domain display.
## Connects to MetricBrowser C# bridge for discovery operations.

signal metric_chosen(metric_name: String)

@onready var namespace_tree: Tree = $HSplitContainer/NamespaceTree
@onready var details_panel: VBoxContainer = $HSplitContainer/DetailsPanel
@onready var metric_name_label: Label = $HSplitContainer/DetailsPanel/MetricNameLabel
@onready var type_label: Label = $HSplitContainer/DetailsPanel/TypeLabel
@onready var semantics_label: Label = $HSplitContainer/DetailsPanel/SemanticsLabel
@onready var units_label: Label = $HSplitContainer/DetailsPanel/UnitsLabel
@onready var help_label: RichTextLabel = $HSplitContainer/DetailsPanel/HelpLabel
@onready var instances_list: ItemList = $HSplitContainer/DetailsPanel/InstancesList
@onready var visualise_button: Button = $HSplitContainer/DetailsPanel/VisualiseButton

var _browser: Node  # MetricBrowser C# node
var _tree_root: TreeItem
var _selected_metric: String = ""
var _pending_prefix_to_item: Dictionary = {}  # prefix -> TreeItem
var _expanding_prefixes: Array[String] = []  # tracks in-flight expansions


func _ready() -> void:
	_browser = get_node_or_null("/root/Main/MetricBrowser")
	if _browser == null:
		push_warning("[MetricBrowser] MetricBrowser C# node not found")
		return

	_browser.connect("ChildrenLoaded", _on_children_loaded)
	_browser.connect("MetricDescribed", _on_metric_described)
	_browser.connect("InstanceDomainLoaded", _on_instance_domain_loaded)
	_browser.connect("DiscoveryError", _on_discovery_error)

	namespace_tree.item_selected.connect(_on_tree_item_selected)
	namespace_tree.item_collapsed.connect(_on_tree_item_collapsed)
	visualise_button.pressed.connect(_on_visualise_pressed)
	visualise_button.disabled = true

	# Start by loading the root namespace
	_tree_root = namespace_tree.create_item()
	_tree_root.set_text(0, "PCP Metrics")
	_browse_prefix(_tree_root, "")


func _browse_prefix(parent_item: TreeItem, prefix: String) -> void:
	# Guard against duplicate in-flight requests for the same prefix
	if prefix in _expanding_prefixes:
		return
	_pending_prefix_to_item[prefix] = parent_item
	_expanding_prefixes.append(prefix)
	_browser.call("BrowseChildren", prefix)


func _on_children_loaded(prefix: String, leaves: Array, non_leaves: Array) -> void:
	var parent_item: TreeItem = _pending_prefix_to_item.get(prefix)
	if parent_item == null:
		return
	_pending_prefix_to_item.erase(prefix)
	_expanding_prefixes.erase(prefix)

	# Remove placeholder child if present
	var first_child = parent_item.get_first_child()
	if first_child and first_child.get_text(0) == "Loading...":
		parent_item.remove_child(first_child)

	# Add non-leaf (subtree) items — expandable
	for name in non_leaves:
		var child = namespace_tree.create_item(parent_item)
		var full_prefix = "%s.%s" % [prefix, name] if prefix != "" else name
		child.set_text(0, name)
		child.set_metadata(0, {"type": "nonleaf", "prefix": full_prefix})
		# Add placeholder so the expand arrow appears
		var placeholder = namespace_tree.create_item(child)
		placeholder.set_text(0, "Loading...")

	# Add leaf (metric) items — selectable
	for name in leaves:
		var child = namespace_tree.create_item(parent_item)
		var full_name = "%s.%s" % [prefix, name] if prefix != "" else name
		child.set_text(0, name)
		child.set_metadata(0, {"type": "leaf", "metric": full_name})


func _on_tree_item_collapsed(item: TreeItem) -> void:
	if item.collapsed:
		return

	# When expanding a non-leaf, load its children if we haven't yet
	var meta = item.get_metadata(0)
	if meta == null or meta.get("type") != "nonleaf":
		return

	var first_child = item.get_first_child()
	if first_child and first_child.get_text(0) == "Loading...":
		_browse_prefix(item, meta["prefix"])


func _on_tree_item_selected() -> void:
	var selected = namespace_tree.get_selected()
	if selected == null:
		return

	var meta = selected.get_metadata(0)
	if meta == null or meta.get("type") != "leaf":
		_clear_details()
		return

	_selected_metric = meta["metric"]
	metric_name_label.text = _selected_metric
	_browser.call("DescribeMetric", _selected_metric)
	_browser.call("LoadInstanceDomain", _selected_metric)


func _on_metric_described(descriptor: Dictionary) -> void:
	type_label.text = "Type: %s" % descriptor.get("type", "unknown")
	semantics_label.text = "Semantics: %s" % descriptor.get("semantics", "unknown")

	var units_text = descriptor.get("units", "")
	units_label.text = "Units: %s" % units_text if units_text != "" else "Units: (none)"

	var help_text = descriptor.get("one_line_help", "")
	var long_help = descriptor.get("long_help", "")
	if long_help != "":
		help_label.text = "%s\n\n%s" % [help_text, long_help]
	else:
		help_label.text = help_text

	visualise_button.disabled = false


func _on_instance_domain_loaded(metric_name: String, instances) -> void:
	instances_list.clear()

	if instances.is_empty():
		instances_list.add_item("(singular metric - no instances)")
		return

	for inst in instances:
		instances_list.add_item("%s (id: %d)" % [inst["name"], inst["id"]])


func _on_discovery_error(message: String) -> void:
	push_warning("[MetricBrowser] Discovery error: %s" % message)


func _on_visualise_pressed() -> void:
	if _selected_metric != "":
		metric_chosen.emit(_selected_metric)


func _clear_details() -> void:
	metric_name_label.text = ""
	type_label.text = "Type:"
	semantics_label.text = "Semantics:"
	units_label.text = "Units:"
	help_label.text = ""
	instances_list.clear()
	visualise_button.disabled = true
