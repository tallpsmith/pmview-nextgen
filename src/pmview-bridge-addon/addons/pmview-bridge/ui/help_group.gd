class_name HelpGroup
extends RefCounted

## A named group of key bindings for the HelpPanel.
## Each group has a header colour and can be individually enabled/disabled.

var group_name: String
var header_color: Color
var entries: Array[HelpEntry] = []
var enabled: bool = true


static func create(p_name: String, p_color: Color, p_entries: Array[HelpEntry],
		p_enabled: bool = true) -> HelpGroup:
	var g := HelpGroup.new()
	g.group_name = p_name
	g.header_color = p_color
	g.entries = p_entries
	g.enabled = p_enabled
	return g


class HelpEntry:
	extends RefCounted
	var key_text: String
	var action_text: String
	var enabled: bool = true

	static func create(p_key: String, p_action: String,
			p_enabled: bool = true) -> HelpEntry:
		var e := HelpEntry.new()
		e.key_text = p_key
		e.action_text = p_action
		e.enabled = p_enabled
		return e
