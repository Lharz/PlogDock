using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using PlogDock.UI;

namespace PlogDock;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/plogdock";

    private readonly WindowSystem windowSystem = new("PlogDock");
    private readonly Configuration config;
    private readonly CommandResolver resolver;
    private readonly PluginCatalog catalog;
    private readonly Launcher launcher;
    private readonly IconService icons;
    private readonly ConfigWindow configWindow;
    private readonly BarWindow barWindow;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        this.config = Configuration.Load();

        // The resolver comes first: the catalog consults it to decide what a first
        // launch pre-enables, since a plugin reachable only by command counts too.
        this.resolver = new CommandResolver();
        this.resolver.Refresh();

        this.catalog = new PluginCatalog(this.config, this.resolver);
        this.catalog.Refresh();

        this.launcher = new Launcher(this.resolver);

        this.icons = new IconService();

        // Built before the bar so the bar can be handed a way to open it.
        this.configWindow = new ConfigWindow(this.config, this.catalog, this.launcher, this.icons);
        this.windowSystem.AddWindow(this.configWindow);

        this.barWindow = new BarWindow(this.config, this.catalog, this.launcher, this.icons, this.ShowConfig);
        this.windowSystem.AddWindow(this.barWindow);

        Service.PluginInterface.ActivePluginsChanged += this.OnActivePluginsChanged;

        Service.Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Show or hide the PlogDock shortcut bar. Use /plogdock config for settings.",
        });

        Service.PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        Service.PluginInterface.UiBuilder.OpenMainUi += this.ShowBar;
        Service.PluginInterface.UiBuilder.OpenConfigUi += this.ShowConfig;
    }

    public void Dispose()
    {
        Service.PluginInterface.ActivePluginsChanged -= this.OnActivePluginsChanged;
        Service.PluginInterface.UiBuilder.OpenConfigUi -= this.ShowConfig;
        Service.PluginInterface.UiBuilder.OpenMainUi -= this.ShowBar;
        Service.PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        Service.Commands.RemoveHandler(CommandName);
        this.windowSystem.RemoveAllWindows();
        this.icons.Dispose();
    }

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args)
    {
        // Same order as construction, and for the same reason.
        this.resolver.Refresh();
        this.catalog.Refresh();
    }

    private void ShowBar() => this.barWindow.IsOpen = true;

    private void ShowConfig() => this.configWindow.IsOpen = true;

    private void OnCommand(string command, string arguments)
    {
        if (arguments.Trim().Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            this.configWindow.Toggle();
            return;
        }

        this.barWindow.Toggle();
    }
}
