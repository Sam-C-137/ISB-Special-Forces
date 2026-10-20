using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.InRaid;
using HarmonyLib;
using ISBSpecialForces;
using ISBSpecialForcesServer.Controllers;
using ISBSpecialForcesServer.Patches;
using MoreBotsServer.Services;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Spt.Mod;

if (args.Contains("--prepatch-context"))
{
    try
    {
        await PrepatchContextProbe.RunHost(args.Last());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        Environment.ExitCode = 1;
    }
    return;
}
var moreBots = new MoreBotsCustomBotTypeService(null!, null!, null!, null!, null!);
var registration = new ISBBotTypeRegistration(moreBots);
await registration.OnLoadAsync(CancellationToken.None);
if (moreBots.CustomWildSpawnTypes.Count != 16) throw new Exception("Expected 16 MoreBots roles.");
foreach (var pair in moreBots.CustomWildSpawnTypes)
{
    if (Enum.IsDefined(typeof(SPTarkov.Server.Core.Models.Eft.Common.WildSpawnType), pair.Key))
        throw new Exception("The fixture still has extended server enums.");
    if (pair.Value != pair.Value.ToLowerInvariant()) throw new Exception("MoreBots requires database keys for unnamed enum IDs.");
}
Console.WriteLine("PASS: 16 MoreBots roles register with unmodified server enums and lowercase database keys.");
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await new ISBBotTypeRegistration(moreBots).OnLoadAsync(cancelled.Token);
    throw new Exception("Cancellation was ignored.");
}
catch (OperationCanceledException)
{
    Console.WriteLine("PASS: server startup cancellation propagates.");
}

var harmony = new Harmony("com.samc137.isbspecialforces.validation");
void Patch(Type targetType, string targetName, Type patchType, string? prefix = null, string? postfix = null, string? finalizer = null)
{
    var target = AccessTools.Method(targetType, targetName) ?? throw new MissingMethodException(targetType.FullName, targetName);
    HarmonyMethod? Method(string? name) => name == null ? null : new HarmonyMethod(AccessTools.Method(patchType, name));
    harmony.Patch(target, prefix: Method(prefix), postfix: Method(postfix), finalizer: Method(finalizer));
}

Patch(typeof(BotWeaponGenerator), "GenerateWeaponByTpl", typeof(MixedAmmoPatch), "WeaponGenPrefix");
Patch(typeof(BotWeaponGenerator), "AddExtraMagazinesToInventory", typeof(MixedAmmoPatch), "ExtraMagsPrefix", "ExtraMagsPostfix");
Patch(typeof(ItemHelper), "FillMagazineWithCartridge", typeof(MixedAmmoPatch), postfix: "FillMagazinePostfix");
Patch(typeof(DurabilityLimitsHelper), "GetDurabilityRole", typeof(DurabilityRolePatch), "Prefix");
Patch(typeof(BotGenerator), "PrepareAndGenerateBot", typeof(ISBDogtagGenerationPatch), postfix: "Postfix");
Patch(typeof(BotGenerator), "PrepareAndGenerateBot", typeof(ISBBotPresetGenerationPatch), postfix: "Postfix");
Patch(typeof(BotWeaponGenerator), "GenerateWeaponByTpl", typeof(WeaponPresetGenerationPatch), "Prefix", "Postfix", "Finalizer");
Console.WriteLine("PASS: all seven server Harmony patch registrations accept the 4.1.4 targets and parameter signatures.");


Patch(typeof(LocationLifecycleService), "GenerateLocationAndLoot", typeof(ISBLockdownPopulationPatch), postfix: "Postfix");
Console.WriteLine("PASS: Lockdown postfix installs on the real per-raid location generator.");
var populationChecks = 0;
void CheckPopulation(bool condition, string message) { if (!condition) throw new Exception(message); populationChecks++; }
BossLocationSpawn Spawn(string role, string id) => new() { BossName = role, SptId = id };
var eventPmc = Spawn("pmcUSEC", "isb_lockdown_pmc");
var eventRaider = Spawn("pmcBot", "isb_event_raider");
var normalPmc = Spawn("pmcBEAR", "spt_pmc");
var normalRaider = Spawn("pmcBot", "normal_raider");
var location = new LocationBase {
 Id = "laboratory", BossLocationSpawn = [eventPmc, eventRaider, normalRaider, Spawn("ISBTeamLeader", "isb"), Spawn("assault", "scav")],
 Waves = [new Wave { WildSpawnType = Enum.Parse<WildSpawnType>("pmcBot", true) }, new Wave { WildSpawnType = Enum.Parse<WildSpawnType>("pmcUSEC", true) }, new Wave { WildSpawnType = Enum.Parse<WildSpawnType>("assault", true) }]
};
var waveConfig = Activator.CreateInstance<PmcConfig>();
waveConfig.RemoveExistingPmcWaves = true;
waveConfig.CustomPmcWaves = new() { ["laboratory"] = [normalPmc] };
var generator = new PmcWaveGenerator(null!, waveConfig);
generator.ApplyWaveChangesToMap(location);
CheckPopulation(!location.BossLocationSpawn.Contains(eventPmc) && location.BossLocationSpawn.Contains(normalPmc), "Fixture must reproduce late SPT replacement of event PMC with normal PMC");
var eventCopy = System.Text.Json.JsonSerializer.Deserialize<BossLocationSpawn>(System.Text.Json.JsonSerializer.Serialize(eventPmc))!;
var removed = ISBLockdownPopulation.Apply(location, [eventCopy, eventRaider], true, true);
CheckPopulation(removed == 4, "Remove late normal PMC, raider and both ordinary wave roles");
CheckPopulation(location.BossLocationSpawn.Any(x => x.SptId == eventPmc.SptId), "Restore event PMC removed by core generator");
CheckPopulation(location.BossLocationSpawn.Contains(eventRaider), "Preserve triggered event raider");
CheckPopulation(location.BossLocationSpawn.Count == 4 && location.Waves.Count == 1, "Preserve ISB, scav and event formations only");
CheckPopulation(ISBLockdownPopulation.Apply(location, [eventCopy,eventRaider],true,true) == 0 && location.BossLocationSpawn.Count == 4, "Filter must be idempotent");
foreach(var flags in new[] { (false,false), (true,false), (false,true) }) {
 var sample = new LocationBase { Id = "laboratory", BossLocationSpawn = [normalPmc,normalRaider], Waves=[] };
 ISBLockdownPopulation.Apply(sample, [], flags.Item1, flags.Item2);
 CheckPopulation(sample.BossLocationSpawn.Contains(normalPmc) == !flags.Item1 && sample.BossLocationSpawn.Contains(normalRaider) == !flags.Item2, "Population toggles operate independently");
}
CheckPopulation(!ISBLockdownPopulation.Applies("laboratory", false), "Disabled/inactive event does not filter");
CheckPopulation(!ISBLockdownPopulation.Applies("bigmap", true), "Other maps are unaffected");
CheckPopulation(ISBLockdownPopulation.Applies("Laboratory", true), "Active Labs raid applies filter");
Console.WriteLine($"PASS: {populationChecks} population checks, including real SPT wave generation, serialized event IDs, restoration, flags and map scope.");

