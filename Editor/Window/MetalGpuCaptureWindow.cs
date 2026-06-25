using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace JeminLee.MetalGpuCaptureSkill.Editor
{
    /// <summary>
    /// Entry point A: human-driven dockable window.
    /// Step 1 = environment. Step 2 = build + capture. Step 3 = minimal results (CPU/GPU frame time
    /// + top GPU pass). AI section is added in a later step.
    /// </summary>
    public class MetalGpuCaptureWindow : EditorWindow
    {
        VisualElement _envContainer;
        Label _lastBuildLabel;
        RadioButtonGroup _buildMode;
        IntegerField _warmup;
        Toggle _waitSignal;
        Button _captureBtn;
        Button _recheckBtn;
        Label _statusLabel;
        TextField _traceField;
        Button _inspectBtn;
        VisualElement _resultsContainer;
        ScrollView _logScroll;
        Label _logLabel;

        string _existingBuild;
        bool _busy;

        [MenuItem("Window/Analysis/Metal GPU Capture")]
        public static void Open()
        {
            MetalGpuCaptureWindow w = GetWindow<MetalGpuCaptureWindow>();
            w.titleContent = new GUIContent("Metal GPU Capture");
            w.minSize = new Vector2(480, 560);
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

            // ---------- Environment ----------
            VisualElement envHeader = Row();
            envHeader.style.justifyContent = Justify.SpaceBetween;
            envHeader.style.alignItems = Align.Center;
            envHeader.Add(SectionLabel("Environment"));
            _recheckBtn = new Button(RefreshEnvironment) { text = "Re-check" };
            envHeader.Add(_recheckBtn);
            root.Add(envHeader);

            _envContainer = new VisualElement();
            _envContainer.style.marginTop = 4;
            root.Add(_envContainer);

            root.Add(Separator());

            // ---------- Build / Capture ----------
            root.Add(SectionLabel("Build & Capture"));

            _lastBuildLabel = new Label();
            _lastBuildLabel.style.whiteSpace = WhiteSpace.Normal;
            _lastBuildLabel.style.marginBottom = 4;
            root.Add(_lastBuildLabel);

            _buildMode = new RadioButtonGroup(string.Empty,
                new List<string> { "Reuse last build", "Rebuild (Development, Metal)" });
            root.Add(_buildMode);

            _warmup = new IntegerField("Warm-up (seconds)") { value = 10 };
            _warmup.tooltip = "Time to let the player render a representative frame before capturing.";
            _warmup.style.marginTop = 4;
            root.Add(_warmup);

            _waitSignal = new Toggle("Wait for signal (short-lived players)") { value = false };
            _waitSignal.tooltip = "Sets MTLCAPTURE_WAIT_FOR_SIGNAL=1 for players that exit too quickly to attach.";
            root.Add(_waitSignal);

            _captureBtn = new Button(OnCaptureClicked) { text = "Capture frame" };
            _captureBtn.style.marginTop = 6;
            _captureBtn.style.height = 26;
            root.Add(_captureBtn);

            _statusLabel = new Label(string.Empty);
            _statusLabel.style.marginTop = 4;
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(_statusLabel);

            root.Add(Separator());

            // ---------- Results ----------
            root.Add(SectionLabel("Results (minimal v1)"));

            _traceField = new TextField(".gputrace path");
            _traceField.style.marginTop = 4;
            root.Add(_traceField);

            _inspectBtn = new Button(OnInspectClicked) { text = "Inspect trace" };
            _inspectBtn.style.marginTop = 4;
            _inspectBtn.style.height = 24;
            root.Add(_inspectBtn);

            _resultsContainer = new VisualElement();
            _resultsContainer.style.marginTop = 6;
            root.Add(_resultsContainer);

            root.Add(Separator());

            // ---------- Log ----------
            Foldout logFold = new Foldout { text = "Log", value = false };
            _logScroll = new ScrollView(ScrollViewMode.Vertical);
            _logScroll.style.maxHeight = 150;
            _logScroll.style.minHeight = 50;
            _logLabel = new Label(string.Empty);
            _logLabel.style.whiteSpace = WhiteSpace.Normal;
            _logLabel.selection.isSelectable = true;
            _logScroll.Add(_logLabel);
            logFold.Add(_logScroll);
            root.Add(logFold);

            RefreshEnvironment();
            RefreshBuildSection();
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
            Label icon = new Label(check.pass ? "\u2705" : "\u274C");
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
                MetalCaptureResult cap = await MetalCaptureService.CaptureAsync(appPath, warm, _waitSignal.value, AppendLog);

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
            _statusLabel.text = "Inspecting trace...";
            MetalTraceSummary sum = await MetalTraceInspector.InspectAsync(tracePath, AppendLog);
            ShowResults(sum);
            _statusLabel.text = sum.success ? "Inspection complete." : ("Inspection failed: " + sum.error);
        }

        void ShowResults(MetalTraceSummary s)
        {
            _resultsContainer.Clear();
            if (s == null) return;
            if (!s.success)
            {
                _resultsContainer.Add(new Label("Error: " + s.error));
                return;
            }

            _resultsContainer.Add(KV("Device", s.device));
            _resultsContainer.Add(KV("Command buffers / encoders / draws",
                s.commandBufferCount + " / " + s.encoderCount + " / " + s.drawCallCount));

            _resultsContainer.Add(KV("CPU frame time",
                s.cpuFrameTimeAvailable ? s.cpuFrameMs.ToString("F2") + " ms" : "unavailable"));
            _resultsContainer.Add(KV("GPU frame time",
                s.gpuFrameTimeAvailable ? s.gpuFrameMs.ToString("F2") + " ms" : "unavailable"));

            if (s.topPass != null)
            {
                Label tp = new Label("Top GPU pass (by " + s.topPassMetric + "):");
                tp.style.unityFontStyleAndWeight = FontStyle.Bold;
                tp.style.marginTop = 6;
                _resultsContainer.Add(tp);
                Label tpv = new Label("    " + s.topPass.label + "  -  " + s.topPass.drawCount + " draws");
                tpv.style.whiteSpace = WhiteSpace.Normal;
                _resultsContainer.Add(tpv);
            }

            if (!string.IsNullOrEmpty(s.timingNote))
            {
                Label note = new Label("Note: " + s.timingNote);
                note.style.whiteSpace = WhiteSpace.Normal;
                note.style.marginTop = 6;
                note.style.fontSize = 11;
                note.style.color = new Color(0.7f, 0.7f, 0.7f);
                _resultsContainer.Add(note);
            }
            if (s.passesCapped)
            {
                Label cap = new Label("(Pass list capped at " + s.totalMatches + " by gpudebug 'find'; " +
                                      "encoder total = " + s.encoderCount + ".)");
                cap.style.whiteSpace = WhiteSpace.Normal;
                cap.style.fontSize = 11;
                cap.style.color = new Color(0.7f, 0.7f, 0.7f);
                _resultsContainer.Add(cap);
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

        void SetBusy(bool busy)
        {
            _busy = busy;
            if (_captureBtn != null) { _captureBtn.SetEnabled(!busy); _captureBtn.text = busy ? "Working..." : "Capture frame"; }
            if (_inspectBtn != null) _inspectBtn.SetEnabled(!busy);
            if (_recheckBtn != null) _recheckBtn.SetEnabled(!busy);
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
