using Comfort.Common;
using EFT;
using SPT.SinglePlayer.Utils.InRaid;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public class ISBLocationSecretManager : MonoBehaviour
    {
        private const string TriggerId = "isb_secret_local_hunt";
        private const string TargetMap = "shoreline";
        private static readonly Vector3 TargetPoint = new Vector3(-223.584f, -40.664f, 184.493f);
        private const float Radius = 18f;
        private const float RadiusSqr = Radius * Radius;
        private float nextCheck;
        private void Update()
        {
            if (!ISBFikaAuthority.IsAiAuthority || Time.time < nextCheck)
                return;
            nextCheck = Time.time + 1f;
            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;
            string location = RaidChangesUtil.LocationId;
            if (string.IsNullOrEmpty(location) || !string.Equals(location, TargetMap, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return;
            Player target = null;
            foreach (var player in players)
                if (ISBSecretsManager.IsHuman(player) && (player.Position - TargetPoint).sqrMagnitude <= RadiusSqr)
                {
                    target = player;
                    break;
                }

            if (target == null)
                return;
            if (!ISBSecretTeleport.TryFindSafeDestinationAround(TargetPoint, target.Position, minimumRadius: 22f, maximumRadius: 38f, minimumDistanceFromAvoidPoint: 20f, out var spawnPoint))
            {
                Plugin.LogSource.LogWarning($"[ISB SECRET] Shoreline location secret could not find a valid NavMesh destination near {TargetPoint}. It will retry.");
                return;
            }

            if (!ISBSecretTeleport.Arm(spawnPoint, expectedBots: 2, minimumGather: 1.5f, maximumGather: 6f, hardWindowSeconds: 28f, expectedRoles: new Dictionary<WildSpawnType, int> { [(WildSpawnType)13707] = 1, [(WildSpawnType)13708] = 1, }))
            {
                return;
            }

            if (!ISBSecretsManager.Fire(TriggerId))
                ISBSecretTeleport.Reset();
        }
    }
}
