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
    /// Runs external CLI tools (gpucapture / gpudebug) without blocking the Editor main thread.
    /// Drains stdout AND stderr asynchronously to avoid pipe-buffer deadlocks (addendum rule B).
    /// </summary>
    public static class ProcessRunner
    {
        public static async Task<ProcessResult> RunAsync(
            string fileName,
            string arguments,
            IDictionary<string, string> environment = null,
            string stdin = null,
            string workingDirectory = null,
            int timeoutMs = 120000,
            CancellationToken cancellationToken = default)
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
                    await process.StandardInput.WriteAsync(stdin);
                    process.StandardInput.Close();
                }

                bool exited = await WaitForExitAsync(process, timeoutMs, cancellationToken);
                bool timedOut = false;
                if (!exited)
                {
                    timedOut = true;
                    try { if (!process.HasExited) process.Kill(); } catch { }
                }

                // Allow the async readers a moment to flush their final buffers.
                await Task.WhenAny(Task.WhenAll(outDone.Task, errDone.Task), Task.Delay(1000));

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
                    Task completed = await Task.WhenAny(tcs.Task, delay);
                    return completed == tcs.Task && tcs.Task.Status == TaskStatus.RanToCompletion;
                }
            }
            catch (OperationCanceledException) { return false; }
            finally { process.Exited -= Handler; }
        }
    }
}
