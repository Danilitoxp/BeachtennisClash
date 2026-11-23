using System.Text;
using BeachTennis.Core;
using BeachTennis.Game.Config;
using BeachTennis.Input;
using BeachTennis.Presentation;
using BeachTennis.Presentation.Ball;
using BeachTennis.Presentation.Cameras;
using BeachTennis.Presentation.Environment;
using BeachTennis.Presentation.Players;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using UnityEngine;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace BeachTennis.Game.Training
{
    /// <summary>
    /// Phase 2 training: the player (south) returns balls from a machine (north), or practices serves.
    /// Wires touch input → <see cref="PlayerCommand"/> with input-latency compensation, judges each rally
    /// and shows timing feedback.
    ///
    /// Runs before the simulation each frame (order -2000) so this frame's input reaches this frame's ticks;
    /// consumes simulation events in LateUpdate, after the ticks.
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    public sealed class TrainingMode : MonoBehaviour
    {
        private enum Mode { Receive, Serve }
        private enum Hitter { None, Machine, Player }

        private struct MachineShot
        {
            public string Name;
            public ShotArc Arc;
            public float ApexOrSpeed;
            public float FromHeight;
            public float TopspinRps;
            public Vector2 DepthRange; // distance from the net on the player's side
        }

        [SerializeField] private BallPhysicsSettings physics;
        [SerializeField] private PlayerSettings playerSettings;
        [SerializeField] private BallView ballView;
        [SerializeField] private TrajectoryView trajectory;
        [SerializeField] private ImpactMarkers markers;
        [SerializeField] private PlayerView playerView;
        [SerializeField] private TouchControls controls;
        [SerializeField] private float delayBetweenRallies = 1.3f;
        [SerializeField] private uint seed = 2024;
        [SerializeField] private string menuScene = "Match";

        private static readonly MachineShot[] MachineShots =
        {
            new MachineShot { Name = "Média", Arc = ShotArc.Apex, ApexOrSpeed = 3.6f, FromHeight = 1.3f, TopspinRps = 6f, DepthRange = new Vector2(3.5f, 6.5f) },
            new MachineShot { Name = "Rápida", Arc = ShotArc.Drive, ApexOrSpeed = 15f, FromHeight = 1.9f, TopspinRps = 10f, DepthRange = new Vector2(3.5f, 6.5f) },
            new MachineShot { Name = "Lob", Arc = ShotArc.Apex, ApexOrSpeed = 6.5f, FromHeight = 1.2f, TopspinRps = 4f, DepthRange = new Vector2(5.5f, 7.5f) },
            new MachineShot { Name = "Curtinha", Arc = ShotArc.Apex, ApexOrSpeed = 2.4f, FromHeight = 1.0f, TopspinRps = -10f, DepthRange = new Vector2(1.0f, 2.6f) },
            new MachineShot { Name = "Cruzada", Arc = ShotArc.Drive, ApexOrSpeed = 13f, FromHeight = 1.5f, TopspinRps = 8f, DepthRange = new Vector2(4f, 7f) },
        };

        private SimulationRunner _runner;
        private BallSystem _ball;
        private PlayerSystem _player;
        private CourtDimensions _court;
        private DeterministicRandom _rng;

        private Mode _mode = Mode.Receive;
        private Hitter _lastHitter = Hitter.None;
        private bool _rallyLive;
        private float _nextRallyTimer;
        private int _streak;
        private int _best;
        private int _hits;
        private int _perfects;

        private int _seenControlSerial;
        private double _lastReleaseRealtime;
        private float _latencyAvgMs;
        private int _latencySamples;
        private float _lastTimingMs = float.NaN;

        private Text _panelText;
        private RectTransform _panel;
        private Text _feedbackText;
        private float _feedbackTimer;
        private readonly StringBuilder _sb = new StringBuilder(256);

        private void Start()
        {
            _runner = AppRoot.Instance.Simulation;
            _court = CourtDimensions.For(CourtFormat.Doubles);
            _rng = new DeterministicRandom(seed);
            _showTrajectory = GameSettings.TrajectoryAid;

            _ball = new BallSystem(physics.Config, _court);
            _player = new PlayerSystem(_ball, playerSettings.Config, _court, -1, new Vec3(0f, 0f, -5.5f), seed ^ 0xABCDu);
            _runner.Scheduler.Register(_ball);   // ball first: players read the ball after it moves
            _runner.Scheduler.Register(_player);

            ballView.Bind(_ball, _runner.Clock);
            _camera = new GameObject("CameraDirector").AddComponent<CameraDirector>();
            new GameObject("Environment Director").AddComponent<EnvironmentDirector>().Apply((TimeOfDay)GameSettings.TimeOfDay);
            _sandFx = new GameObject("SandFx").AddComponent<SandFx>();
            _camera.Bind(_player, _ball, _court, _runner.Clock);
            playerView.Bind(_player, _runner.Clock, _ball);
            BuildUi();
            _nextRallyTimer = 1f;
            UpdatePanel();
        }

        private void OnDestroy()
        {
            if (_runner == null) return;
            _runner.Scheduler.Unregister(_player);
            _runner.Scheduler.Unregister(_ball);
        }

        // ---------------------------------------------------------------- input → command (before ticks)

        private void Update()
        {
            _ball.Config = physics.Config;
            _player.Config = playerSettings.Config;

            HandlePanelTap();

            ref PlayerCommand cmd = ref _player.Command;
            Vector2 move = controls.Move;
            // Camera looks north from behind the south baseline: stick up = +Z, right = +X.
            cmd.MoveX = move.x;
            cmd.MoveZ = move.y;
            cmd.AimX = move.x;
            cmd.AimY = move.y;
            cmd.ShotHeld = controls.ShotHeld;
            cmd.HeldType = controls.HeldType;

            if (controls.ReleaseSerial != _seenControlSerial)
            {
                _seenControlSerial = controls.ReleaseSerial;
                cmd.ReleaseSerial++;
                cmd.ReleaseType = controls.ReleaseType;
                cmd.ReleaseTime = ReleaseToSimTime(controls.ReleaseRealtime);
                _lastReleaseRealtime = controls.ReleaseRealtime;
            }

            if (!_rallyLive)
            {
                _nextRallyTimer -= Time.deltaTime;
                if (_nextRallyTimer <= 0f) StartRally();
            }

            if (_feedbackTimer > 0f)
            {
                _feedbackTimer -= Time.deltaTime;
                // CanvasRenderer alpha: fades without regenerating the text mesh.
                _feedbackText.canvasRenderer.SetAlpha(Mathf.Clamp01(_feedbackTimer / 0.4f));
            }
        }

        /// <summary>
        /// Maps a real-time input timestamp onto the simulation timeline. The simulation's "present" after
        /// this frame's advance corresponds to now; the touch happened (now - eventTime) earlier.
        /// </summary>
        private double ReleaseToSimTime(double eventRealtime)
        {
            SimulationClock clock = _runner.Clock;
            double simAfterThisFrame = (clock.TickCount + clock.Alpha) * clock.TickDelta + Time.deltaTime;
            double age = Time.realtimeSinceStartupAsDouble - eventRealtime;
            if (age < 0) age = 0;
            if (age > 0.25) age = 0.25; // never trust a stale timestamp more than a quarter second
            return simAfterThisFrame - age;
        }

        private void HandlePanelTap()
        {
            foreach (Touch t in Touch.activeTouches)
            {
                if (t.phase != TouchPhase.Began) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(_panel, t.screenPosition, null))
                    ToggleMode();
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            // Android back button arrives as Escape.
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
                UnityEngine.SceneManagement.SceneManager.LoadScene(menuScene);
            if (kb != null && kb.mKey.wasPressedThisFrame) ToggleMode();
            if (kb != null && kb.tKey.wasPressedThisFrame) _showTrajectory = !_showTrajectory;
        }

        private bool _showTrajectory = true; // set from GameSettings in Start (PlayerPrefs is not allowed in field initializers)

        private void ToggleMode()
        {
            _mode = _mode == Mode.Receive ? Mode.Serve : Mode.Receive;
            controls.SetServeMode(_mode == Mode.Serve);
            _player.EndServe();
            _ball.Deactivate();
            _rallyLive = false;
            _nextRallyTimer = 0.5f;
            _streak = 0;
            UpdatePanel();
        }

        // ---------------------------------------------------------------- rallies

        private void StartRally()
        {
            _rallyLive = true;
            trajectory.Hide();

            if (_mode == Mode.Serve)
            {
                _lastHitter = Hitter.None;
                _player.BeginServe(1.5f);
                return;
            }

            MachineShot shot = MachineShots[_rng.Range(0, MachineShots.Length)];
            var from = new Vec3(_rng.Range(-2.5f, 2.5f), shot.FromHeight, _rng.Range(3f, 6.5f));
            var target = new Vec3(_rng.Range(-3.2f, 3.2f), 0f, -_rng.Range(shot.DepthRange.x, shot.DepthRange.y));
            var request = new ShotRequest
            {
                From = from, Target = target, Arc = shot.Arc,
                ApexHeight = shot.ApexOrSpeed, Speed = shot.ApexOrSpeed, TopspinRps = shot.TopspinRps,
                EnforceNetClearance = true, MinNetClearance = 0.12f
            };
            ShotSolution s = ShotSolver.Solve(request, _ball.Config, _court, _runner.Clock.TickDeltaF);
            _ball.Launch(from, s.Velocity, s.Spin);
            _lastHitter = Hitter.Machine;
            if (_showTrajectory) trajectory.Show(_ball.State, _ball.Config, _court, _runner.Clock.TickDeltaF);
        }

        private void EndRally(string message, bool success, bool neutral = false)
        {
            if (!_rallyLive) return;
            _rallyLive = false;
            _nextRallyTimer = delayBetweenRallies;
            if (neutral)
            {
                // Not the player's doing: streak unchanged.
            }
            else if (success)
            {
                _streak++;
                if (_streak > _best) _best = _streak;
            }
            else
            {
                _streak = 0;
            }
            ShowFeedback(message, success ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.45f, 0.35f));
            UpdatePanel();
        }

        // ---------------------------------------------------------------- events (after ticks)

        private CameraDirector _camera;
        private SandFx _sandFx;

        private void LateUpdate()
        {
            _camera.Show(_player.IsServing ? CameraShot.Serve : CameraShot.Gameplay);
            var pe = _player.Events;
            for (int i = 0; i < pe.Count; i++)
            {
                ref readonly PlayerEvent e = ref pe[i];
                switch (e.Type)
                {
                    case PlayerEventType.SwingStarted:
                        float ms = (float)((Time.realtimeSinceStartupAsDouble - _lastReleaseRealtime) * 1000.0);
                        _latencyAvgMs = (_latencyAvgMs * _latencySamples + ms) / (_latencySamples + 1);
                        _latencySamples = Mathf.Min(_latencySamples + 1, 30);
                        playerView.PlaySwing(IsBackhand());
                        break;
                    case PlayerEventType.Hit:
                        playerView.PlayContact(e);
                        _lastHitter = Hitter.Player;
                        _hits++;
                        if (e.Grade == TimingGrade.Perfect) _perfects++;
                        _lastTimingMs = e.TimingError * 1000f;
                        ShowFeedback(HitText(e), GradeColor(e.Grade));
                        if (_showTrajectory) trajectory.Show(_ball.State, _ball.Config, _court, _runner.Clock.TickDeltaF);
                        UpdatePanel();
                        break;
                    case PlayerEventType.Whiff:
                        ShowFeedback("CEDO DEMAIS", new Color(1f, 0.6f, 0.3f));
                        break;
                    case PlayerEventType.NoSwing:
                        ShowFeedback("SEM GOLPE", new Color(1f, 0.6f, 0.3f));
                        break;
                    case PlayerEventType.OutOfReach:
                        ShowFeedback("LONGE DEMAIS", new Color(1f, 0.6f, 0.3f));
                        break;
                }
            }
            pe.Clear();

            var be = _ball.Events;
            for (int i = 0; i < be.Count; i++)
            {
                ref readonly BallEvent e = ref be[i];
                if (e.Type == BallEventType.SandContact)
                {
                    markers.Mark(e.Position.ToUnity(), _court);
                    _sandFx.Burst(e.Position.ToUnity(), e.Velocity.Magnitude / 20f);
                }
                if (!_rallyLive) continue;

                switch (e.Type)
                {
                    case BallEventType.SandContact:
                        bool inside = _court.IsInside(e.Position.x, e.Position.z);
                        if (_lastHitter == Hitter.Player)
                        {
                            if (e.Position.z > 0f && inside) EndRally(_mode == Mode.Serve ? "SAQUE DENTRO!" : "PONTO!", true);
                            else EndRally(_mode == Mode.Serve ? "SAQUE FORA" : "FORA", false);
                        }
                        else if (_lastHitter == Hitter.Machine)
                        {
                            // Beach tennis: the ball may not bounce on your side.
                            if (e.Position.z < 0f && inside) EndRally("QUICOU NA SUA QUADRA", false);
                            else EndRally("MÁQUINA ERROU", true, neutral: true);
                        }
                        break;
                    case BallEventType.NetBody:
                        if (_lastHitter == Hitter.Player) EndRally("REDE", false);
                        break;
                }
            }
            be.Clear();
        }

        private bool IsBackhand()
        {
            ref readonly PlayerState s = ref _player.State;
            if (!s.Plan.Valid) return false;
            // South player faces +Z: forehand side is +X.
            return (s.Plan.BallPosition.x - s.Position.x) * -s.Side < 0f;
        }

        private string HitText(in PlayerEvent e)
        {
            string grade = e.Grade switch
            {
                TimingGrade.Perfect => "PERFEITO!",
                TimingGrade.Good => "BOM",
                TimingGrade.Early => "CEDO",
                _ => "TARDE"
            };
            if (e.Smash) grade += "  SMASH";
            if (e.Dive) grade += "  (mergulho)";
            return grade;
        }

        private static Color GradeColor(TimingGrade g) => g switch
        {
            TimingGrade.Perfect => new Color(0.4f, 1f, 0.45f),
            TimingGrade.Good => new Color(0.85f, 1f, 0.5f),
            _ => new Color(1f, 0.75f, 0.35f)
        };

        // ---------------------------------------------------------------- UI

        private void ShowFeedback(string text, Color color)
        {
            _feedbackText.text = text;
            color.a = 1f;
            _feedbackText.color = color;
            _feedbackText.canvasRenderer.SetAlpha(1f);
            _feedbackTimer = 1.2f;
        }

        private void UpdatePanel()
        {
            if (_panelText == null) return;
            _sb.Clear();
            _sb.Append("Treino: ").Append(_mode == Mode.Receive ? "RECEBER" : "SACAR").Append("  (toque aqui: trocar)\n");
            _sb.Append("Sequência ").Append(_streak).Append("   Melhor ").Append(_best).Append('\n');
            _sb.Append("Perfeitos ").Append(_perfects).Append('/').Append(_hits);
            if (!float.IsNaN(_lastTimingMs))
                _sb.Append("   último ").Append(_lastTimingMs >= 0 ? "+" : "").Append(_lastTimingMs.ToString("0")).Append(" ms");
            _sb.Append('\n');
            _sb.Append("Toque→golpe ").Append(_latencySamples > 0 ? _latencyAvgMs.ToString("0") : "-").Append(" ms (média)");
            _panelText.text = _sb.ToString();
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("[TrainingUI]");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            UiBuilder.ConfigureScaler(scaler);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
            _panel = panel.GetComponent<RectTransform>();
            _panel.SetParent(canvasGo.transform, false);
            Rect sa = Screen.safeArea;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(1f, 1f);
            _panel.anchoredPosition = new Vector2(-12f - (Screen.width - sa.xMax), -12f);
            var bg = panel.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.65f);
            bg.raycastTarget = false;
            var fitter = panel.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            panel.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(14, 14, 10, 10);
            _panelText = MakeText(_panel, 28, TextAnchor.UpperLeft);
            UiFonts.PrewarmBuffers(_panelText);

            var fbGo = new GameObject("Feedback", typeof(RectTransform));
            var fbRt = fbGo.GetComponent<RectTransform>();
            fbRt.SetParent(canvasGo.transform, false);
            fbRt.anchorMin = fbRt.anchorMax = new Vector2(0.5f, 0.72f);
            fbRt.sizeDelta = new Vector2(1200f, 140f);
            _feedbackText = MakeText(fbRt, 72, TextAnchor.MiddleCenter);
            var outline = _feedbackText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(3f, -3f);
            UiFonts.PrewarmBuffers(_feedbackText, 60);
            _feedbackText.canvasRenderer.SetAlpha(0f);
        }

        private static Text MakeText(RectTransform parent, int size, TextAnchor anchor)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = go.GetComponent<Text>();
            t.fontSize = size;
            t.font = UiFonts.Get(size);
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}


