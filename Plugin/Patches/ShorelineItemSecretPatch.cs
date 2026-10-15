using Comfort.Common;
using EFT;
using SPT.SinglePlayer.Utils.InRaid;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public class ShorelineItemSecretManager : MonoBehaviour
    {
        private const string TriggerId = "isb_secret_relic_hunt";
        private const string TargetTemplateId = "69860f957139b00c69e69cb7";
        private float nextCheck;
        private void Update()
        {
            if (!ISBFikaAuthority.IsAiAuthority || Time.time < nextCheck)
                return;
            nextCheck = Time.time + 1f;
            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;
            string location = RaidChangesUtil.LocationId;
            if (string.IsNullOrEmpty(location) || !string.Equals(location, "shoreline", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return;
            foreach (var player in players)
            {
                if (!ISBSecretsManager.IsHuman(player))
                    continue;
                var inventory = player.InventoryController?.Inventory ?? player.Profile?.Inventory;
                if (inventory == null)
                    continue;
                foreach (var item in inventory.AllRealPlayerItems)
                {
                    if (item == null || !string.Equals(item.TemplateId, TargetTemplateId, System.StringComparison.Ordinal))
                        continue;
                    ISBSecretsManager.Fire(TriggerId);
                    return;
                }
            }
        }
    }
}
