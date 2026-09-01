using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;

namespace PlogDock;

/// <summary>A plugin as PlogDock sees it: what it is called, and what can be done with it.</summary>
internal sealed record CatalogEntry(
    string InternalName,
    string DisplayName,
    bool IsLoaded,
    bool HasMainUi,
    bool HasConfigUi,
    IExposedPlugin? Source)
{
    /// <summary>Whether Dalamud itself offers a way to open this plugin.</summary>
    public bool HasAnyAction => this.HasMainUi || this.HasConfigUi;
}

/// <summary>
/// A view over the installed plugins, reconciled with the persisted shortcut list.
/// Refreshed when the set of active plugins changes, never per frame.
/// </summary>
internal sealed class PluginCatalog
{
    private readonly Configuration config;
    private readonly CommandResolver resolver;
    private readonly string selfInternalName;

    private Dictionary<string, CatalogEntry> entries = new(StringComparer.Ordinal);

    public PluginCatalog(Configuration config, CommandResolver resolver)
    {
        this.config = config;
        this.resolver = resolver;
        this.selfInternalName = Service.PluginInterface.InternalName;
    }

    public IReadOnlyCollection<CatalogEntry> Entries => this.entries.Values;

    /// <summary>
    /// The entry PlogDock is willing to show for a shortcut: the plugin is installed,
    /// and Dalamud has it loaded. A plugin disabled or uninstalled answers null here,
    /// which drops it from the bar and from the settings list alike. The two must
    /// agree on what is worth showing, so the rule lives in one place.
    /// <para>
    /// The shortcut itself is never dropped from the configuration, so enabling the
    /// plugin again brings it back exactly where it was.
    /// </para>
    /// </summary>
    public CatalogEntry? FindAvailable(string internalName)
        => this.entries.TryGetValue(internalName, out var entry) && entry.IsLoaded ? entry : null;

    /// <summary>Enabled shortcuts, in configured order, whose plugin is available.</summary>
    public IEnumerable<(ShortcutEntry Shortcut, CatalogEntry Catalog)> OrderedShortcuts()
    {
        foreach (var shortcut in this.config.Entries)
        {
            if (!shortcut.Enabled)
                continue;

            if (this.FindAvailable(shortcut.InternalName) is { } catalog)
                yield return (shortcut, catalog);
        }
    }

    public void Refresh()
    {
        var fresh = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);

        foreach (var plugin in Service.PluginInterface.InstalledPlugins)
        {
            // A shortcut to PlogDock inside PlogDock is useless, and it would grow
            // the list by one every time someone reads it. Never list ourselves.
            if (string.Equals(plugin.InternalName, this.selfInternalName, StringComparison.Ordinal))
                continue;

            fresh[plugin.InternalName] = new CatalogEntry(
                plugin.InternalName,
                string.IsNullOrWhiteSpace(plugin.Name) ? plugin.InternalName : plugin.Name,
                plugin.IsLoaded,
                plugin.HasMainUi,
                plugin.HasConfigUi,
                plugin);
        }

        this.entries = fresh;
        this.Reconcile();
    }

    /// <summary>Whether PlogDock has any way of opening this plugin.</summary>
    private bool CanBeOpened(CatalogEntry entry)
        => entry.HasMainUi || this.resolver.ResolveFor(entry.InternalName) is not null;

    /// <summary>
    /// Creates a shortcut entry for every plugin not seen before. On a first launch
    /// everything PlogDock can open starts enabled, whether through a main UI or
    /// through a command, so the bar is useful without any setup. Afterwards a newly
    /// installed plugin arrives disabled — the bar must never grow on its own — unless
    /// <see cref="Configuration.AutoEnableNewPlugins"/> has been turned on.
    /// <para>
    /// A handful of commands act rather than open a window, and one of them sends the
    /// character home. Nothing in the API separates those from the rest, and hardcoding
    /// a list would be wrong on someone else's install, so they are pre-enabled like
    /// the others and left for the user to remove.
    /// </para>
    /// </summary>
    private void Reconcile()
    {
        // Clears the self entry an earlier version may have persisted.
        var removed = this.config.Entries
            .RemoveAll(e => string.Equals(e.InternalName, this.selfInternalName, StringComparison.Ordinal));

        var known = this.config.Entries
            .Select(e => e.InternalName)
            .ToHashSet(StringComparer.Ordinal);

        var firstRun = this.config.FirstRunPending;
        var changed = removed > 0;

        var ordered = this.entries.Values
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ordered)
        {
            if (known.Contains(entry.InternalName))
                continue;

            this.config.Entries.Add(new ShortcutEntry
            {
                InternalName = entry.InternalName,
                Enabled = (firstRun || this.config.AutoEnableNewPlugins) && this.CanBeOpened(entry),
                IsNew = !firstRun,
            });

            changed = true;
        }

        if (firstRun)
        {
            this.config.FirstRunPending = false;
            changed = true;
        }

        if (changed)
            this.config.Save();
    }
}
