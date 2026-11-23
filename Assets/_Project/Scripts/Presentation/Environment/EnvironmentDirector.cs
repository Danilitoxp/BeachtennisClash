using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeachTennis.Presentation.Environment
{
    public enum TimeOfDay
    {
        Day,
        Sunset,
        Night
    }

    /// <summary>
    /// Applies a time-of-day preset: skybox, sun, ambient light, distance fog, post-processing profile,
    /// and court floodlights at night (docs/01 §4.2: one real-time directional light; night adds four
    /// unshadowed spots, cheap under Forward+). Post-processing is skipped on the Low quality tier.
    /// Assets come from Resources/Environment (built by EnvironmentSetup).
    /// </summary>
    public sealed class EnvironmentDirector : MonoBehaviour
    {
        private struct Preset
        {
            public string Name;
            public Vector3 SunEuler;
            public Color SunColor;
            public float SunIntensity;
            public Color AmbientSky, AmbientEquator, AmbientGround;
            public Color Fog;
            public float FogStart, FogEnd;
            public bool Floodlights;
        }

        private static readonly Preset[] Presets =
        {
            new Preset
            {
                Name = "Day", SunEuler = new Vector3(50f, -30f, 0f), SunColor = new Color(1f, 0.96f, 0.88f), SunIntensity = 1.6f,
                AmbientSky = new Color(0.55f, 0.70f, 0.90f), AmbientEquator = new Color(0.75f, 0.72f, 0.65f), AmbientGround = new Color(0.45f, 0.40f, 0.32f),
                Fog = new Color(0.72f, 0.82f, 0.92f), FogStart = 90f, FogEnd = 320f
            },
            new Preset
            {
                Name = "Sunset", SunEuler = new Vector3(12f, -115f, 0f), SunColor = new Color(1f, 0.74f, 0.52f), SunIntensity = 1.0f,
                AmbientSky = new Color(0.50f, 0.46f, 0.55f), AmbientEquator = new Color(0.58f, 0.48f, 0.42f), AmbientGround = new Color(0.30f, 0.26f, 0.22f),
                Fog = new Color(0.88f, 0.62f, 0.48f), FogStart = 70f, FogEnd = 280f
            },
            new Preset
            {
                Name = "Night", SunEuler = new Vector3(40f, 60f, 0f), SunColor = new Color(0.55f, 0.65f, 1f), SunIntensity = 0.15f,
                AmbientSky = new Color(0.10f, 0.12f, 0.20f), AmbientEquator = new Color(0.09f, 0.09f, 0.13f), AmbientGround = new Color(0.05f, 0.05f, 0.06f),
                Fog = new Color(0.04f, 0.06f, 0.11f), FogStart = 50f, FogEnd = 220f, Floodlights = true
            },
        };

        private Light _sun;
        private Volume _volume;
        private Light[] _floodlights;

        public TimeOfDay Current { get; private set; }

        private void Awake()
        {
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { _sun = l; break; }

            var volumeGo = new GameObject("Global Volume");
            volumeGo.transform.SetParent(transform, false);
            _volume = volumeGo.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;

            _floodlights = new Light[4];
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -7.5f : 7.5f, z = i < 2 ? -12f : 12f;
                var go = new GameObject("Floodlight " + i);
                go.transform.SetParent(transform, false);
                go.transform.position = new Vector3(x, 11f, z);
                go.transform.LookAt(new Vector3(x * 0.2f, 0f, z * 0.25f));
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 75f;
                l.innerSpotAngle = 45f;
                l.range = 40f;
                l.intensity = 60f;
                l.color = new Color(1f, 0.95f, 0.85f);
                l.shadows = LightShadows.None; // four shadowed spots would blow the mobile budget
                go.SetActive(false);
                _floodlights[i] = l;
            }
        }

        public void Apply(TimeOfDay time)
        {
            Current = time;
            Preset p = Presets[(int)time];

            var sky = Resources.Load<Material>("Environment/Sky_" + p.Name);
            if (sky != null) RenderSettings.skybox = sky;

            if (_sun != null)
            {
                _sun.transform.rotation = Quaternion.Euler(p.SunEuler);
                _sun.color = p.SunColor;
                _sun.intensity = p.SunIntensity;
                RenderSettings.sun = _sun;
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.AmbientSky;
            RenderSettings.ambientEquatorColor = p.AmbientEquator;
            RenderSettings.ambientGroundColor = p.AmbientGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = p.Fog;
            RenderSettings.fogStartDistance = p.FogStart;
            RenderSettings.fogEndDistance = p.FogEnd;

            foreach (Light l in _floodlights) l.gameObject.SetActive(p.Floodlights);

            _volume.sharedProfile = Resources.Load<VolumeProfile>("Environment/Post_" + p.Name);
            ApplyQuality();
        }

        /// <summary>Post-processing only from the Medium tier up (docs/01 §4.5).</summary>
        public void ApplyQuality()
        {
            bool post = QualitySettings.GetQualityLevel() >= 1;
            var cam = UnityEngine.Camera.main;
            if (cam != null && cam.TryGetComponent(out UniversalAdditionalCameraData data))
                data.renderPostProcessing = post;
            _volume.enabled = post;
        }
    }
}
