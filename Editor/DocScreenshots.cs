using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>
    /// Dev utility (macOS only): regenerates the README tab screenshots. Opens the Metal GPU Capture
    /// window, walks the tabs, and captures the window's screen rect with `screencapture -R` into
    /// Documentation~/images/. GUI session only — the window must be visible and unoccluded.
    ///
    /// Tip: float the window (drag it out of any dock) before running so its rect is clean. Run an
    /// Inspect first so the Summary/Details tabs have real content. The Assistant-window screenshot
    /// is captured manually (separate live window).
    /// </summary>
    static class DocScreenshots
    {
        // (tab index, output filename) — filenames match the README image links.
        static readonly (int tab, string file)[] Shots =
        {
            (0, "metal-gpu-capture-summary.png"),
            (1, "metal-gpu-capture-details.png"),
            (2, "metal-gpu-capture-log.png"),
        };

        const int StepMs = 600; // delay between "switch tab" and "capture", and between tabs

        [MenuItem("Tools/Metal GPU Capture/Generate Doc Screenshots")]
        static void Generate()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                EditorUtility.DisplayDialog("Metal GPU Capture",
                    "Doc-screenshot capture uses macOS `screencapture` and only runs in the macOS Editor.", "OK");
                return;
            }

            MetalGpuCaptureWindow w = EditorWindow.GetWindow<MetalGpuCaptureWindow>();
            w.Show();
            // Best-effort fixed size for consistent screenshots (ignored if docked).
            w.position = new Rect(80, 80, 760, 940);
            w.Focus();

            string outDir = Path.Combine(MetalCaptureService.PackageRoot(), "Documentation~", "images");
            Directory.CreateDirectory(outDir);

            VisualElement root = w.rootVisualElement;
            for (int i = 0; i < Shots.Length; i++)
            {
                int idx = Shots[i].tab;
                string path = Path.Combine(outDir, Shots[i].file);
                int baseDelay = i * StepMs * 2;
                root.schedule.Execute(() => { w.SelectTab(idx); w.Focus(); w.Repaint(); }).StartingIn(baseDelay);
                root.schedule.Execute(() => CaptureRegion(w.position, path)).StartingIn(baseDelay + StepMs);
            }
            root.schedule.Execute(() =>
            {
                Debug.Log("[Metal GPU Capture] Doc screenshots written to " + outDir);
                EditorUtility.RevealInFinder(outDir);
            }).StartingIn(Shots.Length * StepMs * 2 + StepMs);
        }

        static void CaptureRegion(Rect r, string outPath)
        {
            // screencapture -R x,y,width,height  (screen points, top-left origin).
            string args = string.Format(CultureInfo.InvariantCulture,
                "-x -R {0},{1},{2},{3} \"{4}\"",
                Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y),
                Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height), outPath);
            try
            {
                var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/sbin/screencapture",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                p?.WaitForExit(5000);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[Metal GPU Capture] screencapture failed: " + e.Message);
            }
        }
    }
}
