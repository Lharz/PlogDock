using System.Collections.Generic;

namespace PlogDock;

/// <summary>
/// The one place that decides how the shortcut list is cut into blocks, so the panel
/// and the settings list can never disagree about it. Both walk <see cref="Order"/>
/// and keep the shortcuts <see cref="Of"/> puts in the section being drawn.
/// <para>
/// There are two, favourites and everything else, and the notion stops there on
/// purpose: user-defined groups have been discussed but not decided, and half a group
/// system sitting in the configuration would have to be worked around until they are.
/// Should they land they replace the three members below and nothing around them —
/// both views draw whatever sections they are handed, in the order they are handed
/// them, and neither knows the word favourite.
/// </para>
/// </summary>
internal static class ShortcutSections
{
    public const string Favourites = "Favourites";

    public const string Others = "Others";

    /// <summary>The sections, in the order they are drawn.</summary>
    public static IReadOnlyList<string> Order { get; } = [Favourites, Others];

    /// <summary>The section a shortcut belongs to.</summary>
    public static string Of(ShortcutEntry shortcut)
        => shortcut.Favourite ? Favourites : Others;

    /// <summary>Moves a shortcut into a section, the inverse of <see cref="Of"/>.</summary>
    public static void Assign(ShortcutEntry shortcut, string section)
        => shortcut.Favourite = section == Favourites;
}
