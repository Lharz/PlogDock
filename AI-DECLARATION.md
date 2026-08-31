---
version: "0.1.2"
level: copilot
components:
  PlogDock/src/: copilot
  PlogDock/images/icon.png: copilot
  PlogDock/PlogDock.json: copilot
---

## Notes

PlogDock was written with Claude (Anthropic) acting as the implementer while I
directed the design, reviewed the result and tested every change in game.

**What I decided.** Every design choice is mine: opening plugins through
`OpenMainUi` with a resolved slash command as the fallback, right click for a
plugin's own settings, a persistent folded bar rather than an ephemeral palette,
real downloaded icons rather than generated tiles, a configurable grid, which
plugins are enabled on a first run, the eight unfold directions, drag and drop
reordering, dropping automated tests in favour of manual verification, refusing
any heuristic that guesses which command belongs to which plugin, and the name.

**What I verified.** Every task was tested by hand in game before being kept.
Two real defects came out of that testing rather than out of the code review: a
feedback loop in the layout that made the button slide across the screen while
the window grew, and the bar drawing over the title screen. Coverage numbers in
the design were measured on a live install of 104 plugins, not estimated.

**What the AI produced.** The C# implementation, its comments, the design and
planning documents, and the placeholder icon in `PlogDock/images/icon.png`, which is the
output of a small Python script rather than an image model. The icon is
provisional and expected to be replaced by hand-drawn artwork.

**How correctness was approached.** Dalamud's API surface was read out of the
shipped assembly metadata before any code was written, rather than recalled.
That is what caught, before the first build, that `ImGui.NET` is no longer
shipped, that the target is .NET 10 rather than .NET 9, and that
`IExposedPlugin.Manifest` and `IReadOnlyCommandInfo.Handler` made two planned
subsystems unnecessary. Where an API signature was uncertain it was looked up,
not guessed.

I have read the code, I understand it, and I can explain any part of it.
