# Metal GPU Capture Skill — Documentation

See the top-level [README](../README.md) for an overview, requirements, and how to load this
package as a local package.

This `Documentation~` folder uses the trailing `~` so Unity's asset importer ignores it.
Use it for design notes, CLI flag references, and the iteration log as the skills and tools
are built out with the Unity AI Assistant.

## Contents (to grow)
- Capture flow design notes
- `gpucapture` / `gpudebug` CLI flag reference (verified on macOS 27)
- Metal-pass → Unity-context mapping notes

## Regenerating the README screenshots
The README images under `images/` are produced by a menu utility (macOS only):

1. Open the window (`Window > Analysis > Metal GPU Capture`), float it (drag it out of any dock),
   and run an **Inspect** so the Summary/Details tabs show real content.
2. Run **`Tools > Metal GPU Capture > Generate Doc Screenshots`**. It walks the tabs and writes
   `metal-gpu-capture-{summary,capture,details,log}.png` here via macOS `screencapture -R`.

The Assistant-window shot (`metal-gpu-capture-assistant.png`) is captured manually (it's a separate
live window). If the captured rect is offset, the window-region math in `Editor/DocScreenshots.cs`
may need a small calibration for your display.
