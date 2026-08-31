# Changelog

Each released version gets a section here, and the section heading must match
`<Version>` in `PlogDock/PlogDock.csproj`. The release workflow reads the section
matching that version and fails if it is missing, so the two can never drift.

The topmost section is the one being worked on. It is filled in as changes land,
and its version is bumped by hand at release time.

## 0.2.0.0

_Nothing yet._

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
