using Comfort.Common;
using EFT;
using ISBSpecialForces.Components;
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

        private float nextCheck;

        private void Update()
        {
            if (Time.time < nextCheck)
                return;

            nextCheck = Time.time + 1f;

            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;

            if (RaidChangesUtil.LocationId == null || RaidChangesUtil.LocationId.ToLower() != TargetMap)
                return;

            if (!ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;

            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld?.MainPlayer == null)
                return;

            if (Vector3.Distance(gameWorld.MainPlayer.Position, TargetPoint) <= Radius)
            {
                if (!ISBSecretTeleport.TryFindSafeDestinationAround(
                        TargetPoint,
                        gameWorld.MainPlayer.Position,
                        minimumRadius: 22f,
                        maximumRadius: 38f,
                        minimumDistanceFromAvoidPoint: 20f,
                        out var spawnPoint))
                {
                    Plugin.LogSource.LogWarning(
                        $"[ISB SECRET] Shoreline location secret could not find a valid NavMesh destination near {TargetPoint}. It will retry.");
                    return;
                }

                // 1 commander + 1 commander follower.
                if (!ISBSecretTeleport.Arm(
                        spawnPoint,
                        expectedBots: 2,
                        minimumGather: 1.5f,
                        maximumGather: 6f,
                        hardWindowSeconds: 28f,
                        expectedRoles: new Dictionary<WildSpawnType, int>
                        {
                            [(WildSpawnType)13707] = 1,
                            [(WildSpawnType)13708] = 1,
                        }))
                {
                    return;
                }

                if (!ISBSecretsManager.Fire(TriggerId))
                    ISBSecretTeleport.Reset();
            }
        }
    }
}