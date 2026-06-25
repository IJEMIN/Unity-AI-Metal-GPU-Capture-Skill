using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>Serializable result of a GPU frame capture (shared by window + [McpTool]).</summary>
    [Serializable]
    public class MetalCaptureResult
    {
        public bool success;
        public string tracePath;
        public int pid;
        public string method;
        public string log;
        public string error;
    }

    /// <summary>
    /// Launches the Standalone Player with Metal capture enabled, waits for a representative
    /// frame WITHOUT blocking the main thread, resolves the PID, and captures a .gputrace via
    /// gpucapture. Tries a single-frame boundary capture first, then falls back to
    /// --until-exit + stop (plan rule D). All CLI I/O goes through ProcessRunner (rule B).
    /// </summary>
    public static class MetalCaptureService
    {
        public const string GpuCapture = "/usr/bin/gpucapture";

        static string Q(string s) => "\"" + s + "\"";

        public static string CapturesDir()
        {
            string dir = Path.Combine(PackageRoot(), "Captures");
            Directory.CreateDirectory(dir);
            return dir;
        }

        static string PackageRoot()
        {
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(MetalCaptureService).Assembly);
            if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath)) return pkg.resolvedPath;
            return Directory.GetParent(Application.dataPath).FullName;
        }

        static string ResolveInnerExecutable(string appPath, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(appPath) || !Directory.Exists(appPath))
            { err = "App bundle not found: " + appPath; return null; }

            string macOsDir = Path.Combine(appPath, "Contents", "MacOS");
            if (!Directory.Exists(macOsDir)) { err = "Contents/MacOS not found in " + appPath; return null; }

            var files = Directory.GetFiles(macOsDir).Where(f => !f.EndsWith(".meta")).ToArray();
            if (files.Length == 0) { err = "No executable found in " + macOsDir; return null; }

            return files.FirstOrDefault(f => string.IsNullOrEmpty(Path.GetExtension(f))) ?? files[0];
        }

        public static async Task<MetalCaptureResult> CaptureAsync(
            string appPath,
            int warmupSeconds,
            bool waitForSignal,
            Action<string> onLog = null,
            CancellationToken cancellationToken = default)
        {
            var r = new MetalCaptureResult();
            var sb = new StringBuilder();
            void L(string m) { sb.AppendLine(m); onLog?.Invoke(m); }

            string exe = ResolveInnerExecutable(appPath, out string err);
            if (exe == null) { r.success = false; r.error = err; r.log = sb.ToString(); return r; }
            L("Launching player: " + exe);

            var env = new Dictionary<string, string> { { "MTL_CAPTURE_ENABLED", "1" } };
            if (waitForSignal) env["MTLCAPTURE_WAIT_FOR_SIGNAL"] = "1";

            System.Diagnostics.Process player;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    UseShellExecute = false,   // player keeps inherited stdout/stderr -> no pipe to deadlock
                    CreateNoWindow = false,
                };
                foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;
                player = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception e)
            {
                r.success = false; r.error = "Failed to launch player: " + e.Message; r.log = sb.ToString(); return r;
            }

            r.pid = player.Id;
            L(string.Format("Player PID = {0}. Warming up {1}s (non-blocking)...", r.pid, warmupSeconds));

            try { await Task.Delay(Math.Max(0, warmupSeconds) * 1000, cancellationToken); }
            catch (OperationCanceledException)
            { TryKill(player); r.error = "Canceled during warm-up."; r.log = sb.ToString(); return r; }

            if (player.HasExited)
            {
                r.success = false;
                r.error = "Player exited before capture (exit " + SafeExit(player) +
                          "). Increase warm-up or enable Wait-For-Signal.";
                r.log = sb.ToString();
                return r;
            }

            // Cross-check PID against gpucapture list (diagnostic only; launched PID is authoritative).
            var list = await ProcessRunner.RunAsync(GpuCapture, "list", timeoutMs: 30000, cancellationToken: cancellationToken);
            L("gpucapture list:\n" + Trunc(list.StdOut) + (string.IsNullOrWhiteSpace(list.StdErr) ? "" : "\n[stderr] " + Trunc(list.StdErr)));

            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string tracePath = Path.Combine(CapturesDir(), "frame_" + ts + ".gputrace");
            string method;

            // --- Attempt 1: single-frame boundary capture (-b 0 -c 1) ---
            L("Capture attempt 1: boundary (-b 0 -c 1) -> " + tracePath);
            var a1 = await ProcessRunner.RunAsync(GpuCapture,
                "start -p " + r.pid + " -o " + Q(tracePath) + " -b 0 -c 1",
                timeoutMs: 60000, cancellationToken: cancellationToken);
            LogCli(L, "boundary start", a1);

            bool ok = TraceExists(tracePath);
            method = "boundary (-b 0 -c 1)";

            // --- Attempt 2 (fallback, SAME step): --until-exit then stop ---
            if (!ok)
            {
                L("Boundary capture produced no usable trace. Falling back to --until-exit + stop.");
                TryDelete(tracePath);

                var startTask = ProcessRunner.RunAsync(GpuCapture,
                    "start -p " + r.pid + " -o " + Q(tracePath) + " --until-exit",
                    timeoutMs: 60000, cancellationToken: cancellationToken);

                try { await Task.Delay(2500, cancellationToken); } catch (OperationCanceledException) { }

                var stop = await ProcessRunner.RunAsync(GpuCapture,
                    "stop -p " + r.pid, timeoutMs: 30000, cancellationToken: cancellationToken);
                LogCli(L, "stop", stop);

                var a2 = await startTask;
                LogCli(L, "until-exit start", a2);

                ok = TraceExists(tracePath);
                method = "until-exit + stop";
            }

            TryKill(player);

            r.success = ok;
            r.tracePath = ok ? tracePath : null;
            r.method = method;
            if (!ok) r.error = "No .gputrace was produced. See log for gpucapture output.";
            r.log = sb.ToString();
            return r;
        }

        static void LogCli(Action<string> L, string tag, ProcessResult p)
        {
            L(string.Format("[{0}] exit={1} timedOut={2}", tag, p.ExitCode, p.TimedOut));
            if (!string.IsNullOrWhiteSpace(p.StdOut)) L("  stdout: " + Trunc(p.StdOut));
            if (!string.IsNullOrWhiteSpace(p.StdErr)) L("  stderr: " + Trunc(p.StdErr));
        }

        static bool TraceExists(string p)
        {
            if (Directory.Exists(p)) return Directory.EnumerateFileSystemEntries(p).Any();
            if (File.Exists(p)) return new FileInfo(p).Length > 0;
            return false;
        }

        static void TryDelete(string p)
        {
            try { if (Directory.Exists(p)) Directory.Delete(p, true); else if (File.Exists(p)) File.Delete(p); }
            catch { }
        }

        static void TryKill(System.Diagnostics.Process p)
        {
            try { if (p != null && !p.HasExited) p.Kill(); } catch { }
        }

        static int SafeExit(System.Diagnostics.Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }

        static string Trunc(string s, int max = 4000)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max) + " ...[truncated]";
        }
    }
}
