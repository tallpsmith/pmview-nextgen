#if TOOLS
using Godot;

namespace PmviewNextgen.Bridge;

/// <summary>
/// Registers pmview/* world configuration in Godot ProjectSettings
/// so developers can configure archive/live mode before pressing Play.
/// Editor-only — runtime code reads ProjectSettings directly.
/// </summary>
[Tool]
public partial class PmviewBridgePlugin : EditorPlugin
{
    private static readonly (string Key, Variant Default, Variant.Type Type,
        PropertyHint Hint, string HintString)[] Settings =
    [
        ("pmview/endpoint", "http://localhost:44322",
            Variant.Type.String, PropertyHint.None, ""),
        ("pmview/mode", 0,
            Variant.Type.Int, PropertyHint.Enum, "Archive:0,Live:1"),
        ("pmview/archive_start_timestamp", "",
            Variant.Type.String, PropertyHint.None, ""),
        ("pmview/archive_speed", 10.0f,
            Variant.Type.Float, PropertyHint.Range, "0.1,100.0,0.1"),
        ("pmview/archive_loop", false,
            Variant.Type.Bool, PropertyHint.None, ""),
    ];

    public override void _EnterTree()
    {
        foreach (var (key, defaultValue, type, hint, hintString) in Settings)
        {
            if (!ProjectSettings.HasSetting(key))
                ProjectSettings.SetSetting(key, defaultValue);

            ProjectSettings.SetInitialValue(key, defaultValue);

            ProjectSettings.AddPropertyInfo(new Godot.Collections.Dictionary
            {
                { "name", key },
                { "type", (int)type },
                { "hint", (int)hint },
                { "hint_string", hintString },
            });
        }
    }

    public override void _ExitTree()
    {
        // Settings persist in project.godot — no cleanup needed.
    }
}
#endif
