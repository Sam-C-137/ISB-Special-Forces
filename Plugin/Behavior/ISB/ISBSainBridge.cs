using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using EFT;
#if SPT40
using LayersMaskController = LayerMaskClass;
using NativeLookData = LookAllDataClass;
#else
using NativeLookData = LookAllData;
#endif
using HarmonyLib;
using UnityEngine;

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBSainBridge
    {
        private static bool _installed;
        private static readonly Dictionary<Type, Func<object, BotOwner>> Owners = new Dictionary<Type, Func<object, BotOwner>>();
        private static readonly Dictionary<Type, Dictionary<string, Func<object, object>>> Members = new Dictionary<Type, Dictionary<string, Func<object, object>>>();
        private static FieldInfo _fileSettings;
        private static MethodInfo _coreApply;
        private static PropertyInfo _settings;
        private static PropertyInfo _loadedPreset;
        private static MethodInfo _getSain, _dangerPoint;
        private static MethodInfo _checkEnemy;
        private static PropertyInfo _goalEnemy;
        private sealed class HearingState { internal float NextReport; }
        private static readonly ConditionalWeakTable<BotOwner, HearingState> Hearing = new ConditionalWeakTable<BotOwner, HearingState>();
#if !SPT40
        private static FieldInfo _targetChoice;
#endif
        internal static bool Installed => _installed;

        internal static void Install()
        {
            if (_installed || !Chainloader.PluginInfos.TryGetValue("me.sol.sain", out var plugin) || plugin?.Instance == null)
                return;
            var assembly = plugin.Instance.GetType().Assembly;
            var harmony = new Harmony("com.isb.sain.attributes");
            try
            {
                var enabled = assembly.GetType("SAIN.SAINEnableClass", true);
                _getSain = AccessTools.Method(enabled, "GetSAIN");
                harmony.Patch(AccessTools.Method(enabled, "IsBotExcluded", new[] { typeof(BotOwner) }),
                    prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(ExclusionPrefix)));
                foreach (string name in new[] { "SAINDecisionClass", "SAINSelfActionClass", "SAINBotUnstuckClass", "SAINMoverClass", "SAINSteeringClass" })
                {
                    var type = assembly.GetTypes().Single(t => t.Name == name);
                    var update = type.GetMethod("ManualUpdate", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    if (update == null) throw new MissingMethodException(type.FullName, "ManualUpdate");
                    harmony.Patch(update, prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(AutonomyPrefix)));
                }
                var aimSteering = assembly.GetTypes().Single(t => t.Name == "BotAimSteerPatch");
                harmony.Patch(AccessTools.Method(aimSteering, "Patch"),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(NativeAimPostfix)));
                var activation = assembly.GetTypes().Single(t => t.Name == "SAINActivationClass");
                harmony.Patch(AccessTools.Method(activation, "SetActiveLayer"),
                    prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(AutonomyPrefix)));
                var component = assembly.GetTypes().Single(t => t.Name == "BotComponent");
                harmony.Patch(AccessTools.Method(component, "HandleDumbShit"),
                    prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(AutonomyPrefix)));
                harmony.Patch(AccessTools.PropertyGetter(component, "DistanceToAimTarget"),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(AimDistancePostfix)));
                var enemies = assembly.GetTypes().Single(t => t.Name == "SAINEnemyController");
                _checkEnemy = AccessTools.Method(enemies, "CheckAddEnemy", new[] { typeof(IPlayer) });
                _goalEnemy = AccessTools.Property(enemies, "GoalEnemy");
                if (_checkEnemy == null || _goalEnemy?.GetSetMethod(true) == null)
                    throw new MissingMemberException("SAIN attribute target context");
                harmony.Patch(AccessTools.Method(typeof(BotEnemiesController), "AddNew"),
                    prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(EnemyInfoPrefix)));
                harmony.Patch(AccessTools.Method(enemies, "ManualUpdate"),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(EnemyContextPostfix)));
                var look = assembly.GetTypes().Single(t => t.Name == "SAINBotLookClass");
                harmony.Patch(AccessTools.Method(look, "UpdateLookData"),
                    prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(LookPrefix)),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(LookPostfix)));
                var hearing = assembly.GetTypes().Single(t => t.Name == "EnemyHearing");
                harmony.Patch(AccessTools.Method(hearing, "SetHeard"),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(HearingPostfix)));
                foreach (string name in new[] { "ShallKnowEnemyPatch", "ShallKnowEnemyLatePatch" })
                {
                    var knowledge = assembly.GetTypes().Single(t => t.Name == name);
                    harmony.Patch(AccessTools.Method(knowledge, "PatchPostfix"),
                        prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(KnowledgePrefix)));
                }
                var info = assembly.GetTypes().Single(t => t.Name == "SAINBotInfoClass");
                _fileSettings = AccessTools.Field(info, "_fileSettings");
                _settings = AccessTools.Property(info, "FileSettings");
                if (_fileSettings == null || _settings == null) throw new MissingMemberException("SAIN bot settings");
                var core = _settings.PropertyType.GetField("Core")?.FieldType ?? _settings.PropertyType.GetProperty("Core")?.PropertyType;
                _coreApply = core?.GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
                if (_coreApply == null)
                    _coreApply = assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                        .Single(m => m.Name == "Apply" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == core);
                harmony.Patch(AccessTools.Method(info, "ConfigureBot"),
                    prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(ConfigurePrefix)),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(ConfigurePostfix)));
                _loadedPreset = AccessTools.Property(plugin.Instance.GetType(), "LoadedPreset");
                if (_loadedPreset == null) throw new MissingMemberException("SAIN active preset");
                var grenadePatch = assembly.GetTypes().Single(t => t.Name == "GrenadeThrownActionPatch");
                _dangerPoint = assembly.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                    .Single(m => m.Name == "DangerPoint" && m.GetParameters().Length == 3);
                harmony.Patch(AccessTools.Method(grenadePatch, "PatchPrefix"),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(GrenadePostfix)));
                var panicPatch = assembly.GetTypes().Single(t => t.Name == "SetPanicPointPatch");
                harmony.Patch(AccessTools.Method(panicPatch, "PatchPrefix"),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(PanicPostfix)));
#if SPT40
                var part = assembly.GetTypes().Single(t => t.Name == "BodyPartToShootPatch");
                harmony.Patch(AccessTools.Method(part, "Patch"),
                    postfix: new HarmonyMethod(typeof(ISBSainBridge), nameof(CenterMassPostfix)));
#else
                var selector = assembly.GetTypes().Single(t => t.Name == "EnemyAimTarget");
                if (AccessTools.Property(selector, "BotOwner") == null) throw new MissingMemberException("SAIN aim owner");
                _targetChoice = selector.GetField("_chosen", BindingFlags.NonPublic | BindingFlags.Instance);
                if (_targetChoice == null) throw new MissingMemberException("SAIN aim selection");
                harmony.Patch(AccessTools.Method(selector, "GetPointToShoot"),
                    prefix: new HarmonyMethod(typeof(ISBSainBridge), nameof(AimSelectorPrefix)));
#endif
                _installed = true;
            }
            catch
            {
                harmony.UnpatchSelf();
                throw;
            }
        }

        private static BotOwner Owner(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (!Owners.TryGetValue(type, out var getter))
            {
                getter = obj => Read(obj, "BotOwner") as BotOwner ?? Read(obj, "BotOwner_0") as BotOwner ??
                    Read(obj, "Owner") as BotOwner ?? Read(obj, "_owner") as BotOwner;
                Owners.Add(type, getter);
            }
            return getter(value);
        }

        private static bool Controls(BotOwner bot) => ISBRuntimeBindings.IsAuthority() && ISBNativeBrain.Owns(bot);
        private static bool Eligible(BotOwner bot) => ISBRuntimeBindings.IsAuthority() && ISBRuntimeBindings.IsEligible(bot);

        private static bool ExclusionPrefix(BotOwner __0, ref bool __result)
        {
            if (!Controls(__0) && !Eligible(__0)) return true;
            __result = false;
            return false;
        }

        private static bool AutonomyPrefix(object __instance) => !Controls(Owner(__instance));

        private static void NativeAimPostfix(object __0, ref bool __result)
        {
            if (Controls(Owner(__0))) __result = true;
        }

        private static bool KnowledgePrefix(object __0) => !Controls(Owner(__0));

        private static void LookPrefix(object __instance, NativeLookData __0, out bool __state)
        {
            __state = Controls(Owner(__instance)) && __0.ShallRecalcGoal;
        }

        private static void LookPostfix(object __instance, bool __state)
        {
            if (__state) Owner(__instance).CalcGoal();
        }

        private static void HearingPostfix(object __instance, object __result)
        {
            var bot = Owner(__instance);
            if (__result == null || !Controls(bot) || bot.BotsGroup == null)
                return;
            var source = Read(__instance, "EnemyPlayer") as IPlayer;
            if (source?.HealthController?.IsAlive != true || !bot.BotsGroup.IsPlayerEnemy(source) ||
                !(Read(__result, "Position") is Vector3 position))
                return;
            var state = Hearing.GetValue(bot, _ => new HearingState());
            if (Time.time < state.NextReport) return;
            state.NextReport = Time.time + 1f;
            ISBThreatMemory.Heard(bot, source, position);
            if (bot.Memory?.GoalEnemy != null) return;
            bool danger = Read(__result, "IsDanger") as bool? == true;
            var place = bot.BotsGroup.AddPointToSearch(position, danger ? 5550f : 1f, bot);
            if (place == null) return;
            if (danger) bot.Memory.SetPanicPoint(place, true);
            bot.CalcGoal();
        }

        private static void AimDistancePostfix(object __instance, ref float __result)
        {
            var bot = Owner(__instance);
            if (!Controls(bot)) return;
            var enemy = bot.Memory?.GoalEnemy;
            __result = enemy?.Person?.HealthController?.IsAlive == true ?
                Vector3.Distance(bot.Position, enemy.EnemyLastPositionReal) : float.MaxValue;
        }

        private static void EnemyContextPostfix(object __instance)
        {
            var bot = Owner(__instance);
            if (!Controls(bot)) return;
            var person = bot.Memory?.GoalEnemy?.Person;
            var enemy = person?.HealthController?.IsAlive == true ? _checkEnemy.Invoke(__instance, new object[] { person }) : null;
            if (!ReferenceEquals(_goalEnemy.GetValue(__instance, null), enemy))
            {
                _goalEnemy.SetValue(__instance, null, null);
                if (enemy != null) _goalEnemy.SetValue(__instance, enemy, null);
            }
        }

        private static bool EnemyInfoPrefix(BotEnemiesController __instance, ref IPlayer __1, ref EnemyInfo __result)
        {
            var bot = Owner(__instance);
            if ((!Controls(bot) && !Eligible(bot)) || __1 == null) return true;
            __1 = ISBThreatMemory.Identity(bot, __1);
            if (__instance.EnemyInfos.TryGetValue(__1, out __result)) return false;
            var args = new object[] { bot.ProfileId, null };
            if (!(_getSain.Invoke(null, args) is bool present && present)) return true;
            var enemies = Read(Read(args[1], "EnemyController"), "Enemies") as System.Collections.IDictionary;
            if (enemies == null || !enemies.Contains(__1.ProfileId) ||
                !(Read(enemies[__1.ProfileId], "EnemyInfo") is EnemyInfo existing)) return true;
            __result = existing;
            return false;
        }

        private static void PanicPostfix(object __0, ref bool __result)
        {
            if (Controls(Owner(__0))) __result = true;
        }

        private static void GrenadePostfix(BotsController __0, Grenade __1, Vector3 __2, Vector3 __3, float __4)
        {
            if (!ISBRuntimeBindings.IsAuthority() || __1 == null) return;
            var point = (Vector3)_dangerPoint.Invoke(null, new object[] { __2, __3, __4 });
            if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z) ||
                float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                point = __1.transform.position;
            foreach (var bot in __0.Bots.BotOwners)
            {
                if (!ISBNativeBrain.Owns(bot)) continue;
                var args = new object[] { bot.ProfileId, null };
                if (_getSain.Invoke(null, args) is bool present && present)
                    bot.BewareGrenade.AddGrenadeDanger(point, __1);
            }
        }

        private static void AimSelectorPrefix(object __instance, ref bool __0)
        {
            if (Controls(Owner(__instance))) __0 = true;
        }

        private static void ConfigurePrefix(object __instance)
        {
            var bot = Owner(__instance);
            if (Controls(bot) || Eligible(bot)) _fileSettings.SetValue(__instance, null);
        }

        private static void ConfigurePostfix(object __instance)
        {
            var bot = Owner(__instance);
            if (!Controls(bot) && !Eligible(bot)) return;
            var settings = _settings.GetValue(__instance, null);
            var core = Read(settings, "Core");
            if (core != null)
            {
                if (_coreApply.IsStatic) _coreApply.Invoke(null, new[] { core, (object)bot.Settings.FileSettings });
                else _coreApply.Invoke(core, new[] { (object)bot.Settings.FileSettings });
            }
            if (bot.EnemiesController?.EnemyInfos != null)
                foreach (var enemy in bot.EnemiesController.EnemyInfos.Values) enemy.LastPartToShoot = null;
#if !SPT40
            if (_targetChoice != null && Read(Read(Read(__instance, "Bot"), "EnemyController"), "Enemies") is System.Collections.IDictionary enemies)
                foreach (var enemy in enemies.Values)
                {
                    var target = Read(enemy, "AimTarget");
                    if (target != null) _targetChoice.SetValue(target, null);
                }
#endif
        }

        private static object Read(object value, string name)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (!Members.TryGetValue(type, out var names)) Members.Add(type, names = new Dictionary<string, Func<object, object>>());
            if (!names.TryGetValue(name, out var getter))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                for (var current = type; current != null && getter == null; current = current.BaseType)
                {
                    var field = current.GetField(name, flags);
                    var property = current.GetProperty(name, flags);
                    if (field != null) getter = field.GetValue;
                    else if (property != null) getter = obj => property.GetValue(obj, null);
                }
                names.Add(name, getter = getter ?? (_ => null));
            }
            return getter(value);
        }

#if SPT40
        private static void CenterMassPostfix(ref Vector3 __0, EnemyInfo __1)
        {
            var bot = __1?.Owner;
            if (!Controls(bot) || !__1.IsVisible || !__1.CanShoot || bot.WeaponManager.UnderbarrelLauncherController.IsActive)
                return;
            var preset = _loadedPreset?.GetValue(null, null);
            var botSettings = Read(Read(preset, "BotSettings"), "SAINSettings") as System.Collections.IDictionary;
            object aiming = null;
            if (botSettings != null && botSettings.Contains(bot.Profile.Info.Settings.Role))
            {
                var group = botSettings[bot.Profile.Info.Settings.Role];
                var difficulties = Read(group, "Settings") as System.Collections.IDictionary;
                if (difficulties != null && difficulties.Contains(bot.Profile.Info.Settings.BotDifficulty))
                    aiming = Read(difficulties[bot.Profile.Info.Settings.BotDifficulty], "Aiming");
            }
            bool center = Read(Read(Read(preset, "GlobalSettings"), "Aiming"), "AimCenterMassGlobal") as bool? == true ||
                Read(aiming, "AimCenterMass") as bool? == true;
            var point = __1.GetBodyPartPosition();
            if (center && !Physics.Linecast(bot.WeaponRoot.position, point, LayersMaskController.HighPolyWithTerrainMask))
                __0 = point;
        }
#endif
    }
}
