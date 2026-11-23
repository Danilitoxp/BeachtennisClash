using System;
using System.Text;
using BeachTennis.Core;
using BeachTennis.Game.Config;
using BeachTennis.Presentation;
using BeachTennis.Presentation.Ball;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BeachTennis.Game.Sandbox
{
    /// <summary>
    /// Phase 1 test bench: a ball machine on the far side fires every shot type towards the camera
    /// side, with the predicted trajectory and landing marks visible, so ball physics can be judged
    /// by eye on a device and tuned live (edit the BallPhysics asset during Play Mode).
    ///
    /// Controls: tap the court / Space = fire next shot now, tap the info panel / C (or two fingers) =
    /// switch broadcast/side camera, 1–6 = choose shot type, A = toggle auto-fire, T = toggle trajectory.
    /// </summary>
    public sealed class BallMachineSandbox : MonoBehaviour
    {
        [Serializable]
        public struct ShotPreset
        {
            public string Name;
            public Vector3 From;
            public Vector2 TargetXRange;
            public Vector2 TargetZRange;
            public ShotArc Arc;
            public float ApexHeight;
            public float Speed;
            public float TopspinRps;
            public float SidespinRps;
        }

        [SerializeField] private BallPhysicsSettings physics;
        [SerializeField] private BallView ballView;
        [SerializeField] private TrajectoryView trajectory;
        [SerializeField] private ImpactMarkers markers;
        [SerializeField] private bool autoFire = true;
        [SerializeField] private float autoFireInterval = 3f;
        [SerializeField] private bool showTrajectory = true;
        [SerializeField] private uint seed = 12345;

        // Machine on the north side (z > 0) firing towards the south side, where the camera is.
        [SerializeField] private ShotPreset[] presets =
        {
            new ShotPreset { Name = "Saque", From = new Vector3(1.5f, 2.6f, 8.3f), TargetXRange = new Vector2(-3f, 3f), TargetZRange = new Vector2(-7f, -4f), Arc = ShotArc.Drive, Speed = 17f, TopspinRps = 25f, SidespinRps = 5f },
            new ShotPreset { Name = "Lob", From = new Vector3(0f, 1.2f, 6f), TargetXRange = new Vector2(-3f, 3f), TargetZRange = new Vector2(-7.6f, -6.5f), Arc = ShotArc.Apex, ApexHeight = 6.5f, TopspinRps = 5f },
            new ShotPreset { Name = "Voleio", From = new Vector3(0.5f, 1.3f, 3f), TargetXRange = new Vector2(-3.5f, 3.5f), TargetZRange = new Vector2(-6f, -3f), Arc = ShotArc.Drive, Speed = 14f, TopspinRps = 10f },
            new ShotPreset { Name = "Smash", From = new Vector3(0f, 2.8f, 2f), TargetXRange = new Vector2(-3.5f, 3.5f), TargetZRange = new Vector2(-6f, -3f), Arc = ShotArc.Drive, Speed = 25f, TopspinRps = 10f },
            new ShotPreset { Name = "Curtinha (drop)", From = new Vector3(0f, 1.0f, 3f), TargetXRange = new Vector2(-2.5f, 2.5f), TargetZRange = new Vector2(-2.5f, -1f), Arc = ShotArc.Apex, ApexHeight = 2.4f, TopspinRps = -10f },
            new ShotPreset { Name = "Efeito lateral", From = new Vector3(-2f, 1.5f, 4f), TargetXRange = new Vector2(-1f, 3.5f), TargetZRange = new Vector2(-6f, -4f), Arc = ShotArc.Drive, Speed = 14f, SidespinRps = 25f },
        };

        private BallSystem _ball;
        private SimulationRunner _runner;
        private DeterministicRandom _rng;
        private CourtDimensions _court;
        private int _presetIndex;
        private float _timer;

        private Text _label;
        private RectTransform _panel;
        private readonly StringBuilder _sb = new StringBuilder(256);
        private string _lastShotText = "";
        private string _lastLandingText = "";
        private bool _netCordThisShot;

        private bool _sideView;
        private bool _cameraCaptured;
        private Vector3 _broadcastPos;
        private Quaternion _broadcastRot;
        private bool _wasTwoFingerDown;
        private bool _multiTouchSinceLastTap;

        private void Start()
        {
            _runner = AppRoot.Instance.Simulation;
            _court = CourtDimensions.For(CourtFormat.Doubles);
            _ball = new BallSystem(physics != null ? physics.Config : BallPhysicsConfig.Default, _court);
            _runner.Scheduler.Register(_ball);
            _rng = new DeterministicRandom(seed);

            ballView.Bind(_ball, _runner.Clock);
            BuildLabel();
            _timer = 1f; // first shot shortly after start
            UpdateLabel();
        }

        private void OnDestroy()
        {
            if (_runner != null && _ball != null) _runner.Scheduler.Unregister(_ball);
        }

        private void Update()
        {
            if (physics != null) _ball.Config = physics.Config; // live tuning

            // Events first: they belong to the ball as it was during this frame's ticks. Launching before
            // reading them would attribute the previous ball's bounces to the new shot.
            ConsumeEvents();
            HandleInput();

            if (autoFire)
            {
                _timer -= Time.deltaTime;
                if (_timer <= 0f)
                {
                    Fire();
                    _presetIndex = (_presetIndex + 1) % presets.Length;
                }
            }
        }

        private void HandleInput()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) FireAndAdvance();
                if (kb.aKey.wasPressedThisFrame) { autoFire = !autoFire; UpdateLabel(); }
                if (kb.tKey.wasPressedThisFrame) { showTrajectory = !showTrajectory; if (!showTrajectory) trajectory.Hide(); }
                for (int i = 0; i < presets.Length && i < 9; i++)
                {
                    if (kb[Key.Digit1 + i].wasPressedThisFrame)
                    {
                        _presetIndex = i;
                        Fire();
                    }
                }
            }

            if (kb != null && kb.cKey.wasPressedThisFrame) ToggleCamera();

            var touch = Touchscreen.current;
            if (touch != null)
            {
                int pressed = CountPressedTouches(touch);
                bool twoDown = pressed == 2;
                if (twoDown && !_wasTwoFingerDown) ToggleCamera();
                _wasTwoFingerDown = twoDown;

                // Fire when a single-finger tap is released, so a two-finger gesture never also fires.
                if (pressed > 1) _multiTouchSinceLastTap = true;
                bool released = touch.primaryTouch.press.wasReleasedThisFrame;
                bool tap = released && !_multiTouchSinceLastTap;
                if (pressed == 0) _multiTouchSinceLastTap = false; // gesture over: reset
                if (tap)
                {
                    // Tapping the info panel switches camera (works with a mouse in BlueStacks too).
                    Vector2 pos = touch.primaryTouch.position.ReadValue();
                    if (_panel != null && RectTransformUtility.RectangleContainsScreenPoint(_panel, pos, null))
                        ToggleCamera();
                    else
                        FireAndAdvance();
                    return;
                }
            }

#if UNITY_EDITOR || UNITY_STANDALONE
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                if (_panel != null && RectTransformUtility.RectangleContainsScreenPoint(_panel, mouse.position.ReadValue(), null))
                    ToggleCamera();
                else
                    FireAndAdvance();
            }
#endif
        }

        private static int CountPressedTouches(Touchscreen ts)
        {
            int n = 0;
            var touches = ts.touches;
            for (int i = 0; i < touches.Count; i++)
                if (touches[i].press.isPressed) n++;
            return n;
        }

        private void ToggleCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            if (!_cameraCaptured)
            {
                _broadcastPos = cam.transform.position;
                _broadcastRot = cam.transform.rotation;
                _cameraCaptured = true;
            }

            _sideView = !_sideView;
            if (_sideView)
            {
                // Side-on view of the whole court: arcs, net clearance and depth are readable.
                cam.transform.position = new Vector3(-15f, 4f, 0f);
                cam.transform.LookAt(new Vector3(0f, 1.8f, 0f));
            }
            else
            {
                cam.transform.SetPositionAndRotation(_broadcastPos, _broadcastRot);
            }
            UpdateLabel();
        }

        private void FireAndAdvance()
        {
            Fire();
            _presetIndex = (_presetIndex + 1) % presets.Length;
        }

        private void Fire()
        {
            _timer = autoFireInterval;
            ref readonly ShotPreset p = ref presets[_presetIndex];

            var request = new ShotRequest
            {
                From = p.From.ToSim(),
                Target = new Vec3(_rng.Range(p.TargetXRange.x, p.TargetXRange.y), 0f, _rng.Range(p.TargetZRange.x, p.TargetZRange.y)),
                Arc = p.Arc,
                ApexHeight = p.ApexHeight,
                Speed = p.Speed,
                TopspinRps = p.TopspinRps,
                SidespinRps = p.SidespinRps,
                EnforceNetClearance = true,
                MinNetClearance = 0.10f
            };

            float t0 = Time.realtimeSinceStartup;
            ShotSolution s = ShotSolver.Solve(request, _ball.Config, _court, _runner.Clock.TickDeltaF);
            float solveMs = (Time.realtimeSinceStartup - t0) * 1000f;

            _ball.Launch(request.From, s.Velocity, s.Spin);
            if (showTrajectory) trajectory.Show(_ball.State, _ball.Config, _court, _runner.Clock.TickDeltaF);

            float speedKmh = s.Velocity.Magnitude * 3.6f;
            _sb.Clear();
            _sb.Append(p.Name).Append(": ").Append(speedKmh.ToString("0")).Append(" km/h, voo ")
               .Append(s.FlightTime.ToString("0.00")).Append(" s, rede ");
            if (float.IsInfinity(s.NetClearance)) _sb.Append("—");
            else _sb.Append(s.NetClearance >= 0 ? "+" : "").Append((s.NetClearance * 100f).ToString("0")).Append(" cm");
            if (s.ArcRaised) _sb.Append(" (arco ajustado)");
            _sb.Append("\nsolver ").Append(s.Iterations).Append(" iter, erro ")
               .Append((s.LandingError * 100f).ToString("0.0")).Append(" cm, ").Append(solveMs.ToString("0.00")).Append(" ms");
            _lastShotText = _sb.ToString();
            _lastLandingText = "";
            _netCordThisShot = false;
            UpdateLabel();
        }

        private void ConsumeEvents()
        {
            var events = _ball.Events;
            for (int i = 0; i < events.Count; i++)
            {
                ref readonly BallEvent e = ref events[i];
                switch (e.Type)
                {
                    case BallEventType.SandContact:
                        markers.Mark(e.Position.ToUnity(), _court);
                        if (_lastLandingText.Length == 0) // first sand contact after the shot (net results win)
                        {
                            bool inside = _court.IsInside(e.Position.x, e.Position.z);
                            _lastLandingText = (_netCordThisShot ? "FITA → " : "") + (inside ? "DENTRO" : "FORA");
                            UpdateLabel();
                        }
                        break;
                    case BallEventType.NetBody:
                        _lastLandingText = "REDE";
                        UpdateLabel();
                        break;
                    case BallEventType.NetCord:
                        // Shown until the landing call; the landing then reads "FITA → DENTRO/FORA".
                        _lastLandingText = "";
                        _netCordThisShot = true;
                        break;
                }
            }
            events.Clear();
        }

        private void UpdateLabel()
        {
            if (_label == null) return;
            _sb.Clear();
            _sb.Append("Máquina de bolas — próximo: ").Append(presets[_presetIndex].Name)
               .Append(autoFire ? "  [auto]" : "  [manual]").Append('\n');
            _sb.Append(_lastShotText);
            if (_lastLandingText.Length > 0) _sb.Append("\n→ ").Append(_lastLandingText);
            _sb.Append("\nToque na quadra: lançar\nToque aqui: câmera ").Append(_sideView ? "(lateral)" : "(fundo)");
            _label.text = _sb.ToString();
        }

        private void BuildLabel()
        {
            var canvasGo = new GameObject("[SandboxLabel]");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UiBuilder.ConfigureScaler(scaler);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
            var rt = panel.GetComponent<RectTransform>();
            rt.SetParent(canvasGo.transform, false);
            _panel = rt;
            Rect sa = Screen.safeArea;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-12f - (Screen.width - sa.xMax), -12f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
            panel.GetComponent<Image>().raycastTarget = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(14, 14, 10, 10);

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(rt, false);
            _label = textGo.GetComponent<Text>();
            _label.fontSize = 28;
            _label.font = UiFonts.Get(_label.fontSize);
            _label.color = Color.white;
            _label.raycastTarget = false;
            _label.horizontalOverflow = HorizontalWrapMode.Overflow;
            _label.verticalOverflow = VerticalWrapMode.Overflow;
            UiFonts.PrewarmBuffers(_label);
        }

        private void OnDrawGizmos()
        {
            if (presets == null) return;
            Gizmos.color = Color.yellow;
            foreach (var p in presets) Gizmos.DrawWireSphere(p.From, 0.15f);
        }
    }
}
