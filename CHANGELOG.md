# Changelog

## How this works

`<Version>` in `PlogDock/PlogDock.csproj` is always **the version being prepared**,
never the last one published. The topmost section here carries that same version
and collects changes as they land.

Releasing is therefore: open the DalamudPluginsD17 pull request pinning the
current commit, *then* bump `<Version>` and open a new section here for the next
cycle. Bumping first would publish the release under the following version's
number.

The workflow enforces both halves of that: it refuses to produce a manifest when
the csproj version has no section, when that section is empty, or when the
topmost section is not the csproj version.

Versions are `major.minor.patch.build`. The fourth component is unused and stays
at zero. Fixes only bump the patch, a new feature bumps the minor, and a change
that breaks how existing settings behave bumps the major.

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
