using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using ISBSpecialForcesServer;
using ISBSpecialForcesServer.Controllers;
using ISBSpecialForcesServer.Patches;
using Microsoft.Extensions.Logging.Abstractions;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using Path = System.IO.Path;

public static class PrepatchContextProbe
{
    public static async Task RunHost(string sptRoot)
    {
        var runtime = Path.Combine(Path.GetFullPath(sptRoot), "SPT_Runtime");
        var original = typeof(BotGenerator).Assembly;
        var server = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(runtime, "SPT.Server.dll"));
        var contextType = server.GetType("SPTarkov.Server.Modding.PrepatchLoadContext", throwOnError: true)!;
        var patchedCore = Path.Combine(runtime, "SPTarkov.Server.Core.Patched.dll");
        var corePath = File.Exists(patchedCore) ? patchedCore : Path.Combine(runtime, "SPTarkov.Server.Core.dll");
        var context = (AssemblyLoadContext)Activator.CreateInstance(contextType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [Path.Combine(runtime, "SPT.Server.dll"), File.ReadAllBytes(corePath), null], null)!;
        context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "MoreBotsServer.dll"));
        context.LoadFromAssemblyPath(Path.Combine(AppContext.BaseDirectory, "ISBSpecialForces.dll"));
        var probe = context.LoadFromAssemblyPath(typeof(PrepatchContextProbe).Assembly.Location);
        try
        {
            await (Task)probe.GetType(typeof(PrepatchContextProbe).FullName!)!
                .GetMethod(nameof(RunPatches))!.Invoke(null, [original])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw ex.InnerException;
        }
    }

    public static async Task RunPatches(Assembly original)
    {
        var active = typeof(BotGenerator).Assembly;
        if (active == original || AssemblyLoadContext.GetLoadContext(active)?.Name != "SPT.PrepatchHost")
            throw new Exception("The test must use SPT's real prepatch context with two different Core assemblies.");
        var target = AccessTools.Method(typeof(BotGenerator), "PrepareAndGenerateBot");
        if (target.ReturnType != typeof(BotBase))
            throw new Exception("The active target and postfix result must have identical type identity.");
        var globalTarget = AccessTools.TypeByName(typeof(BotGenerator).FullName!);
        if (globalTarget == typeof(BotGenerator))
            throw new Exception("The fixture did not reproduce the ambiguous global type lookup.");
        Console.WriteLine("PASS: real SPT.PrepatchHost reproduces the wrong-Core selection from TypeByName.");
        var config = (ConfigController)RuntimeHelpers.GetUninitializedObject(typeof(ConfigController));
        config.ModConfig = new MainConfig();
        config.ModConfig.botPresets.enabled = false;
        var dogtagTemplate = new MongoId("123456789012345678901234");
        config.ModConfig.dogtags.dogtagTplByWildType["ISBSpecialForces"] = dogtagTemplate.ToString();
        var templates = (TemplateTable)RuntimeHelpers.GetUninitializedObject(typeof(TemplateTable));
        typeof(TemplateTable).GetProperty(nameof(TemplateTable.Items))!.SetValue(templates,
            new Dictionary<MongoId, TemplateItem> { [dogtagTemplate] = new TemplateItem() });
        var controller = new ISBBotPresetController(config, null!, null!, null!, null!, null!, []);
        var dogtags = new ISBDogtagGenerationPatch(config, templates, NullLogger<ISBDogtagGenerationPatch>.Instance);
        var presets = new ISBBotPresetGenerationPatch(controller, NullLogger<ISBBotPresetGenerationPatch>.Instance);
        await dogtags.OnLoadAsync(CancellationToken.None);
        await presets.OnLoadAsync(CancellationToken.None);
        var info = Harmony.GetPatchInfo(target);
        if (info == null || !info.Owners.Contains(ISBDogtagGenerationPatch.HarmonyId)
            || !info.Owners.Contains(ISBBotPresetGenerationPatch.HarmonyId))
            throw new Exception("Both production startup callbacks must patch the active Core.");
        var originalTarget = AccessTools.Method(original.GetType(typeof(BotGenerator).FullName!), target.Name);
        if (Harmony.GetPatchInfo(originalTarget)?.Owners.Any(owner => info.Owners.Contains(owner)) == true)
            throw new Exception("ISB must not patch the original prepatch-pass Core.");
        Console.WriteLine("PASS: dogtag and preset OnLoadAsync register on the active Core with production Harmony IDs and ordering.");
        var fixtureHarmony = new Harmony("isb.validation.fixture");
        fixtureHarmony.Patch(target, prefix: new HarmonyMethod(typeof(PrepatchContextProbe), nameof(GenerateFixture)));
        var generator = RuntimeHelpers.GetUninitializedObject(typeof(BotGenerator));
        var bot = (BotBase)target.Invoke(generator, [new MongoId(), null])!;
        var dogtag = bot.Inventory!.Items!.Single(item => item.SlotId == "Dogtag");
        if (dogtag.Template != dogtagTemplate || dogtag.Upd?.Dogtag?.Nickname != "ISB fixture"
            || dogtag.ParentId != bot.Inventory.Equipment.ToString())
            throw new Exception("The active-Core Harmony wrapper did not populate the ISB dogtag.");
        Console.WriteLine("PASS: the patched method executes and populates the ISB dogtag with active-Core BotBase/Item types.");
    }

    private static bool GenerateFixture(ref BotBase __result)
    {
        __result = new BotBase
        {
            Id = new MongoId(), Aid = 123,
            Info = new Info { Nickname = "ISB fixture", Settings = new BotInfoSettings { Role = "13701" } },
            Inventory = new BotBaseInventory { Equipment = new MongoId(), Items = [] }
        };
        return false;
    }
}
