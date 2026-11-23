using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace BeachTennis.EditorTools
{
    /// <summary>
    /// Imports the Mixamo clips as Humanoid (sharing the X Bot avatar) and builds the X Bot animator:
    /// a 2D locomotion blend tree driven by the simulated velocity, plus dive → get up and the
    /// victory / disappointed reactions. Root motion stays off: the simulation owns the position.
    /// Shots are still procedural + IK on top (no tennis clips yet), applied by HumanoidPlayerPose.
    /// Re-runnable: overwrites the controller and the import settings.
    /// </summary>
    public static class XBotAnimationSetup
    {
        public const string Folder = "Assets/_Project/Art/Characters/XBot/Animations";
        public const string ControllerPath = "Assets/_Project/Art/Characters/XBot/XBot.controller";
        private const string ModelPath = "Assets/_Project/Art/Characters/XBot/XBot.fbx";
        private const string PrefabPath = "Assets/_Project/Art/Characters/XBot/XBot.prefab";

        // Animator parameter names (shared with PlayerView).
        public const string Forward = "Forward";
        public const string Strafe = "Strafe";
        public const string Dive = "Dive";
        public const string DiveMirror = "DiveMirror";
        public const string Victory = "Victory";
        public const string Disappointed = "Disappointed";
        public const string LocomotionState = "Locomotion";

        /// <summary>Seconds the dive and the get-up take on screen; together they match PlayerConfig.DiveRecovery.</summary>
        public const float DiveSeconds = 0.55f;
        public const float GetUpSeconds = 0.75f;

        private static readonly string[] Looping = { "Idle", "Run", "RunBackward", "StrafeLeft", "StrafeRight" };

        [MenuItem("Beach Tennis/Setup/X Bot Animations")]
        public static void Apply()
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new System.Exception("X Bot avatar missing or not humanoid; run X Bot setup first.");

            foreach (string file in Directory.GetFiles(Folder, "*.fbx"))
                ConfigureClipImport(file.Replace('\\', '/'), avatar);

            AnimatorController controller = BuildController();
            AssignToPrefab(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("[XBot] Animator built: locomotion blend tree, dive/get up, reactions.");
        }

        private static void ConfigureClipImport(string path, Avatar avatar)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            string name = Path.GetFileNameWithoutExtension(path);
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.animationCompression = ModelImporterAnimationCompression.Optimal;

            bool loop = Looping.Contains(name);
            ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
            for (int i = 0; i < clips.Length; i++)
            {
                clips[i].name = name;
                clips[i].loopTime = loop;
                clips[i].loopPose = loop;
                // Body stays over the simulated position: bake root rotation and XZ into the pose so
                // clips downloaded without "In Place" do not drift away from the root.
                clips[i].lockRootRotation = true;
                clips[i].keepOriginalOrientation = true;
                clips[i].lockRootHeightY = true;
                clips[i].keepOriginalPositionY = true;
                clips[i].lockRootPositionXZ = loop;
                clips[i].keepOriginalPositionXZ = true;
            }
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static AnimationClip Clip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath($"{Folder}/{name}.fbx").OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        private static AnimatorController BuildController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            controller.AddParameter(Forward, AnimatorControllerParameterType.Float);
            controller.AddParameter(Strafe, AnimatorControllerParameterType.Float);
            controller.AddParameter(Dive, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(DiveMirror, AnimatorControllerParameterType.Bool);
            controller.AddParameter(Victory, AnimatorControllerParameterType.Trigger);
            controller.AddParameter(Disappointed, AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine sm = controller.layers[0].stateMachine;

            // Locomotion: local velocity in m/s (x = strafe right, y = forward), from the simulation.
            AnimatorState locomotion = controller.CreateBlendTreeInController(LocomotionState, out BlendTree tree, 0);
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = Strafe;
            tree.blendParameterY = Forward;
            tree.AddChild(Clip("Idle"), new Vector2(0f, 0f));
            tree.AddChild(Clip("Run"), new Vector2(0f, 5f));
            tree.AddChild(Clip("RunBackward"), new Vector2(0f, -4f));
            tree.AddChild(Clip("StrafeLeft"), new Vector2(-4f, 0f));
            tree.AddChild(Clip("StrafeRight"), new Vector2(4f, 0f));
            sm.defaultState = locomotion;

            AnimationClip diveClip = Clip("Dive");
            AnimationClip getUpClip = Clip("GetUp");
            AnimatorState dive = sm.AddState("Dive");
            dive.motion = diveClip;
            dive.mirrorParameterActive = true;
            dive.mirrorParameter = DiveMirror;
            dive.speed = diveClip.length / DiveSeconds;
            AnimatorState getUp = sm.AddState("GetUp");
            getUp.motion = getUpClip;
            getUp.mirrorParameterActive = true;
            getUp.mirrorParameter = DiveMirror;
            getUp.speed = getUpClip.length / GetUpSeconds;

            AnimatorStateTransition toDive = sm.AddAnyStateTransition(dive);
            toDive.AddCondition(AnimatorConditionMode.If, 0f, Dive);
            toDive.duration = 0.05f;
            toDive.canTransitionToSelf = false;
            AnimatorStateTransition diveToGetUp = dive.AddTransition(getUp);
            diveToGetUp.hasExitTime = true;
            diveToGetUp.exitTime = 0.95f;
            diveToGetUp.duration = 0.08f;
            AnimatorStateTransition getUpToLoco = getUp.AddTransition(locomotion);
            getUpToLoco.hasExitTime = true;
            getUpToLoco.exitTime = 0.9f;
            getUpToLoco.duration = 0.15f;

            AddReaction(sm, locomotion, "Victory", Clip("Victory"), Victory);
            AddReaction(sm, locomotion, "Disappointed", Clip("Disappointed"), Disappointed);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void AddReaction(AnimatorStateMachine sm, AnimatorState locomotion, string name, AnimationClip clip, string trigger)
        {
            AnimatorState state = sm.AddState(name);
            state.motion = clip;
            AnimatorStateTransition enter = sm.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);
            enter.duration = 0.15f;
            enter.canTransitionToSelf = false;
            AnimatorStateTransition exit = state.AddTransition(locomotion);
            exit.hasExitTime = true;
            exit.exitTime = 0.9f;
            exit.duration = 0.2f;
        }

        private static void AssignToPrefab(AnimatorController controller)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
                {
                    a.runtimeAnimatorController = controller;
                    a.applyRootMotion = false;
                    a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                    a.enabled = true;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
