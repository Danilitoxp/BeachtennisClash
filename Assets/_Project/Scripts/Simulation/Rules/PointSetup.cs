using System;
using BeachTennis.Core;
using BeachTennis.Simulation.Players;

namespace BeachTennis.Simulation.Rules
{
    /// <summary>
    /// Places every player for the next point according to the score: the server behind the baseline
    /// (R17), the server's partner and the receivers outside the prohibited zone (App. I), each team on
    /// the half given by the change-of-ends rule (R11). Shared by the game and headless simulations.
    /// </summary>
    public static class PointSetup
    {
        public static void Prepare(MatchScore score, PlayerSystem[] players, PlayerId[] ids, PointReferee referee)
        {
            PlayerId server = score.Server;
            float zone = score.Rules.ProhibitedZone;
            int perTeam = score.PlayersPerTeam;

            for (int k = 0; k < players.Length; k++)
            {
                PlayerId id = ids[k];
                int side = id.Team == score.SouthTeam ? -1 : 1;
                float lateral = perTeam == 1 ? 0.6f : (id.Index == 0 ? 1.8f : -1.8f);
                Vec3 pos = id.Team == server.Team
                    ? new Vec3(-lateral, 0f, side * (zone + 0.6f))                // server's partner
                    : new Vec3(lateral, 0f, side * MathF.Max(zone + 0.5f, 6.0f)); // receivers

                players[k].SetSide(side, pos);
                if (id.Equals(server)) players[k].BeginServe(perTeam == 1 ? 0.8f : lateral);
            }

            referee.BeginPoint();
        }
    }
}
