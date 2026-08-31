using System;

namespace PlogDock;

/// <summary>
/// Runs the action behind a click. Every call here crosses into third-party code,
/// so each one is wrapped: a misbehaving plugin gets logged, it never takes the
/// bar down with it.
/// </summary>
internal sealed class Launcher
{
    private readonly CommandResolver resolver;

    public Launcher(CommandResolver resolver)
    {
        this.resolver = resolver;
    }

    public bool CanOpenMain(CatalogEntry entry)
        => entry.IsLoaded
           && (entry.HasMainUi || this.resolver.ResolveFor(entry.InternalName) is not null);

    public bool CanOpenConfig(CatalogEntry entry)
        => entry.IsLoaded && entry.HasConfigUi;

    public void OpenMain(CatalogEntry entry)
    {
        // Rechecked here rather than trusted from the last refresh: a plugin can be
        // unloaded between the frame that drew the icon and the click on it.
        if (entry.Source is null || !entry.Source.IsLoaded)
        {
            Service.Log.Debug("{Plugin} is not loaded, ignoring click", entry.InternalName);
            return;
        }

        if (entry.HasMainUi)
        {
            Guard(entry, "OpenMainUi", entry.Source.OpenMainUi);
            return;
        }

        var command = this.resolver.ResolveFor(entry.InternalName);
        if (command is null)
            return;

        Guard(entry, "ProcessCommand", () => Service.Commands.ProcessCommand(command));
    }

    public void OpenConfig(CatalogEntry entry)
    {
        if (entry.Source is null || !entry.Source.IsLoaded || !entry.HasConfigUi)
            return;

        Guard(entry, "OpenConfigUi", entry.Source.OpenConfigUi);
    }

    private static void Guard(CatalogEntry entry, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Service.Log.Error(ex, "{What} failed for {Plugin}", what, entry.InternalName);
        }
    }
}
