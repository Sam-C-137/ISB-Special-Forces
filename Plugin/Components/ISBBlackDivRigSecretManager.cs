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
                Plugin.LogSource.LogInfo("[ISB SECRET] Black Division not installed. BD rig secret disabled for this raid.");
        }

        private void Update()
        {
            if (!modPresent)
                return;

            if (ISBSecretsManager.AlreadyFired(TriggerId))
                return;

            if (Time.time < nextCheck)
                return;

            nextCheck = Time.time + 1f;

            var gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld == null || gameWorld.MainPlayer == null)
                return;

            var equipment = gameWorld.MainPlayer.Profile?.Inventory?.Equipment;

            if (equipment == null)
                return;

            var vestSlot = equipment.GetSlot(EquipmentSlot.TacticalVest);

            if (vestSlot == null || vestSlot.ContainedItem == null)
                return;

            if (RigTemplateIds.Contains(vestSlot.ContainedItem.TemplateId))
            {
                Plugin.LogSource.LogInfo("[ISB SECRET] Player is wearing a Black Division rig. Trigging");
                ISBSecretsManager.Fire(TriggerId);
            }
        }
    }
}
