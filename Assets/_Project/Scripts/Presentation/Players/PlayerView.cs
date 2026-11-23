using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using UnityEngine;

namespace BeachTennis.Presentation.Players
{
    /// <summary>
    /// Gray-box player: interpolated capsule, a racket that swings on release, a lean while diving, a ring
    /// on the sand where the assist is taking the player, and a ring on the opponent court showing the aim.
    /// Replaced by the animated character in Phase 5; the simulation contract stays the same.
    /// </summary>
    public sealed class PlayerView : MonoBehaviour
    {
        [SerializeField] private Transform body;
        [SerializeField] private Transform racketPivot;
        [SerializeField] private float swingDuration = 0.28f;

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

        public void Bind(PlayerSystem player, ISimulationStats clock)
        {
            _player = player;
            _clock = clock;
        }

        private void Awake()
        {
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
            _swingTimer = 0f;
            _swingSign = backhand ? -1 : 1;
        }

        private void LateUpdate()
        {
            if (_player == null) return;

            ref readonly PlayerState s = ref _player.State;
            float alpha = _clock?.Alpha ?? 1f;
            Vec3 p = Vec3.Lerp(_player.PreviousState.Position, s.Position, alpha);
            transform.position = p.ToUnity();

            // Face the net.
            transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, -s.Side));

            // Dive: lean towards the side the player lunged to.
            float lean = s.Phase == PlayerPhase.Recovering ? 70f : 0f;
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(0f, 0f, -lean * _swingSign), 1f - Mathf.Exp(-20f * Time.deltaTime));

            AnimateRacket();
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
