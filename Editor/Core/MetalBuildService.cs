using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>Serializable result of a Standalone build (shared by window + [McpTool]).</summary>
    [Serializable]
    public class MetalBuildResult
    {
        public bool success;
        public string appPath;
        public string summary;
        public int totalErrors;
        public double totalTimeSeconds;
    }

    /// <summary>Locates / produces a capture-enabled macOS Standalone Development Build (Metal).</summary>
    public static class MetalBuildService
    {
        /// <summary>Last macOS Standalone build location, or empty if none / missing on disk.</summary>
        public static string FindExistingBuild()
        {
            string loc = EditorUserBuildSettings.GetBuildLocation(BuildTarget.StandaloneOSX);
            if (!string.IsNullOrEmpty(loc) && Directory.Exists(loc)) return loc;
            return string.Empty;
        }

        public static string DefaultBuildPath()
        {
            string product = string.IsNullOrEmpty(PlayerSettings.productName) ? "Player" : PlayerSettings.productName;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, "Builds", product + ".app");
        }

        /// <summary>
        /// Builds a macOS Standalone Development Build (Metal) using enabled scenes from
        /// EditorBuildSettings. Synchronous on the main thread (acceptable per plan rule A).
        /// </summary>
        public static MetalBuildResult BuildStandalonePlayer(string outputPath = null)
        {
            var res = new MetalBuildResult();

            if (string.IsNullOrEmpty(outputPath))
            {
                string existing = FindExistingBuild();
                outputPath = !string.IsNullOrEmpty(existing) ? existing : DefaultBuildPath();
            }

            try { Directory.CreateDirectory(Path.GetDirectoryName(outputPath)); }
            catch (Exception e) { res.success = false; res.summary = "Cannot create output dir: " + e.Message; return res; }

            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                res.success = false;
                res.summary = "No enabled scenes in Build Settings. Add at least one scene to build.";
                return res;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary s = report.summary;

            res.success = s.result == UnityEditor.Build.Reporting.BuildResult.Succeeded;
            res.appPath = outputPath;
            res.totalErrors = (int)s.totalErrors;
            res.totalTimeSeconds = s.totalTime.TotalSeconds;
            res.summary = string.Format("{0}: {1} error(s), {2} warning(s), {3:F1}s",
                s.result, s.totalErrors, s.totalWarnings, s.totalTime.TotalSeconds);

            if (res.success)
                EditorUserBuildSettings.SetBuildLocation(BuildTarget.StandaloneOSX, outputPath);

            return res;
        }
    }
}
