using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PlogDock.UI;

internal sealed class BarWindow : Window
{
    private readonly Configuration config;
    private readonly PluginCatalog catalog;
    private readonly Launcher launcher;
    private readonly IconService icons;
    private readonly Action openSettings;

    /// <summary>Set as soon as the press on the toggle turns into a drag, so the
    /// release that ends the drag does not also fold the bar.</summary>
    private bool draggingByToggle;

    public BarWindow(
        Configuration config,
        PluginCatalog catalog,
        Launcher launcher,
        IconService icons,
        Action openSettings)
        : base("PlogDock##PlogDockBar", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar)
    {
        this.config = config;
        this.catalog = catalog;
        this.launcher = launcher;
        this.icons = icons;
        this.openSettings = openSettings;
        this.IsOpen = true;

        // Escape must not close the bar. It is a permanent fixture rather than a
        // dialog, it has no title bar to reopen it from, and closing it by accident
        // while aiming at a game window would leave the user with no visible way back
        // short of typing the command.
        this.RespectCloseHotkey = false;
    }

    /// <summary>
    /// Keeps the bar off the title screen and the character select. Its shortcuts open
    /// plugin windows that have nothing to act on before a character is loaded, and a
    /// launcher floating over the title art is just clutter.
    /// </summary>
    public override bool DrawConditions() => Service.ClientState.IsLoggedIn;

    public override void PreDraw()
    {
        // NoMove unconditionally: the position belongs to us now, and letting ImGui
        // move the window too would leave two owners fighting over it every frame.
        this.Flags = ImGuiWindowFlags.AlwaysAutoResize
                     | ImGuiWindowFlags.NoTitleBar
                     | ImGuiWindowFlags.NoScrollbar
                     | ImGuiWindowFlags.NoScrollWithMouse
                     | ImGuiWindowFlags.NoMove;

        // Folded, the bar is one icon and the window chrome around it is just a grey
        // box sitting on the game. NoBackground drops the fill and the border
        // together. Unfolded the background earns its place, holding the grid.
        if (!this.config.Expanded)
            this.Flags |= ImGuiWindowFlags.NoBackground;

        if (this.config.AnchorInitialised)
        {
            ImGui.SetNextWindowPos(
                this.config.AnchorPosition,
                ImGuiCond.Always,
                this.config.Placement.Pivot());
        }
    }

    public override void PostDraw()
    {
        // Seeds the anchor from wherever ImGui had put the window, so upgrading from
        // a build that owned no position leaves the bar exactly where it was.
        if (this.config.AnchorInitialised)
            return;

        this.config.AnchorPosition = ImGui.GetWindowPos();
        this.config.AnchorInitialised = true;
        this.config.Save();
    }

    public override void Draw()
    {
        var placement = this.config.Placement;
        var size = this.config.IconSize;

        if (!this.config.Expanded)
        {
            this.DrawToggle(size);
            return;
        }

        if (placement.IsHorizontal())
            this.DrawHorizontal(placement, size);
        else
            this.DrawVertical(placement, size);
    }

    private void DrawHorizontal(BarPlacement placement, float size)
    {
        if (placement.PanelFirst())
        {
            this.DrawPanelGroup(size);
            ImGui.SameLine();
            this.DrawToggle(size);
            return;
        }

        this.DrawToggle(size);
        ImGui.SameLine();
        this.DrawPanelGroup(size);
    }

    /// <summary>
    /// The panel is always drawn before the button, even when it belongs below it,
    /// because aligning the button needs the width of the panel and ImGui only knows
    /// that once it has been laid out. For the placements where the button comes
    /// first the cursor is rewound afterwards to put it back on top.
    /// </summary>
    private void DrawVertical(BarPlacement placement, float size)
    {
        if (placement.PanelFirst())
        {
            var panelWidth = this.DrawPanelGroup(size);
            AlignButton(placement, size, panelWidth);
            this.DrawToggle(size);
            return;
        }

        var start = ImGui.GetCursorPos();
        ImGui.SetCursorPosY(start.Y + size + ImGui.GetStyle().ItemSpacing.Y);

        var width = this.DrawPanelGroup(size);
        var resume = ImGui.GetCursorPos();

        ImGui.SetCursorPos(new Vector2(start.X + Slack(width, size, placement), start.Y));
        this.DrawToggle(size);

        ImGui.SetCursorPos(resume);
    }

    private static float Slack(float panelWidth, float size, BarPlacement placement)
        => Math.Max(0f, panelWidth - size) * placement.ButtonAlign();

    /// <summary>Offsets the button along the width of the panel, per the placement.</summary>
    private static void AlignButton(BarPlacement placement, float size, float panelWidth)
    {
        var slack = Slack(panelWidth, size, placement);
        if (slack <= 0f)
            return;

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + slack);
    }

    /// <summary>Draws the grid inside a group and returns its measured width. A group
    /// reports the bounding box of everything in it, unlike the last item drawn, which
    /// on a partial final row is narrower than the grid and, when the button itself is
    /// that item, feeds its own offset back into the next frame.</summary>
    private float DrawPanelGroup(float size)
    {
        ImGui.BeginGroup();
        this.DrawPanel(size);
        ImGui.EndGroup();

        return ImGui.GetItemRectSize().X;
    }

    private void DrawPanel(float size)
    {
        var columns = Math.Max(1, this.config.Columns);
        var index = 0;

        foreach (var (shortcut, entry) in this.catalog.OrderedShortcuts())
        {
            if (index % columns != 0)
                ImGui.SameLine();

            this.DrawShortcut(shortcut, entry, size);
            index++;
        }

        if (index == 0)
            ImGui.TextDisabled("No shortcut enabled.");
    }

    /// <summary>
    /// Draws the toggle itself and reports whether it was clicked. The plugin icon
    /// replaces the chevrons where it is available, and the chevrons remain as the
    /// fallback so a missing or unreadable file leaves a usable button rather than
    /// an invisible one.
    /// </summary>
    private bool DrawToggleFace(float size)
    {
        var icon = this.icons.TryGetOwnIcon();

        if (icon is null)
        {
            return ImGui.Button(
                this.config.Expanded ? "<<##PlogDockToggle" : ">>##PlogDockToggle",
                new Vector2(size, size));
        }

        // This overload derives the widget id from the texture, so the id is pushed
        // explicitly to keep the toggle distinct from any shortcut sharing an icon.
        ImGui.PushID("PlogDockToggle");

        // Zero padding so the icon fills the square the shortcuts also occupy, and a
        // transparent frame so nothing of the button shows around the artwork.
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);

        var clicked = ImGui.ImageButton(icon.Handle, new Vector2(size, size));

        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);

        ImGui.PopID();

        return clicked;
    }

    /// <summary>
    /// The toggle doubles as the handle for the window. Folded, the bar is nothing
    /// but this button, with no title bar and barely any margin left to grab: without
    /// this it could not be moved at all. Dragging it moves the anchor, and only a
    /// press that never moved folds or unfolds the bar.
    /// </summary>
    private void DrawToggle(float size)
    {
        var clicked = this.DrawToggleFace(size);

        var draggable = !this.config.ButtonLocked;

        if (draggable && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            this.draggingByToggle = true;
            this.config.AnchorPosition += ImGui.GetIO().MouseDelta;
        }

        // Read before the flag is cleared: ImGui reports the click on the same frame
        // it reports the item going inactive, which is also when a drag ends.
        if (clicked && !this.draggingByToggle)
        {
            this.config.Expanded = !this.config.Expanded;
            this.config.Save();
        }

        if (ImGui.IsItemDeactivated())
        {
            // Saved once the drag ends rather than on every frame it moves.
            if (this.draggingByToggle)
                this.config.Save();

            this.draggingByToggle = false;
        }

        // Same convention as the shortcuts themselves: right click opens the settings
        // of whatever the icon stands for, and this icon stands for PlogDock.
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
            this.openSettings();

        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.Text(this.config.Expanded ? "Collapse PlogDock" : "Expand PlogDock");
        ImGui.Separator();
        ImGui.TextDisabled("Right click: PlogDock settings");
        ImGui.TextDisabled(draggable ? "Drag to move" : "Position locked");
        ImGui.EndTooltip();
    }

    private void DrawShortcut(ShortcutEntry shortcut, CatalogEntry entry, float size)
    {
        var canMain = this.launcher.CanOpenMain(entry);
        var canConfig = this.launcher.CanOpenConfig(entry);

        var origin = ImGui.GetCursorScreenPos();

        var texture = this.icons.TryGet(entry);

        if (texture is not null)
        {
            ImGui.Image(texture.Handle, new Vector2(size, size));

            // Image advances the cursor, the initials tile does not. Rewinding here
            // keeps the hit area below aligned in both cases.
            ImGui.SetCursorScreenPos(origin);
        }
        else
        {
            InitialsIcon.Draw(entry.InternalName, entry.DisplayName, size);
        }

        // An invisible hit area laid over the tile. Deliberately never disabled:
        // a plugin with no way in on the left click may still have a config UI on
        // the right, and ImGui swallows every click on a disabled item.
        ImGui.InvisibleButton($"##shortcut-{entry.InternalName}", new Vector2(size, size));

        if (canMain && ImGui.IsItemClicked(ImGuiMouseButton.Left))
            this.launcher.OpenMain(entry);

        if (canConfig && ImGui.IsItemClicked(ImGuiMouseButton.Right))
            this.launcher.OpenConfig(entry);

        if (ImGui.IsItemHovered())
            DrawTooltip(entry, canMain, canConfig);

        // A plugin Dalamud has unloaded never gets this far, the catalog drops it.
        // What is left is a loaded plugin with nothing PlogDock can open, and that one
        // is dimmed rather than hidden: its shortcut was ticked deliberately, and the
        // grid keeping its shape reads better than a tile quietly going missing.
        if (!canMain && !canConfig)
        {
            var corner = new Vector2(origin.X + size, origin.Y + size);
            ImGui.GetWindowDrawList().AddRectFilled(origin, corner, 0xAA000000u, size * 0.18f);
        }
    }

    private static void DrawTooltip(CatalogEntry entry, bool canMain, bool canConfig)
    {
        ImGui.BeginTooltip();
        ImGui.Text(entry.DisplayName);
        ImGui.Separator();

        if (canMain)
            ImGui.TextDisabled("Left click: open");

        if (canConfig)
            ImGui.TextDisabled("Right click: settings");

        if (!canMain && !canConfig)
            ImGui.TextDisabled("Nothing PlogDock can open");

        ImGui.EndTooltip();
    }
}
