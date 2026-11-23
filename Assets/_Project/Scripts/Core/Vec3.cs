using System;

namespace BeachTennis.Core
{
    /// <summary>
    /// Minimal 3D vector for the pure-C# simulation (no UnityEngine dependency).
    /// Same axis convention as Unity: +Y up. Presentation converts with an extension method.
    /// </summary>
    [Serializable]
    public struct Vec3 : IEquatable<Vec3>
    {
        public float x;
        public float y;
        public float z;

        public Vec3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public static readonly Vec3 Up = new Vec3(0f, 1f, 0f);
        public static readonly Vec3 Forward = new Vec3(0f, 0f, 1f);
        public static readonly Vec3 Right = new Vec3(1f, 0f, 0f);

        public float SqrMagnitude => x * x + y * y + z * z;
        public float Magnitude => MathF.Sqrt(x * x + y * y + z * z);

        /// <summary>Copy with Y set to zero.</summary>
        public Vec3 Horizontal => new Vec3(x, 0f, z);

        public Vec3 Normalized
        {
            get
            {
                float m = Magnitude;
                return m > 1e-8f ? new Vec3(x / m, y / m, z / m) : Zero;
            }
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.x, -a.y, -a.z);
        public static Vec3 operator *(Vec3 a, float s) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static Vec3 operator *(float s, Vec3 a) => new Vec3(a.x * s, a.y * s, a.z * s);
        public static Vec3 operator /(Vec3 a, float s) => new Vec3(a.x / s, a.y / s, a.z / s);

        public static float Dot(Vec3 a, Vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vec3 Cross(Vec3 a, Vec3 b) =>
            new Vec3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t) =>
            new Vec3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        public static float Distance(Vec3 a, Vec3 b) => (a - b).Magnitude;

        public static float HorizontalDistance(Vec3 a, Vec3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// Rotates a vector around the +Y axis by <paramref name="radians"/>, same sense as Unity's
        /// Quaternion.Euler(0, degrees, 0): positive turns Forward towards Right.
        /// </summary>
        public static Vec3 RotateAroundY(Vec3 v, float radians)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            return new Vec3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
        }

        public bool Equals(Vec3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }
}
