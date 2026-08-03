using Comfort.Common;
using EFT;
using System.Linq;
using Newtonsoft.Json;
using SPT.Common.Http;
using SPT.SinglePlayer.Utils.InRaid;
using System;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public sealed class ISBManagedSpawnResponse
    {
        public bool success { get; set; }
        public string locationId { get; set; }
        public string reason { get; set; }
        public List<BossLocationSpawn> spawns { get; set; }
    }

    public static class ISBSpawnRefreshBridge
    {
        private const string SecretSpawnTriggerPrefix = "isb_secret_";

        public static void RefreshCurrentRaid()
        {
            try
            {
                var localGame = Singleton<AbstractGame>.Instance as LocalGame;
                var currentSpawns = localGame?.Location_0?.BossLocationSpawn;

                if (currentSpawns == null)
                {
                    Plugin.LogSource.LogWarning("[ISB SPAWNS] Current location spawn data is unavailable. Keeping the cached waves.");
                    return;
                }

                var requestedLocationId = RaidChangesUtil.LocationId;

                if (string.IsNullOrWhiteSpace(requestedLocationId))
                {
                    Plugin.LogSource.LogWarning("[ISB SPAWNS] Current location id is unavailable. Keeping the cached waves.");
                    return;
                }

                var request = JsonConvert.SerializeObject(new
                {
                    locationId = requestedLocationId
                });

                var json = RequestHandler.PostJson("/ISB/spawns/refresh", request);
                var response = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonConvert.DeserializeObject<ISBManagedSpawnResponse>(json);

                if (response == null || !response.success || response.spawns == null || response.spawns.Count == 0)
                {
                    var reason = response?.reason;

                    if (string.IsNullOrWhiteSpace(reason))
                        reason = "The server returned no fresh waves.";

                    Plugin.LogSource.LogWarning($"[ISB SPAWNS] Refresh for '{requestedLocationId}' was skipped: {reason} Keeping the cached waves.");
                    return;
                }

                var updatedSpawns = currentSpawns.ToList();
                var removedCount = 0;

                for (var index = updatedSpawns.Count - 1; index >= 0; index--)
                {
                    if (!IsManagedISBSpawn(updatedSpawns[index]))
                        continue;

                    updatedSpawns.RemoveAt(index);
                    removedCount++;
                }

                updatedSpawns.AddRange(response.spawns);
                localGame.Location_0.BossLocationSpawn = updatedSpawns.ToArray();

                Plugin.LogSource.LogInfo(
                    $"[ISB SPAWNS] Refreshed {response.locationId}: removed {removedCount} cached wave(s), injected {response.spawns.Count} fresh wave(s).");
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError($"[ISB SPAWNS] Failed to refresh the current raid waves: {ex.Message}");
            }
        }

        private static bool IsManagedISBSpawn(BossLocationSpawn spawn)
        {
            if (spawn == null)
                return false;

            if (string.Equals(spawn.BossName, "ISBTeamLeader", StringComparison.Ordinal))
                return true;

            return !string.IsNullOrEmpty(spawn.TriggerId)
                && spawn.TriggerId.StartsWith(SecretSpawnTriggerPrefix, StringComparison.Ordinal);
        }
    }
}
