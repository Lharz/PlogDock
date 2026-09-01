using System;
using System.Linq;
using System.Numerics;

namespace PlogDock;

/// <summary>
/// Where the shortcut panel appears relative to the toggle button, named after the
/// cell clicked in the settings picker: BottomRight means the panel sits below the
/// button and extends to its right.
/// </summary>
internal enum BarPlacement
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>
/// Pure geometry for a placement. ImGui lays content out from the top left and grows
/// a window from its top left corner, so unfolding upwards or leftwards is achieved
/// by two separate means: the drawing order puts the button at the right corner of
/// the window, and the pivot anchors the window by that same corner so it grows away
/// from the button instead of pushing it.
/// </summary>
internal static class BarPlacementGeometry
{
    /// <summary>Whether the panel sits beside the button rather than above or below.</summary>
    public static bool IsHorizontal(this BarPlacement placement)
        => placement is BarPlacement.Left or BarPlacement.Right;

    /// <summary>Whether the panel is drawn before the button.</summary>
    public static bool PanelFirst(this BarPlacement placement)
        => placement is BarPlacement.TopLeft
            or BarPlacement.Top
            or BarPlacement.TopRight
            or BarPlacement.Left;

    /// <summary>
    /// Where the button sits along the window's width, for the placements that stack
    /// vertically: 0 at the left edge, 0.5 centred, 1 at the right edge.
    /// </summary>
    public static float ButtonAlign(this BarPlacement placement) => placement switch
    {
        BarPlacement.TopRight or BarPlacement.BottomRight => 0f,
        BarPlacement.Top or BarPlacement.Bottom => 0.5f,
        BarPlacement.TopLeft or BarPlacement.BottomLeft => 1f,
        _ => 0f,
    };

    /// <summary>
    /// The pivot handed to SetNextWindowPos, expressed as the corner of the window
    /// the button occupies, so that anchoring that corner keeps the button still.
    /// </summary>
    public static Vector2 Pivot(this BarPlacement placement) => placement switch
    {
        BarPlacement.BottomRight => new Vector2(0f, 0f),
        BarPlacement.Bottom => new Vector2(0.5f, 0f),
        BarPlacement.BottomLeft => new Vector2(1f, 0f),
        BarPlacement.TopRight => new Vector2(0f, 1f),
        BarPlacement.Top => new Vector2(0.5f, 1f),
        BarPlacement.TopLeft => new Vector2(1f, 1f),
        BarPlacement.Right => new Vector2(0f, 0f),
        BarPlacement.Left => new Vector2(1f, 0f),
        _ => Vector2.Zero,
    };

    /// <summary>The cell of the three by three picker this placement occupies,
    /// as column and row from the top left. The centre cell holds the button.</summary>
    public static (int Column, int Row) Cell(this BarPlacement placement) => placement switch
    {
        BarPlacement.TopLeft => (0, 0),
        BarPlacement.Top => (1, 0),
        BarPlacement.TopRight => (2, 0),
        BarPlacement.Left => (0, 1),
        BarPlacement.Right => (2, 1),
        BarPlacement.BottomLeft => (0, 2),
        BarPlacement.Bottom => (1, 2),
        BarPlacement.BottomRight => (2, 2),
        _ => (2, 2),
    };

    public static BarPlacement? FromCell(int column, int row)
        => Enum.GetValues<BarPlacement>()
            .Cast<BarPlacement?>()
            .FirstOrDefault(p => p!.Value.Cell() == (column, row));
}
