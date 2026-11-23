using System;
using BeachTennis.Core;
using BeachTennis.Simulation.Ball;

namespace BeachTennis.Simulation.Players
{
    /// <summary>
    /// Finds where and when a player can meet an incoming ball, from the exact predicted trajectory.
    /// Rule: the earliest point on the player's half, at a hittable height, that the player can run to
    /// in time <b>with some slack</b> (reaction time, not starting at full speed). Beach tennis has no
    /// bounce, so meeting the ball early is the correct instinct, but a point that is only reachable
    /// with a perfect instant start turns every small delay into a dive.
    /// Falls back to the earliest point reachable without slack, then to the closest miss
    /// (Reachable = false) so the player can still dive for it.
    /// </summary>
    public static class ContactPlanner
    {
        /// <summary>Fraction of the theoretical running distance the plan counts on.</summary>
        public const float RunMargin = 0.8f;
        /// <summary>Extra time (s) assumed lost before starting to run.</summary>
        public const float StartDelay = 0.15f;

        public static ContactPlan Plan(in PlayerState player, in PlayerConfig cfg, in CourtDimensions court,
            BallState[] predicted, int count, long firstTick, float dt, float accelMultiplier = 1f)
        {
            var best = new ContactPlan();
            var tight = new ContactPlan();
            float bestDeficit = float.MaxValue;
            int side = player.Side;

            for (int i = 0; i < count; i++)
            {
                ref readonly BallState b = ref predicted[i];
                if (b.Mode != BallMode.Flight || b.BounceCount > 0) break;
                if (b.Position.z * side < 0.3f) continue;                  // not on our half yet
                float h = b.Position.y;
                if (h < cfg.ReachHeightMin || h > cfg.ReachHeightMax) continue;

                float t = (i + 1) * dt;
                Vec3 stand = StandPosition(b.Position, player.Position, cfg, court, side);
                float need = Vec3.HorizontalDistance(player.Position, stand);
                float can = cfg.TravelDistance(t, accelMultiplier);
                float deficit = need - can;

                float comfortable = cfg.TravelDistance(t - StartDelay, accelMultiplier) * RunMargin;
                if (need <= comfortable)
                    return Make(true, firstTick + i, t, b.Position, stand);

                if (deficit <= 0f && !tight.Valid)
                    tight = Make(true, firstTick + i, t, b.Position, stand);

                if (deficit > 0f && deficit < bestDeficit)
                {
                    bestDeficit = deficit;
                    best = Make(false, firstTick + i, t, b.Position, stand);
                }
            }
            return tight.Valid ? tight : best;
        }

        private static ContactPlan Make(bool reachable, long tick, float timeFromNow, Vec3 ball, Vec3 stand) => new ContactPlan
        {
            Valid = true,
            Reachable = reachable,
            Tick = tick,
            Time = timeFromNow, // caller converts to absolute simulation time
            BallPosition = ball,
            StandPosition = stand
        };

        /// <summary>
        /// Body position that puts the ball at the ideal hitting distance, on the forehand or backhand
        /// side (whichever needs less running), slightly in front of the body.
        /// </summary>
        public static Vec3 StandPosition(Vec3 ball, Vec3 player, in PlayerConfig cfg, in CourtDimensions court, int side)
        {
            // Facing the net: a south player (side -1) faces +Z, so their right hand is +X.
            var right = new Vec3(-side, 0f, 0f);
            Vec3 ballFlat = ball.Horizontal;
            Vec3 forehand = ballFlat - right * cfg.IdealContactOffset;
            Vec3 backhand = ballFlat + right * cfg.IdealContactOffset;
            Vec3 stand = Vec3.HorizontalDistance(player, forehand) <= Vec3.HorizontalDistance(player, backhand) ? forehand : backhand;
            stand.z += side * 0.15f; // ball slightly in front of the body
            return ClampToBounds(stand, court, side);
        }

        /// <summary>Closest a player may get to the net during play (never touching it, R24f).</summary>
        public const float DefaultMinNetDistance = 0.4f;

        public static Vec3 ClampToBounds(Vec3 p, in CourtDimensions court, int side, float nearNet = DefaultMinNetDistance)
        {
            float maxX = court.HalfWidth + 2.5f;
            float farBack = court.HalfLength + 4f;
            p.x = MathF.Max(-maxX, MathF.Min(maxX, p.x));
            float zAbs = MathF.Max(nearNet, MathF.Min(farBack, p.z * side));
            p.z = zAbs * side;
            p.y = 0f;
            return p;
        }
    }
}
