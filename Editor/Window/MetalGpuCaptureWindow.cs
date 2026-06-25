using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>
    /// Entry point A: human-driven dockable window, organized into tabs:
    ///   Summary  - at-a-glance GPU frame time, budget gauge, Top 3 insights.
    ///   Capture  - environment checks, build/capture controls, trace input + actions.
    ///   Details  - full per-category breakdown, top GPU passes, counts, notes.
    ///   Log      - verbose CLI log.
    /// Tabs are a simple button-bar + page-swap (no TabView dependency). The status line is
    /// persistent across tabs. After a capture/inspect the window auto-switches to Summary.
    /// </summary>
    public class MetalGpuCaptureWindow : EditorWindow
    {
        const string AssistantMenuItem = "Window/AI/Assistant";
        static readonly string[] TabNames = { "Summary", "Capture", "Details", "Log" };

        // Tabs
        Button[] _tabButtons;
        VisualElement[] _pages;
        int _activeTab;

        // Capture page widgets
        VisualElement _envContainer;
        Label _lastBuildLabel;
        RadioButtonGroup _buildMode;
        IntegerField _warmup;
        Toggle _waitSignal;
        Button _captureBtn;
        Button _recheckBtn;
        TextField _traceField;
        Button _browseBtn;
        Button _xcodeBtn;
        Toggle _loadTiming;
        Toggle _classifyBottlenecks;
        IntegerField _targetFps;
        Button _inspectBtn;
        Button _askAiBtn;

        // Persistent + result pages
        Label _statusLabel;
        VisualElement _summaryContainer;
        VisualElement _detailsContainer;
        ScrollView _logScroll;
        Label _logLabel;

        string _existingBuild;
        bool _busy;
        MetalTraceSummary _lastSummary;

        [MenuItem("Window/Analysis/Metal GPU Capture")]
        public static void Open()
        {
            MetalGpuCaptureWindow w = GetWindow<MetalGpuCaptureWindow>();
            w.titleContent = new GUIContent("Metal GPU Capture");
            w.minSize = new Vector2(480, 600);
            w.Show();
        }

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 8; root.style.paddingRight = 8;
            root.style.paddingTop = 8; root.style.paddingBottom = 8;

            Label title = new Label("Metal GPU Capture");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 14;
            title.style.marginBottom = 6;
            root.Add(title);

            // ---------- Tab bar ----------
            VisualElement tabBar = Row();
            tabBar.style.marginBottom = 6;
            _tabButtons = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int idx = i;
                Button b = new Button(() => SelectTab(idx)) { text = TabNames[i] };
                b.style.flexGrow = 1;
                b.style.marginRight = (i < TabNames.Length - 1) ? 2 : 0;
                _tabButtons[i] = b;
                tabBar.Add(b);
            }
            root.Add(tabBar);

            // ---------- Persistent status ----------
            _statusLabel = new Label(string.Empty);
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusLabel.style.whiteSpace = WhiteSpace.Normal;
            _statusLabel.style.marginBottom = 6;
            root.Add(_statusLabel);

            // ---------- Pages ----------
            ScrollView pageSummary = MakeScrollPage();
            ScrollView pageCapture = MakeScrollPage();
            ScrollView pageDetails = MakeScrollPage();
            ScrollView pageLog = MakeScrollPage();
            _pages = new VisualElement[] { pageSummary, pageCapture, pageDetails, pageLog };
            root.Add(pageSummary);
            root.Add(pageCapture);
            root.Add(pageDetails);
            root.Add(pageLog);

            // --- Summary page ---
            pageSummary.Add(SectionLabel("Summary"));
            _summaryContainer = new VisualElement();
            _summaryContainer.style.marginTop = 4;
            pageSummary.Add(_summaryContainer);

            // --- Capture page ---
            VisualElement envHeader = Row();
            envHeader.style.justifyContent = Justify.SpaceBetween;
            envHeader.style.alignItems = Align.Center;
            envHeader.Add(SectionLabel("Environment"));
            _recheckBtn = new Button(RefreshEnvironment) { text = "Re-check" };
            envHeader.Add(_recheckBtn);
            pageCapture.Add(envHeader);

            _envContainer = new VisualElement();
            _envContainer.style.marginTop = 4;
            pageCapture.Add(_envContainer);

            pageCapture.Add(Separator());
            pageCapture.Add(SectionLabel("Build & Capture"));

            _lastBuildLabel = new Label();
            _lastBuildLabel.style.whiteSpace = WhiteSpace.Normal;
            _lastBuildLabel.style.marginBottom = 4;
            pageCapture.Add(_lastBuildLabel);

            _buildMode = new RadioButtonGroup(string.Empty,
                new List<string> { "Reuse last build", "Rebuild (Development, Metal)" });
            pageCapture.Add(_buildMode);

            _warmup = new IntegerField("Warm-up (seconds)") { value = 10 };
            _warmup.tooltip = "Time to let the player render a representative frame before capturing.";
            _warmup.style.marginTop = 4;
            pageCapture.Add(_warmup);

            _waitSignal = new Toggle("Wait for signal (short-lived players)") { value = false };
            _waitSignal.tooltip = "Sets MTLCAPTURE_WAIT_FOR_SIGNAL=1 for players that exit too quickly to attach.";
            pageCapture.Add(_waitSignal);

            _captureBtn = new Button(OnCaptureClicked) { text = "Capture frame" };
            _captureBtn.style.marginTop = 6;
            _captureBtn.style.height = 26;
            pageCapture.Add(_captureBtn);

            pageCapture.Add(Separator());
            pageCapture.Add(SectionLabel("Trace"));

            _traceField = new TextField(".gputrace path");
            _traceField.style.marginTop = 4;
            _traceField.RegisterValueChangedCallback(_ => UpdateAskAiEnabled());
            pageCapture.Add(_traceField);

            VisualElement traceBtns = Row();
            traceBtns.style.marginTop = 4;
            _browseBtn = new Button(OnBrowseClicked) { text = "Browse…" };
            _browseBtn.style.flexGrow = 1;
            _browseBtn.tooltip = "Pick a .gputrace bundle (or paste/type the path in the field).";
            traceBtns.Add(_browseBtn);
            _xcodeBtn = new Button(OnOpenInXcodeClicked) { text = "Open in Xcode" };
            _xcodeBtn.style.flexGrow = 1;
            _xcodeBtn.style.marginLeft = 4;
            _xcodeBtn.tooltip = "Open the current .gputrace in Xcode's Metal debugger (convenience only).";
            traceBtns.Add(_xcodeBtn);
            pageCapture.Add(traceBtns);

            _loadTiming = new Toggle("Load GPU timing (profile, ~15-20s)") { value = true };
            _loadTiming.tooltip = "Runs `gpudebug profile load` to read real GPU frame/pass time. Slower.";
            _loadTiming.style.marginTop = 4;
            pageCapture.Add(_loadTiming);

            _classifyBottlenecks = new Toggle("Classify bottlenecks (GPU counters, +~15-20s)") { value = true };
            _classifyBottlenecks.tooltip = "Runs `info --all` on the top passes to find the GPU limiter " +
                "(ALU / texture / fragment-launch / bandwidth). Requires GPU timing; adds another ~15-20s.";
            _classifyBottlenecks.style.marginTop = 2;
            pageCapture.Add(_classifyBottlenecks);

            _targetFps = new IntegerField("Target frame rate (fps)") { value = 60 };
            _targetFps.tooltip = "Used by the frame-budget gauge (60 fps = 16.67 ms, 90 = 11.1, 120 = 8.3).";
            _targetFps.style.marginTop = 2;
            _targetFps.RegisterValueChangedCallback(_ => { if (_lastSummary != null) ShowResults(_lastSummary); });
            pageCapture.Add(_targetFps);

            VisualElement resultBtns = Row();
            resultBtns.style.marginTop = 4;
            _inspectBtn = new Button(OnInspectClicked) { text = "Inspect trace" };
            _inspectBtn.style.flexGrow = 1;
            _inspectBtn.style.height = 24;
            resultBtns.Add(_inspectBtn);
            _askAiBtn = new Button(OnAskAiClicked) { text = "Ask AI Assistant for insights" };
            _askAiBtn.style.flexGrow = 1;
            _askAiBtn.style.height = 24;
            _askAiBtn.style.marginLeft = 4;
            resultBtns.Add(_askAiBtn);
            pageCapture.Add(resultBtns);

            // --- Details page ---
            pageDetails.Add(SectionLabel("Details"));
            _detailsContainer = new VisualElement();
            _detailsContainer.style.marginTop = 4;
            pageDetails.Add(_detailsContainer);

            // --- Log page ---
            pageLog.Add(SectionLabel("Log"));
            _logScroll = new ScrollView(ScrollViewMode.Vertical);
            _logScroll.style.flexGrow = 1;
            _logScroll.style.marginTop = 4;
            _logLabel = new Label(string.Empty);
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.selection.isSelectable = true;
            _logScroll.Add(_logLabel);
            pageLog.Add(_logScroll);

            SelectTab(0);
            RefreshEnvironment();
            RefreshBuildSection();
            ShowResults(null);
            UpdateAskAiEnabled();
        }

        // ---------- Tabs ----------
        void SelectTab(int idx)
        {
            _activeTab = idx;
            for (int i = 0; i < _pages.Length; i++)
                _pages[i].style.display = (i == idx) ? DisplayStyle.Flex : DisplayStyle.None;
            for (int i = 0; i < _tabButtons.Length; i++)
            {
                bool active = i == idx;
                _tabButtons[i].style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
                _tabButtons[i].style.backgroundColor = active ? new Color(1, 1, 1, 0.12f) : new Color(1, 1, 1, 0.02f);
            }
        }

        static ScrollView MakeScrollPage()
        {
            ScrollView sv = new ScrollView(ScrollViewMode.Vertical);
            sv.style.flexGrow = 1;
            return sv;
        }

        // ---------- Environment ----------
        void RefreshEnvironment()
        {
            if (_envContainer == null) return;
            _envContainer.Clear();

            MetalEnvironmentStatus status = MetalCaptureEnvironment.Check();
            AddCheckRow(status.macOs);
            AddCheckRow(status.gpucapture);
            AddCheckRow(status.gpudebug);
            AddCheckRow(status.metalActive);

            Label summary = new Label(status.AllPass
                ? "All checks passed - ready to capture."
                : "Some checks failed - capture may not work.");
            summary.style.marginTop = 6;
            summary.style.unityFontStyleAndWeight = FontStyle.Bold;
            summary.style.color = status.AllPass
                ? new Color(0.40f, 0.80f, 0.40f)
                : new Color(0.90f, 0.60f, 0.30f);
            _envContainer.Add(summary);
        }

        void AddCheckRow(EnvironmentCheck check)
        {
            VisualElement row = Row();
            row.style.marginBottom = 2;
            Label icon = new Label(check.pass ? "✅" : "❌");
            icon.style.width = 22;
            row.Add(icon);
            Label text = new Label(check.label + "  -  " + check.detail);
            text.style.flexGrow = 1;
            text.style.whiteSpace = WhiteSpace.Normal;
            row.Add(text);
            _envContainer.Add(row);
        }

        // ---------- Build section ----------
        void RefreshBuildSection()
        {
            _existingBuild = MetalBuildService.FindExistingBuild();
            if (string.IsNullOrEmpty(_existingBuild))
            {
                _lastBuildLabel.text = "Last build: (none found - will rebuild)";
                _buildMode.value = 1;
                _buildMode.SetEnabled(false);
            }
            else
            {
                _lastBuildLabel.text = "Last build: " + _existingBuild;
                _buildMode.SetEnabled(true);
                if (_buildMode.value < 0) _buildMode.value = 0;
            }
        }

        // ---------- Capture ----------
        async void OnCaptureClicked()
        {
            if (_busy) return;
            SetBusy(true);
            ClearLog();

            try
            {
                bool rebuild = _buildMode.value == 1 || string.IsNullOrEmpty(_existingBuild);
                string appPath;

                if (rebuild)
                {
                    _statusLabel.text = "Building... (Editor unresponsive during build)";
                    AppendLog("Building macOS Standalone (Development, Metal)...");
                    MetalBuildResult b = MetalBuildService.BuildStandalonePlayer();
                    AppendLog(b.summary);
                    if (!b.success) { _statusLabel.text = "Build failed."; return; }
                    appPath = b.appPath;
                    AppendLog("Built: " + appPath);
                }
                else
                {
                    appPath = _existingBuild;
                    AppendLog("Reusing build: " + appPath);
                }

                _statusLabel.text = "Capturing...";
                int warm = Mathf.Max(0, _warmup.value);
                MetalCaptureResult cap = await MetalCaptureService.CaptureAsync(appPath, warm, _waitSignal.value, AppendLog).ConfigureAwait(true);

                if (cap.success)
                {
                    _statusLabel.text = "Capture complete (" + cap.method + ").";
                    _traceField.value = cap.tracePath;
                    await InspectAndShow(cap.tracePath);
                }
                else
                {
                    _statusLabel.text = "Capture failed: " + cap.error;
                }
            }
            catch (Exception e) { _statusLabel.text = "Error: " + e.Message; AppendLog(e.ToString()); }
            finally { SetBusy(false); RefreshBuildSection(); }
        }

        // ---------- Inspect ----------
        async void OnInspectClicked()
        {
            if (_busy) return;
            string path = _traceField.value;
            if (string.IsNullOrEmpty(path)) { _statusLabel.text = "Enter a .gputrace path first."; return; }
            SetBusy(true);
            try { await InspectAndShow(path); }
            catch (Exception e) { _statusLabel.text = "Inspect error: " + e.Message; AppendLog(e.ToString()); }
            finally { SetBusy(false); }
        }

        async Task InspectAndShow(string tracePath)
        {
            bool timing = _loadTiming != null && _loadTiming.value;
            bool classify = timing && _classifyBottlenecks != null && _classifyBottlenecks.value;
            _statusLabel.text = !timing ? "Inspecting trace..."
                : (classify ? "Inspecting + GPU timing + bottlenecks (~30-40s)..." : "Inspecting + loading GPU timing (~15-20s)...");
            MetalTraceSummary sum = await MetalTraceInspector.InspectAsync(tracePath, AppendLog, timing, classify).ConfigureAwait(true);
            _lastSummary = sum;
            ShowResults(sum);
            UpdateAskAiEnabled();
            _statusLabel.text = sum.success ? "Inspection complete." : ("Inspection failed: " + sum.error);
            if (sum.success) SelectTab(0); // jump to Summary
        }

        // ---------- Ask AI Assistant ----------
        async void OnAskAiClicked()
        {
            string tracePath = _traceField.value;
            if (string.IsNullOrEmpty(tracePath))
            {
                _statusLabel.text = "No trace to analyze. Capture or set a .gputrace path first.";
                return;
            }

            string prompt = BuildInsightPrompt(tracePath, _lastSummary);
            bool launched = false;

#if METAL_GPU_CAPTURE_ASSISTANT_PRESENT
            try
            {
                var ctx = new Unity.AI.Assistant.Editor.Api.AssistantApi.AttachedContext();
                if (_lastSummary != null && _lastSummary.success)
                {
                    string json = JsonUtility.ToJson(_lastSummary);
                    ctx.Add(new Unity.AI.Assistant.VirtualAttachment(
                        payload: json,
                        type: "MetalTraceSummary",
                        displayName: "Metal GPU Trace Summary",
                        metadata: null));
                }

                AppendLog("Opening AI Assistant with prefilled insight prompt...");
                // Show a prompt popup anchored to this window so the user can review/submit.
                await Unity.AI.Assistant.Editor.Api.AssistantApi.PromptThenRun(rootVisualElement, prompt, ctx).ConfigureAwait(true);
                launched = true;
            }
            catch (Exception e)
            {
                AppendLog("Programmatic Assistant launch failed: " + e.Message);
            }
#endif

            if (!launched)
            {
                EditorGUIUtility.systemCopyBuffer = prompt;
                AppendLog("Insight prompt copied to clipboard.");
                bool open = EditorUtility.DisplayDialog(
                    "Metal GPU Capture",
                    "The insight prompt has been copied to your clipboard.\n\nOpen the AI Assistant window and paste it to get URP-focused optimization insights?",
                    "Open Assistant", "Cancel");
                if (open)
                {
                    if (!EditorApplication.ExecuteMenuItem(AssistantMenuItem))
                        _statusLabel.text = "Could not open the AI Assistant (" + AssistantMenuItem + ").";
                }
            }
        }

        string BuildInsightPrompt(string tracePath, MetalTraceSummary s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Analyze this captured Metal GPU frame and give URP-focused optimization insights, prioritized by impact.");
            sb.AppendLine("Run the interpret-gpu-trace skill (tool Metal_InspectTrace) on this trace file:");
            sb.AppendLine(tracePath);
            if (s != null && s.success)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Known summary: device {0}; {1} command buffers, {2} encoders, {3} draw calls.",
                    s.device, s.commandBufferCount, s.encoderCount, s.drawCallCount));
                if (s.gpuFrameTimeAvailable)
                    sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "GPU frame time: {0:F2} ms.", s.gpuFrameMs));
                if (s.gpuTimingLoaded && s.gpuPasses.Count > 0)
                {
                    sb.AppendLine("Top GPU passes by GPU time:");
                    int n = Mathf.Min(5, s.gpuPasses.Count);
                    for (int i = 0; i < n; i++)
                    {
                        MetalPassInfo p = s.gpuPasses[i];
                        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "  - {0}: {1:F2} ms ({2:F1}%)", p.label, p.gpuMs, p.costPercent));
                    }
                }
                else if (s.topPass != null)
                {
                    sb.AppendLine(string.Format("Top pass by draw count: {0} ({1} draws).", s.topPass.label, s.topPass.drawCount));
                }
                sb.AppendLine("CPU frame time is not in a GPU trace; use Unity FrameTimingManager for CPU.");
            }
            return sb.ToString();
        }

        void UpdateAskAiEnabled()
        {
            bool hasTrace = !string.IsNullOrEmpty(_traceField != null ? _traceField.value : null);
            if (_askAiBtn != null) _askAiBtn.SetEnabled(!_busy && hasTrace);
            if (_xcodeBtn != null) _xcodeBtn.SetEnabled(hasTrace);
        }

        // ---------- Browse / Open in Xcode ----------
        void OnBrowseClicked()
        {
            string start = GetBrowseStartDir();
            // .gputrace is a macOS bundle directory: OpenFilePanel selects it when it's registered as
            // a package; otherwise fall back to a folder picker.
            string picked = EditorUtility.OpenFilePanel("Select .gputrace", start, "gputrace");
            if (string.IsNullOrEmpty(picked))
                picked = EditorUtility.OpenFolderPanel("Select .gputrace bundle", start, string.Empty);
            if (!string.IsNullOrEmpty(picked))
            {
                _traceField.value = picked;
                UpdateAskAiEnabled();
            }
        }

        void OnOpenInXcodeClicked()
        {
            string path = _traceField != null ? _traceField.value : null;
            if (string.IsNullOrEmpty(path) || !(System.IO.File.Exists(path) || System.IO.Directory.Exists(path)))
            {
                _statusLabel.text = "No valid .gputrace to open. Set a path first.";
                return;
            }
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "/usr/bin/open",
                    Arguments = "-a Xcode \"" + path + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                AppendLog("Opening in Xcode: " + path);
                _statusLabel.text = "Opening in Xcode…";
            }
            catch (Exception e)
            {
                _statusLabel.text = "Could not open Xcode: " + e.Message;
                AppendLog("open -a Xcode failed: " + e.Message + " (is Xcode installed?)");
            }
        }

        string GetBrowseStartDir()
        {
            string cur = _traceField != null ? _traceField.value : null;
            if (!string.IsNullOrEmpty(cur))
            {
                try
                {
                    string d = System.IO.Path.GetDirectoryName(cur);
                    if (!string.IsNullOrEmpty(d) && System.IO.Directory.Exists(d)) return d;
                }
                catch { }
            }
            return System.IO.Directory.GetCurrentDirectory();
        }

        // ---------- Results rendering (Summary + Details pages) ----------
        void ShowResults(MetalTraceSummary s)
        {
            ShowSummary(s);
            ShowDetails(s);
        }

        void ShowSummary(MetalTraceSummary s)
        {
            if (_summaryContainer == null) return;
            _summaryContainer.Clear();

            if (s == null) { _summaryContainer.Add(Hint("Capture a frame (Capture tab) or inspect a .gputrace to see a summary.")); return; }
            if (!s.success) { _summaryContainer.Add(new Label("Error: " + s.error)); return; }

            _summaryContainer.Add(KV("Device", s.device));
            _summaryContainer.Add(KV("Encoders / draws", s.encoderCount + " / " + s.drawCallCount));
            _summaryContainer.Add(KV("GPU frame time",
                s.gpuFrameTimeAvailable
                    ? s.gpuFrameMs.ToString("F2", CultureInfo.InvariantCulture) + " ms"
                    : "unavailable (enable 'Load GPU timing' on the Capture tab)"));

            if (s.gpuTimingLoaded)
            {
                AddBudgetGauge(s, _summaryContainer);
                if (s.bottlenecksClassified && s.gpuPasses.Count > 0 && !string.IsNullOrEmpty(s.gpuPasses[0].bottleneck))
                    _summaryContainer.Add(KV("Top pass bottleneck", s.gpuPasses[0].bottleneck));
                AddInsights(s, _summaryContainer);
            }
            else
            {
                _summaryContainer.Add(Hint(string.IsNullOrEmpty(s.timingNote)
                    ? "Enable 'Load GPU timing' (Capture tab) for the budget gauge and Top 3 insights."
                    : s.timingNote));
            }
        }

        void ShowDetails(MetalTraceSummary s)
        {
            if (_detailsContainer == null) return;
            _detailsContainer.Clear();

            if (s == null) { _detailsContainer.Add(Hint("No trace inspected yet.")); return; }
            if (!s.success) { _detailsContainer.Add(new Label("Error: " + s.error)); return; }

            _detailsContainer.Add(KV("Device", s.device));
            _detailsContainer.Add(KV("Command buffers / encoders / draws",
                s.commandBufferCount + " / " + s.encoderCount + " / " + s.drawCallCount));
            _detailsContainer.Add(KV("CPU frame time",
                s.cpuFrameTimeAvailable ? s.cpuFrameMs.ToString("F2", CultureInfo.InvariantCulture) + " ms" : "unavailable"));
            _detailsContainer.Add(KV("GPU frame time",
                s.gpuFrameTimeAvailable ? s.gpuFrameMs.ToString("F2", CultureInfo.InvariantCulture) + " ms" : "unavailable"));

            if (s.gpuTimingLoaded)
                AddBreakdown(s, _detailsContainer);

            if (s.gpuTimingLoaded && s.gpuPasses != null && s.gpuPasses.Count > 0)
            {
                Label h = new Label("Top GPU passes (by GPU time):");
                h.style.unityFontStyleAndWeight = FontStyle.Bold;
                h.style.marginTop = 8;
                _detailsContainer.Add(h);
                int n = Mathf.Min(8, s.gpuPasses.Count);
                for (int i = 0; i < n; i++)
                {
                    MetalPassInfo p = s.gpuPasses[i];
                    Label row = new Label(string.Format(CultureInfo.InvariantCulture,
                        "    {0}. {1}  -  {2:F2} ms ({3:F1}%)", i + 1, p.label, p.gpuMs, p.costPercent));
                    row.style.whiteSpace = WhiteSpace.Normal;
                    _detailsContainer.Add(row);

                    if (!string.IsNullOrEmpty(p.bottleneck))
                    {
                        Label bl = new Label("        ↳ " + p.bottleneck +
                            (string.IsNullOrEmpty(p.bottleneckDetail) ? string.Empty : "  [" + p.bottleneckDetail + "]"));
                        bl.style.whiteSpace = WhiteSpace.Normal;
                        bl.style.fontSize = 11;
                        bl.style.color = new Color(0.75f, 0.75f, 0.75f);
                        _detailsContainer.Add(bl);
                    }
                }
            }
            else if (s.topPass != null)
            {
                Label tp = new Label("Top GPU pass (by " + s.topPassMetric + "):");
                tp.style.unityFontStyleAndWeight = FontStyle.Bold;
                tp.style.marginTop = 8;
                _detailsContainer.Add(tp);
                Label tpv = new Label("    " + s.topPass.label + "  -  " + s.topPass.drawCount + " draws");
                tpv.style.whiteSpace = WhiteSpace.Normal;
                _detailsContainer.Add(tpv);
            }

            if (!string.IsNullOrEmpty(s.timingNote))
            {
                Label note = new Label("Note: " + s.timingNote);
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginTop = 8;
                note.style.fontSize = 11;
                note.style.color = new Color(0.7f, 0.7f, 0.7f);
                _detailsContainer.Add(note);
            }
            if (s.passesCapped)
            {
                Label cap = new Label("(Pass list capped at " + s.totalMatches + " by gpudebug 'find'; " +
                                      "encoder total = " + s.encoderCount + ".)");
                cap.style.whiteSpace = WhiteSpace.Normal;
                cap.style.fontSize = 11;
                cap.style.color = new Color(0.7f, 0.7f, 0.7f);
                _detailsContainer.Add(cap);
            }
        }

        VisualElement KV(string k, string v)
        {
            VisualElement row = Row();
            row.style.marginBottom = 2;
            Label key = new Label(k + ":");
            key.style.width = 240;
            key.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(key);
            Label val = new Label(string.IsNullOrEmpty(v) ? "-" : v);
            val.style.flexGrow = 1;
            val.style.whiteSpace = WhiteSpace.Normal;
            row.Add(val);
            return row;
        }

        static Label Hint(string text)
        {
            Label l = new Label(text);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.color = new Color(0.7f, 0.7f, 0.7f);
            l.style.marginTop = 4;
            return l;
        }

        // ---------- Insight UI (frame budget / breakdown / Top 3) ----------
        void AddBudgetGauge(MetalTraceSummary s, VisualElement c)
        {
            int fps = (_targetFps != null && _targetFps.value > 0) ? _targetFps.value : 60;
            double target = MetalInsights.TargetMs(fps);
            double frame = s.gpuFrameMs;
            bool over = frame > target;
            double delta = Math.Abs(frame - target);
            double nowFps = frame > 0 ? 1000.0 / frame : 0;

            Label h = new Label(string.Format(CultureInfo.InvariantCulture,
                "Frame budget @ {0} fps = {1:F2} ms", fps, target));
            h.style.unityFontStyleAndWeight = FontStyle.Bold;
            h.style.marginTop = 6;
            c.Add(h);

            // Bar fills to 50% at target, 100% at 2x target.
            float frac = target > 0 ? (float)Math.Min(frame / target, 2.0) / 2f : 0f;
            VisualElement track = new VisualElement();
            track.style.height = 16;
            track.style.marginTop = 2;
            track.style.backgroundColor = new Color(1, 1, 1, 0.08f);
            VisualElement fill = new VisualElement();
            fill.style.height = 16;
            fill.style.width = Length.Percent(frac * 100f);
            fill.style.backgroundColor = over ? new Color(0.85f, 0.45f, 0.30f) : new Color(0.40f, 0.75f, 0.45f);
            track.Add(fill);
            c.Add(track);

            Label verdict = new Label(over
                ? string.Format(CultureInfo.InvariantCulture,
                    "OVER budget by {0:F2} ms (≈ {1:F0} fps now). Cut {0:F2} ms to hit {2} fps.", delta, nowFps, fps)
                : string.Format(CultureInfo.InvariantCulture,
                    "Within budget — {0:F2} ms headroom (≈ {1:F0} fps now).", delta, nowFps));
            verdict.style.whiteSpace = WhiteSpace.Normal;
            verdict.style.marginTop = 2;
            verdict.style.color = over ? new Color(0.90f, 0.60f, 0.30f) : new Color(0.50f, 0.80f, 0.50f);
            c.Add(verdict);
        }

        void AddBreakdown(MetalTraceSummary s, VisualElement c)
        {
            List<CategoryCost> bd = MetalInsights.Breakdown(s);
            if (bd.Count == 0) return;

            Label h = new Label("GPU time by category");
            h.style.unityFontStyleAndWeight = FontStyle.Bold;
            h.style.marginTop = 8;
            c.Add(h);

            int shown = Mathf.Min(10, bd.Count);
            for (int i = 0; i < shown; i++)
            {
                CategoryCost cc = bd[i];
                VisualElement row = Row();
                row.style.alignItems = Align.Center;
                row.style.marginBottom = 1;

                Label name = new Label(cc.category);
                name.style.width = 200;
                name.style.whiteSpace = WhiteSpace.Normal;
                row.Add(name);

                VisualElement track = new VisualElement();
                track.style.flexGrow = 1;
                track.style.height = 12;
                track.style.backgroundColor = new Color(1, 1, 1, 0.06f);
                VisualElement fill = new VisualElement();
                fill.style.height = 12;
                fill.style.width = Length.Percent((float)Math.Min(cc.percent, 100.0));
                fill.style.backgroundColor = new Color(0.40f, 0.60f, 0.85f);
                track.Add(fill);
                row.Add(track);

                Label val = new Label(string.Format(CultureInfo.InvariantCulture, "  {0:F2} ms ({1:F0}%)", cc.ms, cc.percent));
                val.style.width = 110;
                row.Add(val);
                c.Add(row);
            }
        }

        void AddInsights(MetalTraceSummary s, VisualElement c)
        {
            List<MetalInsight> ins = MetalInsights.TopInsights(s, 3);

            Label h = new Label("Top 3 optimization insights");
            h.style.unityFontStyleAndWeight = FontStyle.Bold;
            h.style.marginTop = 10;
            c.Add(h);

            if (ins.Count == 0)
            {
                c.Add(Hint("No high-impact issues detected from measured pass costs."));
                return;
            }

            for (int i = 0; i < ins.Count; i++)
            {
                MetalInsight it = ins[i];
                VisualElement card = new VisualElement();
                card.style.marginTop = 4;
                card.style.paddingLeft = 8; card.style.paddingRight = 8;
                card.style.paddingTop = 6; card.style.paddingBottom = 6;
                card.style.backgroundColor = new Color(1, 1, 1, 0.05f);
                card.style.borderTopLeftRadius = 4; card.style.borderTopRightRadius = 4;
                card.style.borderBottomLeftRadius = 4; card.style.borderBottomRightRadius = 4;

                Label t = new Label(string.Format(CultureInfo.InvariantCulture,
                    "{0}. {1}{2}", i + 1, it.title, it.quickWin ? "  [quick win]" : string.Empty));
                t.style.unityFontStyleAndWeight = FontStyle.Bold;
                t.style.whiteSpace = WhiteSpace.Normal;
                card.Add(t);

                Label ev = new Label("Evidence: " + it.evidence);
                ev.style.whiteSpace = WhiteSpace.Normal;
                ev.style.fontSize = 11;
                ev.style.color = new Color(0.75f, 0.75f, 0.75f);
                card.Add(ev);

                Label fx = new Label("Fix: " + it.fix);
                fx.style.whiteSpace = WhiteSpace.Normal;
                card.Add(fx);

                c.Add(card);
            }
        }

        void SetBusy(bool busy)
        {
            _busy = busy;
            if (_captureBtn != null) { _captureBtn.SetEnabled(!busy); _captureBtn.text = busy ? "Working..." : "Capture frame"; }
            if (_inspectBtn != null) _inspectBtn.SetEnabled(!busy);
            if (_recheckBtn != null) _recheckBtn.SetEnabled(!busy);
            UpdateAskAiEnabled();
        }

        // ---------- Log helpers ----------
        void ClearLog() { if (_logLabel != null) _logLabel.text = string.Empty; }

        void AppendLog(string msg)
        {
            if (_logLabel == null || string.IsNullOrEmpty(msg)) return;
            _logLabel.text += (_logLabel.text.Length > 0 ? "\n" : "") + msg;
            _logScroll.schedule.Execute(() => _logScroll.scrollOffset = new Vector2(0, float.MaxValue));
        }

        // ---------- UI builders ----------
        static VisualElement Row()
        {
            VisualElement v = new VisualElement();
            v.style.flexDirection = FlexDirection.Row;
            return v;
        }

        static Label SectionLabel(string text)
        {
            Label l = new Label(text);
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        static VisualElement Separator()
        {
            VisualElement s = new VisualElement();
            s.style.height = 1;
            s.style.marginTop = 8;
            s.style.marginBottom = 8;
            s.style.backgroundColor = new Color(1, 1, 1, 0.12f);
            return s;
        }
    }
}
