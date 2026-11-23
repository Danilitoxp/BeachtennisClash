using System.Text;
using BeachTennis.Core;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BeachTennis.Presentation.Diagnostics
{
    /// <summary>
    /// On-device performance overlay (Editor and development builds only).
    /// Shows frame time (avg / worst), CPU main-thread work, GPU, GC allocation, memory, rendering
    /// counters and simulation tick rate, so every phase can be checked against docs/01 §4.1.
    ///
    /// Toggle: F1 on keyboard, or tap with three fingers.
    ///
    /// Allocation-aware: counters are sampled every frame without allocating; the text is rebuilt only
    /// twice per second (uGUI, no per-frame IMGUI cost), and the GC reading of that refresh frame is
    /// excluded so the overlay does not report its own allocations.
    /// </summary>
    public sealed class PerformanceHud : MonoBehaviour
    {
        private const float RefreshInterval = 0.5f;

        [SerializeField] private bool visible = true;

        private ISimulationStats _sim;

        private ProfilerRecorder _gcAlloc;
        private ProfilerRecorder _systemMemory;
        private ProfilerRecorder _setPass;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _triangles;

        private readonly FrameTiming[] _timings = new FrameTiming[1];
        private readonly StringBuilder _sb = new StringBuilder(512);

        // Window accumulators (reset on each refresh).
        private int _frames;
        private float _frameTimeSum;
        private float _frameTimeMax;
        private double _cpuSum;
        private int _cpuSamples;
        private double _gpuSum;
        private int _gpuSamples;
        private long _gcMax;
        private long _gcFramesWithAlloc;
        private int _gcSamples;
        private long _lastTickCount;
        private float _windowTime;
        private int _skipGcSamples;

        private GameObject _canvasGo;
        private Text _text;
        private bool _wasThreeFingerDown;

        public void Bind(ISimulationStats sim) => _sim = sim;

        private void Awake()
        {
            BuildOverlay();
        }

        private void OnEnable()
        {
            _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            _systemMemory = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory", 1);
            _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1);
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1);
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1);
            _triangles = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1);
            if (_canvasGo != null) _canvasGo.SetActive(visible);
        }

        private void OnDisable()
        {
            _gcAlloc.Dispose();
            _systemMemory.Dispose();
            _setPass.Dispose();
            _drawCalls.Dispose();
            _batches.Dispose();
            _triangles.Dispose();
            if (_canvasGo != null) _canvasGo.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_canvasGo != null) Destroy(_canvasGo);
        }

        private void Update()
        {
            HandleToggle();
            Sample();

            _windowTime += Time.unscaledDeltaTime;
            if (_windowTime >= RefreshInterval)
            {
                Refresh();
                ResetWindow();
                // The recorder reports the previous frame, and uGUI rebuilds the text mesh on the
                // following frame: skip two samples so the overlay's own work is not counted.
                _skipGcSamples = 2;
            }

            _logTime += Time.unscaledDeltaTime;
            if (_logTime >= LogInterval)
            {
                LogSummary();
                _skipGcSamples = 2;
            }
        }

        // ---- Periodic log line (logcat / Player.log), for soak tests on devices without watching the screen.
        private const float LogInterval = 5f;
        private float _logTime;
        private int _logFrames;
        private float _logFrameSum;
        private float _logFrameMax;
        private int _logHitches;
        private int _logGcFrames;
        private long _logGcMax;
        private float _sinceStart;

        private void SampleLog(float frameMs, long gcBytes, bool gcValid)
        {
            _logFrames++;
            _logFrameSum += frameMs;
            if (frameMs > _logFrameMax) _logFrameMax = frameMs;
            float budget = Application.targetFrameRate > 0 ? 1000f / Application.targetFrameRate : 16.7f;
            if (frameMs > budget * 1.5f) _logHitches++;
            if (gcValid && gcBytes > 0) _logGcFrames++;
            if (gcValid && gcBytes > _logGcMax) _logGcMax = gcBytes;
        }

        private void LogSummary()
        {
            _sinceStart += _logTime;
            float avg = _logFrames > 0 ? _logFrameSum / _logFrames : 0f;
            Debug.Log($"[Perf] t={_sinceStart:0}s fps={(avg > 0 ? 1000f / avg : 0f):0.0} frame_avg={avg:0.0}ms " +
                      $"frame_max={_logFrameMax:0.0}ms hitches={_logHitches} gc_frames={_logGcFrames} gc_max={_logGcMax}B " +
                      $"mem={(_systemMemory.Valid ? _systemMemory.LastValue / (1024 * 1024) : 0)}MB");
            _logTime = 0f;
            _logFrames = 0;
            _logFrameSum = 0f;
            _logFrameMax = 0f;
            _logHitches = 0;
            _logGcFrames = 0;
            _logGcMax = 0;
        }

        private void HandleToggle()
        {
            bool toggle = false;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame) toggle = true;

            var touchscreen = Touchscreen.current;
            if (touchscreen != null)
            {
                int pressed = 0;
                var touches = touchscreen.touches;
                for (int i = 0; i < touches.Count; i++)
                    if (touches[i].press.isPressed) pressed++;

                bool threeDown = pressed >= 3;
                if (threeDown && !_wasThreeFingerDown) toggle = true;
                _wasThreeFingerDown = threeDown;
            }

            if (!toggle) return;
            visible = !visible;
            _canvasGo.SetActive(visible);
        }

        private void Sample()
        {
            float dt = Time.unscaledDeltaTime * 1000f;
            _frames++;
            _frameTimeSum += dt;
            if (dt > _frameTimeMax) _frameTimeMax = dt;

            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timings) > 0)
            {
                // Main-thread work only: exclude time spent waiting for Present / frame pacing.
                double cpu = _timings[0].cpuMainThreadFrameTime - _timings[0].cpuMainThreadPresentWaitTime;
                if (cpu > 0)
                {
                    _cpuSum += cpu;
                    _cpuSamples++;
                }
                if (_timings[0].gpuFrameTime > 0)
                {
                    _gpuSum += _timings[0].gpuFrameTime;
                    _gpuSamples++;
                }
            }

            bool gcSampleValid = false;
            long gcBytes = 0;
            if (_skipGcSamples > 0)
            {
                _skipGcSamples--;
            }
            else if (_gcAlloc.Valid)
            {
                gcBytes = _gcAlloc.LastValue;
                gcSampleValid = true;
                _gcSamples++;
                if (gcBytes > 0) _gcFramesWithAlloc++;
                if (gcBytes > _gcMax) _gcMax = gcBytes;
            }
            SampleLog(dt, gcBytes, gcSampleValid);
        }

        private void Refresh()
        {
            if (!visible) return;

            float avg = _frames > 0 ? _frameTimeSum / _frames : 0f;
            float fps = avg > 0 ? 1000f / avg : 0f;

            _sb.Clear();
            _sb.Append("FPS ").Append(fps.ToString("0")).Append("  alvo ").Append(Application.targetFrameRate)
               .Append("  tela ").Append(Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value)).Append("Hz\n");
            _sb.Append("Frame ").Append(avg.ToString("0.0")).Append(" ms  pior ").Append(_frameTimeMax.ToString("0.0")).Append(" ms\n");
            _sb.Append("CPU main ").Append(_cpuSamples > 0 ? (_cpuSum / _cpuSamples).ToString("0.0") : "n/d").Append(" ms  ");
            _sb.Append("GPU ").Append(_gpuSamples > 0 ? (_gpuSum / _gpuSamples).ToString("0.0") : "n/d").Append(" ms\n");
            _sb.Append("GC/frame max ").Append(_gcMax).Append(" B  (").Append(_gcFramesWithAlloc).Append('/').Append(_gcSamples).Append(" frames)\n");
            _sb.Append("Mem sistema ").Append(_systemMemory.Valid ? (_systemMemory.LastValue / (1024 * 1024)).ToString() : "-").Append(" MB\n");
            _sb.Append("SetPass ").Append(_setPass.LastValue).Append("  Draw ").Append(_drawCalls.LastValue)
               .Append("  Batches ").Append(_batches.LastValue).Append('\n');
            _sb.Append("Tris ").Append((_triangles.LastValue / 1000f).ToString("0.0")).Append("k\n");

            if (_sim != null)
            {
                long ticks = _sim.TickCount - _lastTickCount;
                _lastTickCount = _sim.TickCount;
                float tps = _windowTime > 0 ? ticks / _windowTime : 0f;
                _sb.Append("Sim ").Append(tps.ToString("0")).Append('/').Append(_sim.TickRate).Append(" Hz");
                if (_sim.DroppedTime > 0) _sb.Append("  perdido ").Append((_sim.DroppedTime * 1000).ToString("0")).Append(" ms");
                _sb.Append('\n');
            }

            _sb.Append("Qualidade ").Append(QualitySettings.names[QualitySettings.GetQualityLevel()])
               .Append("  ").Append(SystemInfo.graphicsDeviceType);

            _text.text = _sb.ToString();
        }

        private void ResetWindow()
        {
            _frames = 0;
            _frameTimeSum = 0;
            _frameTimeMax = 0;
            _cpuSum = 0;
            _cpuSamples = 0;
            _gpuSum = 0;
            _gpuSamples = 0;
            _gcMax = 0;
            _gcFramesWithAlloc = 0;
            _gcSamples = 0;
            _windowTime = 0;
        }

        private void BuildOverlay()
        {
            _canvasGo = new GameObject("[PerformanceHud]");
            DontDestroyOnLoad(_canvasGo);

            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            canvas.pixelPerfect = false;

            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UiBuilder.ConfigureScaler(scaler);

            // Safe-area container so notches/cutouts never cover the readout.
            var safe = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(_canvasGo.transform, false);
            Rect sa = Screen.safeArea;
            safe.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            safe.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            safe.offsetMin = safe.offsetMax = Vector2.zero;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.SetParent(safe, false);
            panelRt.anchorMin = panelRt.anchorMax = panelRt.pivot = new Vector2(0f, 1f);
            panelRt.anchoredPosition = new Vector2(12f, -150f); // below the scoreboard
            var bg = panel.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.65f);
            bg.raycastTarget = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(panelRt, false);
            _text = textGo.GetComponent<Text>();
            _text.fontSize = 24;
            _text.font = UiFonts.Get(_text.fontSize);
            _text.color = Color.white;
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.text = "...";
            UiFonts.PrewarmBuffers(_text);

            _canvasGo.SetActive(visible);
        }
    }
}
