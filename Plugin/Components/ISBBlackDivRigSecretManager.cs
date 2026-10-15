using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public class ISBBlackDivRigSecretManager : MonoBehaviour
    {
        private const string BlackDivGuid = "com.blackdiv.tacticaltoaster";
        private const string TriggerId = "isb_secret_bd_rig_hunt";
        private static readonly HashSet<string> RigTemplateIds = new HashSet<string>
        {
            "689479cb47e5acd1e10be986",
            "68947a4be4bf255d1b0ca746",
            "689479a4a733b1602007e2eb",
            "689479eb30cc5ba7be00f5ff",
            "69e2441a18cb3157560855ec",
        };
        private bool modPresent;
        private float nextCheck;
        private void Start()
        {
            modPresent = Chainloader.PluginInfos.ContainsKey(BlackDivGuid);
            if (!modPresent)
            {
                enabled = false;
            }
        }

        private void Update()
        {
            if (!ISBFikaAuthority.IsAiAuthority || Time.time < nextCheck)
                return;
            nextCheck = Time.time + 1f;
            if (ISBSecretsManager.AlreadyFired(TriggerId) || !ISBSecretsManager.IsEnabledForRaid(TriggerId))
                return;
            var players = Singleton<GameWorld>.Instance?.AllAlivePlayersList;
            if (players == null)
                return;
            foreach (var player in players)
            {
                if (!ISBSecretsManager.IsHuman(player))
                    continue;
                var equipment = player.Equipment ?? player.Profile?.Inventory?.Equipment;
                var item = equipment?.GetSlot(EquipmentSlot.TacticalVest)?.ContainedItem;
                if (item == null || !RigTemplateIds.Contains(item.TemplateId))
                    continue;
                ISBSecretsManager.Fire(TriggerId);
                return;
            }
        }
    }
}
