# Metal GPU Capture Skill — for the Unity AI Assistant

A Unity (UPM) package that adds **Unity AI Assistant skills + custom `[McpTool]` tools**
so the in-Editor Assistant can:

1. Build (or reuse) a **macOS Standalone, Metal, Development Build** of the current project — or attach to an already-running build,
2. Capture a Metal GPU frame to a `.gputrace` using **macOS 27 `gpucapture`**,
3. Inspect the trace with `gpucapture` / `gpudebug` and **interpret it in Unity project context** (SRP pass, shader, material, profiler markers),
4. Surface **performance-optimization insights**.

> Status: **scaffold (v0.1.0)**. Skill bodies and the C# `[McpTool]` tools are intentionally
> left as stubs — they are meant to be implemented iteratively *inside* the Unity AI Assistant.
> See [Activation / TODO](#activation--todo).

## Requirements

- **macOS 27** with the `gpucapture` and `gpudebug` command-line tools installed.
- **Unity 6** (`6000.0`+).
- **`com.unity.ai.assistant`** package installed in the host project (provides the AI Assistant
  and the `[McpTool]` registry). Declared per-skill via `required_packages` rather than as a hard
  package dependency, so this package stays loadable anywhere.

## Version control

Two separate repositories are in play — keep them straight:

- The **host project** (`Metal GPU Capture Dev Project`) is managed with **Unity Version Control**.
  It only *references* this package via a local `file:` path; the package sources do **not** live
  in the host project's working tree.
- **This package** is managed in its **own Git / GitHub repository**, separate from Unity Version Control.

So any edits the Unity AI Assistant makes to this package (under
`Packages/com.jeminlee.metal-gpu-capture-skill/`) are versioned **here, in this Git repo** — *not* in
the host project's Unity Version Control. Commit package changes from this folder.

## Loading as a local package

This repo *is* a UPM package (its root holds `package.json`). To develop/test it inside a Unity project:

**Option A — `file:` reference (recommended for dev):** in the host project's `Packages/manifest.json`:

```jsonc
{
  "dependencies": {
    "com.jeminlee.metal-gpu-capture-skill": "file:/absolute/path/to/Unity-AI-Metal-GPU-Capture-Skill"
  }
}
```

**Option B — Package Manager UI:** `Window > Package Manager > + > Add package from disk…`
and pick this folder's `package.json`.

**Option C — Git URL (later):** `+ > Add package from git URL…` once this is pushed.

## Package layout

```
.
├── package.json                       # UPM manifest
├── Editor/                            # Editor-only assembly for the [McpTool] tools
│   ├── *.asmdef
│   └── MetalGpuCaptureTools.cs        # tool stubs + activation notes
├── AIAssistantSkills/                # discovered by the AI Assistant in UPM packages
│   ├── capture-metal-frame/SKILL.md
│   └── interpret-gpu-trace/SKILL.md
└── Documentation~/                   # docs (the ~ keeps it out of Unity's importer)
```

The AI Assistant discovers skills under `AIAssistantSkills/<skill>/SKILL.md` inside UPM packages.
Skills are **deny-by-default** — enable them in `Project Settings / Preferences > AI > Skills`.

## Planned custom tools (`Unity.AI.MCP.Editor.ToolRegistry`)

| Tool id                       | Purpose |
|-------------------------------|---------|
| `Metal.CheckCaptureEnvironment` | Preflight: macOS version, `gpucapture`/`gpudebug` on PATH, Metal graphics API. |
| `Metal.FindExistingBuild`       | Locate the last macOS Standalone build (`EditorUserBuildSettings.GetBuildLocation(StandaloneOSX)`). |
| `Metal.BuildStandalonePlayer`   | Build a capture-enabled macOS Development Build (Metal API). |
| `Metal.CaptureStandalonePlayer` | Launch with `MTL_CAPTURE_ENABLED=1`, wait, attach `gpucapture`, capture a `.gputrace`. |
| `Metal.InspectTrace`            | Parse a `.gputrace` via `gpucapture`/`gpudebug` for the interpretation skill. |

## Activation / TODO

These steps require a local **macOS 27** machine with the CLIs installed and are deferred until implementation:

- [ ] Add the AI Assistant MCP editor assembly to `Editor/*.asmdef` **Assembly Definition References**
      (exact name to confirm in-project — likely `Unity.AI.Assistant.Editor` and/or `Unity.AI.MCP.Editor`).
- [ ] Implement the five `[McpTool]` tools in `Editor/MetalGpuCaptureTools.cs`.
- [ ] Fill in the two `SKILL.md` bodies and flip their `enabled:` to `true`.
- [ ] Confirm exact `gpucapture` / `gpudebug` flags via `man` and pin them in the skill references.
- [ ] Decide a `LICENSE`.

## References

- Apple **Game Porting Toolkit 4** Claude Code marketplace (`game-porting-skills`) — `using-gpucapture`,
  `using-gpudebug`, `debugging-rendering-issues` are the closest style/format templates.
- Unity AI Assistant docs (`com.unity.ai.assistant`).
