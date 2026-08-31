using System;
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

    private void DrawShortcutList()
    {
        var enabled = this.config.Entries.Count(e => e.Enabled);
        ImGui.Text($"{enabled} of {this.config.Entries.Count} plugins enabled");

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

        for (var i = 0; i < this.config.Entries.Count; i++)
        {
            var shortcut = this.config.Entries[i];
            var entry = this.catalog.Find(shortcut.InternalName);
            var label = entry?.DisplayName ?? shortcut.InternalName;

            if (filtering && label.IndexOf(this.search, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            ImGui.PushID(shortcut.InternalName);
            this.DrawShortcutRow(i, shortcut, entry, label, filtering);
            ImGui.PopID();
        }

        ImGui.EndChild();
    }

    private void DrawShortcutRow(
        int index,
        ShortcutEntry shortcut,
        CatalogEntry? entry,
        string label,
        bool filtering)
    {
        // A plugin with nothing to open cannot be enabled: the checkbox would only
        // add a tile that does nothing when clicked.
        var actionable = entry is not null
                         && (this.launcher.CanOpenMain(entry) || this.launcher.CanOpenConfig(entry));

        if (!actionable)
            ImGui.BeginDisabled();

        var enabled = shortcut.Enabled;
        if (ImGui.Checkbox(label, ref enabled))
        {
            shortcut.Enabled = enabled;
            this.config.Save();
        }

        if (!actionable)
            ImGui.EndDisabled();

        if (!filtering)
            this.HandleRowDragAndDrop(index, label);

        if (shortcut.IsNew)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), "new");
        }

        if (entry is null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(not installed)");
        }
        else if (!actionable)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("(nothing to open)");
        }

        if (filtering)
            return;

        // Kept alongside the drag: one notch at a time is easier to aim than a drag,
        // and dragging across a hundred rows means scrolling while holding the mouse.
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - 34f);

        if (ImGui.SmallButton("^") && index > 0)
            this.pendingMove = (index, index - 1);

        ImGui.SameLine();

        if (ImGui.SmallButton("v") && index < this.config.Entries.Count - 1)
            this.pendingMove = (index, index + 1);
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
