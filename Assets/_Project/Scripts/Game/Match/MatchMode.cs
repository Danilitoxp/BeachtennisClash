using System.Text;
using BeachTennis.AI;
using BeachTennis.Core;
using BeachTennis.Game.Config;
using BeachTennis.Input;
using BeachTennis.Presentation;
using BeachTennis.Presentation.Ball;
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
        private enum Screen { Menu, Playing, Finished }

        private struct MenuOption
        {
            public string Label;
            public int PlayersPerTeam;
            public bool Official;
            public bool Training;
            public bool DifficultySelector;
            public bool AidToggle;
            public RectTransform Rect;
        }

        [SerializeField] private BallPhysicsSettings physics;
        [SerializeField] private PlayerSettings playerSettings;
        [SerializeField] private BallView ballView;
        [SerializeField] private TrajectoryView trajectory;
        [SerializeField] private ImpactMarkers markers;
        [Tooltip("Team 0 player 0, team 0 player 1, team 1 player 0, team 1 player 1")]
        [SerializeField] private PlayerView[] playerViews;
        [SerializeField] private CourtView courtView;
        [SerializeField] private TouchControls controls;
        [SerializeField] private float pointPause = 2.2f;
        [SerializeField] private EventCategory category = EventCategory.Women;
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
        private Difficulty _difficulty = Difficulty.Medium;
        private readonly int[] _seenCalls = new int[2];
        private Text _callText;
        private RectTransform _callRect;
        private int _callPlayer = -1;
        private float _callTimer;
        private Text _difficultyLabel;
        private Text _aidLabel;
        /// <summary>Draw the path of balls coming to the human (readability aid, on by default).</summary>
        private bool _showAid = true;
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
        private RectTransform _menu;
        private MenuOption[] _options;
        private RectTransform _result;
        private Text _resultText;
        private readonly StringBuilder _sb = new StringBuilder(256);

        private void Start()
        {
            _runner = AppRoot.Instance.Simulation;
            BuildUi();
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
            AiProfile profile = AiProfile.For(_difficulty);
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
            _menu.gameObject.SetActive(false);
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

        private void PlaceCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;
            int side = _players[Human].State.Side;
            // Closer than a TV camera: on a phone the ball must stay several pixels wide on the far side.
            cam.fieldOfView = 55f;
            cam.transform.position = new Vector3(0f, 6.2f, side * (_court.HalfLength + 6.0f));
            cam.transform.LookAt(new Vector3(0f, 0f, -side * 2.5f));
        }

        // ------------------------------------------------------------------ frame (before ticks)

        private void Update()
        {
            switch (_screen)
            {
                case Screen.Menu: HandleMenuInput(); return;
                case Screen.Finished: if (TappedAnywhere()) ShowMenu(); return;
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

        private void ShowMenu()
        {
            TearDown();
            _screen = Screen.Menu;
            _menu.gameObject.SetActive(true);
            _result.gameObject.SetActive(false);
            _hud.gameObject.SetActive(false);
            controls.gameObject.SetActive(false);
            foreach (var v in playerViews) v.gameObject.SetActive(false);
        }

        private void HandleMenuInput()
        {
            int chosen = -1;
            foreach (Touch t in Touch.activeTouches)
            {
                if (t.phase != TouchPhase.Ended) continue;
                for (int i = 0; i < _options.Length; i++)
                    if (RectTransformUtility.RectangleContainsScreenPoint(_options[i].Rect, t.screenPosition, null)) chosen = i;
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
                for (int i = 0; i < _options.Length && i < 9; i++)
                    if (kb[UnityEngine.InputSystem.Key.Digit1 + i].wasPressedThisFrame) chosen = i;
            if (chosen < 0) return;

            MenuOption o = _options[chosen];
            if (o.DifficultySelector)
            {
                _difficulty = (Difficulty)(((int)_difficulty + 1) % 4);
                _difficultyLabel.text = DifficultyText();
                return;
            }
            if (o.AidToggle)
            {
                _showAid = !_showAid;
                _aidLabel.text = AidText();
                return;
            }
            if (o.Training) SceneManager.LoadScene(trainingScene);
            else StartMatch(o.PlayersPerTeam, o.Official);
        }

        private static bool TappedAnywhere()
        {
            foreach (Touch t in Touch.activeTouches)
                if (t.phase == TouchPhase.Ended) return true;
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb.spaceKey.wasPressedThisFrame;
        }

        // ------------------------------------------------------------------ UI

        private string AidText() => "6  Linha da trajetória: " + (_showAid ? "LIGADA" : "DESLIGADA");

        /// <summary>Shows the predicted path of each ball heading to the human's half (when the aid is on).</summary>
        private void UpdateTrajectoryAid()
        {
            if (_ball.LaunchVersion == _trajectoryVersion) return;
            _trajectoryVersion = _ball.LaunchVersion;
            ref readonly BallState b = ref _ball.State;
            bool towardsHuman = b.Mode == BallMode.Flight && b.Velocity.z * _players[Human].State.Side > 0f;
            if (_showAid && towardsHuman) trajectory.Show(b, _ball.Config, _court, _runner.Clock.TickDeltaF);
            else trajectory.Hide();
        }

        private string DifficultyText() => "5  Dificuldade: " + AiProfile.For(_difficulty).Name + "  (tocar para trocar)";

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

            // Menu.
            _menu = UiBuilder.Canvas("[MatchMenu]", 80);
            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            bg.SetParent(_menu, false);
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = bg.offsetMax = Vector2.zero;
            bg.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            bg.GetComponent<Image>().raycastTarget = false;
            var title = UiBuilder.Text(_menu, 64, TextAnchor.MiddleCenter);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.85f);
            title.rectTransform.sizeDelta = new Vector2(1200f, 100f);
            title.text = "BEACH TENNIS 3D";

            _options = new[]
            {
                new MenuOption { Label = "1  Simples · Rápida (1 set curto)", PlayersPerTeam = 1 },
                new MenuOption { Label = "2  Duplas · Rápida (1 set curto)", PlayersPerTeam = 2 },
                new MenuOption { Label = "3  Simples · Oficial ITF (melhor de 3)", PlayersPerTeam = 1, Official = true },
                new MenuOption { Label = "4  Treino", Training = true },
                new MenuOption { Label = "", DifficultySelector = true },
                new MenuOption { Label = "", AidToggle = true },
            };
            for (int i = 0; i < _options.Length; i++)
                _options[i].Rect = UiBuilder.Button(_menu, _options[i].Label, new Vector2(0f, 270f - i * 112f), new Vector2(900f, 92f), 34);
            _difficultyLabel = _options[_options.Length - 2].Rect.GetComponentInChildren<Text>();
            _difficultyLabel.text = DifficultyText();
            _aidLabel = _options[_options.Length - 1].Rect.GetComponentInChildren<Text>();
            _aidLabel.text = AidText();

            var note = UiBuilder.Text(_menu, 26, TextAnchor.MiddleCenter);
            note.rectTransform.anchorMin = note.rectTransform.anchorMax = new Vector2(0.5f, 0.08f);
            note.rectTransform.sizeDelta = new Vector2(1600f, 60f);
            note.text = "Regras ITF 2026 · categoria feminina/mista (rede 1,70 m, zona proibida 3 m) · em duplas o parceiro é IA do mesmo nível";

            // Result.
            _result = UiBuilder.Canvas("[MatchResult]", 90);
            _resultText = UiBuilder.Panel(_result, new Vector2(0.5f, 0.5f), Vector2.zero, 56, TextAnchor.MiddleCenter, out _, 0.8f);
            _result.gameObject.SetActive(false);
        }
    }
}


