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

        // GPU timing, populated only when the trace's profiling session is loaded
        // (`profile load`; see MetalTraceInspector). costPercent is the encoder's share of frame
        // GPU time; gpuMs = costPercent% of the frame GPU time; vertex/fragment are per-stage ms.
        public bool gpuTimeAvailable;   // true once profile timing is loaded
        public double gpuMs;            // encoder GPU time (0 when timing not loaded)
        public double costPercent;      // % of frame GPU time (performance/encoders cost column)
        public double vertexMs;         // per-stage GPU time (0 if absent)
        public double fragmentMs;
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

        // Frame timings. GPU frame time is available when the profiling session is loaded
        // (gpuFrameTimeAvailable / gpuTimingLoaded). CPU frame time is never in a GPU trace, so it
        // stays unavailable here (get it from Unity FrameTimingManager). Availability flags let the
        // UI/Assistant distinguish "0 ms" from "not measured".
        public bool cpuFrameTimeAvailable;
        public double cpuFrameMs;
        public bool gpuFrameTimeAvailable;
        public double gpuFrameMs;
        public bool gpuTimingLoaded;     // true when `profile load` succeeded and timing was parsed
        public string timingNote;

        // Render passes enumerated via `find [R]`.
        public List<MetalPassInfo> passes = new List<MetalPassInfo>();
        public int totalMatches;        // gpudebug find totalMatches (capped at 100)
        public bool passesCapped;       // true when find hit its 100-result cap

        // Render encoders ranked by GPU cost (from performance/encoders after profile load);
        // empty unless GPU timing was loaded. Highest cost first.
        public List<MetalPassInfo> gpuPasses = new List<MetalPassInfo>();

        // "Top pass": the top GPU-cost encoder when timing is loaded, else the most-draws pass.
        // topPassMetric says which ("GPU time" or "draw count").
        public MetalPassInfo topPass;
        public string topPassMetric;

        public string rawLog;
    }
}
