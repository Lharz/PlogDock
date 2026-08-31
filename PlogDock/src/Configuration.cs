using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;

namespace PlogDock;

/// <summary>One shortcut in the bar. Kept for every plugin ever seen, not just
/// the enabled ones, so unchecking never loses its position or its overrides.</summary>
[Serializable]
internal sealed class ShortcutEntry
{
    public string InternalName { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    /// <summary>Name of a FontAwesome glyph replacing the plugin icon, or null.</summary>
    public string? IconOverride { get; set; }

    /// <summary>Set when the plugin appeared after the first run. Flags it in the
    /// settings list so a newly installed plugin can be found among a hundred
    /// others, and cleared once that list has been seen.</summary>
    public bool IsNew { get; set; }
}

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public int Columns { get; set; } = 6;

    public float IconSize { get; set; } = 32f;

    public bool ButtonLocked { get; set; }

    /// <summary>Where the panel unfolds relative to the button.</summary>
    public BarPlacement Placement { get; set; } = BarPlacement.Right;

    /// <summary>
    /// Screen position of the toggle button. Owned here rather than left to ImGui:
    /// unfolding upwards or leftwards means pinning the window by a corner other than
    /// its top left every frame, which overrides whatever dalamudUI.ini holds.
    /// </summary>
    public Vector2 AnchorPosition { get; set; }

    /// <summary>False until the anchor has been seeded from the window's actual
    /// position, so upgrading from a build that let ImGui own the position does not
    /// teleport the bar.</summary>
    public bool AnchorInitialised { get; set; }

    /// <summary>Whether the bar is unfolded. Restored across sessions.</summary>
    public bool Expanded { get; set; }

    /// <summary>The order of this list is the order of the grid.</summary>
    public List<ShortcutEntry> Entries { get; set; } = new();

    /// <summary>True until the initial reconciliation has run. Tells a first
    /// launch, where plugins exposing a main UI are pre-enabled, apart from a
    /// later install, where a new plugin arrives disabled.</summary>
    public bool FirstRunPending { get; set; } = true;

    public static Configuration Load()
        => Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
