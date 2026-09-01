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

    /// <summary>Whether the shortcut belongs to the section pinned at the top of the
    /// panel. Independent of <see cref="Enabled"/>, and deliberately never reflected in
    /// the order of <see cref="Configuration.Entries"/>: both views group the list as
    /// they draw it, so clearing this drops the tile back exactly where it was.</summary>
    public bool Favourite { get; set; }

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

    /// <summary>Whether a plugin installed after the first run arrives enabled.
    /// Off by default: a bar that grows on its own is a surprise, and the new-plugin
    /// flag already surfaces the arrival. On, for anyone who would rather have a
    /// shortcut to whatever they just installed without asking for it.</summary>
    public bool AutoEnableNewPlugins { get; set; }

    /// <summary>The order of this list is the order of the grid.</summary>
    public List<ShortcutEntry> Entries { get; set; } = new();

    /// <summary>True until the initial reconciliation has run. A first launch
    /// discovers the whole catalog at once: it is pre-enabled whatever
    /// <see cref="AutoEnableNewPlugins"/> says, and none of it is news. Every plugin
    /// seen after that is a genuine arrival, and is flagged as one.</summary>
    public bool FirstRunPending { get; set; } = true;

    public static Configuration Load()
        => Service.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

    public void Save() => Service.PluginInterface.SavePluginConfig(this);
}
