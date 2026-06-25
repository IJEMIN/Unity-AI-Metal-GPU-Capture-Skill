using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>Result of running an external CLI process.</summary>
    public struct ProcessResult
    {
        public int ExitCode;
        public string StdOut;
        public string StdErr;
        public bool TimedOut;

        public bool Success => !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// Runs external CLI tools (gpucapture / gpudebug).
    ///
    /// THREADING (critical): the entire process lifecycle — Start, async stdout/stderr draining,
    /// stdin write, and exit wait — runs on a thread-pool thread via Task.Run, and every await uses
    /// ConfigureAwait(false). This guarantees the work NEVER depends on the Unity main-thread
    /// SynchronizationContext. The Unity AI Assistant invokes MCP tools on the main thread
    /// (UnityEditor.Search.Dispatcher); doing blocking process IO there previously froze the Editor.
    /// Offloading here makes the tools behave identically whether called from the window or the
    /// Assistant. Drains stdout AND stderr asynchronously to avoid pipe-buffer deadlocks.
    /// </summary>
    public static class ProcessRunner
    {
        public static Task<ProcessResult> RunAsync(
            string fileName,
            string arguments,
            IDictionary<string, string> environment = null,
            string stdin = null,
            string workingDirectory = null,
            int timeoutMs = 120000,
            CancellationToken cancellationToken = default)
        {
            // Offload the whole thing to the thread pool so it never runs on / blocks the main thread.
            return Task.Run(() => RunCoreAsync(fileName, arguments, environment, stdin, workingDirectory, timeoutMs, cancellationToken), cancellationToken);
        }

        static async Task<ProcessResult> RunCoreAsync(
            string fileName, string arguments, IDictionary<string, string> environment,
            string stdin, string workingDirectory, int timeoutMs, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = stdin != null,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!string.IsNullOrEmpty(workingDirectory))
                psi.WorkingDirectory = workingDirectory;
            if (environment != null)
                foreach (var kv in environment)
                    psi.EnvironmentVariables[kv.Key] = kv.Value;

            using (var process = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var stdout = new StringBuilder();
                var stderr = new StringBuilder();
                var outDone = new TaskCompletionSource<bool>();
                var errDone = new TaskCompletionSource<bool>();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data == null) outDone.TrySetResult(true);
                    else lock (stdout) stdout.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data == null) errDone.TrySetResult(true);
                    else lock (stderr) stderr.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (stdin != null)
                {
                    await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
                    process.StandardInput.Close();
                }

                bool exited = await WaitForExitAsync(process, timeoutMs, cancellationToken).ConfigureAwait(false);
                bool timedOut = false;
                if (!exited)
                {
                    timedOut = true;
                    try { if (!process.HasExited) process.Kill(); } catch { }
                }

                // Allow the async readers a moment to flush their final buffers.
                await Task.WhenAny(Task.WhenAll(outDone.Task, errDone.Task), Task.Delay(1000)).ConfigureAwait(false);

                string outStr; lock (stdout) outStr = stdout.ToString();
                string errStr; lock (stderr) errStr = stderr.ToString();

                return new ProcessResult
                {
                    ExitCode = exited ? SafeExitCode(process) : -1,
                    StdOut = outStr,
                    StdErr = errStr,
                    TimedOut = timedOut,
                };
            }
        }

        static int SafeExitCode(Process p)
        {
            try { return p.ExitCode; } catch { return -1; }
        }

        static async Task<bool> WaitForExitAsync(Process process, int timeoutMs, CancellationToken ct)
        {
            var tcs = new TaskCompletionSource<bool>();
            void Handler(object s, EventArgs e) => tcs.TrySetResult(true);
            process.Exited += Handler;
            try
            {
                if (process.HasExited) return true;
                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    Task delay = Task.Delay(timeoutMs, ct);
                    Task completed = await Task.WhenAny(tcs.Task, delay).ConfigureAwait(false);
                    return completed == tcs.Task && tcs.Task.Status == TaskStatus.RanToCompletion;
                }
            }
            catch (OperationCanceledException) { return false; }
            finally { process.Exited -= Handler; }
        }
    }
}
