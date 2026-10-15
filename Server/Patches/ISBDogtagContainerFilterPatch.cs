using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Services;

namespace ISBSpecialForcesServer.Patches;
[Injectable(TypePriority = OnLoadOrder.PostLoad + 10)]
public class ISBDogtagContainerFilterPatch(ConfigController configController, TemplateTable templateTable) : IOnLoad
{
    private static readonly HashSet<string> VanillaDogtagTpls = new(StringComparer.OrdinalIgnoreCase)
    {
        "59f32c3b86f77472a31742f0",
        "6662ea05f6259762c56f3189",
        "6662e9f37fa79a6d83730fa0",
        "6764207f2fa5e32733055c4a",
        "6764202ae307804338014c1a",
        "68418091b5b0c9e4c60f0e7a",
        "59f32bb586f774757e1e8442",
        "6662e9cda7e0b43baa3d5f76",
        "6662e9aca7e0b43baa3d5f74",
        "684181208d035f60230f63f9",
        "684180bc51bf8645f7067bc8",
        "675dcb0545b1a2d108011b2b",
        "675dc9d37ae1a8792107ca96"
    };
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var items = templateTable.Items;
        if (items is null)
            return Task.CompletedTask;
        var isb = configController.ModConfig?.dogtags?.dogtagTplByWildType?.Values ?? Enumerable.Empty<string>();
        if (configController.ModConfig?.dogtags is { enabled: true, populateIdentityData: true })
        {
            var selected = isb.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.Values)
                if (item.Properties != null && selected.Contains(item.Id.ToString()))
                    item.Properties.DogTagQualities = true;
        }

        ExtendFilters(items.Values, isb);
        return Task.CompletedTask;
    }

    public static int ExtendFilters(IEnumerable<SPTarkov.Server.Core.Models.Eft.Common.Tables.TemplateItem> templates, IEnumerable<string> isbTags)
    {
        var items = templates.ToArray();
        var present = items.Select(i => i.Id.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var isbDogtagTpls = isbTags.Concat(new[] { "6a461aed7391ab085a093760", "6a461bf82b2264dbe10d0ee6", "6a461c41ec88c6b9a509fb17" }).Where(id => !string.IsNullOrWhiteSpace(id) && present.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var recognized = VanillaDogtagTpls.Concat(isbDogtagTpls).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var touchedItems = 0;
        var touchedFilters = 0;
        var addedEntries = 0;
        foreach (var item in items)
        {
            var grids = item?.Properties?.Grids;
            if (grids is null || !grids.Any())
                continue;
            var itemTouched = false;
            foreach (var grid in grids)
            {
                var filters = grid?.Properties?.Filters;
                if (filters is null || !filters.Any())
                    continue;
                foreach (var filter in filters)
                {
                    var allowed = filter?.Filter;
                    if (allowed is null || !allowed.Any())
                        continue;
                    if (!allowed.Any(id => recognized.Contains(id)))
                        continue;
                    var filterTouched = false;
                    foreach (var customTpl in isbDogtagTpls)
                    {
                        if (allowed.Any(x => string.Equals(x, customTpl, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        allowed.Add(customTpl);
                        addedEntries++;
                        filterTouched = true;
                    }

                    if (!filterTouched)
                        continue;
                    touchedFilters++;
                    itemTouched = true;
                }
            }

            if (itemTouched)
                touchedItems++;
        }

        return addedEntries;
    }
}
