# PlogDock

A shortcut dock for your installed Dalamud plugins.

One small button sits on your interface. Click it and it unfolds into a grid of
plugin icons; click it again and the grid folds away. No more pressing Escape,
opening the plugin installer and scrolling through a hundred entries to find the
one you want, and no more remembering which slash command belongs to which
plugin.

## Using it

- **Left click** an icon to open that plugin.
- **Right click** an icon to open that plugin's settings.
- **Left click** the dock button to fold and unfold the grid, **drag** it to move
  the dock, **right click** it for PlogDock's own settings.
- `/plogdock` shows or hides the dock, `/plogdock config` opens its settings.

Plugins are opened through Dalamud's own handler where they provide one. Where
they do not, PlogDock falls back to the slash command the plugin registered —
attributed by reading which assembly registered it, never guessed from its name.
A plugin that offers neither cannot be added, and says so rather than sitting
there doing nothing when clicked.

## Settings

Every installed plugin appears in the settings list with a checkbox. Enabled
shortcuts can be reordered by dragging them or one notch at a time, and the list
is searchable.

The dock unfolds in any of eight directions, picked from a three by three grid so
you choose the geometry rather than decode a name for it. Column count, icon
size and whether the dock is pinned in place are all adjustable.

On a first run every plugin PlogDock can open starts enabled, so the dock is
useful immediately. A plugin installed later arrives disabled and flagged as new:
the dock never grows on its own. Tick *Enable new plugins automatically* if you
would rather it did.

Note that a handful of plugins expose a command that performs an action rather
than opening a window — Lifestream's `/li` sends your character home, for
instance. Nothing in the Dalamud API distinguishes those, so they are enabled
like the rest and you may want to remove them.

## Icons

Real plugin icons are fetched once and cached on disk. Anything without one
falls back to a tile carrying the plugin's initials, on a colour derived from its
name so it stays recognisable and does not change between sessions. Icons never
block the interface: the dock is usable on its first frame and fills in behind
you.

## Installing

PlogDock is submitted to the Dalamud plugin repository on the testing channel.
Once it is available there, enable testing plugins in Dalamud's settings and
install it from the plugin installer.

## Building

Requires the .NET 10 SDK and a Dalamud installation.

```
dotnet build PlogDock/PlogDock.csproj -c Release
```

The build writes straight into `%AppData%\XIVLauncher\devPlugins\PlogDock\`,
where Dalamud picks it up and hot-reloads it without the game being restarted.
Point Dalamud at it once through Settings, Experimental, Dev Plugin Locations,
giving the path to `PlogDock.dll` rather than to its folder.

Set `DALAMUD_HOME` to build against a specific Dalamud instead of the local
installation; doing so also leaves the output in the usual `bin/` directory,
which is how the CI builds it.

## Releases

`<Version>` in the csproj is the version being prepared, and the topmost section
of [CHANGELOG.md](CHANGELOG.md) carries the same number. Releasing means opening
a pull request against
[DalamudPluginsD17](https://github.com/goatcorp/DalamudPluginsD17) pinning the
current commit, then bumping the version for the next cycle. CI builds the
submission for you and refuses to produce one when the version and the changelog
disagree.

## Licence

Copyright (C) 2026 Lharz.

Released under the GNU Affero General Public License, version 3 or later. See
[LICENSE](LICENSE). In short: use it, change it, share it, but a version you
distribute has to carry its source under the same terms.

## AI usage

Written with AI assistance, declared in [AI-DECLARATION.md](AI-DECLARATION.md)
per the [Dalamud AI usage policy](https://dalamud.dev/plugin-publishing/ai-policy).
