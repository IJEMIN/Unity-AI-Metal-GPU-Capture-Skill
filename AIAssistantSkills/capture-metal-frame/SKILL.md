---
name: capture-metal-frame
description: Build (or reuse) a macOS Standalone Metal Development Build of the current Unity project, or attach to an already-running build, and capture a .gputrace frame using macOS 27 gpucapture.
required_packages:
  - com.unity.ai.assistant
required_editor_version: 6000.0
enabled: false
tools:
  - Metal.CheckCaptureEnvironment
  - Metal.FindExistingBuild
  - Metal.BuildStandalonePlayer
  - Metal.CaptureStandalonePlayer
---

# Capture a Metal GPU frame

> SCAFFOLD — body to be authored with the Unity AI Assistant. `enabled: false` until ready.

## When to use
Use this skill when the user wants to capture a Metal GPU frame from this Unity project on macOS
for performance analysis. Pair with `interpret-gpu-trace` to analyze the result.

## Capture flow (v1)
1. **Preflight** — run `Metal.CheckCaptureEnvironment` (macOS 27, `gpucapture`/`gpudebug` on PATH,
   active graphics API is Metal).
2. **Find or build** — `Metal.FindExistingBuild` locates the last macOS Standalone build. Ask the
   user **reuse vs. rebuild**. If rebuilding, `Metal.BuildStandalonePlayer` produces a
   capture-enabled macOS **Development Build** (Metal API).
3. **Capture** — `Metal.CaptureStandalonePlayer` launches the player with `MTL_CAPTURE_ENABLED=1`,
   waits (~10s) for the first frames, then attaches `gpucapture` and captures a `.gputrace`.

## Scope
- In scope: **macOS Standalone Player (Metal)**.
- Out of scope (v1): iOS on-device, Editor Play Mode, context-aware capture.

## CLI reference
Verified `gpucapture` flags and the v1 capture sequence: [references/gpucapture-cli.md](references/gpucapture-cli.md).
Commands are `list` / `boundaries` / `start` / `stop`; `start` takes `-p <pid> -o <path> [-l label | -b id] [-c count] [-u]`.

## TODO
- [ ] Implement the `Metal.*` tools and reference them from `tools:` above.
- [ ] Runtime-confirm boundary/count semantics for a single Unity frame (see CLI reference).
- [ ] Confirm what PID/labels Unity's Metal layer exposes to `gpucapture list` / `boundaries`.
