using System;
using BeachTennis.Presentation.Players;
using BeachTennis.Presentation;
using BeachTennis.Simulation.Players;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BeachTennis.Tests
{
    public class ShotAnimationTests
    {
        [TestCase(0.05)]
        [TestCase(0.14)]
        [TestCase(0.4)]
        public void DifferentReleaseTimesReachContactAtTheSameInstant(double lead)
        {
            Assert.That(ShotContactTiming.Phase(10, 10 - lead, 10),
                Is.EqualTo(ShotContactTiming.ContactPhase).Within(0.00001));
        }

        [Test]
        public void FollowThroughFinishesAndDoesNotOvershoot()
        {
            Assert.That(ShotContactTiming.Phase(11, 9.8, 10), Is.EqualTo(1f));
            Assert.That(ShotContactTiming.Phase(9, 9.8, 10), Is.EqualTo(0f));
            Assert.That(ShotContactTiming.Phase(10, 10, 10), Is.EqualTo(ShotContactTiming.ContactPhase));
        }

        [Test]
        public void ContactBlendApproachesSmoothlyAndReleases()
        {
            Assert.That(ShotContactTiming.ContactWeight(9, 10), Is.Zero);
            Assert.That(ShotContactTiming.ContactWeight(9.93, 10), Is.InRange(0.49f, 0.51f));
            Assert.That(ShotContactTiming.ContactWeight(10, 10), Is.EqualTo(1f));
            Assert.That(ShotContactTiming.ContactWeight(10.11, 10), Is.InRange(0.49f, 0.51f));
            Assert.That(ShotContactTiming.ContactWeight(11, 10), Is.Zero);
        }

        [Test]
        public void ShotSelectionDistinguishesServeSmashAndBackhand()
        {
            Assert.That(ShotAnimationSelector.Select(ShotType.Main, true, true, false), Is.EqualTo(ShotAnimation.Serve));
            Assert.That(ShotAnimationSelector.Select(ShotType.Main, false, true, false), Is.EqualTo(ShotAnimation.Smash));
            Assert.That(ShotAnimationSelector.Select(ShotType.Main, false, false, true), Is.EqualTo(ShotAnimation.Backhand));
            Assert.That(ShotAnimationSelector.Select(ShotType.Lob, false, false, false), Is.EqualTo(ShotAnimation.Lob));
            Assert.That(ShotAnimationSelector.Select(ShotType.Drop, false, false, false), Is.EqualTo(ShotAnimation.Drop));
        }

        [Test]
        public void AvatarConvergesBeforeHitAndCancelsInvalidPlan()
        {
            WithPose((pose, root, racket) =>
            {
                var state = PlannedState();
                pose.Synchronize(state, true, 0.95, ShotAnimation.Forehand);
                pose.Update(state, true, 1f / 60f, 0.95);
                Assert.That(pose.ContactBlend, Is.GreaterThan(0.6f), "Prediction must act before the Hit event.");
                state.Plan.Valid = false;
                pose.Synchronize(state, true, 0.96, ShotAnimation.Forehand);
                pose.Update(state, true, 1f / 60f, 0.96);
                Assert.That(pose.ContactBlend, Is.Zero, "Cancelled plans must not attract the racket.");
            });
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(120)]
        public void AvatarReachesContactWithoutAHitEventAtDifferentFrameRates(int fps)
        {
            WithPose((pose, root, racket) =>
            {
                var state = PlannedState();
                for (int frame = 0; frame <= fps; frame++)
                {
                    double now = frame / (double)fps;
                    pose.Synchronize(state, true, now, ShotAnimation.Forehand);
                    pose.Update(state, true, 1f / fps, now);
                }
                Vector3 face = racket.TransformPoint(new Vector3(0.32f, 0, 0));
                Assert.That(Vector3.Distance(face, new Vector3(0.5f, 1.3f, 0.45f)), Is.LessThan(0.02f));
                var before = racket.position;
                // Same simulation time represents pause, regardless of wall-clock frame interval.
                pose.Synchronize(state, true, 1.0, ShotAnimation.Forehand);
                pose.Update(state, true, 0f, 1.0);
                Assert.That(Vector3.Distance(before, racket.position), Is.LessThan(0.00001f));
            });
        }

        [TestCase("Assets/_Project/Scenes/Match.unity")]
        [TestCase("Assets/_Project/Scenes/Training.unity")]
        public void SavedPlayerModelsAreCenteredOnSimulationRoot(string path)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                int count = 0;
                foreach (var go in scene.GetRootGameObjects())
                    foreach (var view in go.GetComponentsInChildren<PlayerView>(true))
                    {
                        var model = view.transform.Find("BodyPivot/XBot");
                        Assert.That(model, Is.Not.Null);
                        Assert.That(model.localPosition.sqrMagnitude, Is.LessThan(0.000001f),
                            "A displaced skeleton makes leg IK stretch towards the wrong ground origin.");
                        count++;
                    }
                Assert.That(count, Is.GreaterThan(0));
            }
            finally { UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true); }
        }

        [Test]
        public void NeutralPoseKeepsFeetBelowHipsAfterRootMovementAndTurn()
        {
            WithPose((pose, root, racket) =>
            {
                root.position = new Vector3(0.31f, 0, -4.31f);
                root.rotation = Quaternion.Euler(0, 180, 0);
                var state = new PlayerState { Side = 1, SwingTime = double.NaN };
                pose.Update(state, false, 1f / 60f, 0);
                var animator = root.GetComponentInChildren<Animator>(true);
                foreach (var bone in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    Vector3 foot = root.InverseTransformPoint(animator.GetBoneTransform(bone).position);
                    Assert.That(foot.y, Is.InRange(0.05f, 0.25f));
                    Assert.That(Mathf.Abs(foot.z), Is.LessThan(0.15f));
                }
            });
        }

        [Test]
        public void GripStaysInsidePalmAcrossTheSwing()
        {
            WithPose((pose, root, racket) =>
            {
                var state = PlannedState();
                pose.Update(state, false, 0, 0);
                Vector3 socket = racket.localPosition;
                Quaternion rotation = racket.localRotation;
                Assert.That(socket.magnitude, Is.InRange(0.03f, 0.10f), "Grip belongs in the palm, beyond the wrist.");
                for (int frame = 0; frame <= 60; frame++)
                {
                    double now = frame / 60.0;
                    pose.Synchronize(state, true, now, ShotAnimation.Forehand);
                    pose.Update(state, true, 1f / 60f, now);
                    Assert.That(Vector3.Distance(socket, racket.localPosition), Is.LessThan(0.0001f));
                    Assert.That(Quaternion.Angle(rotation, racket.localRotation), Is.LessThan(0.1f));
                }
            });
        }
        [Test]
        public void GazeIsLimitedPausesAndReturnsToNeutral()
        {
            WithPose((pose, root, racket) =>
            {
                var state = new PlayerState { Side = -1, SwingTime = double.NaN };
                var head = root.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.Head);
                Quaternion neutral = head.localRotation;
                pose.SetLookTarget(root.TransformPoint(new Vector3(50, 15, 3)));
                for (int i = 0; i < 60; i++) pose.Update(state, false, 1f / 60f);
                Assert.That(pose.GazeYaw, Is.InRange(60f, 65.01f));
                Assert.That(Mathf.Abs(pose.GazePitch), Is.LessThanOrEqualTo(30.01f));
                Assert.That(Quaternion.Angle(neutral, head.localRotation), Is.GreaterThan(10f));
                float yaw = pose.GazeYaw;
                pose.SetLookTarget(root.TransformPoint(new Vector3(-50, 0, 3)));
                pose.Update(state, false, 0f);
                Assert.That(pose.GazeYaw, Is.EqualTo(yaw));
                pose.SetLookTarget(null);
                for (int i = 0; i < 60; i++) pose.Update(state, false, 1f / 60f);
                Assert.That(Mathf.Abs(pose.GazeYaw), Is.LessThan(0.01f));
                Assert.That(Mathf.Abs(pose.GazePitch), Is.LessThan(0.01f));
            });
        }

        [Test]
        public void GazeDoesNotTurnTowardsBallBehindBody()
        {
            WithPose((pose, root, racket) =>
            {
                var state = new PlayerState { Side = -1, SwingTime = double.NaN };
                pose.SetLookTarget(root.TransformPoint(new Vector3(2, 2, -3)));
                for (int i = 0; i < 60; i++) pose.Update(state, false, 1f / 60f);
                Assert.That(pose.GazeYaw, Is.Zero);
                Assert.That(pose.GazePitch, Is.Zero);
            });
        }

        [Test]
        public void AnimationLodPreservesNearbyAndCriticalUpdates()
        {
            Assert.That(AnimationLod.ShouldUpdate(1.0 / 60, 0, 100, 0, false), Is.True);
            Assert.That(AnimationLod.ShouldUpdate(1.0 / 60, 0, 900, 0, false), Is.False);
            Assert.That(AnimationLod.ShouldUpdate(1.0 / 15, 0, 900, 0, false), Is.True);
            Assert.That(AnimationLod.ShouldUpdate(0.001, 0, 900, 0, true), Is.True);
            Assert.That(AnimationLod.ShouldUpdate(1, 1, 100, 0, false), Is.False);
            Assert.That(AnimationLod.ShouldUpdate(0, -1, 900, 0, false), Is.True);
        }

        [Test]
        public void AnimationLodUsesThirtyHertzForMediumDistance()
        {
            Assert.That(AnimationLod.Interval(225, 0), Is.EqualTo(1.0 / 30));
            Assert.That(AnimationLod.Interval(225, 3), Is.Zero);
            Assert.That(AnimationLod.Interval(625, 3), Is.EqualTo(1.0 / 30));
            Assert.That(AnimationLod.Interval(900, 3), Is.EqualTo(1.0 / 15));
        }
        static PlayerState PlannedState() => new PlayerState
        {
            Side = -1, SwingTime = double.NaN,
            Plan = new ContactPlan { Valid = true, Reachable = true, Time = 1,
                BallPosition = new BeachTennis.Core.Vec3(0.5f, 1.3f, 0.45f) }
        };

        static void WithPose(Action<HumanoidPlayerPose, Transform, Transform> test)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Art/Characters/XBot/XBot.prefab");
            Assert.That(prefab, Is.Not.Null);
            var root = new GameObject("PoseTest");
            try
            {
                var model = Object.Instantiate(prefab, root.transform);
                var racket = new GameObject("Racket").transform;
                racket.SetParent(root.transform, false);
                var pose = new HumanoidPlayerPose(root.transform, model.GetComponentInChildren<Animator>(true), racket);
                Assert.That(pose.Ready, Is.True);
                test(pose, root.transform, racket);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}






