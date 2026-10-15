using EFT;
using EFT.InventoryLogic;
using EFT.NetworkPackets;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal class ISBDogtagDeathPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => typeof(Player).GetMethod("SetDogtagInfo", BindingFlags.Instance | BindingFlags.Public);
        [PatchPrefix]
        protected static bool PatchPrefix(Player __instance, DeathPacket __0)
        {
            var item = __instance.Equipment?.GetSlot(EquipmentSlot.Dogtag)?.ContainedItem;
            var tag = item?.GetItemComponent<DogtagComponent>();
            if (tag == null || !ISBDogtagState.ShouldDisplayAsISB(item.TemplateId.ToString()))
                return true;
            tag.AccountId = __0.AccountId;
            tag.ProfileId = __0.ProfileId;
            tag.Nickname = __0.Nickname;
            tag.Level = ISB.Compatibility.ISBDogtagLevels.Choose(item.TemplateId.ToString(), item.Id.ToString(), tag.Level);
            tag.Side = tag.Side == EPlayerSide.Bear ? EPlayerSide.Bear : EPlayerSide.Usec;
            tag.Time = DateTimeExtensions.UniversalDateTimeFromUnixTime(__0.Time).ToLocalTime();
            tag.Status = __0.Status;
            tag.WeaponName = __0.WeaponName;
            tag.KillerAccountId = __0.KillerAccountId;
            tag.KillerProfileId = __0.KillerProfileId;
            tag.KillerName = __0.KillerName;
            tag.GroupId = __instance.GroupId;
            item.SpawnedInSession = true;
            return false;
        }
    }
}
