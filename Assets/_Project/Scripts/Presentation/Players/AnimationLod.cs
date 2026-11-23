namespace BeachTennis.Presentation.Players
{
    /// <summary>Visual-only update cadence. Critical shots always bypass animation throttling.</summary>
    public static class AnimationLod
    {
        public static double Interval(float distanceSquared, int quality)
        {
            if (distanceSquared >= 30f * 30f) return 1.0 / 15.0;
            if (distanceSquared >= 20f * 20f || (quality <= 1 && distanceSquared >= 14f * 14f))
                return 1.0 / 30.0;
            return 0.0;
        }

        public static bool ShouldUpdate(double now, double previous, float distanceSquared, int quality, bool critical)
        {
            if (previous < 0 || critical) return true;
            if (now <= previous) return false;
            return now - previous + 0.000001 >= Interval(distanceSquared, quality);
        }
    }
}

