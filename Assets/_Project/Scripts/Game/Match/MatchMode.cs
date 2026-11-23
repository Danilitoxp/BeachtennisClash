using System.Text;
using BeachTennis.AI;
using BeachTennis.Core;
using BeachTennis.Game.Config;
using BeachTennis.Input;
using BeachTennis.Presentation;
using BeachTennis.Presentation.Ball;
using BeachTennis.Presentation.Cameras;
using BeachTennis.Presentation.Players;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using BeachTennis.Simulation.Rules;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace BeachTennis.Game.Match
{
    /// <summary>
    /// Phase 3: a full match under the ITF rules (singles or doubles) — human (team 0, player 0) with
    /// basic bots for everyone else. Owns the match flow between points: formation for serve and
    /// return (prohibited zone included), change of ends, scoreboard, point calls and the result screen.
    /// The rules themselves live in <see cref="MatchScore"/> and <see cref="PointReferee"/>.
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    public sealed class MatchMode : MonoBehaviour
    {
        private enum Screen { Menu, Playing, Paused, Finished }


        [SerializeField] private BallPhysicsSettings physics;
        [SerializeField] private PlayerSettings playerSettings;
        [SerializeField] private BallView ballView;
        [SerializeField] private TrajectoryView trajectory;
        [SerializeField] private ImpactMarkers markers;
        [Tooltip("Team 0 player 0, team 0 player 1, team 1 player 0, team 1 player 1")]
        [SerializeField] private PlayerView[] playerViews;
        [SerializeField] private CourtView courtView;
        [SerializeField] private TouchControls controls;
        [Tooltip("Optional: created at runtime when left empty.")]
        [SerializeField] private CameraDirector cameraDirector;
        [SerializeField] private float pointPause = 2.2f;
        [SerializeField] private string trainingScene = "Training";

        private const int Human = 0;

        private Screen _screen = Screen.Menu;
        private SimulationRunner _runner;
        private CourtDimensions _court;
        private RuleSet _rules;
        private BallSystem _ball;
        private PlayerSystem[] _players;
        private PlayerId[] _ids;
        private AiPlayer[] _bots;
        private TeamBrain[] _brains;
        private readonly int[] _seenCalls = new int[2];
        private Text _callText;
        private RectTransform _callRect;
        private int _callPlayer = -1;
        private float _callTimer;
        /// <summary>Draw the path of balls coming to the human (readability aid, on by default).</summary>
        private int _trajectoryVersion = -1;
        private MatchScore _score;
        private PointReferee _referee;
        private DeterministicRandom _rng = new DeterministicRandom(777);

        private int _seenDecisions;
        private float _pauseTimer = -1f;
        private int _seenControlSerial;

        private RectTransform _hud;
        private Text _scoreText;
        private Text _bannerText;
        private RectTransform _bannerPanel;
        private Text _feedbackText;
        private float _feedbackTimer;
        private MainMenuView _menuView;
        private RectTransform _result;
        private RectTransform _pauseButton;
        private RectTransform _pauseMenu;
        private RectTransform _resumeButton;
        private RectTransform _quitButton;
        private Text _resultText;
        private readonly StringBuilder _sb = new StringBuilder(256);

        private void Start()
        {
            _runner = AppRoot.Instance.Simulation;
            if (cameraDirector == null) cameraDirector = new GameObject("CameraDirector").AddComponent<CameraDirector>();
            BuildUi();
            _menuView = gameObject.AddComponent<MainMenuView>();
            _menuView.PlayRequested += StartMatch;
            _menuView.TrainingRequested += () => SceneManager.LoadScene(trainingScene);
            foreach (var v in playerViews) v.gameObject.SetActive(false);
            ShowMenu();
        }

        private void OnDestroy() => TearDown();

        // Menus read touches while the touch controls are hidden, so keep EnhancedTouch on here too
        // (Enable/Disable are reference counted).
        private void OnEnable()
        {
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Enable();
#if UNITY_EDITOR
            UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Enable();
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEngine.InputSystem.EnhancedTouch.TouchSimulation.Disable();
#endif
            UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.Disable();
        }

        // ------------------------------------------------------------------ setup

        private void StartMatch(int playersPerTeam, bool official)
        {
            TearDown();

            EventCategory category = GameSettings.Category;
            _rules = official ? RuleSet.ItfOfficial(category) : RuleSet.QuickShortSet(category);
            _court = CourtDimensions.For(playersPerTeam == 1 ? CourtFormat.Singles : CourtFormat.Doubles);
            _court.NetHeight = _rules.NetHeight;
            courtView.Apply(_court);

            _ball = new BallSystem(physics.Config, _court);
            int n = playersPerTeam * 2;
            _players = new PlayerSystem[n];
            _ids = new PlayerId[n];
            _bots = new AiPlayer[n];
            _brains = new TeamBrain[2];
            int k = 0;
            for (int team = 0; team < 2; team++)
            for (int i = 0; i < playersPerTeam; i++, k++)
            {
                _ids[k] = new PlayerId(team, i);
                _players[k] = new PlayerSystem(_ball, playerSettings.Config, _court, team == 0 ? -1 : 1, Vec3.Zero, 1000u + (uint)k);
            }

            // AI for everyone but the human. The partner plays at the chosen level too, so the human's
            // team is never handicapped by a weak partner on hard settings.
            AiProfile profile = AiProfile.For(GameSettings.Difficulty);
            for (int team = 0; team < 2; team++)
            {
                var members = new PlayerSystem[playersPerTeam];
                var opponents = new PlayerSystem[playersPerTeam];
                var isAi = new bool[playersPerTeam];
                for (int i = 0; i < playersPerTeam; i++)
                {
                    members[i] = _players[team * playersPerTeam + i];
                    opponents[i] = _players[(1 - team) * playersPerTeam + i];
                    isAi[i] = team * playersPerTeam + i != Human;
                }
                _brains[team] = playersPerTeam > 1 ? new TeamBrain(_ball, members, isAi) : null;
                for (int i = 0; i < playersPerTeam; i++)
                {
                    int idx = team * playersPerTeam + i;
                    if (idx == Human) continue;
                    _bots[idx] = new AiPlayer(_players[idx], opponents, playersPerTeam > 1 ? members[1 - i] : null,
                        _brains[team], profile, 5000u + (uint)idx)
                    {
                        Role = playersPerTeam > 1 ? (i == 0 ? 1 : -1) : 0
                    };
                }
            }
            _seenCalls[0] = _seenCalls[1] = 0;
            _callTimer = 0f;
            _callRect.gameObject.SetActive(false);

            int firstServer = _rng.Range(0, 2); // R10: decided by toss
            _score = new MatchScore(_rules, playersPerTeam, firstServer, southTeam: 0);
            _referee = new PointReferee(_score, _ball, _players, _ids, _court);

            // Order inside a tick: bots decide → ball moves → players act → referee judges.
            foreach (var b in _bots) if (b != null) _runner.Scheduler.Register(b);
            _runner.Scheduler.Register(_ball);
            foreach (var p in _players) _runner.Scheduler.Register(p);
            _runner.Scheduler.Register(_referee);

            ballView.Bind(_ball, _runner.Clock);
            cameraDirector.Bind(_players[Human], _ball, _court, _runner.Clock);
            for (int v = 0; v < playerViews.Length; v++)
            {
                int simIndex = ViewToSimIndex(v, playersPerTeam);
                bool used = simIndex >= 0;
                playerViews[v].gameObject.SetActive(used);
                if (used)
                {
                    playerViews[v].Bind(_players[simIndex], _runner.Clock, _ball);
                    playerViews[v].ShowGuides = simIndex == Human; // stand/aim rings only for the human
                }
            }

            _seenDecisions = 0;
            _screen = Screen.Playing;
            _menuView.Hide();
            _result.gameObject.SetActive(false);
            _hud.gameObject.SetActive(true);
            controls.gameObject.SetActive(true);
            PreparePoint();
            ShowBanner(firstServer == 0 ? "VOCÊ SACA PRIMEIRO" : "ADVERSÁRIO SACA PRIMEIRO", 1.6f);
        }

        /// <summary>playerViews are laid out [T0P0, T0P1, T1P0, T1P1]; sim arrays are packed by team.</summary>
        private static int ViewToSimIndex(int view, int playersPerTeam)
        {
            int team = view / 2, index = view % 2;
            return index < playersPerTeam ? team * playersPerTeam + index : -1;
        }

        private void TearDown()
        {
            if (_runner == null || _players == null) return;
            _runner.Scheduler.Unregister(_referee);
            foreach (var p in _players) _runner.Scheduler.Unregister(p);
            _runner.Scheduler.Unregister(_ball);
            foreach (var b in _bots) if (b != null) _runner.Scheduler.Unregister(b);
            _players = null;
        }

        /// <summary>Places everybody for the next point: server behind the baseline, others outside the prohibited zone.</summary>
        private void PreparePoint()
        {
            PointSetup.Prepare(_score, _players, _ids, _referee);
            foreach (var v in playerViews) if (v.gameObject.activeSelf) v.ResetAnimation();
            trajectory.Hide();
            PlaceCamera();
            controls.SetServeMode(_ids[Human].Equals(_score.Server));
            UpdateScoreboard();
        }

        /// <summary>Serve shot while the human serves, gameplay otherwise (the director cuts on change of ends).</summary>
        private void PlaceCamera()
        {
            cameraDirector.Show(_players[Human].IsServing ? CameraShot.Serve : CameraShot.Gameplay);
        }

        // ------------------------------------------------------------------ frame (before ticks)

        private void Update()
        {
            switch (_screen)
            {
                case Screen.Menu: return; // the UI Toolkit menu raises events
                case Screen.Finished: if (TappedAnywhere()) ShowMenu(); return;
                case Screen.Paused: HandlePauseInput(); return;
            }

            if (PauseRequested())
            {
                SetPaused(true);
                return;
            }

            _ball.Config = physics.Config;
            for (int k = 0; k < _players.Length; k++) _players[k].Config = playerSettings.Config;

            ApplyHumanInput();

            if (_referee.DecisionCount != _seenDecisions)
            {
                _seenDecisions = _referee.DecisionCount;
                AnnouncePoint();
                _pauseTimer = pointPause;
            }

            if (_pauseTimer > 0f)
            {
                _pauseTimer -= Time.deltaTime;
                if (_pauseTimer <= 0f)
                {
                    if (_score.IsOver) ShowResult();
                    else PreparePoint();
                }
            }

            if (_feedbackTimer > 0f)
            {
                _feedbackTimer -= Time.deltaTime;
                _feedbackText.canvasRenderer.SetAlpha(Mathf.Clamp01(_feedbackTimer / 0.4f));
            }
        }

        private void ApplyHumanInput()
        {
            PlayerSystem me = _players[Human];
            ref PlayerCommand cmd = ref me.Command;
            int side = me.State.Side;
            Vector2 move = controls.Move;
            // Camera sits behind the human's baseline: stick up = towards the net.
            cmd.MoveX = move.x * -side;
            cmd.MoveZ = move.y * -side;
            cmd.AimX = move.x;
            cmd.AimY = move.y;
            cmd.ShotHeld = controls.ShotHeld;
            cmd.HeldType = controls.HeldType;

            if (controls.ReleaseSerial != _seenControlSerial)
            {
                _seenControlSerial = controls.ReleaseSerial;
                SimulationClock clock = _runner.Clock;
                double simNow = (clock.TickCount + clock.Alpha) * clock.TickDelta + Time.deltaTime;
                double age = System.Math.Min(0.25, System.Math.Max(0.0, Time.realtimeSinceStartupAsDouble - controls.ReleaseRealtime));
                cmd.ReleaseSerial++;
                cmd.ReleaseType = controls.ReleaseType;
                cmd.ReleaseTime = simNow - age;
            }
        }

        // ------------------------------------------------------------------ events (after ticks)

        private void LateUpdate()
        {
            if (_screen != Screen.Playing) return;
            // Serve struck: back to the gameplay framing.
            if (cameraDirector.Shot == CameraShot.Serve && !_players[Human].IsServing) cameraDirector.Show(CameraShot.Gameplay);
            UpdateCallOut();
            UpdateTrajectoryAid();

            for (int k = 0; k < _players.Length; k++)
            {
                PlayerEventBuffer events = _players[k].Events;
                for (int i = 0; i < events.Count; i++)
                {
                    ref readonly PlayerEvent e = ref events[i];
                    if (e.Type == PlayerEventType.SwingStarted) ViewFor(k)?.PlaySwing(false);
                    if (e.Type == PlayerEventType.Hit) ViewFor(k)?.PlayContact(e);
                    if (k == Human && e.Type == PlayerEventType.Hit) ShowFeedback(GradeText(e), GradeColor(e.Grade));
                }
                events.Clear();
            }

            BallEventBuffer be = _ball.Events;
            for (int i = 0; i < be.Count; i++)
                if (be[i].Type == BallEventType.SandContact) markers.Mark(be[i].Position.ToUnity(), _court);
            be.Clear();
        }

        private PlayerView ViewFor(int simIndex)
        {
            for (int v = 0; v < playerViews.Length; v++)
                if (ViewToSimIndex(v, _score.PlayersPerTeam) == simIndex) return playerViews[v];
            return null;
        }

        // ------------------------------------------------------------------ announcements

        private void AnnouncePoint()
        {
            PointOutcome o = _referee.LastOutcome;
            int winner = _referee.LastWinner;
            bool ours = winner == _ids[Human].Team;

            _sb.Clear();
            if (winner < 0) _sb.Append("REPETIR O PONTO");
            else _sb.Append(ours ? "PONTO SEU" : "PONTO DO ADVERSÁRIO").Append(" — ").Append(ReasonText(_referee.LastReason, ours));

            if (o.MatchWon) _sb.Append("\nFIM DE JOGO");
            else if (o.SetWon) _sb.Append("\nSET!");
            else if (o.GameWon) _sb.Append("\nGAME!");
            if (o.TieBreakStarted) _sb.Append(_score.InMatchTieBreak ? "\nMATCH TIE-BREAK (10 pontos)" : "\nTIE-BREAK");
            if (o.ChangeEnds) _sb.Append("\nTROCA DE LADO");
            if (!_score.IsOver && _score.IsDecidingPoint) _sb.Append("\nPONTO DECISIVO");

            ShowBanner(_sb.ToString(), pointPause);
            if (o.GameWon && winner >= 0) PlayReactions(winner);
            UpdateScoreboard();
        }

        /// <summary>Winners celebrate, losers lament (authored clips) at the end of each game.</summary>
        private void PlayReactions(int winningTeam)
        {
            cameraDirector.Show(CameraShot.CloseUp); // PreparePoint returns to gameplay/serve
            for (int v = 0; v < playerViews.Length; v++)
            {
                int sim = ViewToSimIndex(v, _score.PlayersPerTeam);
                if (sim >= 0) playerViews[v].PlayReaction(_ids[sim].Team == winningTeam);
            }
        }

        private static string ReasonText(PointReason r, bool ours) => r switch
        {
            PointReason.BallLandedInOwnCourt => ours ? "a bola caiu na quadra deles" : "a bola caiu na sua quadra",
            PointReason.Out => "bola fora",
            PointReason.Net => "bola na rede",
            PointReason.ServeMissed => "falta: errou o saque",
            PointReason.ServeOut => "saque fora",
            PointReason.ServeNet => "saque na rede",
            PointReason.DoubleTouch => "dois toques",
            PointReason.BodyHit => "bola no corpo",
            PointReason.Let => "repetir",
            _ => ""
        };

        private void UpdateScoreboard()
        {
            _sb.Clear();
            for (int team = 0; team < 2; team++)
            {
                bool serving = _score.ServingTeam == team && !_score.IsOver;
                _sb.Append(serving ? "● " : "   ");
                _sb.Append(team == _ids[Human].Team ? (_score.PlayersPerTeam == 1 ? "VOCÊ      " : "VOCÊS     ") : (_score.PlayersPerTeam == 1 ? "CPU        " : "CPUs       "));
                for (int s = 0; s < _score.CompletedSets; s++)
                {
                    _sb.Append(_score.SetGames(s, team));
                    _sb.Append("   ");
                }
                if (!_score.IsOver)
                {
                    if (!_score.InMatchTieBreak) _sb.Append('[').Append(_score.Games[team]).Append("]   ");
                    _sb.Append(_score.PointText(team));
                }
                if (team == 0) _sb.Append('\n');
            }
            if (_score.InTieBreak && !_score.IsOver) _sb.Append(_score.InMatchTieBreak ? "\nmatch tie-break" : "\ntie-break");
            _sb.Append('\n').Append(_rules.Name).Append(" · rede ").Append(_rules.NetHeight.ToString("0.00")).Append(" m");
            _scoreText.text = _sb.ToString();
        }

        private void ShowResult()
        {
            _screen = Screen.Finished;
            bool won = _score.Winner == _ids[Human].Team;
            _sb.Clear();
            _sb.Append(won ? "VITÓRIA!" : "DERROTA").Append("\n\n");
            for (int s = 0; s < _score.CompletedSets; s++)
            {
                if (s > 0) _sb.Append("   ");
                _sb.Append(_score.SetGames(s, _ids[Human].Team)).Append('-').Append(_score.SetGames(s, 1 - _ids[Human].Team));
                if (_score.SetWasMatchTieBreak(s)) _sb.Append(" (TB)");
            }
            _sb.Append("\n\nToque para voltar ao menu");
            _resultText.text = _sb.ToString();
            _result.gameObject.SetActive(true);
            controls.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ menu

        // ------------------------------------------------------------------ pause

        /// <summary>Mobile: leaving the app (call, notification, home) pauses the match.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused && _screen == Screen.Playing) SetPaused(true);
        }

        private bool PauseRequested()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) return true; // Android back
            foreach (Touch t in Touch.activeTouches)
                if (t.phase == TouchPhase.Ended && RectTransformUtility.RectangleContainsScreenPoint(_pauseButton, t.screenPosition, null))
                    return true;
            return false;
        }

        private void HandlePauseInput()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) { SetPaused(false); return; }
            foreach (Touch t in Touch.activeTouches)
            {
                if (t.phase != TouchPhase.Ended) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(_resumeButton, t.screenPosition, null)) { SetPaused(false); return; }
                if (RectTransformUtility.RectangleContainsScreenPoint(_quitButton, t.screenPosition, null))
                {
                    SetPaused(false);
                    ShowMenu();
                    return;
                }
            }
        }

        /// <summary>Freezes simulation, animation and timers; the touch controls are hidden meanwhile.</summary>
        private void SetPaused(bool paused)
        {
            _screen = paused ? Screen.Paused : Screen.Playing;
            _runner.Paused = paused;
            Time.timeScale = paused ? 0f : 1f;
            _pauseMenu.gameObject.SetActive(paused);
            _pauseButton.gameObject.SetActive(!paused);
            controls.gameObject.SetActive(!paused);
        }

        private void ShowMenu()
        {
            _runner.Paused = false;
            Time.timeScale = 1f;
            TearDown();
            _screen = Screen.Menu;
            _menuView.Show();
            _result.gameObject.SetActive(false);
            _hud.gameObject.SetActive(false);
            controls.gameObject.SetActive(false);
            foreach (var v in playerViews) v.gameObject.SetActive(false);
        }

        private static bool TappedAnywhere()
        {
            foreach (Touch t in Touch.activeTouches)
                if (t.phase == TouchPhase.Ended) return true;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
        }

        // ------------------------------------------------------------------ UI


        /// <summary>Shows the predicted path of each ball heading to the human's half (when the aid is on).</summary>
        private void UpdateTrajectoryAid()
        {
            if (_ball.LaunchVersion == _trajectoryVersion) return;
            _trajectoryVersion = _ball.LaunchVersion;
            ref readonly BallState b = ref _ball.State;
            bool towardsHuman = b.Mode == BallMode.Flight && b.Velocity.z * _players[Human].State.Side > 0f;
            if (GameSettings.TrajectoryAid && towardsHuman) trajectory.Show(b, _ball.Config, _court, _runner.Clock.TickDeltaF);
            else trajectory.Hide();
        }


        /// <summary>Shows the AI partner's "MINHA!" / "SUA!" above their head (human's team only).</summary>
        private void UpdateCallOut()
        {
            int team = _ids[Human].Team;
            TeamBrain brain = _brains[team];
            if (brain != null && brain.CallCount != _seenCalls[team])
            {
                _seenCalls[team] = brain.CallCount;
                _callPlayer = team * _score.PlayersPerTeam + brain.CallerIndex;
                _callText.text = brain.CallMine ? "MINHA!" : "SUA!";
                _callText.color = brain.CallMine ? new Color(1f, 0.85f, 0.3f) : new Color(0.5f, 0.9f, 1f);
                _callTimer = 0.9f;
            }

            bool show = _callTimer > 0f && _callPlayer >= 0;
            _callRect.gameObject.SetActive(show);
            if (!show) return;
            _callTimer -= Time.deltaTime;

            var cam = Camera.main;
            Vector3 head = _players[_callPlayer].State.Position.ToUnity() + Vector3.up * 2.3f;
            Vector3 screen = cam.WorldToScreenPoint(head);
            float scale = UnityEngine.Screen.height / UiBuilder.RefHeight;
            _callRect.anchoredPosition = new Vector2(screen.x / scale, screen.y / scale);
        }

        private void ShowBanner(string text, float seconds)
        {
            _bannerText.text = text;
            _bannerPanel.gameObject.SetActive(true);
            CancelInvoke(nameof(HideBanner));
            Invoke(nameof(HideBanner), seconds);
        }

        private void HideBanner() => _bannerPanel.gameObject.SetActive(false);

        private void ShowFeedback(string text, Color color)
        {
            _feedbackText.text = text;
            color.a = 1f;
            _feedbackText.color = color;
            _feedbackText.canvasRenderer.SetAlpha(1f);
            _feedbackTimer = 0.9f;
        }

        private static string GradeText(in PlayerEvent e)
        {
            string g = e.Grade switch
            {
                TimingGrade.Perfect => "PERFEITO!",
                TimingGrade.Good => "BOM",
                TimingGrade.Early => "CEDO",
                _ => "TARDE"
            };
            return e.Smash ? g + "  SMASH" : g;
        }

        private static Color GradeColor(TimingGrade g) => g switch
        {
            TimingGrade.Perfect => new Color(0.4f, 1f, 0.45f),
            TimingGrade.Good => new Color(0.85f, 1f, 0.5f),
            _ => new Color(1f, 0.75f, 0.35f)
        };

        private void BuildUi()
        {
            // HUD: scoreboard (top centre), point banner, timing feedback.
            _hud = UiBuilder.Canvas("[MatchHUD]", 60);
            _scoreText = UiBuilder.Panel(_hud, new Vector2(0.5f, 1f), new Vector2(0f, -10f), 30, TextAnchor.UpperLeft, out _);
            _bannerText = UiBuilder.Panel(_hud, new Vector2(0.5f, 0.62f), Vector2.zero, 44, TextAnchor.MiddleCenter, out _bannerPanel, 0.55f);
            _bannerPanel.gameObject.SetActive(false);

            var fb = new GameObject("Feedback", typeof(RectTransform)).GetComponent<RectTransform>();
            fb.SetParent(_hud, false);
            fb.anchorMin = fb.anchorMax = new Vector2(0.5f, 0.36f);
            fb.sizeDelta = new Vector2(900f, 100f);
            _feedbackText = UiBuilder.Text(fb, 56, TextAnchor.MiddleCenter);
            var outline = _feedbackText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(3f, -3f);
            UiFonts.PrewarmBuffers(_feedbackText, 60);
            _feedbackText.canvasRenderer.SetAlpha(0f);

            _callText = UiBuilder.Panel(_hud, new Vector2(0f, 0f), Vector2.zero, 34, TextAnchor.MiddleCenter, out _callRect, 0.7f);
            _callRect.pivot = new Vector2(0.5f, 0f);
            _callRect.gameObject.SetActive(false);

            // Result.
            _result = UiBuilder.Canvas("[MatchResult]", 90);
            _resultText = UiBuilder.Panel(_result, new Vector2(0.5f, 0.5f), Vector2.zero, 56, TextAnchor.MiddleCenter, out _, 0.8f);
            _result.gameObject.SetActive(false);

            // Pause button (top-right, inside the safe area) and pause menu.
            _pauseButton = UiBuilder.Button(_hud, "II", Vector2.zero, new Vector2(110f, 110f), 48);
            _pauseButton.anchorMin = _pauseButton.anchorMax = _pauseButton.pivot = new Vector2(1f, 1f);
            Rect sa = UnityEngine.Screen.safeArea;
            float scale = UnityEngine.Screen.height / UiBuilder.RefHeight;
            _pauseButton.anchoredPosition = new Vector2(-20f - (UnityEngine.Screen.width - sa.xMax) / scale, -20f - (UnityEngine.Screen.height - sa.yMax) / scale);

            _pauseMenu = UiBuilder.Canvas("[PauseMenu]", 95);
            var dim = new GameObject("Dim", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            dim.SetParent(_pauseMenu, false);
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = dim.offsetMax = Vector2.zero;
            dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
            dim.GetComponent<Image>().raycastTarget = false;
            var pauseTitle = UiBuilder.Text(_pauseMenu, 64, TextAnchor.MiddleCenter);
            pauseTitle.rectTransform.anchorMin = pauseTitle.rectTransform.anchorMax = new Vector2(0.5f, 0.72f);
            pauseTitle.rectTransform.sizeDelta = new Vector2(900f, 100f);
            pauseTitle.text = "PAUSA";
            _resumeButton = UiBuilder.Button(_pauseMenu, "Continuar", new Vector2(0f, 40f), new Vector2(700f, 110f), 40);
            _quitButton = UiBuilder.Button(_pauseMenu, "Sair para o menu", new Vector2(0f, -110f), new Vector2(700f, 110f), 40);
            _pauseMenu.gameObject.SetActive(false);
        }
    }
}


