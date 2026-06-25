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
    /// When includeGpuTiming is set, a second pass runs `profile load` and reads whole-frame GPU
    /// time from performance/timeline and per-encoder GPU cost from performance/encoders (verified
    /// against a real URP Standalone capture on M3 Pro). ~15-20s; needs an embedded profiling
    /// session (or an M3/A17+ device). CPU frame time is never in a GPU trace.
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
            bool includeGpuTiming = false,
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

            // ----- top pass by draw count (fallback metric) -----
            MetalPassInfo top = null;
            foreach (var p in s.passes)
                if (top == null || p.drawCount > top.drawCount) top = p;
            s.topPass = top;
            s.topPassMetric = "draw count";

            // CPU frame time is never recorded in a GPU trace.
            s.cpuFrameTimeAvailable = false;

            // ----- GPU timing (optional; ~15-20s; requires `profile load`) -----
            if (includeGpuTiming && !cancellationToken.IsCancellationRequested)
            {
                await LoadGpuTimingAsync(s, onLog, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                s.gpuFrameTimeAvailable = false;
                s.timingNote = "GPU timing not loaded (fast inspect). Enable GPU timing to run " +
                               "`profile load` and read real GPU frame/pass time. " +
                               "CPU frame time is not in a GPU trace.";
            }

            s.success = true;
            return s;
        }

        /// <summary>
        /// Second pass: `profile load` then read whole-frame GPU time (performance/timeline) and
        /// per-encoder GPU cost (performance/encoders). Mutates the summary. ~15-20s. All IO runs off
        /// the main thread via ProcessRunner. JSON schema verified against a real URP Standalone
        /// capture on M3 Pro: timeline info -> {"GPU time":"31.1000 ms", ...}; encoders list ->
        /// {"children":[{"name":"reN","values":[label, {percentage}, vertex ms, frag ms]}], ...}.
        /// </summary>
        static async Task LoadGpuTimingAsync(MetalTraceSummary s, Action<string> onLog, CancellationToken ct)
        {
            void L(string m) => onLog?.Invoke(m);
            string args = "-t \"" + s.tracePath + "\" --json --oneshot -q";
            string stdin = "profile load\ngo performance/timeline\ninfo --all\ngo performance/encoders\nlist --all\n";

            L("Loading GPU timing (profile load, ~15-20s)...");
            ProcessResult pr = await ProcessRunner
                .RunAsync(GpuDebug, args, null, stdin, null, 120000, ct)
                .ConfigureAwait(false);
            s.rawLog += "\n[gpu-timing]\n" + pr.StdOut +
                        (string.IsNullOrWhiteSpace(pr.StdErr) ? "" : "\n[stderr]\n" + pr.StdErr);

            if (pr.TimedOut)
            {
                s.timingNote = "GPU timing load timed out (120s).";
                return;
            }

            List<string> objs = ExtractJsonObjects(pr.StdOut);

            // whole-frame GPU time: the timeline info object carries a "GPU time" field.
            string timelineJson = objs.Find(o => o.Contains("\"GPU time\""));
            if (!string.IsNullOrEmpty(timelineJson))
            {
                double ms = ParseMs(JsonUnescape(MatchString(timelineJson, "GPU time")));
                if (ms <= 0) ms = ParseMs(JsonUnescape(MatchString(timelineJson, "Frame duration")));
                if (ms > 0) { s.gpuFrameMs = ms; s.gpuFrameTimeAvailable = true; }
            }

            // per-encoder GPU cost: pick the encoders list with the MOST entries (the `list --all`
            // output, not the shorter auto-list `go` emits first). Children carry a "percentage" value.
            string encJson = null;
            int bestCount = -1;
            var entryRx = new Regex("\"name\"\\s*:\\s*\"[A-Za-z]+\\d+\"");
            foreach (string o in objs)
            {
                if (!o.Contains("\"percentage\"") || !o.Contains("\"children\"")) continue;
                int cnt = entryRx.Matches(o).Count;
                if (cnt > bestCount) { bestCount = cnt; encJson = o; }
            }
            if (!string.IsNullOrEmpty(encJson))
                ParseEncoders(encJson, s);

            if (s.gpuFrameTimeAvailable)
            {
                s.gpuTimingLoaded = true;
                if (s.gpuPasses.Count > 0)
                {
                    s.topPass = s.gpuPasses[0];   // performance/encoders is sorted by cost (highest first)
                    s.topPassMetric = "GPU time";
                }
                s.timingNote = "GPU timing from the trace's profiling session. " +
                               "CPU frame time is not in a GPU trace (use Unity FrameTimingManager).";
            }
            else
            {
                s.timingNote = "Could not load GPU timing (no embedded profiling session, or a " +
                               "non-M3/A17 replay device). Showing passes ranked by draw count.";
            }
        }

        static void ParseEncoders(string encJson, MetalTraceSummary s)
        {
            // Segment by encoder "name" rather than capturing the values array with [^\]]*, because
            // encoder labels contain ']' (e.g. "[R] ...") which would truncate a bracket-bounded match.
            // Match any encoder id (reN render, plus compute/blit prefixes), not just "reN".
            var nameRx = new Regex("\"name\"\\s*:\\s*\"([A-Za-z]+\\d+)\"");
            var strRx  = new Regex("\"value\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            var pctRx  = new Regex("\"percentage\"\\s*,\\s*\"value\"\\s*:\\s*([0-9.]+)");
            var msRx   = new Regex("\"value\"\\s*:\\s*\"([0-9.]+)\\s*ms\"");

            MatchCollection names = nameRx.Matches(encJson);
            for (int k = 0; k < names.Count; k++)
            {
                string name = names[k].Groups[1].Value;
                int segStart = names[k].Index;
                int segEnd = (k + 1 < names.Count) ? names[k + 1].Index : encJson.Length;
                string seg = encJson.Substring(segStart, segEnd - segStart);

                // values[0] is the label (first string value in the segment).
                Match lm = strRx.Match(seg);
                string label = lm.Success ? TrimQuotes(JsonUnescape(lm.Groups[1].Value)) : name;

                double pct = 0;
                Match pm = pctRx.Match(seg);
                if (pm.Success) double.TryParse(pm.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out pct);

                double vMs = 0, fMs = 0;
                MatchCollection ms = msRx.Matches(seg);
                if (ms.Count > 0) double.TryParse(ms[0].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out vMs);
                if (ms.Count > 1) double.TryParse(ms[1].Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out fMs);

                s.gpuPasses.Add(new MetalPassInfo
                {
                    label = label,
                    url = "performance/encoders/" + name,
                    costPercent = pct,
                    vertexMs = vMs,
                    fragmentMs = fMs,
                    gpuMs = s.gpuFrameTimeAvailable ? pct / 100.0 * s.gpuFrameMs : (vMs + fMs),
                    gpuTimeAvailable = true,
                    drawCount = 0,
                });
            }
        }

        static double ParseMs(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            Match m = Regex.Match(text, "([0-9.]+)\\s*ms");
            return (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) ? v : 0;
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
