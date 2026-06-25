using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>
    /// Inspects a .gputrace using `gpudebug -t <trace> --json --oneshot -q` and parses the result
    /// into a MetalTraceSummary. Schema verified against a real Standalone Metal capture:
    ///   status  -> {device, profileState, summary:"N command buffers, M encoders, K draw calls", trace}
    ///   find [R] -> {matches:[{label, summary:"n draws", url}], totalMatches}
    /// Per-pass / frame GPU time is NOT exposed by gpudebug v1.0 (all cost/counter views empty), so
    /// timing is reported as unavailable and the "top pass" is ranked by draw count.
    ///
    /// THREADING: all IO runs on the thread pool (ProcessRunner) and parsing uses only thread-safe
    /// APIs (regex / string), with ConfigureAwait(false). This method must be safe to complete
    /// without the Unity main thread, because the AI Assistant invokes it on the main thread and
    /// depending on the main-thread SynchronizationContext there froze the Editor.
    /// </summary>
    public static class MetalTraceInspector
    {
        public const string GpuDebug = "/usr/bin/gpudebug";

        public static async Task<MetalTraceSummary> InspectAsync(
            string tracePath,
            Action<string> onLog = null,
            CancellationToken cancellationToken = default)
        {
            var s = new MetalTraceSummary { tracePath = tracePath };
            void L(string m) => onLog?.Invoke(m);

            if (string.IsNullOrEmpty(tracePath) || !(File.Exists(tracePath) || Directory.Exists(tracePath)))
            {
                s.success = false;
                s.error = "Trace not found: " + tracePath;
                return s;
            }

            string args = "-t \"" + tracePath + "\" --json --oneshot -q";
            // status first, then enumerate render encoders. No `profile run`: timing is unavailable
            // and skipping it keeps inspection fast (~1s vs ~11s).
            string stdin = "status\nfind [R]\n";

            L("Running: gpudebug " + args);
            ProcessResult pr = await ProcessRunner
                .RunAsync(GpuDebug, args, null, stdin, null, 60000, cancellationToken)
                .ConfigureAwait(false);
            s.rawLog = pr.StdOut + (string.IsNullOrWhiteSpace(pr.StdErr) ? "" : "\n[stderr]\n" + pr.StdErr);

            if (pr.TimedOut)
            {
                s.success = false;
                s.error = "gpudebug timed out (60s). The trace may be very large or gpudebug hung.";
                return s;
            }

            List<string> objects = ExtractJsonObjects(pr.StdOut);
            string statusJson = objects.Find(o => o.Contains("\"device\"") && o.Contains("\"summary\""));
            string findJson = objects.Find(o => o.Contains("\"matches\"") || o.Contains("\"totalMatches\""));

            // ----- status -----
            if (!string.IsNullOrEmpty(statusJson))
            {
                s.device = JsonUnescape(MatchString(statusJson, "device"));
                ParseCounts(JsonUnescape(MatchString(statusJson, "summary")), s);
            }
            else L("warning: no status JSON found in gpudebug output.");

            // ----- find [R] -----
            if (!string.IsNullOrEmpty(findJson))
            {
                int tm = MatchInt(findJson, "totalMatches");
                s.totalMatches = tm;
                s.passesCapped = tm >= 100; // gpudebug caps find at 100

                // Each match: {"label":"...","summary":"...","url":"..."} (key order stable).
                var rx = new Regex(
                    "\\{\\s*\"label\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"\\s*,\\s*" +
                    "\"summary\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"\\s*,\\s*" +
                    "\"url\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"\\s*\\}");
                foreach (Match m in rx.Matches(findJson))
                {
                    s.passes.Add(new MetalPassInfo
                    {
                        label = TrimQuotes(JsonUnescape(m.Groups[1].Value)),
                        drawCount = ParseDraws(JsonUnescape(m.Groups[2].Value)),
                        url = JsonUnescape(m.Groups[3].Value),
                        gpuTimeAvailable = false,
                        gpuMs = 0,
                    });
                }
            }
            else L("warning: no find JSON found in gpudebug output.");

            // ----- timing: unavailable (never fabricated) -----
            s.cpuFrameTimeAvailable = false;
            s.gpuFrameTimeAvailable = false;
            s.timingNote = "CPU/GPU frame time not available: gpudebug v1.0 exposes no frame or " +
                           "per-encoder GPU timing for Standalone Metal captures (performance/encoders " +
                           "and timeline/counters return empty).";

            // ----- top pass by draw count (proxy for GPU cost) -----
            MetalPassInfo top = null;
            foreach (var p in s.passes)
                if (top == null || p.drawCount > top.drawCount) top = p;
            s.topPass = top;
            s.topPassMetric = "draw count (GPU time unavailable)";

            s.success = true;
            return s;
        }

        static void ParseCounts(string summary, MetalTraceSummary s)
        {
            if (string.IsNullOrEmpty(summary)) return;
            s.commandBufferCount = ReadInt(summary, @"(\d+)\s+command buffer");
            s.encoderCount       = ReadInt(summary, @"(\d+)\s+encoder");
            s.drawCallCount      = ReadInt(summary, @"(\d+)\s+draw call");
        }

        static int ParseDraws(string summary) => ReadInt(summary, @"(\d+)\s+draw");

        static int ReadInt(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            Match m = Regex.Match(text, pattern);
            return (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) ? v : 0;
        }

        // Extract a JSON string field value (still escaped) for the given key from a JSON object string.
        static string MatchString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static int MatchInt(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(\\d+)");
            return (m.Success && int.TryParse(m.Groups[1].Value, out int v)) ? v : 0;
        }

        // Minimal JSON string unescape for the escapes gpudebug emits (\/ \" \\ \n \t).
        static string JsonUnescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length)
                {
                    char n = s[++i];
                    switch (n)
                    {
                        case '/': sb.Append('/'); break;
                        case '\"': sb.Append('\"'); break;
                        case '\\': sb.Append('\\'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        default: sb.Append(n); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        static string TrimQuotes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '\"' && s[s.Length - 1] == '\"') s = s.Substring(1, s.Length - 2);
            return s;
        }

        /// <summary>Pulls top-level {...} JSON objects out of gpudebug's prompt-prefixed stdout.</summary>
        public static List<string> ExtractJsonObjects(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text)) return list;
            int depth = 0, start = -1;
            bool inStr = false, esc = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '\"') inStr = false;
                    continue;
                }
                if (c == '\"') { inStr = true; continue; }
                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    if (depth > 0)
                    {
                        depth--;
                        if (depth == 0 && start >= 0)
                        {
                            list.Add(text.Substring(start, i - start + 1));
                            start = -1;
                        }
                    }
                }
            }
            return list;
        }
    }
}
