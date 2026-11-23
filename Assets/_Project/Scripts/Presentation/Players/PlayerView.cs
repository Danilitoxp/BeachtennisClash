using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using UnityEngine;

namespace BeachTennis.Presentation.Players
{
    /// <summary>
    /// Interpolated player with provisional humanoid posing, contact IK, dive lean and a ring
    /// on the sand where the assist is taking the player, and a ring on the opponent court showing the aim.
    /// Falls back to the capsule swing when no humanoid is present.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform racketPivot;
        [SerializeField] private float swingDuration = 0.28f;

        private double _lastVisualTime = -1;
        private BallSystem _ball;
        private Transform _animationCamera;
        private double _lastPoseTime = -1;
        private bool _forcePose;
        private HumanoidPlayerPose _pose;
        private PlayerSystem _player;
        private ISimulationStats _clock;
        private float _swingTimer = -1f;
        private int _swingSign = 1;

        private SpriteRenderer _standMarker;
        private SpriteRenderer _aimMarker;

        private static readonly Color Reachable = new Color(0.25f, 1f, 0.35f, 0.9f);
        private static readonly Color DiveOnly = new Color(1f, 0.6f, 0.1f, 0.9f);
        private static readonly Color AimColor = new Color(1f, 1f, 1f, 0.85f);

        /// <summary>Show the stand/aim rings (human player only).</summary>
        public bool ShowGuides { get; set; } = true;

        public void Bind(PlayerSystem player, ISimulationStats clock, BallSystem ball = null)
        {
            _lastPoseTime = -1;
            _forcePose = true;
            _ball = ball;
            _animationCamera = Camera.main != null ? Camera.main.transform : null;
            _lastVisualTime = -1;
            _pose?.Reset();
            _player = player;
            _clock = clock;
        }

        private void Awake()
        {
            _pose = new HumanoidPlayerPose(transform, GetComponentInChildren<Animator>(true), racketPivot);
            _standMarker = Marker("StandMarker", 0.7f);
            _aimMarker = Marker("AimMarker", 0.9f);
        }

        private SpriteRenderer Marker(string name, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform.parent, false);
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(size, size, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = RuntimeSprites.Ring;
            sr.sharedMaterial = RuntimeSprites.SpriteMaterial;
            sr.enabled = false;
            return sr;
        }

        /// <summary>Call for each SwingStarted/Hit event so the racket animates.</summary>
        public void PlaySwing(bool backhand)
        {
            if (_player != null && _player.State.Plan.Valid)
                backhand = transform.InverseTransformPoint(_player.State.Plan.BallPosition.ToUnity()).x < 0f;
            _forcePose = true;
            _swingTimer = 0f;
            _swingSign = backhand ? -1 : 1;
            if (_player != null)
                _pose?.Swing(ShotAnimationSelector.Select(_player.Command.ReleaseType,
                    _player.State.Phase == PlayerPhase.Serving,
                    _player.State.Plan.Valid && _player.State.Plan.BallPosition.y >= _player.Config.SmashHeight, backhand));
        }

        public void PlayContact(in PlayerEvent e)
        {
            _forcePose = true;
            bool backhand = transform.InverseTransformPoint(e.ContactPoint.ToUnity()).x < 0f;
            _pose?.Contact(e.ContactPoint.ToUnity(), ShotAnimationSelector.Select(e.Shot,
                (_pose != null && _pose.CurrentShot == ShotAnimation.Serve) ||
                (_player != null && _player.State.Phase == PlayerPhase.Serving), e.Smash, backhand),
                (e.Tick + 1) * (double)(1f / (_clock?.TickRate ?? 120)));
        }

        private void OnDestroy()
        {
            if (_standMarker != null) Destroy(_standMarker.gameObject);
            if (_aimMarker != null) Destroy(_aimMarker.gameObject);
        }

        private void LateUpdate()
        {
            if (_player == null) return;

            ref readonly PlayerState s = ref _player.State;
            float alpha = _clock?.Alpha ?? 1f;
            // Same interpolated time as the visual root; pause freezes the whole pose.
            double visualTime = _clock == null ? Time.timeAsDouble :
                System.Math.Max(0.0, _clock.TickCount - 1 + alpha) * (double)(1f / _clock.TickRate);
            float poseDelta = _lastVisualTime < 0 ? 0f : (float)System.Math.Max(0.0, visualTime - _lastVisualTime);
            Vec3 p = Vec3.Lerp(_player.PreviousState.Position, s.Position, alpha);
            transform.position = p.ToUnity();

            // Face the net.
            transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, -s.Side));

            // Dive: lean towards the side the player lunged to.
            float lean = s.Phase == PlayerPhase.Recovering ? 70f : 0f;
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(0f, 0f, -lean * _swingSign), 1f - Mathf.Exp(-20f * poseDelta));

            if (_pose != null && _pose.Ready)
            {

                bool backhand = s.Plan.Valid && transform.InverseTransformPoint(s.Plan.BallPosition.ToUnity()).x < 0f;
                ShotType type = double.IsNaN(s.SwingTime) ? _player.Command.HeldType : s.SwingType;
                ShotAnimation selected = ShotAnimationSelector.Select(type, s.Phase == PlayerPhase.Serving,
                    s.Plan.Valid && s.Plan.BallPosition.y >= _player.Config.SmashHeight, backhand);
                _pose.Synchronize(s, _player.Command.ShotHeld, visualTime, selected);

                Vector3? lookTarget = null;
                if (_ball != null && _ball.State.Mode == BallMode.Flight)
                    lookTarget = Vec3.Lerp(_ball.PreviousState.Position, _ball.State.Position, alpha).ToUnity();
                _pose.SetLookTarget(lookTarget);
                float distanceSquared = _animationCamera != null ? (_animationCamera.position - transform.position).sqrMagnitude : 0f;
                bool critical = _forcePose || _player.Command.ShotHeld || !double.IsNaN(s.SwingTime) ||
                    s.Phase == PlayerPhase.Recovering || _pose.ContactActive(visualTime);
                if (AnimationLod.ShouldUpdate(visualTime, _lastPoseTime, distanceSquared, QualitySettings.GetQualityLevel(), critical))
                {
                    float elapsed = _lastPoseTime < 0 ? 0f : (float)System.Math.Max(0, visualTime - _lastPoseTime);
                    _pose.Update(s, _player.Command.ShotHeld, elapsed, visualTime);
                    _lastPoseTime = visualTime;
                    _forcePose = false;
                }

            }
            else AnimateRacket();
            _lastVisualTime = visualTime;
            UpdateMarkers(s);
        }

        private void AnimateRacket()
        {
            if (racketPivot == null) return;
            if (_swingTimer >= 0f)
            {
                _swingTimer += Time.deltaTime;
                float t = Mathf.Clamp01(_swingTimer / swingDuration);
                // Back-swing to follow-through: -70° → +110°, ease out.
                _racketAngle = Mathf.Lerp(-70f, 110f, 1f - (1f - t) * (1f - t));
                if (t >= 1f) _swingTimer = -1f;
            }
            else
            {
                // Racket back while preparing, relaxed otherwise.
                float rest = _player.Command.ShotHeld ? -70f : 0f;
                _racketAngle = Mathf.MoveTowards(_racketAngle, rest, 400f * Time.deltaTime);
            }
            racketPivot.localRotation = Quaternion.Euler(0f, _racketAngle * _swingSign, 0f);
        }

        private float _racketAngle;

        private void UpdateMarkers(in PlayerState s)
        {
            if (!ShowGuides)
            {
                _standMarker.enabled = false;
                _aimMarker.enabled = false;
                return;
            }

            bool showStand = s.Plan.Valid && !s.HasHitCurrentBall && s.Phase != PlayerPhase.Serving;
            _standMarker.enabled = showStand;
            if (showStand)
            {
                _standMarker.transform.position = new Vector3(s.Plan.StandPosition.x, 0.012f, s.Plan.StandPosition.z);
                _standMarker.color = s.Plan.Reachable ? Reachable : DiveOnly;
            }

            bool aiming = _player.Command.ShotHeld && (s.Plan.Valid || s.Phase == PlayerPhase.Serving);
            _aimMarker.enabled = aiming;
            if (aiming)
            {
                Vec3 contact = s.Plan.Valid ? s.Plan.BallPosition : s.Position + new Vec3(0f, 2.7f, 0f);
                ShotRequest r = ShotSelector.Build(_player.Command.HeldType, s.Phase == PlayerPhase.Serving, contact,
                    _player.Command.AimX, _player.Command.AimY, s.Side, _player.Config, _player.Court, out _, _player.Command.EffectivePower);
                _aimMarker.transform.position = new Vector3(r.Target.x, 0.012f, r.Target.z);
                _aimMarker.color = AimColor;
            }
        }
    }
}





