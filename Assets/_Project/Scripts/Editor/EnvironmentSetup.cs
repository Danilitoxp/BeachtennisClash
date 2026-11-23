using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BeachTennis.EditorTools
{
    /// <summary>
    /// Phase 7a — beach environment from code (no external art): tileable sand textures, a sea mesh +
    /// shader, low-poly palm trees, skyboxes and post-processing profiles per time of day, and the sand
    /// particle material. <see cref="Apply"/> builds the assets and decorates the existing scenes in place.
    /// All of it is placeholder-grade but production-shaped (materials, LOD-friendly meshes, budgets).
    /// </summary>
    public static class EnvironmentSetup
    {
        private const string ArtDir = "Assets/_Project/Art/Environment";
        private const string ResDir = "Assets/_Project/Resources/Environment";
        public const float ShoreX = 18f;

        public static readonly string[] TimesOfDay = { "Day", "Sunset", "Night" };

        [MenuItem("Beach Tennis/Setup/Environment (Phase 7a)")]
        public static void Apply()
        {
            BuildAssets();
            foreach (string path in new[] { GrayboxSceneBuilder.MatchScenePath, GrayboxSceneBuilder.TrainingScenePath, GrayboxSceneBuilder.SandboxScenePath })
            {
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Decorate();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[BeachTennis] Environment assets built and scenes decorated.");
        }

        // ------------------------------------------------------------------ assets

        public static void BuildAssets()
        {
            foreach (string d in new[] { ArtDir + "/Textures", ArtDir + "/Materials", ArtDir + "/Meshes", ResDir }) Directory.CreateDirectory(d);

            BuildSand();
            BuildSea();
            BuildPalm();
            BuildSkies();
            BuildPostProfiles();
            BuildParticleMaterial();
            AssetDatabase.SaveAssets();
        }

        private static void BuildSand()
        {
            const int size = 512;
            var albedo = new Texture2D(size, size, TextureFormat.RGB24, true);
            var height = new float[size * size];
            var baseColor = new Color(0.86f, 0.76f, 0.58f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size, v = (float)y / size;
                // Wind ripples (low) + grains (high), all periodic so the texture tiles seamlessly.
                float ripples = Fbm(u, v, 3, 2, 0.5f, 11) * 0.6f + Mathf.Sin((v + Fbm(u, v, 4, 2, 0.5f, 3) * 0.15f) * 6.2831853f * 9f) * 0.25f;
                float grains = Fbm(u, v, 64, 3, 0.55f, 7);
                height[y * size + x] = ripples * 0.6f + grains * 0.4f;
                float shade = 1f + (grains - 0.5f) * 0.18f + (ripples - 0.3f) * 0.06f;
                float sparkle = Hash(x, y, 99) > 0.995f ? 0.12f : 0f; // a few bright grains
                albedo.SetPixel(x, y, new Color(baseColor.r * shade + sparkle, baseColor.g * shade + sparkle, baseColor.b * shade * 0.98f + sparkle));
            }
            albedo.Apply();
            string albedoPath = SavePng(albedo, ArtDir + "/Textures/Sand_Albedo.png");

            var normal = new Texture2D(size, size, TextureFormat.RGB24, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float hl = height[y * size + (x + size - 1) % size], hr = height[y * size + (x + 1) % size];
                float hd = height[((y + size - 1) % size) * size + x], hu = height[((y + 1) % size) * size + x];
                var n = new Vector3((hl - hr) * 3f, (hd - hu) * 3f, 1f).normalized;
                normal.SetPixel(x, y, new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f));
            }
            normal.Apply();
            string normalPath = SavePng(normal, ArtDir + "/Textures/Sand_Normal.png", isNormal: true);

            Material sand = GetOrCreateMaterial(ArtDir + "/Materials/Sand.mat", "Universal Render Pipeline/Lit");
            sand.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath));
            sand.SetColor("_BaseColor", Color.white);
            sand.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            sand.SetFloat("_BumpScale", 0.9f);
            sand.EnableKeyword("_NORMALMAP");
            sand.SetFloat("_Smoothness", 0.12f);
            sand.SetTextureScale("_BaseMap", new Vector2(40f, 70f)); // ~1.45 m per repeat on the 58×100 m ground
            EditorUtility.SetDirty(sand);
        }

        private static void BuildSea()
        {
            // Grid: x = distance from the shore (uv.x 0→1 over 380 m), z along the shore.
            const int nx = 48, nz = 48;
            const float lengthX = 380f, lengthZ = 400f;
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= nz; j++)
            for (int i = 0; i <= nx; i++)
            {
                // Denser near the shore, where the swell fades and the foam line is.
                float t = Mathf.Pow((float)i / nx, 1.8f);
                vertices.Add(new Vector3(t * lengthX, 0f, ((float)j / nz - 0.5f) * lengthZ));
                uvs.Add(new Vector2(t, (float)j / nz));
            }
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tris.AddRange(new[] { a, c, b, b, c, d });
            }
            var mesh = new Mesh { name = "Sea" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(mesh.bounds.center, mesh.bounds.size + Vector3.up * 2f); // vertex swell
            SaveMesh(mesh, ArtDir + "/Meshes/Sea.asset");

            Material sea = GetOrCreateMaterial(ArtDir + "/Materials/Sea.mat", "BeachTennis/Sea");
            EditorUtility.SetDirty(sea);
        }

        /// <summary>Low-poly palm: curved 8-sided trunk + 9 drooping fronds. Two submeshes (trunk, leaves).</summary>
        private static void BuildPalm()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var uv = new List<Vector2>();
            var trunk = new List<int>();
            var leaves = new List<int>();

            const int rings = 10, sides = 8;
            const float trunkHeight = 7f;
            Vector3 TrunkPoint(float t) => new Vector3(Mathf.Sin(t * 1.2f) * 1.1f * t, t * trunkHeight, 0f);
            for (int r = 0; r <= rings; r++)
            {
                float t = (float)r / rings;
                Vector3 c = TrunkPoint(t);
                float radius = Mathf.Lerp(0.22f, 0.13f, t) * (1f + 0.08f * Mathf.Sin(r * 2.7f)); // ring bumps
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    v.Add(c + dir * radius);
                    n.Add(dir);
                    uv.Add(new Vector2((float)s / sides, t * 4f));
                }
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = r * (sides + 1) + s, b = a + 1, c = a + sides + 1, d = c + 1;
                trunk.AddRange(new[] { a, c, b, b, c, d });
            }

            Vector3 top = TrunkPoint(1f);
            const int fronds = 9, segs = 6;
            for (int f = 0; f < fronds; f++)
            {
                float yaw = f * Mathf.PI * 2f / fronds + 0.3f * Mathf.Sin(f * 3.1f);
                var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                var side = Vector3.Cross(Vector3.up, dir).normalized;
                float length = 3.2f + 0.6f * Mathf.Sin(f * 1.7f);
                int baseIndex = v.Count;
                for (int s = 0; s <= segs; s++)
                {
                    float t = (float)s / segs;
                    // Rises a little, then droops.
                    Vector3 p = top + dir * (t * length) + Vector3.up * (0.6f * t - 1.9f * t * t);
                    float width = Mathf.Sin(t * Mathf.PI) * 0.55f + 0.05f;
                    v.Add(p - side * width); n.Add(Vector3.up); uv.Add(new Vector2(0f, t));
                    v.Add(p + side * width); n.Add(Vector3.up); uv.Add(new Vector2(1f, t));
                }
                for (int s = 0; s < segs; s++)
                {
                    int a = baseIndex + s * 2, b = a + 1, c = a + 2, d = a + 3;
                    leaves.AddRange(new[] { a, c, b, b, c, d });
                }
            }

            var mesh = new Mesh { name = "Palm", subMeshCount = 2 };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(trunk, 0);
            mesh.SetTriangles(leaves, 1);
            mesh.RecalculateBounds();
            SaveMesh(mesh, ArtDir + "/Meshes/Palm.asset");

            Material bark = GetOrCreateMaterial(ArtDir + "/Materials/PalmTrunk.mat", "Universal Render Pipeline/Lit");
            bark.SetColor("_BaseColor", new Color(0.45f, 0.36f, 0.26f));
            bark.SetFloat("_Smoothness", 0.05f);
            EditorUtility.SetDirty(bark);
            Material leaf = GetOrCreateMaterial(ArtDir + "/Materials/PalmLeaves.mat", "Universal Render Pipeline/Lit");
            leaf.SetColor("_BaseColor", new Color(0.24f, 0.5f, 0.2f));
            leaf.SetFloat("_Smoothness", 0.25f);
            leaf.SetFloat("_Cull", (float)CullMode.Off); // fronds are single sheets
            leaf.doubleSidedGI = true;
            EditorUtility.SetDirty(leaf);
        }

        private static void BuildSkies()
        {
            Sky("Day", new Color(0.45f, 0.6f, 0.85f), 1.0f, 1.3f, 0.04f);
            Sky("Sunset", new Color(0.85f, 0.5f, 0.35f), 1.6f, 1.0f, 0.06f);
            Sky("Night", new Color(0.12f, 0.16f, 0.3f), 0.6f, 0.25f, 0.02f);
        }

        private static void Sky(string name, Color tint, float atmosphere, float exposure, float sunSize)
        {
            Material m = GetOrCreateMaterial($"{ResDir}/Sky_{name}.mat", "Skybox/Procedural");
            m.SetColor("_SkyTint", tint);
            m.SetColor("_GroundColor", new Color(0.75f, 0.68f, 0.55f));
            m.SetFloat("_AtmosphereThickness", atmosphere);
            m.SetFloat("_Exposure", exposure);
            m.SetFloat("_SunSize", sunSize);
            EditorUtility.SetDirty(m);
        }

        private static void BuildPostProfiles()
        {
            Post("Day", 0.1f, 12f, 8f, 0.25f, new Color(1f, 0.98f, 0.95f));
            Post("Sunset", -0.1f, 14f, 0f, 0.4f, new Color(1f, 0.93f, 0.85f));
            Post("Night", 0.3f, 10f, -10f, 0.6f, new Color(0.85f, 0.9f, 1f));
        }

        private static void Post(string name, float exposure, float contrast, float saturation, float bloom, Color filter)
        {
            string path = $"{ResDir}/Post_{name}.asset";
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.value = TonemappingMode.ACES;
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.value = exposure;
            color.contrast.value = contrast;
            color.saturation.value = saturation;
            color.colorFilter.value = filter;
            var b = profile.Add<Bloom>(true);
            b.threshold.value = 1.0f;
            b.intensity.value = bloom;
            b.scatter.value = 0.6f;
            b.highQualityFiltering.value = false; // mobile
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.value = 0.18f;

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
        }

        private static void BuildParticleMaterial()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
            tex.Apply();
            string path = SavePng(tex, ResDir + "/SandPuff.png", alpha: true);

            Material m = GetOrCreateMaterial(ResDir + "/SandParticle.mat", "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
            m.SetFloat("_Surface", 1f);   // transparent
            m.SetFloat("_Blend", 0f);     // alpha
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ scene decoration

        /// <summary>Applies the environment to the open scene (idempotent).</summary>
        public static void Decorate()
        {
            GameObject env = GameObject.Find("Environment");
            if (env == null) return;

            // Sand ground: from the west edge to the shore.
            Transform ground = env.transform.Find("Sand");
            if (ground != null)
            {
                ground.position = new Vector3((-40f + ShoreX) * 0.5f, 0f, 0f);
                ground.localScale = new Vector3((ShoreX + 40f) / 10f, 1f, 10f); // Unity plane = 10 m
                ground.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "/Materials/Sand.mat");
            }

            Replace(env.transform, "Sea", go =>
            {
                go.transform.position = new Vector3(ShoreX, -0.04f, 0f);
                go.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ArtDir + "/Meshes/Sea.asset");
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "/Materials/Sea.mat");
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            });

            Replace(env.transform, "Palms", root =>
            {
                Mesh palm = AssetDatabase.LoadAssetAtPath<Mesh>(ArtDir + "/Meshes/Palm.asset");
                var mats = new[]
                {
                    AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "/Materials/PalmTrunk.mat"),
                    AssetDatabase.LoadAssetAtPath<Material>(ArtDir + "/Materials/PalmLeaves.mat")
                };
                var rng = new System.Random(7);
                foreach (Vector3 p in PalmSpots(rng))
                {
                    var go = new GameObject("Palm");
                    go.transform.SetParent(root.transform, false);
                    go.transform.position = p;
                    go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    float s = 0.85f + (float)rng.NextDouble() * 0.4f;
                    go.transform.localScale = new Vector3(s, s, s);
                    go.AddComponent<MeshFilter>().sharedMesh = palm;
                    go.AddComponent<MeshRenderer>().sharedMaterials = mats;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                }
            });
        }

        private static IEnumerable<Vector3> PalmSpots(System.Random rng)
        {
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            for (int i = 0; i < 7; i++) yield return new Vector3(R(-24f, -13f), 0f, R(-22f, 22f));   // west side
            for (int i = 0; i < 4; i++) yield return new Vector3(R(-12f, 14f), 0f, R(24f, 32f));    // behind north
            for (int i = 0; i < 4; i++) yield return new Vector3(R(-12f, 14f), 0f, R(-32f, -24f));  // behind south
            for (int i = 0; i < 3; i++) yield return new Vector3(R(11f, 16f), 0f, R(-18f, 18f));     // by the shore
        }

        private static void Replace(Transform parent, string name, System.Action<GameObject> build)
        {
            Transform old = parent.Find(name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            build(go);
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Periodic value-noise fBm on the unit square (tiles seamlessly).</summary>
        private static float Fbm(float u, float v, int baseFreq, int octaves, float gain, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * ValueNoise(u * freq, v * freq, freq, seed + o * 31);
                norm += amp;
                amp *= gain;
                freq *= 2;
            }
            return sum / norm;
        }

        private static float ValueNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Hash(Mod(x0, period), Mod(y0, period), seed), b = Hash(Mod(x0 + 1, period), Mod(y0, period), seed);
            float c = Hash(Mod(x0, period), Mod(y0 + 1, period), seed), d = Hash(Mod(x0 + 1, period), Mod(y0 + 1, period), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 362437);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        private static string SavePng(Texture2D tex, string path, bool isNormal = false, bool alpha = false)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = alpha;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();
            return path;
        }

        private static void SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }
        }

        private static Material GetOrCreateMaterial(string path, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception("Shader not found: " + shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader)
            {
                m.shader = shader;
            }
            return m;
        }
    }
}
