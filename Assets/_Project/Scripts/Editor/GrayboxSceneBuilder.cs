using System.IO;
using BeachTennis.Game;
using BeachTennis.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BeachTennis.EditorTools
{
    /// <summary>
    /// Generates the Boot and gray-box Match scenes from <see cref="CourtDimensions"/>, so the court
    /// geometry always matches the simulation's numbers. Re-running overwrites both scenes.
    /// </summary>
    public static class GrayboxSceneBuilder
    {
        public const string ScenesDir = "Assets/_Project/Scenes";
        public const string BootScenePath = ScenesDir + "/Boot.unity";
        public const string MatchScenePath = ScenesDir + "/Match.unity";
        public const string SandboxScenePath = ScenesDir + "/Sandbox_BallMachine.unity";
        public const string TrainingScenePath = ScenesDir + "/Training.unity";
        private const string MaterialsDir = "Assets/_Project/Art/Materials/Graybox";
        private const string PhysicsSettingsDir = "Assets/_Project/Settings/Physics";

        // Same framing the match uses at runtime (Game.Match.MatchMode.PlaceCamera).
        private const float MatchCameraHeight = 6.2f;
        private const float MatchCameraBack = 6.0f;
        private const float MatchCameraLookAhead = 2.5f;

        /// <summary>Scene the Boot scene opens. Phase 3 = match menu (singles / doubles / training).</summary>
        public const string FirstSceneName = "Match";

        [MenuItem("Beach Tennis/Setup/Build Graybox Scenes")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(ScenesDir);
            Directory.CreateDirectory(MaterialsDir);

            var court = CourtDimensions.For(CourtFormat.Doubles);
            BuildBootScene();
            BuildMatchScene(court);
            BuildSandboxScene(court);
            BuildTrainingScene(court);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(MatchScenePath, true),
                new EditorBuildSettingsScene(TrainingScenePath, true),
                new EditorBuildSettingsScene(SandboxScenePath, true)
            };

            AssetDatabase.SaveAssets();
            Debug.Log("[BeachTennis] Graybox scenes built.");
        }

        private static void BuildBootScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var boot = new GameObject("Boot");
            var loader = boot.AddComponent<BootLoader>();
            var so = new SerializedObject(loader);
            so.FindProperty("firstScene").stringValue = FirstSceneName;
            so.ApplyModifiedPropertiesWithoutUndo();

            // A camera so the boot frame clears to black instead of showing garbage.
            var cam = new GameObject("Boot Camera").AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;

            EditorSceneManager.SaveScene(scene, BootScenePath);
        }

        private static void BuildMatchScene(CourtDimensions court)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var courtView = BuildCourt(court, southReferencePlayers: false, northReferencePlayers: false);
            BallRig rig = BuildBallRig();

            var views = new[]
            {
                BuildPlayerRig("Team0_Player0 (human)", GetMaterial("PlayerA", new Color(0.90f, 0.30f, 0.20f), 0.3f)),
                BuildPlayerRig("Team0_Player1", GetMaterial("PlayerA2", new Color(0.95f, 0.55f, 0.25f), 0.3f)),
                BuildPlayerRig("Team1_Player0", GetMaterial("PlayerB", new Color(0.20f, 0.55f, 0.90f), 0.3f)),
                BuildPlayerRig("Team1_Player1", GetMaterial("PlayerB2", new Color(0.35f, 0.75f, 0.95f), 0.3f)),
            };

            var controls = new GameObject("TouchControls").AddComponent<Input.TouchControls>();
            var match = new GameObject("MatchMode").AddComponent<Game.Match.MatchMode>();
            var so = new SerializedObject(match);
            so.FindProperty("physics").objectReferenceValue = GetOrCreateSettings<Game.Config.BallPhysicsSettings>("BallPhysics");
            so.FindProperty("playerSettings").objectReferenceValue = GetOrCreateSettings<Game.Config.PlayerSettings>("Player");
            so.FindProperty("ballView").objectReferenceValue = rig.View;
            so.FindProperty("trajectory").objectReferenceValue = rig.Trajectory;
            so.FindProperty("markers").objectReferenceValue = rig.Markers;
            so.FindProperty("courtView").objectReferenceValue = courtView;
            so.FindProperty("controls").objectReferenceValue = controls;
            var arr = so.FindProperty("playerViews");
            arr.arraySize = views.Length;
            for (int i = 0; i < views.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = views[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, MatchScenePath);
        }

        private struct BallRig
        {
            public Presentation.Ball.BallView View;
            public Presentation.Ball.TrajectoryView Trajectory;
            public Presentation.Ball.ImpactMarkers Markers;
        }

        private static BallRig BuildBallRig()
        {
            // Fluorescent yellow with a little emission: stands out against the sand and the sky.
            var ballMat = GetMaterial("Ball", new Color(0.95f, 1.0f, 0.15f), 0.35f);
            ballMat.EnableKeyword("_EMISSION");
            ballMat.SetColor("_EmissionColor", new Color(0.55f, 0.6f, 0.05f));
            ballMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(ballMat);
            var ballRoot = new GameObject("Ball").transform;

            var ball = Primitive(PrimitiveType.Sphere, "BallVisual", ballRoot, ballMat);
            float d = 2f * Simulation.Ball.BallPhysicsConfig.Default.Radius;
            ball.transform.localScale = new Vector3(d, d, d);
            GameObjectUtility.SetStaticEditorFlags(ball, 0); // dynamic
            ball.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off; // contact shadow sprite does the job

            var trajectoryGo = new GameObject("Trajectory", typeof(LineRenderer));
            trajectoryGo.transform.SetParent(ballRoot, false);

            var markersGo = new GameObject("ImpactMarkers");
            markersGo.transform.SetParent(ballRoot, false);

            return new BallRig
            {
                View = ball.AddComponent<Presentation.Ball.BallView>(),
                Trajectory = trajectoryGo.AddComponent<Presentation.Ball.TrajectoryView>(),
                Markers = markersGo.AddComponent<Presentation.Ball.ImpactMarkers>()
            };
        }

        private static void BuildSandboxScene(CourtDimensions court)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildCourt(court);
            BallRig rig = BuildBallRig();

            var sandboxGo = new GameObject("BallMachineSandbox");
            var sandbox = sandboxGo.AddComponent<Game.Sandbox.BallMachineSandbox>();
            var so = new SerializedObject(sandbox);
            so.FindProperty("physics").objectReferenceValue = GetOrCreateSettings<Game.Config.BallPhysicsSettings>("BallPhysics");
            so.FindProperty("ballView").objectReferenceValue = rig.View;
            so.FindProperty("trajectory").objectReferenceValue = rig.Trajectory;
            so.FindProperty("markers").objectReferenceValue = rig.Markers;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, SandboxScenePath);
        }

        private static void BuildTrainingScene(CourtDimensions court)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildCourt(court, southReferencePlayers: false);
            BallRig rig = BuildBallRig();

            var playerView = BuildPlayerRig("Player", GetMaterial("PlayerA", new Color(0.90f, 0.30f, 0.20f), 0.3f));

            var controls = new GameObject("TouchControls").AddComponent<Input.TouchControls>();

            var training = new GameObject("TrainingMode").AddComponent<Game.Training.TrainingMode>();
            var so = new SerializedObject(training);
            so.FindProperty("physics").objectReferenceValue = GetOrCreateSettings<Game.Config.BallPhysicsSettings>("BallPhysics");
            so.FindProperty("playerSettings").objectReferenceValue = GetOrCreateSettings<Game.Config.PlayerSettings>("Player");
            so.FindProperty("ballView").objectReferenceValue = rig.View;
            so.FindProperty("trajectory").objectReferenceValue = rig.Trajectory;
            so.FindProperty("markers").objectReferenceValue = rig.Markers;
            so.FindProperty("playerView").objectReferenceValue = playerView;
            so.FindProperty("controls").objectReferenceValue = controls;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, TrainingScenePath);
        }

        private static T GetOrCreateSettings<T>(string name) where T : ScriptableObject
        {
            Directory.CreateDirectory(PhysicsSettingsDir);
            string path = $"{PhysicsSettingsDir}/{name}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }
            return asset;
        }

        private static Presentation.CourtView BuildCourt(CourtDimensions court, bool southReferencePlayers = true, bool northReferencePlayers = true)
        {
            var sand = GetMaterial("Sand", new Color(0.86f, 0.76f, 0.58f), 0.15f);
            var line = GetMaterial("Line", new Color(0.10f, 0.35f, 0.85f), 0.3f);
            var net = GetMaterial("Net", new Color(0.12f, 0.12f, 0.12f), 0.2f);
            var post = GetMaterial("Post", new Color(0.85f, 0.85f, 0.85f), 0.5f);
            var playerA = GetMaterial("PlayerA", new Color(0.90f, 0.30f, 0.20f), 0.3f);
            var playerB = GetMaterial("PlayerB", new Color(0.20f, 0.55f, 0.90f), 0.3f);

            var env = new GameObject("Environment").transform;
            var courtRoot = new GameObject("Court").transform;

            // Lighting: one real-time sun (docs/01 §4.2).
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(env);
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.6f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.70f, 0.90f);
            RenderSettings.ambientEquatorColor = new Color(0.75f, 0.72f, 0.65f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.40f, 0.32f);

            // Sand around the court.
            var ground = Primitive(PrimitiveType.Plane, "Sand", env, sand);
            ground.transform.localScale = new Vector3(6f, 1f, 6f); // 60 x 60 m

            // Lines, net and posts are placed by CourtView (singles/doubles width, net height by category),
            // so they are dynamic objects, not static-batched.
            var lineSouth = Box("Baseline_South", courtRoot, line, Vector3.zero, Vector3.one);
            var lineNorth = Box("Baseline_North", courtRoot, line, Vector3.zero, Vector3.one);
            var lineWest = Box("Sideline_West", courtRoot, line, Vector3.zero, Vector3.one);
            var lineEast = Box("Sideline_East", courtRoot, line, Vector3.zero, Vector3.one);
            var netBody = Box("Net", courtRoot, net, Vector3.zero, Vector3.one);
            var netTape = Box("NetTape", courtRoot, post, Vector3.zero, Vector3.one);
            var postWest = Primitive(PrimitiveType.Cylinder, "Post_West", courtRoot, post).transform;
            var postEast = Primitive(PrimitiveType.Cylinder, "Post_East", courtRoot, post).transform;
            foreach (var t in new[] { lineSouth, lineNorth, lineWest, lineEast, netBody, netTape, postWest, postEast })
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.ContributeGI);

            var courtView = courtRoot.gameObject.AddComponent<Presentation.CourtView>();
            var cv = new SerializedObject(courtView);
            cv.FindProperty("sidelineWest").objectReferenceValue = lineWest;
            cv.FindProperty("sidelineEast").objectReferenceValue = lineEast;
            cv.FindProperty("baselineSouth").objectReferenceValue = lineSouth;
            cv.FindProperty("baselineNorth").objectReferenceValue = lineNorth;
            cv.FindProperty("net").objectReferenceValue = netBody;
            cv.FindProperty("netTape").objectReferenceValue = netTape;
            cv.FindProperty("postWest").objectReferenceValue = postWest;
            cv.FindProperty("postEast").objectReferenceValue = postEast;
            cv.ApplyModifiedPropertiesWithoutUndo();
            courtView.Apply(court);

            // Scale references: 1.80 m capsules in doubles positions.
            var players = new GameObject("Players (scale reference)").transform;
            float px = court.Width * 0.25f;
            if (southReferencePlayers)
            {
                Capsule("Player_S1", players, playerA, new Vector3(-px, 0, -court.HalfLength + 2.5f));
                Capsule("Player_S2", players, playerA, new Vector3(px, 0, -court.HalfLength + 5.5f));
            }
            if (northReferencePlayers)
            {
                Capsule("Player_N1", players, playerB, new Vector3(-px, 0, court.HalfLength - 2.5f));
                Capsule("Player_N2", players, playerB, new Vector3(px, 0, court.HalfLength - 5.5f));
            }

            // Broadcast-style camera behind the south baseline (Cinemachine replaces this in Phase 6).
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 200f;
            camGo.transform.position = new Vector3(0f, MatchCameraHeight, -court.HalfLength - MatchCameraBack);
            camGo.transform.LookAt(new Vector3(0f, 0f, MatchCameraLookAhead));
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            return courtView;
        }

        private static void Capsule(string name, Transform parent, Material mat, Vector3 feet)
        {
            var c = Primitive(PrimitiveType.Capsule, name, parent, mat);
            const float height = 1.8f;
            c.transform.position = feet + Vector3.up * (height * 0.5f);
            c.transform.localScale = new Vector3(0.5f, height * 0.5f, 0.5f);
        }

        private static Transform Box(string name, Transform parent, Material mat, Vector3 center, Vector3 size)
        {
            var b = Primitive(PrimitiveType.Cube, name, parent, mat);
            b.transform.position = center;
            b.transform.localScale = size;
            return b.transform;
        }

        /// <summary>
        /// Gray-box player: root at the feet (PlayerView), a body pivot that leans on dives, a racket pivot.
        /// </summary>
        private static Presentation.Players.PlayerView BuildPlayerRig(string name, Material bodyMat)
        {
            var racketMat = GetMaterial("Racket", new Color(0.15f, 0.15f, 0.18f), 0.5f);
            var playerRoot = new GameObject(name).transform;
            var bodyPivot = new GameObject("BodyPivot").transform;
            bodyPivot.SetParent(playerRoot, false);
            var capsule = Primitive(PrimitiveType.Capsule, "Body", bodyPivot, bodyMat);
            capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            capsule.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
            GameObjectUtility.SetStaticEditorFlags(capsule, 0);
            var racketPivot = new GameObject("RacketPivot").transform;
            racketPivot.SetParent(bodyPivot, false);
            racketPivot.localPosition = new Vector3(0.3f, 1.1f, 0f);
            var racket = Primitive(PrimitiveType.Cube, "Racket", racketPivot, racketMat);
            racket.transform.localPosition = new Vector3(0.32f, 0f, 0f);
            racket.transform.localScale = new Vector3(0.5f, 0.28f, 0.03f);
            GameObjectUtility.SetStaticEditorFlags(racket, 0);
            var view = playerRoot.gameObject.AddComponent<Presentation.Players.PlayerView>();
            var so = new SerializedObject(view);
            so.FindProperty("body").objectReferenceValue = bodyPivot;
            so.FindProperty("racketPivot").objectReferenceValue = racketPivot;
            so.ApplyModifiedPropertiesWithoutUndo();
            return view;
        }

        private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            // Gameplay never uses PhysX colliders (docs/01 §2.3): strip them.
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
            return go;
        }

        private static Material GetMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialsDir}/M_Graybox_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
