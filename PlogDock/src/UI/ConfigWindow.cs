using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace PlogDock.UI;

internal sealed class ConfigWindow : Window
{
    private readonly Configuration config;
    private readonly PluginCatalog catalog;
    private readonly Launcher launcher;

    /// <summary>The rows to draw this frame, each paired with its index in the
    /// configuration. Refilled every frame, kept as a field so a hundred plugins do
    /// not allocate a list per frame for as long as the window stays open.</summary>
    private readonly List<(int Index, CatalogEntry Entry)> listed = [];

    private string search = string.Empty;

    /// <summary>A reorder requested this frame, applied once the list is drawn.
    /// Moving an entry mid-iteration would displace one the loop has yet to reach.</summary>
    private (int From, int To)? pendingMove;

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
    /// </summary>
    private void CollectListed()
    {
        this.listed.Clear();

        for (var i = 0; i < this.config.Entries.Count; i++)
        {
            if (this.catalog.FindAvailable(this.config.Entries[i].InternalName) is { } entry)
                this.listed.Add((i, entry));
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

        if (filtering)
            ImGui.TextDisabled("Reordering is hidden while searching.");

        if (!ImGui.BeginChild("##shortcuts", new Vector2(0f, 0f), true))
        {
            ImGui.EndChild();
            return;
        }

        for (var position = 0; position < this.listed.Count; position++)
        {
            var (index, entry) = this.listed[position];
            var shortcut = this.config.Entries[index];

            if (filtering && entry.DisplayName.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            ImGui.PushID(shortcut.InternalName);
            this.DrawShortcutRow(position, index, shortcut, entry, filtering);
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    /// <summary><paramref name="position"/> is the row in the list as drawn,
    /// <paramref name="index"/> the entry it stands for in the configuration. The two
    /// part company as soon as a plugin is left out, and both are needed: moving a row
    /// swaps it with its neighbour on screen, not with whatever entry happens to sit
    /// next to it in the configuration.</summary>
    private void DrawShortcutRow(
        int position,
        int index,
        ShortcutEntry shortcut,
        CatalogEntry entry,
        bool filtering)
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
            this.HandleRowDragAndDrop(index, entry.DisplayName);

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

        if (filtering)
            return;

        // Kept alongside the drag: one notch at a time is easier to aim than a drag,
        // and dragging across a hundred rows means scrolling while holding the mouse.
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 34f);

        if (ImGui.SmallButton("^") && position > 0)
            this.pendingMove = (index, this.listed[position - 1].Index);

        ImGui.SameLine();

        if (ImGui.SmallButton("v") && position < this.listed.Count - 1)
            this.pendingMove = (index, this.listed[position + 1].Index);
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

    private void HandleRowDragAndDrop(int index, string label)
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
            this.pendingMove = (this.dragSource, index);
            this.dragSource = -1;
        }

        ImGui.EndDragDropTarget();
    }
}
