using HarmonyLib;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Spt.Bots;
using System.Security.Cryptography;
using System.Text;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ISBSpecialForcesServer.Services;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 11)]
public sealed class ISBLargePocketSlots(TemplateTable templates, ICloner cloner) : IOnLoad
{
    internal const string LargePocketId = "64cbd95a29b9b4283e216ff5";
    internal const string SpecialPocketId = "627a4e6b255f7527fb05a0f6";
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var items = templates.Items;
        if (!items.TryGetValue(new MongoId(LargePocketId), out var large) || !items.TryGetValue(new MongoId(SpecialPocketId), out var donor) || large.Properties == null || donor.Properties?.Slots == null)
            return Task.CompletedTask;
        large.Properties.Slots = Complete(large.Properties.Slots ?? [], donor.Properties.Slots, cloner);
        var target = AccessTools.Method(typeof(BotEquipmentModGenerator), nameof(BotEquipmentModGenerator.GenerateModsForEquipment));
        new Harmony("com.samc137.isb.large-pocket-pool").Patch(target, prefix: new HarmonyMethod(typeof(ISBLargePocketSlots), nameof(BeforeGenerate)));
        return Task.CompletedTask;
    }

    private static void BeforeGenerate(TemplateItem __2, GenerateEquipmentProperties __3)
    {
        if (__2?.Id.ToString() == LargePocketId && __3 != null)
            CompletePool(__2, __3);
    }

    internal static void CompletePool(TemplateItem pocket, GenerateEquipmentProperties settings)
    {
        var id = new MongoId(LargePocketId);
        if (settings.ModPool?.ContainsKey(id) == true)
            return;
        var slots = pocket.Properties?.Slots?.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        var contents = new Dictionary<string, HashSet<MongoId>>(StringComparer.OrdinalIgnoreCase);
        if (settings.ModPool?.TryGetValue(new MongoId(SpecialPocketId), out var donor) == true)
            foreach (var(name, values)in donor)
                if (slots.Contains(name) && values.Count > 0)
                    contents[name] = new HashSet<MongoId>(values);
        var pool = settings.ModPool == null ? new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>() : new Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>(settings.ModPool);
        pool[id] = contents;
        settings.ModPool = pool;
    }

    internal static List<Slot> Complete(IEnumerable<Slot> existing, IEnumerable<Slot> standard, ICloner cloner)
    {
        var result = existing.ToList();
        var names = result.Select(slot => slot.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var source in standard)
        {
            if (source.Name?.StartsWith("SpecialSlot", StringComparison.Ordinal) != true || !names.Add(source.Name))
                continue;
            var slot = cloner.Clone(source) ?? throw new InvalidOperationException("ISB: unable to clone a pocket special slot.");
            slot.Parent = LargePocketId;
            slot.Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(LargePocketId + "|" + source.Name))).ToLowerInvariant()[..24];
            result.Add(slot);
        }

        return result;
    }
}
