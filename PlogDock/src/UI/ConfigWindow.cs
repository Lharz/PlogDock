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

    public ConfigWindow(Configuration config, PluginCatalog catalog, Launcher launcher)
        : base("PlogDock settings##PlogDockConfig")
    {
        this.config = config;
        this.catalog = catalog;
        this.launcher = launcher;

        this.Size = new Vector2(520f, 640f);
        this.SizeCondition = ImGuiCond.FirstUseEver;
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
