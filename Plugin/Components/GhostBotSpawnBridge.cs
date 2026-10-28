using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
#if ISB_SPT41
using NativeDescriptor = EFT.ProfileDescriptor;
using NativeJson = JsonExtensions;
#else
using NativeDescriptor = CompleteProfileDescriptorClass;
using NativeJson = JsonParserClass;
#endif
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using HarmonyLib;
using ISB.Compatibility;
#if ISB_SPT41
using NativeProfileData = GetProfileDataParams;
using NativeCreation = BotCreationData;
using NativeCreator = BotProfileClient;
using NativeSpawner = BotBossSpawn;
using NativeToken = ITokenGetter;
using NativeWave = EFT.CountTypeBotWave;

#else
using NativeProfileData = BotProfileDataClass;
using NativeCreation = BotCreationDataClass;
using NativeCreator = BotsPresets;
using NativeSpawner = BossSpawnerClass;
using NativeToken = GInterface22;
using NativeWave = WaveInfoClass;
#endif
namespace ISBSpecialForces.Components
{
    internal static class GhostBotSpawnBridge
    {
        private sealed class WaveFlags
        {
            internal string Boss;
            internal string Escort;
            internal string[] Supports;
        }

        private static readonly ConditionalWeakTable<BossLocationSpawn, WaveFlags> Waves = new ConditionalWeakTable<BossLocationSpawn, WaveFlags>();
        private static bool _initialized;
        internal static void Initialize()
        {
            if (_initialized)
                return;
            var harmony = new Harmony("com.samc137.isbspecialforces.ghostvariant.client");
            harmony.Patch(AccessTools.Method(typeof(BotSpawner), nameof(BotSpawner.GetGroupAndSetEnemies)), prefix: new HarmonyMethod(typeof(ISBEventHostility), nameof(ISBEventHostility.SelectChaosGroup)));
            harmony.Patch(AccessTools.Method(typeof(BotsGroup), nameof(BotsGroup.IsAlly), new[] { typeof(IPlayer) }), postfix: new HarmonyMethod(typeof(ISBEventHostility), nameof(ISBEventHostility.ChaosAlliance)) { priority = Priority.Last });
            harmony.Patch(AccessTools.Method(typeof(EFT.UI.SessionEnd.KillListVictim), "Show"), postfix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(DisplayRole)));
            harmony.Patch(AccessTools.Method(typeof(BossLocationSpawn), "Init"), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(PrepareWave)));
            harmony.Patch(AccessTools.Method(typeof(BossLocationSpawn), "ParseMainTypesTypes"), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(PrepareWave)));
            harmony.Patch(AccessTools.Method(typeof(BossLocationSpawn), "ParseDifficulty"), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(PrepareWave)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(BossLocationSpawn), "ParseEscortDif"), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(PrepareWave)) { priority = Priority.First });
            harmony.Patch(AccessTools.Method(typeof(BossLocationSpawn), "Copy"), postfix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(CopyWave)));
            harmony.Patch(AccessTools.Method(typeof(BossLocationSpawn), "GetUsingTypes"), postfix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(FilterPreload)));
            harmony.Patch(AccessTools.Method(typeof(NativeSpawner), "Spawn"), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(SpawnBoss)));
#if ISB_SPT41
            string supportsMethod = "SpawnBossSupports", escortMethod = "SpawnEscort";
#else
            string supportsMethod = "method_5", escortMethod = "method_6";
#endif
            harmony.Patch(AccessTools.Method(typeof(NativeSpawner), supportsMethod), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(SpawnSupports)));
            harmony.Patch(AccessTools.Method(typeof(NativeSpawner), escortMethod), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(SpawnEscort)));
            harmony.Patch(AccessTools.Method(typeof(EFT.UI.SessionEnd.SessionResultKillList), "Show", new[] { typeof(Diz.Binding.UpdatableBindableList<VictimStats>), typeof(EFT.InventoryLogic.DogtagComponent[]) }), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(RefreshGhostNames)));
            InitializeDevTools();
            GameWorld.OnDispose += ISBEventHostility.ClearVariants;
            _initialized = true;
        }

        private static readonly AsyncLocal<string> DebugVariant = new AsyncLocal<string>();
        private static void InitializeDevTools()
        {
#if ISB_SPT41
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.sipto.devtools", out var debug) ||
                !BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.blackdiv.tacticaltoaster")) return;
            var harmony = new Harmony("com.isb.ghostvariant.devtools");
            try
            {
                var type = debug.Instance.GetType().Assembly.GetType("DevTools.BotSpawn", true);
                harmony.Patch(AccessTools.Method(type, "AllTypes"), postfix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(DebugTypesPostfix)));
                harmony.Patch(AccessTools.Method(type, "Resolve"), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(DebugResolvePrefix)));
                foreach (string method in new[] { "SpawnSquad", "SpawnLoners", "SpawnBossSquad" })
                    harmony.Patch(AccessTools.Method(type, method), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(DebugContextPrefix)), finalizer: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(DebugContextFinalizer)));
                harmony.Patch(AccessTools.Method(typeof(NativeCreation), "Create", new[] { typeof(IGetProfileData), typeof(IBotCreator), typeof(int), typeof(NativeToken) }), prefix: new HarmonyMethod(typeof(GhostBotSpawnBridge), nameof(DebugCreationPrefix)));
                if (AccessTools.Field(debug.Instance.GetType(), "SpawnType").GetValue(null) is BepInEx.Configuration.ConfigEntry<string> entry &&
                    entry.Description.AcceptableValues is BepInEx.Configuration.AcceptableValueList<string> acceptable)
                    AccessTools.Field(acceptable.GetType(), "<AcceptableValues>k__BackingField").SetValue(acceptable, acceptable.AcceptableValues.Concat(GhostBotVariant.Names).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray());
            }
            catch (Exception error)
            {
                harmony.UnpatchSelf();
                Plugin.LogSource.LogError("ISB variant selector compatibility failed: " + error.GetBaseException().Message);
            }
#endif
        }

        private static void DebugTypesPostfix(ref string[] __result) => __result = (__result ?? Array.Empty<string>()).Concat(GhostBotVariant.Names).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray();
        private static bool DebugResolvePrefix(string name, ref WildSpawnType role, ref EPlayerSide side, ref bool __result)
        {
            if (!GhostBotVariant.Is(name)) return true;
            role = (WildSpawnType)GhostBotVariant.BaseRoleValue(name);
            side = EPlayerSide.Savage;
            __result = true;
            return false;
        }

        private static void DebugContextPrefix(string name, out string __state)
        {
            __state = DebugVariant.Value;
            DebugVariant.Value = GhostBotVariant.CanonicalName(name);
        }

        private static void DebugContextFinalizer(string __state) => DebugVariant.Value = __state;
        private static bool DebugCreationPrefix(IGetProfileData profileData, IBotCreator botCreator, int count, NativeToken token, ref Task<NativeCreation> __result)
        {
            var variant = DebugVariant.Value;
            if (variant == null || !profileData.TryGetRole(out var role, out _) || (int)role != GhostBotVariant.BaseRoleValue(variant)) return true;
            if (profileData.SpawnParams != null) profileData.SpawnParams.Id_spawn = "isb_debug_variant";
            var data = new NativeCreation(profileData) { _tokenGetter = token, _botCreator = botCreator };
            __result = LoadVariant(data, botCreator as NativeCreator, count, variant, token.GetCancelToken());
            return false;
        }

        private static void PrepareWave(BossLocationSpawn __instance)
        {
            var supports = __instance.Supports ?? new WildSpawnSupports[0];
            bool existing = Waves.TryGetValue(__instance, out var flags);
            flags = flags ?? new WaveFlags();
            flags.Boss = GhostBotVariant.FromMarker(__instance.BossDifficult, __instance.BossName) ?? flags.Boss;
            flags.Escort = GhostBotVariant.FromMarker(__instance.BossEscortDifficult, __instance.BossEscortType) ?? flags.Escort;
            var variants = new string[supports.Length];
            for (int i = 0; i < supports.Length; i++)
                variants[i] = supports[i]?.BossEscortDifficult?.Select(marker => GhostBotVariant.FromMarker(marker, supports[i].BossEscortType.ToString())).FirstOrDefault(value => value != null)
                    ?? (flags.Supports != null && i < flags.Supports.Length ? flags.Supports[i] : null);
            flags.Supports = variants;
            if (flags.Boss == null && flags.Escort == null && !variants.Any(value => value != null))
                return;
            if (!existing) Waves.Add(__instance, flags);
            if (flags.Boss != null) __instance.BossDifficult = "impossible";
            if (flags.Escort != null) __instance.BossEscortDifficult = "impossible";
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null && supports[i] != null)
                    supports[i].BossEscortDifficult = new[] { "impossible" };
        }

        private static void CopyWave(BossLocationSpawn __instance, BossLocationSpawn __result)
        {
            if (__result != null && Waves.TryGetValue(__instance, out var flags))
                Waves.GetValue(__result, _ => flags);
        }

        private static void FilterPreload(BossLocationSpawn __instance, ref List<NativeWave> __result)
        {
            if (!Waves.TryGetValue(__instance, out var flags) || __result == null)
                return;
            for (int i = __result.Count - 1; i >= 0; i--)
                if (i == 0 ? flags.Boss != null : __instance.GetEscors() != null ? i - 1 < flags.Supports.Length && flags.Supports[i - 1] != null : flags.Escort != null)
                    __result.RemoveAt(i);
        }

        private static IBotCreator Creator(NativeSpawner spawner) => 
#if ISB_SPT41
        spawner._botCreator;
#else
            spawner.IBotCreator;
#endif
        private static BotSpawner Spawner(NativeSpawner spawner) => 
#if ISB_SPT41
        spawner._spawner;
#else
            spawner.BotSpawner_0;
#endif
        private static bool SpawnBoss(NativeSpawner __instance, BossLocationSpawn wave, BotSpawnParams spawnParams, NativeCreation data, ref Task __result)
        {
            if (data != null || Variant(wave, null, 0) == null)
                return true;
            __result = SpawnVariantBoss(__instance, wave, spawnParams);
            return false;
        }

        private static async Task SpawnVariantBoss(NativeSpawner spawner, BossLocationSpawn wave, BotSpawnParams spawnParams)
        {
            var data = await CreateWaveData(new NativeProfileData(EPlayerSide.Savage, wave.BossType, wave.BossDif, wave.Time, spawnParams), Creator(spawner), 1, Spawner(spawner), wave, null, 0);
            if (data != null && !data.SpawnStopped)
                await spawner.Spawn(wave, spawnParams, data);
        }

        private static bool SpawnSupports(NativeSpawner __instance, BotZone zone, int followersCount, BotSpawnParams spawnParams, BossLocationSpawn wave, List<ISpawnPoint> pointsToSpawn, bool forceSpawn, ref Task __result)
        {
            if (wave == null || wave.GetEscors() != null || Variant(wave, null, 1) == null)
                return true;
            __result = SpawnVariantSupports(__instance, zone, followersCount, spawnParams, wave, pointsToSpawn, forceSpawn);
            return false;
        }

        private static async Task SpawnVariantSupports(NativeSpawner spawner, BotZone zone, int count, BotSpawnParams spawnParams, BossLocationSpawn wave, List<ISpawnPoint> points, bool forceSpawn)
        {
            if (count <= 0)
                return;
            var data = await CreateWaveData(new NativeProfileData(EPlayerSide.Savage, wave.EscortType, wave.EscortDif, wave.Time, spawnParams), Creator(spawner), count, Spawner(spawner), wave, null, 1);
            if (data != null && !data.SpawnStopped)
                Spawner(spawner).TryToSpawnInZoneAndDelay(zone, data, false, true, points, forceSpawn);
        }

        private static bool SpawnEscort(NativeSpawner __instance, BotZone zone, EPlayerSide side, BossLocationSpawn wave, List<BossLocationSpawnSubData> escorts, BotSpawnParams spawnParams, List<ISpawnPoint> pointsToSpawn, bool forceSpawn, ref Task __result)
        {
            if (wave == null || escorts == null || !escorts.Any(escort => Variant(wave, escort, 2) != null))
                return true;
            __result = SpawnVariantEscorts(__instance, zone, side, wave, escorts, spawnParams, pointsToSpawn, forceSpawn);
            return false;
        }

        private static async Task SpawnVariantEscorts(NativeSpawner spawner, BotZone zone, EPlayerSide side, BossLocationSpawn wave, List<BossLocationSpawnSubData> escorts, BotSpawnParams spawnParams, List<ISpawnPoint> points, bool forceSpawn)
        {
            if (points != null && wave.EscortCount > points.Count)
                points = null;
            foreach (var escort in escorts)
            {
                List<ISpawnPoint> positions = null;
                if (points != null)
                {
                    positions = new List<ISpawnPoint>();
                    for (int i = 0; i < escort.BossEscortAmount && points.Count > 0; i++)
                    {
                        positions.Add(points[0]);
                        points.RemoveAt(0);
                    }

                    if (positions.Count != escort.BossEscortAmount)
                        positions = null;
                }

                var data = await CreateWaveData(new NativeProfileData(side, escort.BossEscortType, escort.EscortDifficulty, wave.Time, spawnParams), Creator(spawner), escort.BossEscortAmount, Spawner(spawner), wave, escort, 2);
                if (data == null || data.SpawnStopped)
                    return;
                Spawner(spawner).TryToSpawnInZoneAndDelay(zone, data, false, true, positions, forceSpawn);
                await Task.Yield();
            }
        }

        private static Task<NativeCreation> CreateWaveData(IGetProfileData data, IBotCreator creator, int count, NativeToken token, BossLocationSpawn wave, BossLocationSpawnSubData escort, int mode)
        {
            string variant = Variant(wave, escort, mode);
            if (variant == null)
                return NativeCreation.Create(data, creator, count, token);
            int role = mode == 0 ? (int)wave.BossType : mode == 1 ? (int)wave.EscortType : (int)escort.BossEscortType;
            if (role != GhostBotVariant.BaseRoleValue(variant))
                throw new InvalidOperationException("ISB GhostBuster wave has an unsupported native role.");
            var creation = new NativeCreation(data)
            {
#if ISB_SPT41
                _tokenGetter = token,
                _botCreator = creator
#else
                Ginterface22_0 = token, IBotCreator = creator
#endif
            };
            return LoadVariant(creation, creator as NativeCreator, count, variant, token.GetCancelToken());
        }

        private static bool IsVariant(BossLocationSpawn wave, BossLocationSpawnSubData escort, int mode) => Variant(wave, escort, mode) != null;
        private static string Variant(BossLocationSpawn wave, BossLocationSpawnSubData escort, int mode)
        {
            if (wave == null || !Waves.TryGetValue(wave, out var flags))
                return null;
            if (mode == 0)
                return flags.Boss;
            if (mode == 1)
                return flags.Escort;
            int index = ReferenceEquals(escort, null) ? -1 : wave.GetEscors()?.IndexOf(escort) ?? -1;
            return index >= 0 && index < flags.Supports.Length ? flags.Supports[index] : null;
        }

        private static Task<Dictionary<string, string>> GhostNames = FetchGhostNames();
        private static Task<Dictionary<string, string>> FetchGhostNames() => Task.Run(() => JsonConvert.DeserializeObject<Dictionary<string, string>>(RequestHandler.GetJson("/ISB/ghostbots/names")) ?? new Dictionary<string, string>());
        internal static Task<Dictionary<string, string>> FetchVoiceIdentities() => FetchGhostNames();
        private static void RefreshGhostNames() => GhostNames = FetchGhostNames();
        private sealed class VictimDisplay { public VictimDisplay() { } internal VictimStats Victim; }
        private static bool _roleFailureLogged;
        private static readonly ConditionalWeakTable<EFT.UI.SessionEnd.KillListVictim, VictimDisplay> VictimDisplays = new ConditionalWeakTable<EFT.UI.SessionEnd.KillListVictim, VictimDisplay>();
        private static async void DisplayRole(EFT.UI.SessionEnd.KillListVictim __instance, VictimStats victim)
        {
            if (victim == null || __instance == null)
                return;
            var display = VictimDisplays.GetOrCreateValue(__instance);
            display.Victim = victim;
            try
            {
                var names = await GhostNames;
                if (__instance == null || !ReferenceEquals(display.Victim, victim)) return;
                if (!names.TryGetValue("profile|" + victim.ProfileId, out var key) && !names.TryGetValue((int)victim.Role + "|" + victim.Name, out key)) return;
                if (AccessTools.Field(__instance.GetType(), "_faction")?.GetValue(__instance)is TMPro.TMP_Text text && ISBLocale.TryTranslate(key, out var translated))
                    text.text = translated.ToUpperInvariant();
            }
            catch (Exception error)
            {
                if (!_roleFailureLogged)
                {
                    _roleFailureLogged = true;
                    Plugin.LogSource.LogError("ISB variant role display failed: " + error.GetBaseException().Message);
                }
            }
        }

        private static Profile[] ParseProfiles(string json, int requested, string variant)
        {
            var profiles = ParseDescriptors(json, requested).Select(descriptor => new Profile(descriptor)).ToArray();
            if (profiles.Any(profile => profile?.Info?.Settings == null || (int)profile.Info.Settings.Role != GhostBotVariant.BaseRoleValue(variant)))
                throw new InvalidOperationException("ISB GhostBuster received invalid native profiles.");
            return profiles;
        }

        private static NativeDescriptor[] ParseDescriptors(string json, int requested)
        {
            var response = JObject.Parse(json);
            if ((int? )response["err"] != 0 || !(response["data"] is JArray array))
                throw new InvalidOperationException("ISB GhostBuster generation failed: " + (string)response["errmsg"]);
            var descriptors = NativeJson.ParseJsonTo<NativeDescriptor[]>(array.ToString(Formatting.None), Array.Empty<JsonConverter>());
            if (descriptors == null || descriptors.Length != requested)
                throw new InvalidOperationException("ISB GhostBuster received invalid native profiles.");
            return descriptors;
        }

        private static string SerializeRequest(string variant, int count)
        {
            return JsonConvert.SerializeObject(new { conditions = new[] { new { Role = GhostBotVariant.CanonicalName(variant), Difficulty = "impossible", Limit = count } } });
        }

        private static async Task<NativeCreation> LoadVariant(NativeCreation data, NativeCreator creator, int count, string variant, CancellationToken cancellationToken)
        {
            var profiles = new List<Profile>();
            while (profiles.Count < count)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (data.SpawnStopped)
                    return null;
                int requested = Math.Min(32, count - profiles.Count);
                var payload = SerializeRequest(variant, requested);
                var json = await RequestHandler.PostJsonAsync("/ISB/ghostbots/generate", payload);
                cancellationToken.ThrowIfCancellationRequested();
                if (data.SpawnStopped)
                    return null;
                profiles.AddRange(ParseProfiles(json, requested, variant));
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (data.SpawnStopped)
                return null;
            data.AddProfiles(profiles);
            ISBEventHostility.RememberVariants(profiles, variant);
            ISBChaosVoice.Remember(profiles, variant);
            var resources = profiles.SelectMany(profile => profile.GetAllPrefabPaths(allCustomization: false)).Distinct().ToArray();
#if ISB_SPT41
            if (creator != null)
                creator._bundlesLoading++;
#else
            if (creator != null) creator.Int_0++;
#endif
            try
            {
#if ISB_SPT41
                await Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(ObjectsFactory.PoolsCategory.Raid, ObjectsFactory.AssemblyType.Local, resources, Diz.Jobs.JobYieldPriority.General, null, cancellationToken);
#else
                await Singleton<PoolManagerClass>.Instance.LoadBundlesAndCreatePools(PoolManagerClass.PoolsCategory.Raid, PoolManagerClass.AssemblyType.Local,
                    resources, JobPriorityClass.General, null, cancellationToken);
#endif
            }
            finally
            {
#if ISB_SPT41
                if (creator != null)
                    creator._bundlesLoading--;
#else
                if (creator != null) creator.Int_0--;
#endif
            }

            return data;
        }
    }
}
