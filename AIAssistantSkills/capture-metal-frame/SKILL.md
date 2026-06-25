---
name: capture-metal-frame
description: Build (or reuse) a macOS Standalone Metal Development Build of the current Unity project, then launch it and capture a .gputrace frame using macOS 27 gpucapture. Pair with interpret-gpu-trace to analyze the result.
required_packages:
  com.unity.ai.assistant: ">=2.0.0"
required_editor_version: ">=6000.0.0"
enabled: true
tools:
  - Metal_CheckCaptureEnvironment
  - Metal_FindExistingBuild
  - Metal_BuildStandalonePlayer
  - Metal_CaptureStandalonePlayer
---

# Capture a Metal GPU frame

Capture a Metal GPU frame from this Unity project's macOS Standalone Player for performance
analysis. Pair with `interpret-gpu-trace` to analyze the resulting `.gputrace`.

## When to use
Use when the user wants to capture a Metal GPU frame from this Unity project on macOS (e.g. "capture
a GPU frame", "profile the GPU", "grab a gputrace"). macOS Standalone Player (Metal) only.

## Capture flow
1. **Preflight** — call `Metal_CheckCaptureEnvironment`. It verifies macOS major >= 27,
   `/usr/bin/gpucapture` and `/usr/bin/gpudebug` exist, and the active graphics API is Metal (Editor
   device + StandaloneOSX build setting). If any check fails, report it and stop.
2. **Find or build** — call `Metal_FindExistingBuild`.
   - If `exists` is true, ask the user **reuse vs. rebuild**.
   - If it returns no build, or the user wants a fresh one, call `Metal_BuildStandalonePlayer`
     (capture-enabled macOS **Development Build**, Metal). This is **blocking and can take a long
     time** — tell the user before starting.
3. **Capture** — call `Metal_CaptureStandalonePlayer` with the `.app` `appPath` from step 2
   (omit `appPath` to reuse the last build). It launches the player with `MTL_CAPTURE_ENABLED=1`,
   waits `warmupSeconds` (default 10) for a representative frame, then captures via gpucapture:
   a single-frame boundary capture (`-b 0 -c 1`) with an automatic `--until-exit` + `stop` fallback.
   - For players that exit too quickly to attach, set `waitForSignal: true`
     (adds `MTLCAPTURE_WAIT_FOR_SIGNAL=1`).
   - On success it returns the `.gputrace` path (written under the package's gitignored `Captures/`).

## Scope
- In scope: **macOS Standalone Player (Metal)**, Development Build, URP.
- Out of scope: iOS on-device, Editor Play Mode.

## Notes
- The launched process PID is authoritative; `gpucapture list` may report "no capturable processes"
  yet the capture still succeeds against that PID.
- Hand the returned `.gputrace` path to the `interpret-gpu-trace` skill (tool `Metal_InspectTrace`)
  to analyze it.

## CLI reference
Verified `gpucapture` flags and the capture sequence: [references/gpucapture-cli.md](references/gpucapture-cli.md).
Commands are `list` / `boundaries` / `start` / `stop`; `start` takes `-p <pid> -o <path> [-l label | -b id] [-c count] [-u]`.
