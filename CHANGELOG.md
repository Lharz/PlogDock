# Changelog

## 0.2.0.0

### Added

- Shortcuts can be marked as favourites, drawn as their own block at the top of
  the panel. Star a shortcut from the button at the end of its settings row, or
  drag it across the rule. Nothing starts favourited.
- Each row in the settings list carries its plugin's icon.
- New setting, *Enable new plugins automatically*, off by default: a plugin
  installed while PlogDock is running gets its shortcut straight away.
- *Enable all*, *Disable all* and *Sort A-Z* buttons above the settings list.
  Each asks for confirmation and acts on every shortcut, not just the ones the
  search box is showing.

### Changed

- The reorder buttons in the settings list are drawn as arrows.

### Fixed

- A plugin disabled or uninstalled in Dalamud no longer appears in the bar or the
  settings list. Its shortcut is kept and comes back with the plugin.
- The settings window can no longer be resized below the size its own controls
  need.

## 0.1.0.0

- Initial release.
- A single button on the interface that unfolds into a grid of plugin icons.
- Left click opens a plugin, right click opens its settings.
- Plugins are reached through Dalamud's own open handler, falling back to the
  slash command they registered when they expose no window of their own.
- The panel unfolds in any of eight directions, chosen from a grid in the
  settings.
- Shortcuts are enabled, disabled and reordered from a settings window, by drag
  and drop or one notch at a time.
- Real plugin icons are downloaded once and cached, falling back to a coloured
  tile carrying the plugin's initials.
