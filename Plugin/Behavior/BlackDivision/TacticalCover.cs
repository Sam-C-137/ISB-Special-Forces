using EFT;
using UnityEngine;
using UnityEngine.AI;

namespace ISBSpecialForces.Behavior.BlackDivision
{
    internal static class TacticalCover
    {
        internal static bool TryFind(ISBTacticalAgent agent, out Vector3 destination)
        {
            destination = default;
            var bot = agent.Bot;
            var squad = agent.Squad;
            if (bot?.BotsGroup?.CoverPointMaster == null || bot.CoverSearchInfo == null || bot.Settings?.FileSettings?.Cover == null)
                return false;
            var forward = squad.EnemyPosition - bot.Position;
            forward.y = 0f;
            var distance = forward.magnitude;
            if (distance <= BlackDivisionTactics.CloseCombatDistance)
                return false;
            forward /= distance;
            var advance = Mathf.Min(18f, distance - 18f);
            var side = new Vector3(-forward.z, 0f, forward.x) * (agent.Team == 0 ? 1f : -1f);
            var centre = bot.Position + forward * advance;
            if (squad.Policy.Mode == BlackDivisionMode.Flank)
                centre += side * 16f;
            else
                centre += side * 4f;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var desired = Vector3.Lerp(bot.Position, centre, attempt == 0 ? 1f : 0.55f);
                var search = new CoverSearchData(desired, bot.CoverSearchInfo, CoverShootType.hide, 12f * 12f, 0f, CoverSearchType.distToToCenter, null, null, null, ECheckSHootHide.shootAndHide, new CoverSearchDefenceDataClass(bot.Settings.FileSettings.Cover.MIN_DEFENCE_LEVEL), PointsArrayType.byShootType, true);
                var cover = bot.BotsGroup.CoverPointMaster.GetCoverPointMain(search, true);
                if (cover == null || !SainTacticalSensor.IsFinite(cover.Position) || (cover.Position - bot.Position).sqrMagnitude < 9f || (cover.Position - squad.EnemyPosition).sqrMagnitude < 15f * 15f || !squad.IsDestinationFree(agent, cover.Position))
                    continue;
                if (!NavMesh.SamplePosition(cover.Position, out var hit, 1.5f, NavMesh.AllAreas))
                    continue;
                if (!NavMesh.CalculatePath(bot.Position, hit.position, NavMesh.AllAreas, agent.Path) || agent.Path.status != NavMeshPathStatus.PathComplete)
                    continue;
                var corners = agent.Path.corners;
                var length = 0f;
                for (var i = 1; i < corners.Length; i++)
                    length += Vector3.Distance(corners[i - 1], corners[i]);
                if (length > 65f)
                    continue;
                destination = hit.position;
                return true;
            }

            return false;
        }
    }
}
