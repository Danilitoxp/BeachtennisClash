using System;
using BeachTennis.Simulation.Players;
using UnityEngine;

namespace BeachTennis.Presentation.Players
{
    public enum ShotAnimation { Forehand, Backhand, Lob, Drop, Smash, Serve }
    public static class ShotAnimationSelector
    {
        public static ShotAnimation Select(ShotType type, bool serving, bool smash, bool backhand)
        {
            if (serving) return ShotAnimation.Serve;
            if (smash) return ShotAnimation.Smash;
            if (type == ShotType.Lob) return ShotAnimation.Lob;
            if (type == ShotType.Drop) return ShotAnimation.Drop;
            return backhand ? ShotAnimation.Backhand : ShotAnimation.Forehand;
        }
    }

    // Temporary procedural animation until authored sports clips are available.
    // Presentation only: never changes simulated root movement or ball contact.
    public sealed class HumanoidPlayerPose
    {
        readonly Transform root, hand, racket;
        readonly Quaternion handBasis;
        readonly Vector3 palmOffset;
        readonly Transform[] gripFingers;
        readonly Vector3[] fingerAxes;
        readonly Transform[] bones;
        readonly Quaternion[] rest;
        readonly Limb leftLeg, rightLeg, leftArm, rightArm;
        Vector3? lookTarget;
        float gazeYaw, gazePitch;
        public float GazeYaw => gazeYaw;
        public float GazePitch => gazePitch;
        public bool ContactActive(double now) => tracking && now <= contactAt + ShotContactTiming.FollowThroughSeconds;
        public void SetLookTarget(Vector3? target) => lookTarget = target;
        float stride, swing = 1f;
        double swingStart, contactAt, lastTime;
        bool tracking, confirmedContact;
        public float ContactBlend { get; private set; }
        Vector3 contact;
        ShotAnimation shot;
        public bool Ready { get; }
        public ShotAnimation CurrentShot => shot;

        public HumanoidPlayerPose(Transform root, Animator animator, Transform racket)
        {
            this.root = root; this.racket = racket;
            if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return;
            bones = new Transform[(int)HumanBodyBones.LastBone];
            rest = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                bones[i] = animator.GetBoneTransform((HumanBodyBones)i);
                if (bones[i] != null) rest[i] = bones[i].localRotation;
            }
            hand = Bone(HumanBodyBones.RightHand);
            // Calibrate a palm socket from the avatar anatomy, rather than use the wrist as the grip.
            var middle = Bone(HumanBodyBones.RightMiddleProximal);
            var index = Bone(HumanBodyBones.RightIndexProximal);
            var little = Bone(HumanBodyBones.RightLittleProximal);
            if (hand != null && middle != null && index != null && little != null)
            {
                Vector3 fingers = (middle.position - hand.position).normalized;
                Vector3 normal = Vector3.Cross(index.position - little.position, fingers).normalized;
                handBasis = Quaternion.Inverse(hand.rotation) * Quaternion.LookRotation(fingers, normal);
                palmOffset = Quaternion.Inverse(hand.rotation) * ((middle.position - hand.position) * 0.65f + normal * 0.012f);
                var ids = new[] {
                    HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
                    HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal };
                gripFingers = new Transform[ids.Length];
                fingerAxes = new Vector3[ids.Length];
                Vector3 curlAxis = Vector3.Cross(fingers, normal).normalized;
                for (int i = 0; i < ids.Length; i++)
                {
                    gripFingers[i] = Bone(ids[i]);
                    if (gripFingers[i] != null) fingerAxes[i] = gripFingers[i].InverseTransformDirection(curlAxis);
                }
            }
            else handBasis = Quaternion.identity;
            leftLeg = new Limb(Bone(HumanBodyBones.LeftUpperLeg), Bone(HumanBodyBones.LeftLowerLeg), Bone(HumanBodyBones.LeftFoot));
            rightLeg = new Limb(Bone(HumanBodyBones.RightUpperLeg), Bone(HumanBodyBones.RightLowerLeg), Bone(HumanBodyBones.RightFoot));
            leftArm = new Limb(Bone(HumanBodyBones.LeftUpperArm), Bone(HumanBodyBones.LeftLowerArm), Bone(HumanBodyBones.LeftHand));
            rightArm = new Limb(Bone(HumanBodyBones.RightUpperArm), Bone(HumanBodyBones.RightLowerArm), hand);
            Ready = hand != null && leftLeg.Valid && rightLeg.Valid && leftArm.Valid && rightArm.Valid;
            if (!Ready) return;
            animator.enabled = false;
            if (racket != null)
            {
                racket.SetParent(hand, true);
                var scale = hand.lossyScale;
                racket.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            }
        }

        Transform Bone(HumanBodyBones bone) => bones[(int)bone];
        public void Reset()
        {
            tracking = false; confirmedContact = false; swing = 1f;
            stride = 0f; ContactBlend = 0f; lastTime = 0;
            lookTarget = null; gazeYaw = 0; gazePitch = 0;
            shot = ShotAnimation.Forehand;
        }

        public void Swing(ShotAnimation type)
        {
            shot = type; swing = 0f;
            tracking = false; confirmedContact = false;
        }

        public void Synchronize(in PlayerState state, bool preparing, double now, ShotAnimation type)
        {
            // A cancelled plan/whiff must not pull the racket to a stale predicted position.
            if (!confirmedContact)
            {
                tracking = state.Plan.Valid && !state.HasHitCurrentBall &&
                    (preparing || !double.IsNaN(state.SwingTime));
                if (preparing || tracking) shot = type;
                if (tracking)
                {
                    contact = state.Plan.BallPosition.ToUnity();
                    contactAt = state.Plan.Time;
                    swingStart = double.IsNaN(state.SwingTime)
                        ? contactAt - ShotContactTiming.ApproachSeconds : Math.Min(state.SwingTime, contactAt - 0.001);
                }
            }
            if (confirmedContact && now >= contactAt + ShotContactTiming.FollowThroughSeconds)
            {
                confirmedContact = false; tracking = false; swing = 1f;
            }
        }

        public void Contact(Vector3 position, ShotAnimation type, double time = double.NaN)
        {
            shot = type; contact = position;
            contactAt = double.IsNaN(time) ? lastTime : time;
            if (!tracking) swingStart = contactAt - ShotContactTiming.ApproachSeconds;
            tracking = true; confirmedContact = true;
        }

        public void Update(in PlayerState state, bool preparing, float dt, double now = double.NaN)
        {
            if (!Ready) return;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].localRotation = rest[i];
            float speed = Mathf.Sqrt(state.Velocity.x * state.Velocity.x + state.Velocity.z * state.Velocity.z);
            stride += speed * dt * 5.5f;
            float amount = Mathf.Clamp01(speed / 2f);
            Vector3 direction = new Vector3(state.Velocity.x, 0, state.Velocity.z).normalized;
            PoseLeg(leftLeg, -0.12f, stride, amount, direction);
            PoseLeg(rightLeg, 0.12f, stride + Mathf.PI, amount, direction);
            now = double.IsNaN(now) ? lastTime + dt : now;
            lastTime = now;
            swing = tracking
                ? ShotContactTiming.Phase(now, swingStart, contactAt)
                : Mathf.Min(1f, swing + dt / 0.34f);
            ContactBlend = tracking ? ShotContactTiming.ContactWeight(now, contactAt) : 0f;
            bool overhead = shot == ShotAnimation.Smash || shot == ShotAnimation.Serve;
            float side = shot == ShotAnimation.Backhand ? -1f : 1f;
            float arc = Mathf.Sin(swing * Mathf.PI);
            Vector3 local = new Vector3(0.38f, 1.12f, 0.32f);
            if (preparing) local = new Vector3(0.44f * side, overhead ? 1.85f : 1.35f, 0.05f);
            if (swing < 1f)
                local = overhead
                    ? new Vector3(0.28f, 1.3f + arc * 0.8f, 0.15f + arc * 0.4f)
                    : new Vector3(Mathf.Lerp(0.55f * side, -0.25f * side, swing), 1.2f + arc * 0.22f, 0.2f + arc * 0.45f);
            bool contacting = ContactBlend > 0f;
            if (shot == ShotAnimation.Lob && swing < 1f) local.y += arc * 0.35f;
            if (shot == ShotAnimation.Drop && swing < 1f) local.z = Mathf.Lerp(0.2f, local.z, 0.45f);
            Vector3 reachDirection = (contact - rightArm.Origin).normalized;
            Vector3 gripTarget = Vector3.Lerp(root.TransformPoint(local), contact - reachDirection * 0.32f, ContactBlend);
            Quaternion restRacket = Quaternion.LookRotation(root.forward, root.up) * Quaternion.Euler(0, 0, 55);
            Quaternion racketRotation = contacting
                ? Quaternion.Slerp(restRacket, Quaternion.FromToRotation(Vector3.right, reachDirection), ContactBlend)
                : restRacket;
            Quaternion desiredHand = Quaternion.LookRotation(racketRotation * Vector3.down, racketRotation * Vector3.forward) * Quaternion.Inverse(handBasis);
            rightArm.Solve(gripTarget - desiredHand * palmOffset, root.right + root.forward * 0.2f);
            hand.rotation = desiredHand;
            if (gripFingers != null)
                for (int i = 0; i < gripFingers.Length; i++)
                    if (gripFingers[i] != null)
                        gripFingers[i].localRotation *= Quaternion.AngleAxis(i >= 12 ? 35f : (i % 3 == 1 ? 75f : 55f), fingerAxes[i]);
            leftArm.Solve(root.TransformPoint(new Vector3(-0.35f, preparing ? 1.35f : 1.06f, 0.32f)), -root.right);
            if (racket != null)
            {
                racket.position = hand.position + hand.rotation * palmOffset;
                racket.rotation = racketRotation;
            }
            UpdateGaze(dt);

        }

        void UpdateGaze(float dt)
        {
            var head = Bone(HumanBodyBones.Head);
            var neck = Bone(HumanBodyBones.Neck);
            if (head == null) return;
            float yaw = 0, pitch = 0;
            if (lookTarget.HasValue)
            {
                Vector3 delta = root.InverseTransformDirection(lookTarget.Value - head.position);
                // Don't turn the head around to chase a ball behind the body.
                if (delta.z > 0.05f)
                {
                    yaw = Mathf.Clamp(Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg, -65f, 65f);
                    pitch = Mathf.Clamp(-Mathf.Atan2(delta.y, Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z)) * Mathf.Rad2Deg, -30f, 30f);
                }
            }
            gazeYaw = Mathf.MoveTowards(gazeYaw, yaw, 180f * dt);
            gazePitch = Mathf.MoveTowards(gazePitch, pitch, 120f * dt);
            float neckShare = neck != null ? 0.35f : 0f;
            if (neck != null)
                neck.rotation = Quaternion.AngleAxis(gazeYaw * neckShare, root.up) * Quaternion.AngleAxis(gazePitch * neckShare, root.right) * neck.rotation;
            head.rotation = Quaternion.AngleAxis(gazeYaw * (1f - neckShare), root.up) * Quaternion.AngleAxis(gazePitch * (1f - neckShare), root.right) * head.rotation;
        }

        void PoseLeg(Limb leg, float x, float phase, float amount, Vector3 direction)
        {
            Vector3 foot = root.TransformPoint(new Vector3(x, 0.07f + Mathf.Max(0, Mathf.Cos(phase)) * 0.13f * amount, 0));
            foot += direction * (Mathf.Sin(phase) * 0.25f * amount);
            leg.Solve(foot, root.forward);
        }

        readonly struct Limb
        {
            readonly Transform upper, lower, end;
            public Vector3 Origin => upper.position;
            public bool Valid => upper != null && lower != null && end != null;
            public Limb(Transform a, Transform b, Transform c) { upper = a; lower = b; end = c; }
            public void Solve(Vector3 target, Vector3 pole)
            {
                if (!Valid) return;
                Vector3 origin = upper.position;
                float a = Vector3.Distance(origin, lower.position), b = Vector3.Distance(lower.position, end.position);
                if (a < 0.001f || b < 0.001f) return;
                Vector3 delta = target - origin;
                Vector3 forward = delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector3.down;
                float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
                Vector3 bend = Vector3.ProjectOnPlane(pole, forward).normalized;
                if (bend.sqrMagnitude < 0.1f) bend = Vector3.ProjectOnPlane(Vector3.up, forward).normalized;
                float along = (a * a - b * b + distance * distance) / (2f * distance);
                Vector3 joint = origin + forward * along + bend * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
                upper.rotation = Quaternion.FromToRotation(lower.position - origin, joint - origin) * upper.rotation;
                lower.rotation = Quaternion.FromToRotation(end.position - lower.position, origin + forward * distance - lower.position) * lower.rotation;
            }
        }
    }
}








