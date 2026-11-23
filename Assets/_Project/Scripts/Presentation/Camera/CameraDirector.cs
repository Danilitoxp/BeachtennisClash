using BeachTennis.Core;
using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using BeachTennis.Simulation.Players;
using Unity.Cinemachine;
using UnityEngine;

namespace BeachTennis.Presentation.Cameras
{
    public enum CameraShot
    {
        /// <summary>Behind the human's baseline, following them partly and leaning towards the ball.</summary>
        Gameplay,
        /// <summary>Lower and closer behind the human while they serve.</summary>
        Serve,
        /// <summary>Close-up from the net side on the human (end of game reactions).</summary>
        CloseUp
    }

    /// <summary>
    /// Cinemachine camera direction for matches and training (docs/01 §2.7): one gameplay camera that
    /// keeps both halves and the ball readable on a phone, plus serve and close-up shots blended by the
    /// brain. Change of ends cuts instead of blending (a blend would fly through the net).
    /// Reads the simulation only; never affects gameplay.
    /// </summary>
    [DefaultExecutionOrder(300)]
    public sealed class CameraDirector : MonoBehaviour
    {
        [Header("Gameplay framing")]
        [SerializeField] private float height = 6.2f;
        [SerializeField] private float back = 6.0f;
        [SerializeField] private float lookAhead = 2.5f;
        [Tooltip("Fraction of the human's lateral position the camera follows.")]
        [SerializeField] private float lateralFollow = 0.35f;
        [Tooltip("Fraction of the human's move towards the net the camera follows.")]
        [SerializeField] private float depthFollow = 0.25f;
        [Tooltip("How much the look target leans towards the ball in flight.")]
        [SerializeField] private float ballInfluence = 0.2f;
        [SerializeField] private float fieldOfView = 55f;
        [SerializeField] private float blendSeconds = 0.6f;

        [Header("Portrait framing (over the shoulder)")]
        [SerializeField] private float portraitHeight = 6.0f;
        [Tooltip("Distance behind the human (follows their depth fully).")]
        [SerializeField] private float portraitBack = 5.5f;
        [SerializeField] private float portraitLateralFollow = 0.75f;
        [Tooltip("Look target distance past the net, on the opponents' half.")]
        [SerializeField] private float portraitLookAhead = 2.5f;
        [SerializeField] private float portraitFieldOfView = 66f;

        private CinemachineCamera _gameplay, _serve, _closeUp;
        private Transform _rig, _focus, _serveRig, _serveFocus, _closeRig, _closeFocus;
        private PlayerSystem _human;
        private BallSystem _ball;
        private CourtDimensions _court;
        private ISimulationStats _clock;
        private CameraShot _shot = CameraShot.Gameplay;
        private int _lastSide;

        public CameraShot Shot => _shot;

        private void Awake()
        {
            var cam = UnityEngine.Camera.main;
            if (cam != null && cam.GetComponent<CinemachineBrain>() == null)
            {
                var brain = cam.gameObject.AddComponent<CinemachineBrain>();
                brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, blendSeconds);
            }

            _gameplay = CreateCamera("CM Gameplay", out _rig, out _focus, new Vector3(0.5f, 0.35f, 0.5f), new Vector2(0.6f, 0.45f));
            _serve = CreateCamera("CM Serve", out _serveRig, out _serveFocus, new Vector3(0.3f, 0.3f, 0.3f), new Vector2(0.4f, 0.4f));
            _closeUp = CreateCamera("CM CloseUp", out _closeRig, out _closeFocus, new Vector3(0.2f, 0.2f, 0.2f), new Vector2(0.2f, 0.2f));

            // Until a player is bound, frame the court from the south as the scene camera did.
            if (cam != null)
            {
                _rig.position = cam.transform.position;
                _focus.position = cam.transform.position + cam.transform.forward * 15f;
            }
            Apply(CameraShot.Gameplay);
        }

        private CinemachineCamera CreateCamera(string name, out Transform rig, out Transform focus, Vector3 positionDamping, Vector2 aimDamping)
        {
            rig = new GameObject(name + " Rig").transform;
            rig.SetParent(transform, false);
            focus = new GameObject(name + " Focus").transform;
            focus.SetParent(transform, false);

            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var vcam = go.AddComponent<CinemachineCamera>();
            vcam.Follow = rig;
            vcam.LookAt = focus;
            vcam.Lens.FieldOfView = fieldOfView;
            vcam.Lens.NearClipPlane = 0.3f;
            vcam.Lens.FarClipPlane = 200f;

            var follow = go.AddComponent<CinemachineFollow>();
            follow.FollowOffset = Vector3.zero;
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = positionDamping;

            var aim = go.AddComponent<CinemachineRotationComposer>();
            aim.Damping = aimDamping;
            return vcam;
        }

        public void Bind(PlayerSystem human, BallSystem ball, in CourtDimensions court, ISimulationStats clock)
        {
            _human = human;
            _ball = ball;
            _court = court;
            _clock = clock;
            _lastSide = 0; // forces a cut on the first update
        }

        /// <summary>Switches shot with a blend (cuts automatically on change of ends).</summary>
        public void Show(CameraShot shot) => Apply(shot);

        private void Apply(CameraShot shot)
        {
            _shot = shot;
            _gameplay.Priority.Value = shot == CameraShot.Gameplay ? 10 : 0;
            _serve.Priority.Value = shot == CameraShot.Serve ? 10 : 0;
            _closeUp.Priority.Value = shot == CameraShot.CloseUp ? 10 : 0;
        }

        private void LateUpdate()
        {
            if (_human == null) return;

            ref readonly PlayerState s = ref _human.State;
            float alpha = _clock?.Alpha ?? 1f;
            Vector3 p = Vec3.Lerp(_human.PreviousState.Position, s.Position, alpha).ToUnity();
            int side = s.Side;
            float forward = -side; // towards the net from the human's half

            bool portrait = UnityEngine.Screen.height > UnityEngine.Screen.width;
            _gameplay.Lens.FieldOfView = portrait ? portraitFieldOfView : fieldOfView;

            float x;
            Vector3 look;
            if (portrait)
            {
                // Over the shoulder: close behind the human, the far court filling the tall screen.
                x = Mathf.Clamp(p.x * portraitLateralFollow, -_court.HalfWidth * 0.8f, _court.HalfWidth * 0.8f);
                float z = p.z + side * portraitBack;
                _rig.position = new Vector3(x, portraitHeight, z);
                look = new Vector3(x * 0.4f, 0.5f, forward * portraitLookAhead);
            }
            else
            {
                // Landscape: behind the human's baseline.
                x = Mathf.Clamp(p.x * lateralFollow, -_court.HalfWidth * 0.6f, _court.HalfWidth * 0.6f);
                float towardsNet = (side * _court.HalfLength * 0.6f - p.z) * forward; // >0 when ahead of the usual spot
                float z = side * (_court.HalfLength + back) + forward * Mathf.Max(0f, towardsNet) * depthFollow;
                _rig.position = new Vector3(x, height, z);
                look = new Vector3(x * 0.6f, 0.6f, forward * lookAhead);
            }
            if (_ball != null && _ball.State.Mode == BallMode.Flight)
            {
                Vector3 b = Vec3.Lerp(_ball.PreviousState.Position, _ball.State.Position, alpha).ToUnity();
                b.y = Mathf.Min(b.y, 4f);
                look = Vector3.Lerp(look, b, ballInfluence);
            }
            _focus.position = look;

            // Serve: lower and closer behind the server, looking at the receivers.
            _serve.Lens.FieldOfView = _gameplay.Lens.FieldOfView;
            _closeUp.Lens.FieldOfView = portrait ? 55f : fieldOfView;
            _serveRig.position = new Vector3(p.x * 0.6f, 3.8f, p.z + side * 4.5f);
            _serveFocus.position = new Vector3(p.x * 0.3f, 1.0f, forward * 4f);

            // Close-up: from the net side, in front of the human.
            _closeRig.position = p + new Vector3(0.8f, 1.7f, forward * 3.2f);
            _closeFocus.position = p + Vector3.up * 1.2f;

            if (side != _lastSide)
            {
                // Change of ends (or first frame): cut, never fly across the net.
                _lastSide = side;
                Snap(_gameplay);
                Snap(_serve);
                Snap(_closeUp);
            }
        }

        private static void Snap(CinemachineCamera vcam) => vcam.PreviousStateIsValid = false;
    }
}
