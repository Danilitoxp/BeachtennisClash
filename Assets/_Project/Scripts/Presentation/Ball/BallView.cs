using BeachTennis.Core;
using BeachTennis.Simulation.Ball;
using UnityEngine;

namespace BeachTennis.Presentation.Ball
{
    /// <summary>
    /// Renders the simulated ball: interpolates between the last two simulation ticks (smooth at any
    /// frame rate), spins the mesh with the simulated angular velocity and drives a contact shadow.
    ///
    /// The contact shadow is gameplay, not decoration: on a phone screen it is the main depth cue
    /// for judging where the ball will be (docs/01 §4.2), so it is drawn regardless of shadow quality.
    /// </summary>
    public sealed class BallView : MonoBehaviour
    {
        [SerializeField] private Transform visual;
        [Tooltip("Visual-only scale over the physical diameter. A real 6.7 cm ball is a few pixels wide " +
                 "at broadcast distance on a phone; physics always uses the real radius.")]
        [SerializeField] private float visualScale = 2.4f;
        [SerializeField] private float shadowBaseSize = 0.22f;
        [SerializeField] private float shadowFadeHeight = 8f;
        [SerializeField] private float shadowMaxAlpha = 0.75f;
        [Tooltip("Seconds of motion trail behind the ball (readability on small screens).")]
        [SerializeField] private float trailTime = 0.16f;
        [SerializeField] private Color trailColor = new Color(1f, 0.95f, 0.2f, 0.75f);

        private BallSystem _ball;
        private ISimulationStats _clock;
        private int _lastLaunchVersion = -1;

        private SpriteRenderer _shadow;
        private TrailRenderer _trail;
        private MeshRenderer _mesh;

        public void Bind(BallSystem ball, ISimulationStats clock)
        {
            _ball = ball;
            _clock = clock;
        }

        private void Awake()
        {
            if (visual == null) visual = transform;
            _mesh = visual.GetComponent<MeshRenderer>();

            var shadowGo = new GameObject("ContactShadow");
            shadowGo.transform.SetParent(transform.parent, false);
            shadowGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _shadow = shadowGo.AddComponent<SpriteRenderer>();
            _shadow.sprite = RuntimeSprites.SoftDisc;
            _shadow.sharedMaterial = RuntimeSprites.SpriteMaterial;
            _shadow.color = new Color(0f, 0f, 0f, shadowMaxAlpha);
            _shadow.sortingOrder = -1;

            // Short motion trail: makes direction and speed readable even when the ball is a few pixels.
            _trail = visual.gameObject.GetComponent<TrailRenderer>();
            if (_trail == null) _trail = visual.gameObject.AddComponent<TrailRenderer>();
            _trail.time = trailTime;
            _trail.minVertexDistance = 0.05f;
            _trail.sharedMaterial = RuntimeSprites.SpriteMaterial;
            _trail.startColor = trailColor;
            _trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.numCapVertices = 2;
        }

        private void LateUpdate()
        {
            if (_ball == null) return;

            ref readonly BallState current = ref _ball.State;
            bool visible = current.Mode != BallMode.Inactive;
            // Toggle renderers only: this component may live on the visual itself, and deactivating its
            // own GameObject would stop LateUpdate for good (the ball would never reappear).
            if (_mesh != null) _mesh.enabled = visible;
            _trail.enabled = visible;
            _shadow.enabled = visible;
            if (!visible) return;
            _trail.emitting = current.Mode == BallMode.Flight;

            // Never interpolate across a launch/teleport.
            bool snapped = _ball.LaunchVersion != _lastLaunchVersion;
            _lastLaunchVersion = _ball.LaunchVersion;
            if (snapped)
            {
                float d = 2f * _ball.Config.Radius * visualScale;
                visual.localScale = new Vector3(d, d, d);
                _trail.startWidth = d * 0.9f;
                _trail.endWidth = 0f;
            }

            Vec3 pos = snapped
                ? current.Position
                : Vec3.Lerp(_ball.PreviousState.Position, current.Position, _clock?.Alpha ?? 1f);
            visual.position = pos.ToUnity();
            if (snapped) _trail.Clear(); // no streak from the previous ball position

            float spinRate = current.Spin.Magnitude;
            if (spinRate > 1e-3f)
            {
                Vector3 axis = (current.Spin / spinRate).ToUnity();
                visual.Rotate(axis, spinRate * Mathf.Rad2Deg * Time.deltaTime, Space.World);
            }

            UpdateShadow(pos);
        }

        private void UpdateShadow(Vec3 pos)
        {
            float height = Mathf.Max(0f, pos.y);
            float t = Mathf.Clamp01(height / shadowFadeHeight);
            _shadow.transform.position = new Vector3(pos.x, 0.006f, pos.z);
            float size = shadowBaseSize * (1f + t * 1.5f);
            _shadow.transform.localScale = new Vector3(size, size, 1f);
            var c = _shadow.color;
            c.a = shadowMaxAlpha * (1f - t * 0.75f);
            _shadow.color = c;
        }
    }
}
