using BeachTennis.Core;
using UnityEngine;

namespace BeachTennis.Presentation
{
    /// <summary>Bridges the simulation's <see cref="Vec3"/> and Unity's <see cref="Vector3"/> (same axes).</summary>
    public static class VecConversions
    {
        public static Vector3 ToUnity(this Vec3 v) => new Vector3(v.x, v.y, v.z);
        public static Vec3 ToSim(this Vector3 v) => new Vec3(v.x, v.y, v.z);
    }
}
