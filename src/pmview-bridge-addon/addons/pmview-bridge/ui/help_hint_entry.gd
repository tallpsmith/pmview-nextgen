class_name HelpHintEntry
extends RefCounted

## A single cycling hint entry for the HelpHint node.

var key_text: String
var action_text: String


static func create(p_key: String, p_action: String) -> HelpHintEntry:
	var h := HelpHintEntry.new()
	h.key_text = p_key
	h.action_text = p_action
	return h
