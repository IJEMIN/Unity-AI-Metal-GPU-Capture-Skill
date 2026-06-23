// Metal GPU Capture Skill — custom tool host (Editor-only).
//
// This file is a SCAFFOLD. The actual [McpTool] tools are meant to be implemented
// iteratively inside the Unity AI Assistant. The class below compiles as-is (it does
// not yet reference the AI Assistant assemblies) so the package loads cleanly.
//
// ── To implement the tools ────────────────────────────────────────────────────
// 1. Add the AI Assistant MCP editor assembly to this folder's .asmdef
//    "Assembly Definition References" (confirm the exact name in-project — likely
//    "Unity.AI.Assistant.Editor" and/or "Unity.AI.MCP.Editor").
// 2. Wrap tool code in `#if METAL_GPU_CAPTURE_ASSISTANT_PRESENT` (auto-defined by the
//    .asmdef versionDefines when com.unity.ai.assistant is installed) so the package
//    still compiles in projects that don't have the Assistant.
// 3. Decorate each tool method/class with [McpTool("explicit.id", "description")] from
//    namespace Unity.AI.MCP.Editor.ToolRegistry, with [McpDescription(..., Required=)]
//    parameter properties, returning a serializable result object.
//
// ── Planned tools (see README) ────────────────────────────────────────────────
//   Metal.CheckCaptureEnvironment  — preflight macOS version, gpucapture/gpudebug, Metal API.
//   Metal.FindExistingBuild        — EditorUserBuildSettings.GetBuildLocation(StandaloneOSX).
//   Metal.BuildStandalonePlayer    — capture-enabled macOS Development Build (Metal).
//   Metal.CaptureStandalonePlayer  — launch w/ MTL_CAPTURE_ENABLED=1, wait, attach, capture .gputrace.
//   Metal.InspectTrace             — parse .gputrace via gpucapture/gpudebug for interpretation.

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>
    /// Placeholder host for the Metal GPU capture tools. Replace with [McpTool]
    /// implementations once the AI Assistant assembly reference is wired up.
    /// </summary>
    internal static class MetalGpuCaptureTools
    {
        public const string PackageId = "com.jeminlee.metal-gpu-capture-skill";
        public const string Version = "0.1.0";

#if METAL_GPU_CAPTURE_ASSISTANT_PRESENT
        // TODO: implement the [McpTool] tools here.
#endif
    }
}
