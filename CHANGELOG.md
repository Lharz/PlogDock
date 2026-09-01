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
the csproj version has no section, or when the topmost section is not the csproj
version. An empty topmost section is not an error, it is how a cycle starts, and
the workflow simply reports that there is nothing to submit yet.

A new cycle opens at a patch bump. The number is provisional until a release
pins it, so it is raised to a minor or a major the moment the work landing here
justifies it.

Versions are `major.minor.patch.build`. The fourth component is unused and stays
at zero. Fixes only bump the patch, a new feature bumps the minor, and a change
that breaks how existing settings behave bumps the major.

## 0.2.0.0

- Shortcuts can be marked as favourites. The favourites are drawn as their own
  block at the top of the panel, above a rule and ahead of everything else, so the
  handful of plugins reached every day keep a place that nothing below them can
  shift. The settings list is cut into the same two sections, and the order
  arranged there is the order the panel shows. A shortcut is starred from the
  button at the end of its row, or dragged across the rule. Nothing starts
  favourited, so a bar left alone looks exactly as it did.
- A plugin disabled or uninstalled in Dalamud no longer appears in the bar, nor in
  the settings list. Its tile answered no click, and its row was greyed out to the
  point that it could not even be unticked. The shortcut is kept, so enabling the
  plugin again brings it back where it was.
- New setting, *Enable new plugins automatically*, off by default. Turned on, a
  plugin installed while PlogDock is running gets its shortcut straight away
  rather than waiting to be ticked in the settings. It is still flagged as new
  either way, so it can be found among a hundred others.
- Three buttons above the settings list act on every shortcut at once: *Enable
  all*, *Disable all* and *Sort A-Z*. Each asks for confirmation first, and each
  works on the whole configuration rather than on whatever the search box is
  showing. *Enable all* skips the plugins PlogDock has no way of opening, the
  ones whose checkbox is greyed out. *Sort A-Z* replaces the manual order and
  cannot be undone.
- The settings window can no longer be resized below the point where its own
  controls stop fitting. It still opens at its usual size and still remembers
  whatever size it is given afterwards, it simply has a floor now.

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
