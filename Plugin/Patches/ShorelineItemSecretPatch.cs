using Comfort.Common;
using EFT;
using ISBSpecialForces.Components;
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
            if (Time.time < nextCheck)
                return;

            nextCheck = Time.time + 1f;

            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;

            if (RaidChangesUtil.LocationId == null || RaidChangesUtil.LocationId.ToLower() != "shoreline")
                return;

            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld?.MainPlayer == null)
                return;

            foreach (var item in gameWorld.MainPlayer.Profile.Inventory.AllRealPlayerItems)
            {
                if (item.TemplateId == TargetTemplateId)
                {
                    ISBSecretsManager.Fire(TriggerId);
                    return;
                }
            }
        }
    }
}
