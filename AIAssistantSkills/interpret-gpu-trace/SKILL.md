---
name: interpret-gpu-trace
description: Inspect a captured .gputrace with gpudebug and interpret it in Unity URP context (render passes, ScriptableRenderPass, shaders/materials, render targets) to surface prioritized GPU optimization insights. gpudebug v1.0 exposes no per-pass GPU timing, so cost is reasoned from draw counts plus URP pass/shader context.
required_packages:
  com.unity.ai.assistant: ">=2.0.0"
required_editor_version: ">=6000.0.0"
enabled: true
tools:
  - Metal_InspectTrace
---

# Interpret a GPU trace in Unity URP context

Explain what the GPU did in a captured Metal frame and where the cost is, mapped back to Unity URP
concepts, then give prioritized optimization advice. Use after `capture-metal-frame`, or when the
user points at an existing `.gputrace`.

## When to use
The user has a `.gputrace` (from `capture-metal-frame` or on disk) and wants to understand it or make
the frame cheaper: "what's expensive in this frame", "analyze this gputrace", "how do I optimize
my URP rendering".

## Approach
1. **Inspect** — call `Metal_InspectTrace` with the `.gputrace` path. It returns: device; command
   buffer / encoder / draw-call counts; the render passes (`[R] <label>` encoders with draw counts
   and gpudebug node URLs); and the top pass by draw count.
2. **Map to URP** — translate each Metal encoder / debug-group label to its URP construct using the
   table below.
3. **Prioritize** — rank suspected cost by draw count and pass type (no measured GPU time is
   available; see Timing caveat). Produce concrete, URP-specific actions.
4. **(Optional) Drill deeper** — for a specific pass, the gpudebug node URL from `Metal_InspectTrace`
   can be navigated directly (see "Deeper inspection").

## Timing caveat (important, do not fabricate)
gpudebug v1.0 does **not** expose CPU or per-encoder/frame **GPU time** for Standalone Metal frame
captures (its `performance/encoders` and `timeline/counters` views return empty even after
`profile run`). Never invent millisecond numbers. Reason about cost from **draw counts**, **pass
type**, **render-target size/format**, and **pass count**, and say timing is unavailable.

## Metal label -> URP mapping
Labels appear as Unity debug groups (e.g. `ExecuteRenderGraph`) and Metal render encoders prefixed
`[R]`. A trailing `(N)` is an instance counter. Verified against a real URP 6000.x Metal capture:

| Metal label (contains)                         | URP construct / ScriptableRenderPass                         | Notes |
|------------------------------------------------|--------------------------------------------------------------|-------|
| `RenderSingleCameraInternal: <Camera>`         | Per-camera render entry                                       | One block per camera; repeats => multiple cameras/views |
| `ExecuteRenderGraph`                           | URP Render Graph execution scope                             | RenderGraph compiler path |
| `Draw Main Light Shadowmap` / `MainLightShadow`| `MainLightShadowCasterPass`                                  | Cost scales with cascades + caster draws |
| `AdditionalLightsShadow`                       | `AdditionalLightsShadowCasterPass`                          | Per extra shadow-casting light |
| `Shadows.DrawSRPBatcher` / `Shadows.Draw`      | Shadow caster draws (SRP Batcher / non-batched)             | High draw counts => many shadow casters |
| `RenderLoop.DrawSRPBatcher`                    | `DrawObjectsPass` (opaque & transparent), SRP Batcher       | Main geometry; the opaque/transparent workhorse |
| `ColorGradingLUT`                              | `ColorGradingLutPass`                                        | Builds grading LUT once per frame |
| `CopyDepth`                                    | `CopyDepthPass`                                              | Only needed if `_CameraDepthTexture` is used |
| `DrawMotionVectors`                            | `MotionVectorRenderPass`                                     | Needed by TAA / motion blur |
| `SSAO`                                          | `ScreenSpaceAmbientOcclusion` renderer feature              | Shader `Hidden/Universal Render Pipeline/ScreenSpaceAmbientOcclusion`; multiple sub-passes (AO + blur) |
| `DecalDrawIntoDBufferSystem.Execute`           | `DecalRendererFeature` (DBuffer technique)                   | Per decal projector batch |
| `CopyColor`                                     | `CopyColorPass` (`_CameraOpaqueTexture`)                    | Only needed if opaque texture is used (e.g. refraction) |
| `RG_TAA` / `RG_TAACopyHistory`                 | Temporal Anti-Aliasing + history copy                       | Requires motion vectors; per-frame history |
| `RG_BloomPrefilter`                            | Bloom prefilter (post)                                       | Start of the bloom chain |
| `RG_BloomDownsample` (many)                    | Bloom downsample mips                                        | Cost scales with bloom iterations + resolution |
| `RG_BloomUpsample` (many)                      | Bloom upsample mips                                          | Pairs with downsample chain |
| `Blit Bloom Mipmaps`                           | Bloom mip blits                                              | |
| `Blit Lens Flare Occlusion` / `Blit Lens Flares (Data Driven)` | Lens Flare (data-driven) occlusion + draw         | Skippable if lens flares unused |
| `Blit Post Processing`                         | `UberPost` (tonemap, bloom composite, color grading, vignette) | The main post-processing blit |
| `Blit Final Post Processing`                   | `FinalPost` (FXAA, film grain, dithering, final blit)       | Final output pass |
| `GUITexture.Draw`                              | IMGUI / overlay (UGUI overlay, editor overlays)             | UI/overlay rendering |

Inside an encoder, each draw exposes its `pipeline` = shader/material (e.g.
`Hidden/Universal Render Pipeline/...`), `vertex`/`fragment` functions, render state, and bound
render targets (`color0`, etc.), so a pass can be tied to a concrete material/shader.

## Optimization heuristics (prioritize by impact)
- **Many shadow caster draws** (`Shadows.DrawSRPBatcher` with a high count): reduce shadow-casting
  renderers, lower shadow cascade count / shadow distance, or disable "Cast Shadows" on small/distant
  meshes.
- **High opaque/transparent draws** (`RenderLoop.DrawSRPBatcher`): improve batching (keep SRP Batcher
  compatibility, use GPU instancing, combine materials/atlases), cull aggressively, use LODs.
- **Deep bloom chain** (`RG_BloomDownsample`/`Upsample` x many): lower bloom max iterations / downscale,
  or disable bloom on low-end; bloom cost scales with resolution.
- **SSAO sub-passes**: reduce SSAO sample count, increase downsample, lower blur quality, or disable.
- **Decals DBuffer** (`DecalDrawIntoDBufferSystem`): reduce decal projector count, or switch decal
  technique (Screen Space vs DBuffer).
- **TAA + motion vectors**: temporal AA adds motion-vector + history cost; consider FXAA-only on
  low-end tiers.
- **CopyColor / CopyDepth present but unused**: disable Opaque Texture / Depth Texture in the URP
  asset if no shader samples them.
- **Repeated full pipeline across command buffers** (e.g. `cb0`, `cb3`, `cb6`): multiple cameras /
  overlay stack / multi-view; verify each camera is required and check render scale.

## Deeper inspection (when more detail is needed)
`Metal_InspectTrace` gives the per-pass labels, draw counts, and gpudebug node URLs. For a specific
pass, those URLs (e.g. `commands/cb0/grp0/.../re0`) can be navigated with gpudebug:
`status`, `find <text>` (locate nodes by label), `go <url>` / `go ..`, `list`, `info` / `info --all`
(draw render state + `pipeline` shader), and `fetch <resource>` (dump a render target as .png /
buffer as .bin). See [references/gpudebug-cli.md](references/gpudebug-cli.md).

## Output format
Produce:
1. **Frame overview** — device, command buffer / encoder / draw-call counts, top pass by draw count,
   and an explicit "GPU/CPU frame time: unavailable" note.
2. **Pass breakdown** — the render passes mapped to URP constructs (group post-processing chains).
3. **Prioritized recommendations** — concrete URP actions, highest-impact first, each tied to the
   pass/shader/material evidence (draw counts, pass type), not invented timings.
