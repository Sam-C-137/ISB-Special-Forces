using EFT;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Orders.State
{
    public static class ISBOrderSquadRegistry
    {
        private static readonly Dictionary<BotsGroup, ISBOrderSquadState> Squads =
            new Dictionary<BotsGroup, ISBOrderSquadState>();

        public static ISBOrderSquadState Register(BotOwner botOwner)
        {
            if (botOwner == null || botOwner.BotsGroup == null)
                return null;

            if (!Squads.TryGetValue(botOwner.BotsGroup, out var squad))
            {
                squad = new ISBOrderSquadState(botOwner.BotsGroup);
                Squads[botOwner.BotsGroup] = squad;
            }

            squad.AddMember(botOwner);
            return squad;
        }

        public static void Unregister(BotOwner botOwner)
        {
            if (botOwner == null || botOwner.BotsGroup == null)
                return;

            if (!Squads.TryGetValue(botOwner.BotsGroup, out var squad))
                return;

            squad.RemoveMember(botOwner);

            if (squad.IsEmpty)
                Squads.Remove(botOwner.BotsGroup);
        }

        public static ISBOrderSquadState Get(BotOwner botOwner)
        {
            if (botOwner == null || botOwner.BotsGroup == null)
                return null;

            Squads.TryGetValue(botOwner.BotsGroup, out var squad);
            return squad;
        }

        public static void UpdateAll()
        {
            if (!ISBOrderSettings.Enabled)
                return;

            var now = Time.time;
            var emptyGroups = new List<BotsGroup>();

            foreach (var entry in Squads)
            {
                entry.Value.Update(now);

                if (entry.Value.IsEmpty)
                    emptyGroups.Add(entry.Key);
            }

            foreach (var group in emptyGroups)
                Squads.Remove(group);
        }

        public static void Clear()
        {
            Squads.Clear();
        }
    }
}
