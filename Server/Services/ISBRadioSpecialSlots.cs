using HarmonyLib;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
#if ISB_SPT41
using Database = SPTarkov.Server.Core.Models.Spt.Tables.TemplateTable;
#else
using Database = SPTarkov.Server.Core.Services.DatabaseService;
#endif

namespace ISBSpecialForces.Services;

[Injectable(TypePriority = int.MaxValue - 10)]
public sealed class ISBRadioSpecialSlots(Database database) : IOnLoad
{
    private static Database? _database;
    private static readonly MongoId Radio = "6aa5ff857a930fba7793141b";

#if ISB_SPT41
    public Task OnLoadAsync(CancellationToken token)
#else
    public Task OnLoad()
#endif
    {
        _database = database;
        Ensure();
        var harmony = new Harmony("com.samc137.isb.radio-special-slots");
        var callback = AccessTools.Method(typeof(DataCallbacks), nameof(DataCallbacks.GetTemplateItems));
        var owners = Harmony.GetPatchInfo(callback)?.Owners.ToArray() ?? [];
        harmony.Patch(callback, prefix: new HarmonyMethod(typeof(ISBRadioSpecialSlots), nameof(Ensure)) { priority = Priority.Last, after = owners });
        var svm = AccessTools.TypeByName("ServerValueModifier.Sections.CSM");
        var pockets = svm == null ? null : AccessTools.Method(svm, "CSMSection");
        if (pockets != null)
            harmony.Patch(pockets, postfix: new HarmonyMethod(typeof(ISBRadioSpecialSlots), nameof(Ensure)) { priority = Priority.Last });
        return Task.CompletedTask;
    }

    private static void Ensure()
    {
#if ISB_SPT41
        var items = _database?.Items;
#else
        var items = _database?.GetTemplates().Items;
#endif
        if (items?.ContainsKey(Radio) != true)
            return;
        foreach (var template in items.Values)
        {
            foreach (var slot in template.Properties?.Slots ?? [])
            {
                if (slot.Name?.StartsWith("SpecialSlot", StringComparison.OrdinalIgnoreCase) != true)
                    continue;
                slot.Properties ??= new SlotProperties();
                var filters = slot.Properties.Filters?.ToList() ?? [];
                if (filters.Count == 0)
                    filters.Add(new SlotFilter { Filter = [] });
                slot.Properties.Filters = filters;
                foreach (var filter in filters)
                {
                    filter.Filter ??= [];
                    filter.Filter.Add(Radio);
                }
            }
        }
    }
}
