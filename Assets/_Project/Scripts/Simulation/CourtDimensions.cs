using System;

namespace BeachTennis.Simulation
{
    public enum CourtFormat
    {
        Singles,
        Doubles
    }

    /// <summary>
    /// Court geometry in meters. Coordinate convention (shared by simulation and presentation):
    /// origin at the center of the court on the sand, +Y up, the net lies on the plane z = 0,
    /// Z runs along the court length (baselines at ±Length/2, players face each other along Z),
    /// X is the court width (sidelines at ±Width/2).
    ///
    /// Values are data, not hard rules: they will be checked against the current ITF Beach Tennis rules
    /// in Phase 3 (net height in particular differs between categories in some rule revisions).
    /// </summary>
    [Serializable]
    public struct CourtDimensions
    {
        /// <summary>Total court length, baseline to baseline (Z axis).</summary>
        public float Length;

        /// <summary>Court width, sideline to sideline (X axis).</summary>
        public float Width;

        /// <summary>Net height at the top of the tape.</summary>
        public float NetHeight;

        /// <summary>Distance from each sideline to its net post, outside the court.</summary>
        public float NetPostOffset;

        /// <summary>Width of the court lines (bands on the sand).</summary>
        public float LineWidth;

        public float HalfLength => Length * 0.5f;
        public float HalfWidth => Width * 0.5f;

        public static CourtDimensions For(CourtFormat format)
        {
            return new CourtDimensions
            {
                Length = 16f,
                Width = format == CourtFormat.Doubles ? 8f : 4.5f,
                NetHeight = 1.70f,
                NetPostOffset = 0.5f,
                LineWidth = 0.05f
            };
        }

        /// <summary>True when a point on the sand (x, z) is inside the court, lines included.</summary>
        public bool IsInside(float x, float z)
        {
            return x >= -HalfWidth && x <= HalfWidth && z >= -HalfLength && z <= HalfLength;
        }
    }
}
