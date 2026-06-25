using System;
using System.Collections.Generic;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>One render pass / Metal render-command-encoder extracted from the trace.</summary>
    [Serializable]
    public class MetalPassInfo
    {
        public string label;            // e.g. "[R] RenderLoop.DrawSRPBatcher (5)"
        public int drawCount;           // parsed from gpudebug summary ("110 draws")
        public string url;              // gpudebug node URL, e.g. commands/cb0/.../re12

        // GPU timing is NOT exposed by gpudebug v1.0 for Standalone Metal frame captures
        // (every performance/encoders + timeline/counters view returns empty, confirmed against a
        // real trace). Kept here so the model is forward-compatible if a future tool exposes it.
        public bool gpuTimeAvailable;   // false in v1
        public double gpuMs;            // 0 when unavailable
    }

    /// <summary>
    /// Minimal v1 summary of a .gputrace, parsed from `gpudebug --json` (schema verified against a
    /// real Standalone Metal capture). Shared by the Editor window and the [McpTool] tools.
    /// </summary>
    [Serializable]
    public class MetalTraceSummary
    {
        public bool success;
        public string error;
        public string tracePath;
        public string device;

        // Counts from `status` summary (authoritative).
        public int commandBufferCount;
        public int encoderCount;
        public int drawCallCount;

        // Frame timings. gpudebug v1.0 exposes NEITHER cpu nor gpu frame time for these traces,
        // so both are surfaced as "unavailable" (never fabricated). availability flags let the
        // UI/Assistant distinguish "0 ms" from "not measured".
        public bool cpuFrameTimeAvailable;
        public double cpuFrameMs;
        public bool gpuFrameTimeAvailable;
        public double gpuFrameMs;
        public string timingNote;

        // Render passes enumerated via `find [R]`.
        public List<MetalPassInfo> passes = new List<MetalPassInfo>();
        public int totalMatches;        // gpudebug find totalMatches (capped at 100)
        public bool passesCapped;       // true when find hit its 100-result cap

        // "Top GPU pass": gpuMs is unavailable, so this is the pass with the MOST DRAW CALLS
        // (documented proxy). topPassMetric describes which metric was used.
        public MetalPassInfo topPass;
        public string topPassMetric;

        public string rawLog;
    }
}
