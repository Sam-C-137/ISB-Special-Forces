using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace ISBSpecialForcesServer;
public static class OptionalPresetLoot
{
    private static readonly HashSet<MongoId> DynamicMaps = ["6738033eb7305d3bdafe9518", "673803448cb3819668d77b1b", "6738034a9713b5f42b4a8b78", "6738034e9d22459ad7cd1b81", "6738035350b24a4ae4a57997", "68f1ad32317cc52f4c0b6fae"];
    public static int RemoveUnavailable(List<Item> items, Func<MongoId, bool> exists)
    {
        var missing = items.Where(i => DynamicMaps.Contains(i.Template) && !exists(i.Template)).Select(i => i.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        if (missing.Count == 0)
            return 0;
        var parents = items.Where(i => !string.IsNullOrEmpty(i.ParentId)).Select(i => i.ParentId!).ToHashSet(StringComparer.Ordinal);
        missing.ExceptWith(parents);
        return items.RemoveAll(i => missing.Contains(i.Id.ToString()) && i.Location != null && i.SlotId is not ("FirstPrimaryWeapon" or "SecondPrimaryWeapon" or "Holster" or "Scabbard" or "SpecialSlot1" or "SpecialSlot2" or "SpecialSlot3"));
    }
}
