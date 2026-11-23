using BeachTennis.Simulation;
using BeachTennis.Simulation.Ball;
using UnityEngine;

namespace BeachTennis.Presentation.Ball
{
    /// <summary>
    /// Debug/training line showing the predicted path of the ball up to its first sand contact.
    /// Uses the exact same physics as the live ball, so the line is where the ball will go.
    /// Buffers are allocated once; showing a trajectory does not allocate.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class TrajectoryView : MonoBehaviour
    {
        private const int MaxTicks = 960;   // 8 s at 120 Hz
        private const int Stride = 3;       // one line vertex every 3 ticks (40 per second)

        [SerializeField] private Color color = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private float width = 0.06f;

        private readonly BallState[] _states = new BallState[MaxTicks];
        private readonly Vector3[] _points = new Vector3[MaxTicks / Stride + 2];
        private LineRenderer _line;

        private void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.sharedMaterial = RuntimeSprites.SpriteMaterial;
            _line.startColor = _line.endColor = color;
            _line.startWidth = _line.endWidth = width;
            _line.numCornerVertices = 0;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.positionCount = 0;
        }

        public void Show(in BallState start, in BallPhysicsConfig config, in CourtDimensions court, float tickDelta)
        {
            int n = BallPhysics.Predict(start, config, court, tickDelta, _states, stopAtSand: true);

            int count = 0;
            _points[count++] = start.Position.ToUnity();
            for (int i = Stride - 1; i < n; i += Stride)
                _points[count++] = _states[i].Position.ToUnity();
            if (n > 0 && (n - 1) % Stride != Stride - 1)
                _points[count++] = _states[n - 1].Position.ToUnity();

            _line.positionCount = count;
            _line.SetPositions(_points);
        }

        public void Hide() => _line.positionCount = 0;
    }
}
