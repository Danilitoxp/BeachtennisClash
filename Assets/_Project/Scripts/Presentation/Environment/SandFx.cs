using UnityEngine;

namespace BeachTennis.Presentation.Environment
{
    /// <summary>
    /// Sand puffs where the ball lands and where a player dives. One world-space particle system emits
    /// bursts wherever asked: no objects are created during play and the particle count is capped.
    /// </summary>
    public sealed class SandFx : MonoBehaviour
    {
        private ParticleSystem _ps;

        private void Awake()
        {
            var go = new GameObject("SandFx Particles");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1f;
            main.maxParticles = 300;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.17f);
            main.startColor = new Color(0.9f, 0.8f, 0.62f, 0.85f);
            main.gravityModifier = 0.9f;

            var emission = _ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.08f;
            shape.rotation = new Vector3(-90f, 0f, 0f); // hemisphere opening upwards

            var colour = _ps.colorOverLifetime;
            colour.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colour.color = g;

            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.6f));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Resources.Load<Material>("Environment/SandParticle");
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _ps.Play();
        }

        /// <summary>Emits a puff at <paramref name="position"/>; <paramref name="strength"/> 0..1 scales the count.</summary>
        public void Burst(Vector3 position, float strength)
        {
            int count = Mathf.RoundToInt(Mathf.Lerp(6f, 26f, Mathf.Clamp01(strength)));
            _ps.transform.position = new Vector3(position.x, 0.02f, position.z);
            _ps.Emit(count);
        }
    }
}
