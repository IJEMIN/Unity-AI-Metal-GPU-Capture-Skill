---
name: interpret-gpu-trace
description: Inspect a captured .gputrace with gpucapture/gpudebug and interpret it in Unity project context (SRP pass, shader, material, profiler markers) to surface performance-optimization insights.
required_packages:
  - com.unity.ai.assistant
required_editor_version: 6000.0
enabled: false
tools:
  - Metal.InspectTrace
---

# Interpret a GPU trace in Unity context

> SCAFFOLD — body to be authored with the Unity AI Assistant. `enabled: false` until ready.

## When to use
Use after `capture-metal-frame` (or when the user points at an existing `.gputrace`) to explain
what the GPU did and where the cost is, **mapped back to Unity concepts**.

## Approach
1. `Metal.InspectTrace` parses the `.gputrace` via `gpudebug -t <trace> --json --oneshot -q`
   (`gpudebug` v1.0 supports `--json` — prefer structured output over scraping REPL text).
2. Correlate GPU work with **Unity context**: SRP render passes, shaders/materials, draw calls,
   render targets, and Profiler/`CommandBuffer` markers.
3. Produce **optimization insights**: overdraw, expensive passes/shaders, bandwidth, redundant
   state changes, render-target sizing — prioritized by impact.

## CLI reference
Verified `gpudebug` flags (incl. `--json`, `--oneshot`): [references/gpudebug-cli.md](references/gpudebug-cli.md).

## TODO
- [ ] Runtime-confirm the REPL command set (`list/go/info/fetch/find/next/prev/status`) against a real trace.
- [ ] Define the mapping from Metal encoder/pass labels to URP `ScriptableRenderPass` / material names.
- [ ] Output format for the insight report.
