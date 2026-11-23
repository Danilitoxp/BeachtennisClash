using BeachTennis.Simulation;
using UnityEngine;

namespace BeachTennis.Presentation.Ball
{
    /// <summary>
    /// Pooled marks left where the ball touched the sand: green inside the court (lines count as in),
    /// red outside. Placeholder for the sand-mark decal and particles of the visual phase.
    /// </summary>
    public sealed class ImpactMarkers : MonoBehaviour
    {
        [SerializeField] private int poolSize = 8;
        [SerializeField] private float lifetime = 3f;
        [SerializeField] private float diameter = 0.35f;

        private static readonly Color InColor = new Color(0.2f, 0.9f, 0.3f, 1f);
        private static readonly Color OutColor = new Color(0.95f, 0.2f, 0.15f, 1f);

        private SpriteRenderer[] _markers;
        private float[] _age;
        private int _next;

        private void Awake()
        {
            _markers = new SpriteRenderer[poolSize];
            _age = new float[poolSize];
            for (int i = 0; i < poolSize; i++)
            {
                var go = new GameObject("ImpactMarker");
                go.transform.SetParent(transform, false);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = new Vector3(diameter, diameter, 1f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = RuntimeSprites.Ring;
                sr.sharedMaterial = RuntimeSprites.SpriteMaterial;
                sr.enabled = false;
                _markers[i] = sr;
                _age[i] = float.MaxValue;
            }
        }

        public void Mark(Vector3 position, bool inside)
        {
            var sr = _markers[_next];
            sr.transform.position = new Vector3(position.x, 0.008f, position.z);
            sr.color = inside ? InColor : OutColor;
            sr.enabled = true;
            _age[_next] = 0f;
            _next = (_next + 1) % _markers.Length;
        }

        public void Mark(Vector3 position, in CourtDimensions court) => Mark(position, court.IsInside(position.x, position.z));

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _markers.Length; i++)
            {
                if (!_markers[i].enabled) continue;
                _age[i] += dt;
                if (_age[i] >= lifetime)
                {
                    _markers[i].enabled = false;
                    continue;
                }
                var c = _markers[i].color;
                c.a = 1f - _age[i] / lifetime;
                _markers[i].color = c;
            }
        }
    }
}
