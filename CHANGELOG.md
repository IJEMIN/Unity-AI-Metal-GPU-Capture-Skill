# Changelog

All notable changes to this package are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Real GPU frame & per-pass timing via `gpudebug profile load`; frame-budget gauge, URP-category
  breakdown, deterministic Top-3 insights, and per-pass GPU bottleneck classification (perf limiters).
- Tabbed window (Summary / Capture / Details / Log) with Browse… and Open-in-Xcode buttons.
- Configurable capture output folder; the last `.gputrace` path and settings persist via `EditorPrefs`.
- README usage guide + window screenshots (Summary / Capture / Details / Log).
- Usability: **Cancel** button + elapsed-time timer for long capture/inspect; results and the active
  tab **persist across domain reloads**; **Reveal in Finder** and **Copy report** (Markdown) buttons;
  buttons are gated on trace existence and on the capture environment.

### Fixed
- `get_timeSinceStartup` error on Inspect: log callbacks from background threads now marshal to the
  main thread before touching the UI Toolkit scheduler.
- Ask-AI prompt popup's submit button was clipped by the window; insights now go straight to the
  Assistant via `AssistantApi.Run` (no inline popup).

## [0.1.0] - 2026-06-23

### Added
- Initial UPM package scaffold so the package can be loaded as a local package into a Unity 6 project.
- `Editor/` assembly definition and `MetalGpuCaptureTools.cs` stub documenting the five planned `[McpTool]` tools.
- Skill skeletons (`enabled: false`) for `capture-metal-frame` and `interpret-gpu-trace` under `AIAssistantSkills/`.
- README, documentation stub, and activation TODO list.
