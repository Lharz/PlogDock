using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;

namespace PlogDock.UI;

internal sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly PluginCatalog catalog;
    private readonly Launcher launcher;

    /// <summary>The rows to draw this frame, gathered section by section and each
    /// paired with its index in the configuration. Refilled every frame, kept as a
    /// field so a hundred plugins do not allocate a list per frame for as long as the
    /// window stays open.</summary>
    private readonly List<(int Index, CatalogEntry Entry, string Section)> listed = [];

    /// <summary>How many rows each section contributed to <see cref="listed"/>. Known
    /// before the rows are drawn, so a section with none can still show its heading
    /// and offer somewhere to drop a row.</summary>
    private readonly Dictionary<string, int> sectionCounts = new(StringComparer.Ordinal);

    private string search = string.Empty;

    /// <summary>Room to leave at the end of a row for the buttons sitting there.
    /// Measured once a frame rather than per row: the star is drawn in the icon font,
    /// and pushing a font to measure one glyph a hundred times over is work for
    /// an answer that does not change between two rows.</summary>
    private float trailingWidth;

    /// <summary>A reorder requested this frame, applied once the list is drawn.
    /// Moving an entry mid-iteration would displace one the loop has yet to reach.
    /// The section is the one the row landed in: dragging across the rule is how a
    /// shortcut changes section, and a drop that only changes section moves nothing.</summary>
    private (int From, int To, string Section)? pendingMove;

    /// <summary>Index being dragged. ImGui payloads carry raw bytes, and a field is
    /// both simpler and safer than marshalling an int through one, so the payload
    /// itself is a single dummy byte that only serves to name the drag.</summary>
    private int dragSource = -1;

    private static readonly byte[] DragPayload = [0];

    /// <summary>A change asked of the whole list. Held from the click on the button
    /// until the confirmation is answered, and read by nothing else in between.</summary>
    private enum BulkAction
    {
        None,
        EnableAll,
        DisableAll,
        Sort,
    }

    private BulkAction pendingBulk = BulkAction.None;

    private const string ConfirmPopup = "Confirm##PlogDockConfirmBulk";

    public ConfigWindow(Configuration config, PluginCatalog catalog, Launcher launcher)
        : base("PlogDock settings##PlogDockConfig")
    {
        this.config = config;
        this.catalog = catalog;
        this.launcher = launcher;

        this.Size = new Vector2(520f, 640f);
        this.SizeCondition = ImGuiCond.FirstUseEver;

        // FirstUseEver hands the size over to dalamudUI.ini from the second session
        // on, and nothing there has a floor: a window dragged small once stays small
        // forever, across reinstalls. The constraint is what keeps it openable.
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360f, 400f),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    /// <summary>Clears the new-plugin flags: the list has been seen.</summary>
    public override void OnClose()
    {
        var cleared = false;

        foreach (var entry in this.config.Entries.Where(e => e.IsNew))
        {
            entry.IsNew = false;
            cleared = true;
        }

        if (cleared)
            this.config.Save();
    }

    public override void Draw()
    {
        this.DrawSettings();
        ImGui.Separator();
        this.DrawShortcutList();
        this.DrawBulkConfirmation();

        this.ApplyPendingMove();
    }

    private void ApplyPendingMove()
    {
        if (this.pendingMove is not { } move)
            return;

        var entry = this.config.Entries[move.From];
        ShortcutSections.Assign(entry, move.Section);

        this.config.Entries.RemoveAt(move.From);
        this.config.Entries.Insert(Math.Clamp(move.To, 0, this.config.Entries.Count), entry);

        this.pendingMove = null;
        this.config.Save();
    }

    private void DrawSettings()
    {
        var columns = this.config.Columns;
        if (ImGui.SliderInt("Columns", ref columns, 1, 20))
        {
            this.config.Columns = columns;
            this.config.Save();
        }

        var size = this.config.IconSize;
        if (ImGui.SliderFloat("Icon size", ref size, 16f, 64f, "%.0f"))
        {
            this.config.IconSize = size;
            this.config.Save();
        }

        var locked = this.config.ButtonLocked;
        if (ImGui.Checkbox("Lock the bar in place", ref locked))
        {
            this.config.ButtonLocked = locked;
            this.config.Save();
        }

        var autoEnable = this.config.AutoEnableNewPlugins;
        if (ImGui.Checkbox("Enable new plugins automatically", ref autoEnable))
        {
            this.config.AutoEnableNewPlugins = autoEnable;
            this.config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A plugin installed from now on gets its shortcut without being ticked here first.");

        this.DrawPlacementPicker();
    }

    /// <summary>
    /// Three by three grid with the button at its centre: click the cell the panel
    /// should unfold into. Naming eight directions in words turned out to be the
    /// confusing part, so the picker shows the geometry instead of describing it.
    /// </summary>
    private void DrawPlacementPicker()
    {
        ImGui.Spacing();
        ImGui.Text("Unfold toward");

        var current = this.config.Placement;
        var cell = new Vector2(28f, 28f);

        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                if (column > 0)
                    ImGui.SameLine();

                if (column == 1 && row == 1)
                {
                    ImGui.BeginDisabled();
                    ImGui.Button("##PlacementCentre", cell);
                    ImGui.EndDisabled();

                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("The button stays here");

                    continue;
                }

                var placement = BarPlacementGeometry.FromCell(column, row);
                if (placement is not { } target)
                    continue;

                var selected = target == current;

                if (selected)
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));

                if (ImGui.Button($"##placement{column}{row}", cell) && !selected)
                {
                    this.config.Placement = target;
                    this.config.Save();
                }

                if (selected)
                    ImGui.PopStyleColor();

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(target.ToString());
            }
        }

        ImGui.Spacing();
    }

    /// <summary>
    /// Collects the rows to draw: the shortcuts whose plugin Dalamud has installed and
    /// loaded. A plugin disabled or uninstalled is left out entirely rather than shown
    /// greyed out — its checkbox could not be reached and its tile could not be
    /// clicked, so listing it only raises the question of why. The shortcut stays in
    /// the configuration and comes back, in place, when the plugin does.
    /// <para>
    /// Gathered section by section, in the order the panel draws them. Cutting the list
    /// up here as well is what keeps the two views telling one story: an order arranged
    /// flat in this window and then redrawn in blocks in the panel is the confusing
    /// half of the feature.
    /// </para>
    /// </summary>
    private void CollectListed()
    {
        this.listed.Clear();
        this.sectionCounts.Clear();

        foreach (var section in ShortcutSections.Order)
        {
            var count = 0;

            for (var i = 0; i < this.config.Entries.Count; i++)
            {
                var shortcut = this.config.Entries[i];

                if (ShortcutSections.Of(shortcut) != section)
                    continue;

                if (this.catalog.FindAvailable(shortcut.InternalName) is not { } entry)
                    continue;

                this.listed.Add((i, entry, section));
                count++;
            }

            this.sectionCounts[section] = count;
        }
    }

    private void DrawShortcutList()
    {
        this.CollectListed();

        var enabled = this.listed.Count(row => this.config.Entries[row.Index].Enabled);
        ImGui.Text($"{enabled} of {this.listed.Count} plugins enabled");

        this.DrawBulkActions();

        ImGui.SetNextItemWidth(-1f);
        ImGui.InputTextWithHint("##search", "Search...", ref this.search, 128);

        var filtering = this.search.Length > 0;
        this.trailingWidth = TrailingWidth(filtering);

        if (filtering)
            ImGui.TextDisabled("Reordering is hidden while searching.");

        if (!ImGui.BeginChild("##shortcuts", new Vector2(0f, 0f), true))
        {
            ImGui.EndChild();
            return;
        }

        var position = 0;

        foreach (var section in ShortcutSections.Order)
        {
            var count = this.sectionCounts[section];

            ImGui.TextDisabled(section);
            ImGui.Separator();

            // The increment sits in the loop header so that a row skipped by the
            // search still advances the position it stands for in the list.
            for (var row = 0; row < count; row++, position++)
            {
                var (index, entry, _) = this.listed[position];
                var shortcut = this.config.Entries[index];

                if (filtering && entry.DisplayName.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                ImGui.PushID(shortcut.InternalName);
                this.DrawShortcutRow(position, index, shortcut, entry, filtering, row == 0, row == count - 1);
                ImGui.PopID();
            }

            // A section with no rows still needs somewhere to drop one, or emptying
            // the favourites would leave the star as the only way back in.
            if (count == 0 && !filtering)
                this.DrawEmptySection(section);

            ImGui.Spacing();
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// Stands in for a section holding no rows, and accepts a drop so one can be
    /// dragged in. The drop changes the section without moving the entry: where it sits
    /// in the configuration is what brings it back to the same place when it leaves the
    /// section again.
    /// </summary>
    private void DrawEmptySection(string section)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.Selectable($"Drop a plugin here.##empty-{section}");
        ImGui.PopStyleColor();

        if (!ImGui.BeginDragDropTarget())
            return;

        if (!ImGui.AcceptDragDropPayload("PLOGDOCK_ROW").IsNull && this.dragSource >= 0)
        {
            this.pendingMove = (this.dragSource, this.dragSource, section);
            this.dragSource = -1;
        }

        ImGui.EndDragDropTarget();
    }

    /// <summary><paramref name="position"/> is the row in the list as drawn,
    /// <paramref name="index"/> the entry it stands for in the configuration. The two
    /// part company as soon as a plugin is left out, and both are needed: moving a row
    /// swaps it with its neighbour on screen, not with whatever entry happens to sit
    /// next to it in the configuration. <paramref name="first"/> and
    /// <paramref name="last"/> mark the ends of the section rather than of the list:
    /// the notches never leave a section, the rule is crossed by dragging.</summary>
    private void DrawShortcutRow(
        int position,
        int index,
        ShortcutEntry shortcut,
        CatalogEntry entry,
        bool filtering,
        bool first,
        bool last)
    {
        // A plugin with nothing to open cannot be enabled: the checkbox would only
        // add a tile that does nothing when clicked.
        var actionable = this.launcher.CanOpenMain(entry) || this.launcher.CanOpenConfig(entry);

        if (!actionable)
            ImGui.BeginDisabled();

        var enabled = shortcut.Enabled;
        if (ImGui.Checkbox(entry.DisplayName, ref enabled))
        {
            shortcut.Enabled = enabled;
            this.config.Save();
        }

        if (!actionable)
            ImGui.EndDisabled();

        if (!filtering)
            this.HandleRowDragAndDrop(index, entry.DisplayName, shortcut);

        if (shortcut.IsNew)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "new");
        }

        if (!actionable)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(nothing to open)");
        }

        // Measured from the content region rather than from what is left of the line,
        // which the tags above shorten: the right hand controls have to land in the
        // same column on every row, tagged or not. Their width is measured too, rather
        // than guessed at a fixed number of pixels, because Dalamud's interface scale
        // moves both the glyphs and the padding under them.
        //
        // The star stays reachable while searching. Finding one plugin among a hundred
        // and starring it on the spot is exactly what the search box is for.
        ImGui.SameLine(ImGui.GetContentRegionMax().X - this.trailingWidth);
        this.DrawRowControls(position, index, shortcut, filtering, first, last);
    }

    /// <summary>
    /// The buttons at the end of a row, drawn in the icon font under a single push:
    /// the font stack and the colour stack are independent, so the star still takes
    /// its own tint inside it.
    /// </summary>
    private void DrawRowControls(
        int position,
        int index,
        ShortcutEntry shortcut,
        bool filtering,
        bool first,
        bool last)
    {
        using (Service.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            this.DrawFavouriteToggle(shortcut);

            // The star stays reachable while searching. Finding one plugin among a
            // hundred and starring it on the spot is what the search box is for.
            if (filtering)
                return;

            var section = this.listed[position].Section;

            // Kept alongside the drag: one notch at a time is easier to aim than a
            // drag, and dragging across a hundred rows means scrolling while holding
            // the mouse. Neither notch leaves the section.
            ImGui.SameLine();

            if (ImGui.SmallButton(FontAwesomeIcon.ArrowUp.ToIconString()) && !first)
                this.pendingMove = (index, this.listed[position - 1].Index, section);

            ImGui.SameLine();

            if (ImGui.SmallButton(FontAwesomeIcon.ArrowDown.ToIconString()) && !last)
                this.pendingMove = (index, this.listed[position + 1].Index, section);
        }
    }

    /// <summary>How much room the buttons at the end of a row need. The notches are
    /// gone while searching, leaving the star on its own.</summary>
    private static float TrailingWidth(bool filtering)
    {
        var padding = ImGui.GetStyle().FramePadding.X * 2f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        using (Service.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var star = ImGui.CalcTextSize(FontAwesomeIcon.Star.ToIconString()).X + padding;

            if (filtering)
                return star;

            return star
                   + spacing + ImGui.CalcTextSize(FontAwesomeIcon.ArrowUp.ToIconString()).X + padding
                   + spacing + ImGui.CalcTextSize(FontAwesomeIcon.ArrowDown.ToIconString()).X + padding;
        }
    }

    /// <summary>
    /// The star. Available whether or not the shortcut is ticked: the list shows the
    /// sections now, so starring something unticked visibly moves its row rather than
    /// appearing to do nothing, and greying the button out would only forbid what a
    /// drag across the rule can still do.
    /// <para>
    /// A real star, from the icon font Dalamud builds on FontAwesome 5 Free solid. An
    /// asterisk in the default font was tried first and read as nothing at all. The
    /// same glyph serves both states, gold against dimmed, because the solid set holds
    /// no hollow star to pair it with.
    /// </para>
    /// </summary>
    private void DrawFavouriteToggle(ShortcutEntry shortcut)
    {
        var favourite = shortcut.Favourite;

        ImGui.PushStyleColor(
            ImGuiCol.Text,
            favourite
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.82f, 0.25f, 1f))
                : ImGui.GetColorU32(ImGuiCol.TextDisabled));

        bool clicked;
        using (Service.PluginInterface.UiBuilder.IconFontHandle.Push())
            clicked = ImGui.SmallButton(FontAwesomeIcon.Star.ToIconString());

        ImGui.PopStyleColor();

        if (clicked)
        {
            shortcut.Favourite = !favourite;
            this.config.Save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(favourite ? "Remove from favourites" : "Add to favourites");
    }

    /// <summary>
    /// The three list-wide actions. They always act on the whole configuration, never
    /// on what the search box happens to be showing: a button whose reach depends on
    /// the contents of a text field elsewhere is a trap.
    /// </summary>
    private void DrawBulkActions()
    {
        if (ImGui.Button("Enable all"))
            this.Ask(BulkAction.EnableAll);

        ImGui.SameLine();

        if (ImGui.Button("Disable all"))
            this.Ask(BulkAction.DisableAll);

        ImGui.SameLine();

        if (ImGui.Button("Sort A-Z"))
            this.Ask(BulkAction.Sort);
    }

    private void Ask(BulkAction action)
    {
        this.pendingBulk = action;
        ImGui.OpenPopup(ConfirmPopup);
    }

    /// <summary>
    /// One modal serves all three actions. Drawn after the list rather than beside the
    /// buttons, so that confirming mutates the entries once the row loop has finished
    /// reading them.
    /// </summary>
    private void DrawBulkConfirmation()
    {
        if (this.pendingBulk == BulkAction.None)
            return;

        if (!ImGui.BeginPopupModal(ConfirmPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            // Escape closed it. ImGui dismisses a modal without telling the caller,
            // so the pending action has to be dropped here or it outlives its popup.
            this.pendingBulk = BulkAction.None;
            return;
        }

        ImGui.TextWrapped(Prompt(this.pendingBulk));
        ImGui.Spacing();

        var button = new Vector2(120f, 0f);

        if (ImGui.Button("Confirm", button))
        {
            this.Apply(this.pendingBulk);
            this.pendingBulk = BulkAction.None;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();

        if (ImGui.Button("Cancel", button))
        {
            this.pendingBulk = BulkAction.None;
            ImGui.CloseCurrentPopup();
        }

        // Focus lands on Cancel: none of the three actions can be undone.
        ImGui.SetItemDefaultFocus();

        ImGui.EndPopup();
    }

    private static string Prompt(BulkAction action) => action switch
    {
        BulkAction.EnableAll => "Give every plugin PlogDock can open a shortcut in the bar?",
        BulkAction.DisableAll => "Remove every shortcut from the bar? The plugins themselves are left alone.",
        BulkAction.Sort => "Sort every shortcut by name? This replaces your manual order, and cannot be undone.",
        _ => string.Empty,
    };

    private void Apply(BulkAction action)
    {
        switch (action)
        {
            case BulkAction.EnableAll:
                this.EnableAll();
                break;

            case BulkAction.DisableAll:
                // Including the shortcuts whose plugin is currently absent. Sparing
                // them would have a reinstalled plugin light up in a bar emptied on
                // purpose.
                foreach (var shortcut in this.config.Entries)
                    shortcut.Enabled = false;

                break;

            case BulkAction.Sort:
                this.SortAlphabetically();
                break;

            default:
                return;
        }

        this.config.Save();
    }

    /// <summary>Ticks every shortcut PlogDock could actually open, and only those: a
    /// plugin whose own checkbox is greyed out would gain nothing but an inert
    /// tile.</summary>
    private void EnableAll()
    {
        foreach (var shortcut in this.config.Entries)
        {
            if (this.catalog.FindAvailable(shortcut.InternalName) is not { } entry)
                continue;

            if (this.launcher.CanOpenMain(entry) || this.launcher.CanOpenConfig(entry))
                shortcut.Enabled = true;
        }
    }

    /// <summary>
    /// Orders the whole configuration, the shortcuts whose plugin is currently absent
    /// included. Those are invisible here, and leaving them in place would drop a
    /// reinstalled plugin into the middle of an otherwise sorted list. They have no
    /// display name to sort on, so their internal name stands in.
    /// <para>
    /// The list is rewritten in place rather than replaced, so that nothing holding a
    /// reference to it is left reading the old one.
    /// </para>
    /// </summary>
    private void SortAlphabetically()
    {
        var sorted = this.config.Entries
            .OrderBy(this.SortKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        this.config.Entries.Clear();
        this.config.Entries.AddRange(sorted);
    }

    private string SortKey(ShortcutEntry shortcut)
        => this.catalog.FindAvailable(shortcut.InternalName)?.DisplayName ?? shortcut.InternalName;

    private void HandleRowDragAndDrop(int index, string label, ShortcutEntry target)
    {
        if (ImGui.BeginDragDropSource())
        {
            this.dragSource = index;
            ImGui.SetDragDropPayload("PLOGDOCK_ROW", DragPayload);
            ImGui.Text(label);
            ImGui.EndDragDropSource();
        }

        if (!ImGui.BeginDragDropTarget())
            return;

        if (!ImGui.AcceptDragDropPayload("PLOGDOCK_ROW").IsNull
            && this.dragSource >= 0
            && this.dragSource != index)
        {
            // The section comes from the row landed on, so crossing the rule is what
            // takes a shortcut in and out of the favourites.
            this.pendingMove = (this.dragSource, index, ShortcutSections.Of(target));
            this.dragSource = -1;
        }

        ImGui.EndDragDropTarget();
    }
}
