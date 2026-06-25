using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    [Serializable]
    public struct EnvironmentCheck
    {
        public bool pass;
        public string label;
        public string detail;
    }

    [Serializable]
    public class MetalEnvironmentStatus
    {
        public EnvironmentCheck macOs;
        public EnvironmentCheck gpucapture;
        public EnvironmentCheck gpudebug;
        public EnvironmentCheck metalActive;

        public bool AllPass => macOs.pass && gpucapture.pass && gpudebug.pass && metalActive.pass;
    }

    /// <summary>
    /// Preflight for Metal GPU capture on macOS: macOS major >= 27, gpucapture/gpudebug present,
    /// active graphics API = Metal (Editor device + StandaloneOSX build setting).
    /// </summary>
    public static class MetalCaptureEnvironment
    {
        public const string GpuCapturePath = "/usr/bin/gpucapture";
        public const string GpuDebugPath = "/usr/bin/gpudebug";
        public const int MinMacOsMajor = 27;

        public static MetalEnvironmentStatus Check()
        {
            return new MetalEnvironmentStatus
            {
                macOs = CheckMacOs(),
                gpucapture = CheckCli(GpuCapturePath, "gpucapture"),
                gpudebug = CheckCli(GpuDebugPath, "gpudebug"),
                metalActive = CheckMetal(),
            };
        }

        static EnvironmentCheck CheckMacOs()
        {
            string os = SystemInfo.operatingSystem ?? string.Empty;
            int major = ParseMacOsMajor(os);
            return new EnvironmentCheck
            {
                pass = major >= MinMacOsMajor,
                label = "macOS >= " + MinMacOsMajor,
                detail = string.IsNullOrEmpty(os)
                    ? "OS unknown"
                    : (major > 0 ? os + " (major " + major + ")" : os + " (could not parse version)"),
            };
        }

        // Defensive parse: find the first dotted version token and take its first component.
        public static int ParseMacOsMajor(string os)
        {
            if (string.IsNullOrEmpty(os)) return -1;
            Match m = Regex.Match(os, @"(\d+)\.(\d+)");
            if (!m.Success) return -1;
            return int.TryParse(m.Groups[1].Value, out int major) ? major : -1;
        }

        static EnvironmentCheck CheckCli(string path, string name)
        {
            bool exists = System.IO.File.Exists(path);
            return new EnvironmentCheck
            {
                pass = exists,
                label = name,
                detail = exists ? path : "not found at " + path,
            };
        }

        static EnvironmentCheck CheckMetal()
        {
            bool editorMetal = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;
            GraphicsDeviceType[] apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneOSX);
            bool buildMetal = apis != null && Array.IndexOf(apis, GraphicsDeviceType.Metal) >= 0;
            return new EnvironmentCheck
            {
                pass = editorMetal && buildMetal,
                label = "Active Graphics API = Metal",
                detail = "Editor: " + (editorMetal ? "Metal" : SystemInfo.graphicsDeviceType.ToString())
                         + ", StandaloneOSX build: " + (buildMetal ? "Metal" : "Metal not enabled"),
            };
        }
    }
}
