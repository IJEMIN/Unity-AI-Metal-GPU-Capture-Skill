// Metal GPU Capture Skill — [McpTool] host (Entry Point B).
//
// Five thin wrappers exposing the SAME plain-C# core (MetalCaptureEnvironment, MetalBuildService,
// MetalCaptureService, MetalTraceInspector) to the Unity AI Assistant. NO capture/parse logic is
// duplicated here — every tool delegates to the core that the Editor window also uses.
//
// Guarded by METAL_GPU_CAPTURE_ASSISTANT_PRESENT (auto-defined by the .asmdef versionDefine when
// com.unity.ai.assistant is installed) so the package still compiles where the Assistant is absent.

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    internal static class MetalGpuCaptureTools
    {
        public const string PackageId = "com.jeminlee.metal-gpu-capture-skill";
        public const string Version = "0.1.0";
    }
}

#if METAL_GPU_CAPTURE_ASSISTANT_PRESENT
namespace JeminLee.MetalGpuCaptureSkill.Editor.Mcp
{
    using System;
    using System.IO;
    using System.Threading.Tasks;
    using Unity.AI.MCP.Editor.Helpers;
    using Unity.AI.MCP.Editor.ToolRegistry;

    // ---------------------------------------------------------------------------------------------
    // Parameter classes (public properties + [McpDescription]; Newtonsoft-deserialized by registry).
    // ---------------------------------------------------------------------------------------------

    public class MetalBuildParams
    {
        [McpDescription("Optional absolute output path for the .app bundle. If omitted, reuses the " +
            "last StandaloneOSX build location, or builds to <project>/Builds/<Product>.app.")]
        public string outputPath { get; set; }
    }

    public class MetalCaptureParams
    {
        [McpDescription("Absolute path to the macOS .app bundle to launch and capture. If omitted, " +
            "the last StandaloneOSX build is used. Build one first with Metal.BuildStandalonePlayer " +
            "if none exists.")]
        public string appPath { get; set; }

        [McpDescription("Seconds to let the player render a representative frame before capturing " +
            "(default 10). Increase for slow-loading scenes.")]
        public int warmupSeconds { get; set; } = 10;

        [McpDescription("Set MTLCAPTURE_WAIT_FOR_SIGNAL=1 for short-lived players that exit before " +
            "capture can attach (default false).")]
        public bool waitForSignal { get; set; } = false;
    }

    public class MetalInspectParams
    {
        [McpDescription("Absolute path to a .gputrace file to inspect.")]
        public string tracePath { get; set; }

        [McpDescription("Load the trace's GPU profiling session to get real GPU frame time and " +
            "per-pass GPU cost (default true). Adds ~15-20s and needs an embedded profiling session " +
            "or an M3/A17+ device. Set false for a fast structural inspect (counts + passes only).")]
        public bool loadGpuTiming { get; set; } = true;
    }

    // ---------------------------------------------------------------------------------------------
    // Tools — all delegate to the shared core.
    // ---------------------------------------------------------------------------------------------

    public static class MetalCheckCaptureEnvironmentTool
    {
        public const string Title = "Check Metal capture environment";
        public const string Description =
            "Preflight for Metal GPU capture on macOS: verifies macOS major version >= 27, that " +
            "/usr/bin/gpucapture and /usr/bin/gpudebug exist, and that the active graphics API is " +
            "Metal (Editor device and StandaloneOSX build setting).";

        [McpTool("Metal.CheckCaptureEnvironment", Description, Title, Groups = new[] { "metal", "profiling" })]
        public static object HandleCommand()
        {
            try
            {
                MetalEnvironmentStatus status = MetalCaptureEnvironment.Check();
                string msg = status.AllPass
                    ? "All capture-environment checks passed."
                    : "One or more capture-environment checks failed.";
                return Response.Success(msg, status);
            }
            catch (Exception e) { return Response.Error("CheckCaptureEnvironment failed: " + e.Message); }
        }
    }

    public static class MetalFindExistingBuildTool
    {
        public const string Title = "Find existing macOS Standalone build";
        public const string Description =
            "Returns the last macOS Standalone (StandaloneOSX) build location, if one exists on disk.";

        [McpTool("Metal.FindExistingBuild", Description, Title, Groups = new[] { "metal", "profiling" })]
        public static object HandleCommand()
        {
            try
            {
                string path = MetalBuildService.FindExistingBuild();
                bool exists = !string.IsNullOrEmpty(path);
                return Response.Success(
                    exists ? "Found existing build." : "No existing StandaloneOSX build found.",
                    new { path = exists ? path : null, exists, defaultBuildPath = MetalBuildService.DefaultBuildPath() });
            }
            catch (Exception e) { return Response.Error("FindExistingBuild failed: " + e.Message); }
        }
    }

    public static class MetalBuildStandalonePlayerTool
    {
        public const string Title = "Build macOS Standalone (Development, Metal)";
        public const string Description =
            "Builds a capture-enabled macOS Standalone Development Build (Metal) from the enabled " +
            "scenes in Build Settings. Blocking; can take a long time. Returns the .app path and a " +
            "build summary.";

        [McpTool("Metal.BuildStandalonePlayer", Description, Title, Groups = new[] { "metal", "profiling" })]
        public static object HandleCommand(MetalBuildParams parameters)
        {
            try
            {
                MetalBuildResult res = MetalBuildService.BuildStandalonePlayer(parameters?.outputPath);
                return res.success
                    ? Response.Success("Build succeeded: " + res.summary, res)
                    : Response.Error("Build failed: " + res.summary, res);
            }
            catch (Exception e) { return Response.Error("BuildStandalonePlayer failed: " + e.Message); }
        }
    }

    public static class MetalCaptureStandalonePlayerTool
    {
        public const string Title = "Capture a Metal GPU frame from the Standalone Player";
        public const string Description =
            "Launches the macOS Standalone Player with MTL_CAPTURE_ENABLED=1, waits for a " +
            "representative frame, and captures a .gputrace via gpucapture (single-frame boundary " +
            "capture, with an automatic --until-exit + stop fallback). Returns the .gputrace path. " +
            "Provide appPath, or build first with Metal.BuildStandalonePlayer.";

        [McpTool("Metal.CaptureStandalonePlayer", Description, Title, Groups = new[] { "metal", "profiling" })]
        public static async Task<object> HandleCommand(MetalCaptureParams parameters)
        {
            try
            {
                var p = parameters ?? new MetalCaptureParams();
                string appPath = p.appPath;
                if (string.IsNullOrEmpty(appPath))
                    appPath = MetalBuildService.FindExistingBuild();
                if (string.IsNullOrEmpty(appPath))
                    return Response.Error("No appPath provided and no existing StandaloneOSX build found. " +
                                          "Call Metal.BuildStandalonePlayer first.");

                MetalCaptureResult res = await MetalCaptureService.CaptureAsync(
                    appPath, p.warmupSeconds, p.waitForSignal, null).ConfigureAwait(false);

                return res.success
                    ? Response.Success("Captured frame (" + res.method + "): " + res.tracePath, res)
                    : Response.Error("Capture failed: " + res.error, res);
            }
            catch (Exception e) { return Response.Error("CaptureStandalonePlayer failed: " + e.Message); }
        }
    }

    public static class MetalInspectTraceTool
    {
        public const string Title = "Inspect a .gputrace";
        public const string Description =
            "Inspects a .gputrace via gpudebug --json and returns a summary: device, command " +
            "buffer/encoder/draw counts, the render passes ([R] encoders with URP labels), and " +
            "(when loadGpuTiming is true, the default) the whole-frame GPU time plus per-pass GPU " +
            "cost from the trace's profiling session. CPU frame time is not in a GPU trace.";

        [McpTool("Metal.InspectTrace", Description, Title, Groups = new[] { "metal", "profiling" })]
        public static async Task<object> HandleCommand(MetalInspectParams parameters)
        {
            try
            {
                string tracePath = parameters?.tracePath;
                if (string.IsNullOrEmpty(tracePath))
                    return Response.Error("tracePath parameter is required.");
                if (!(File.Exists(tracePath) || Directory.Exists(tracePath)))
                    return Response.Error("Trace not found: " + tracePath);

                bool timing = parameters?.loadGpuTiming ?? true;
                MetalTraceSummary res = await MetalTraceInspector.InspectAsync(tracePath, null, timing).ConfigureAwait(false);
                string gpu = res.gpuFrameTimeAvailable
                    ? ", GPU frame " + res.gpuFrameMs.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + " ms"
                    : "";
                return res.success
                    ? Response.Success("Inspected trace (" + res.encoderCount + " encoders, " +
                        res.drawCallCount + " draws" + gpu + ").", res)
                    : Response.Error("Inspect failed: " + res.error, res);
            }
            catch (Exception e) { return Response.Error("InspectTrace failed: " + e.Message); }
        }
    }
}
#endif
