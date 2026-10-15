using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Console.Core;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
#if ISB_SPT41
using IdentityOwner = EFT.IObserverToPlayerBridge;
using DebugSnapshot = DebugBotStruct;
#else
using IdentityOwner = IPlayerOwner;
using DebugSnapshot = ActorDataStruct;

#endif
namespace ISBSSOFExtra
{
    internal static class Scp173DebugBridge
    {
        private static Harmony _harmony;
        internal static void Enable(ManualLogSource log)
        {
            if (_harmony != null)
                return;
            var harmony = new Harmony("com.samc137.isbsofextras.scp173.identity");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(GameWorld), "GetEverExistedBridgeByProfileID", new[] { typeof(string) }), postfix: new HarmonyMethod(typeof(Scp173DebugBridge), nameof(ResolveIdentity)));
                harmony.Patch(AccessTools.Method(typeof(GameWorld), "GetAlivePlayerBridgeByProfileID", new[] { typeof(string) }), postfix: new HarmonyMethod(typeof(Scp173DebugBridge), nameof(ResolveAliveIdentity)));
                if (Chainloader.PluginInfos.ContainsKey("xyz.drakia.botdebug"))
                    harmony.Patch(AccessTools.Method(typeof(BotSpawner), "GetBotDebugData"), prefix: new HarmonyMethod(typeof(Scp173DebugBridge), nameof(RequireDebugObserver)));
#if ISB_SPT41
                var botDeath = AccessTools.Method(typeof(BotOwner), "OnBeenKilledByAggressor");
#else
                var botDeath = AccessTools.Method(typeof(BotOwner), "method_3");
#endif
                harmony.Patch(botDeath, prefix: new HarmonyMethod(typeof(Scp173DebugBridge), nameof(PhysicalAggressor)));
                var screen = typeof(EFT.UI.SessionEnd.SessionResultExitStatus);
                foreach (var method in screen.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (method.Name == "Show" && method.GetParameters().Length == 7)
                        harmony.Patch(method, postfix: new HarmonyMethod(typeof(Scp173DebugBridge), nameof(HideAccountActions)));
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var service = assembly.GetType("DragonDenDevTool.Features.InRaid.Services.BotService", false);
                    if (service == null)
                        continue;
                    Patch(harmony, service, "ListBots", nameof(AppendEntity), false, new[] { typeof(string) }, typeof(List<(string, string, string)>));
                    Patch(harmony, service, "RegisterBotCount", nameof(AppendCount), false, Type.EmptyTypes, typeof(int));
                    Patch(harmony, service, "KillBotById", nameof(KillEntity), true, new[] { typeof(string) }, typeof(bool));
                    Patch(harmony, service, "TeleportBotToMeById", nameof(BringEntity), true, new[] { typeof(string) }, typeof(void));
                    Patch(harmony, service, "TeleportMeToBot", nameof(GoToEntity), true, new[] { typeof(string) }, typeof(void));
                    Patch(harmony, service, "KillAllBots", nameof(KillAll), false, Type.EmptyTypes, typeof(int));
                    Patch(harmony, service, "TeleportAllBotsToMe", nameof(BringAll), false, new[] { typeof(Player), typeof(float) }, typeof(void));
                }

                _harmony = harmony;
            }
            catch (Exception exception)
            {
                harmony.UnpatchSelf();
                log.LogError("ISB SCP-173 identity integration unavailable: " + exception.GetBaseException().Message);
            }
        }

        private static void Patch(Harmony harmony, Type owner, string name, string callback, bool prefix, Type[] arguments, Type result)
        {
            var method = AccessTools.Method(owner, name, arguments);
            if (method == null || method.ReturnType != result)
                return;
            var patch = new HarmonyMethod(typeof(Scp173DebugBridge), callback);
            harmony.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }

        private static void ResolveIdentity(string __0, ref IdentityOwner __result)
        {
            if (__result == null && Scp173Registry.Find(__0)is Scp173Identity identity)
                __result = identity;
        }

        private static void ResolveAliveIdentity(string __0, ref IdentityOwner __result)
        {
            if (__result == null && Scp173Registry.Find(__0)is Scp173Identity identity && identity.Active)
                __result = identity;
        }

        private static bool PhysicalAggressor(IPlayer __1) => !(__1 is Scp173Identity);
        private static bool RequireDebugObserver(IPlayer __0, ref DebugSnapshot __result)
        {
            if (__0 != null)
                return true;
            __result = default;
            return false;
        }
        private static void HideAccountActions(object __instance, Profile __0)
        {
            var aggressor = __0?.EftStats?.Aggressor;
            if (aggressor == null || aggressor.Name != "SCP-173" || aggressor.AccountId != "0")
                return;
            foreach (string name in new[]
            {
                "_showProfileButton",
                "_reportPanel"
            }

            )
                if (AccessTools.Field(__instance.GetType(), name)?.GetValue(__instance)is Component component)
                    component.gameObject.SetActive(false);
        }

        private static void AppendEntity(string __0, ref List<(string, string, string)> __result)
        {
            var entity = Scp173Registry.Current;
            if (entity?.Active != true || __result == null)
                return;
            string filter = (__0 ?? "").Trim();
            if (filter.Length != 0 && entity.ProfileId.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && entity.Nickname.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 && entity.EntityType.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                return;
            foreach (var entry in __result)
                if (entry.Item1 == entity.ProfileId)
                    return;
            __result.Add((entity.ProfileId, entity.Nickname, entity.EntityType));
        }

        private static void AppendCount(ref int __result)
        {
            if (Scp173Registry.Current?.Active == true)
                __result++;
        }

        private static bool KillEntity(string __0, ref bool __result)
        {
            var entity = Scp173Registry.Find(__0);
            if (entity == null)
                return true;
            __result = entity.Kill();
            return false;
        }

        private static bool BringEntity(string __0)
        {
            var entity = Scp173Registry.Find(__0);
            if (entity == null)
                return true;
            entity.TeleportTo(Singleton<GameWorld>.Instance?.MainPlayer);
            return false;
        }

        private static bool GoToEntity(string __0)
        {
            var entity = Scp173Registry.Find(__0);
            if (entity == null)
                return true;
            entity.TeleportPlayerTo(Singleton<GameWorld>.Instance?.MainPlayer);
            return false;
        }

        private static void KillAll(ref int __result)
        {
            if (Scp173Registry.Current?.Kill() == true)
                __result++;
        }

        private static void BringAll(Player __0, float __1)
        {
            Scp173Registry.Current?.TeleportTo(__0, Mathf.Max(5f, __1));
        }

        internal static void Dispose()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }
    }

    public sealed class Scp173Commands
    {
        [ConsoleCommand("isb_scp173", description: "Shows the registered SCP-173 identity")]
        public static void List()
        {
            var entity = Scp173Registry.Current;
            ConsoleScreen.Log(entity?.Active == true ? entity.Nickname + " | " + entity.ProfileId + " | " + entity.Position : "ISB: no active SCP-173.");
        }

        [ConsoleCommand("isb_scp173_kill", description: "Removes the active SCP-173")]
        public static void Kill() => ConsoleScreen.Log(Scp173Registry.Current?.Kill() == true ? "ISB: SCP-173 removal requested." : "ISB: SCP-173 removal unavailable.");
        [ConsoleCommand("isb_scp173_bring", description: "Brings SCP-173 near the local player")]
        public static void Bring() => ConsoleScreen.Log(Scp173Registry.Current?.TeleportTo(Singleton<GameWorld>.Instance?.MainPlayer) == true ? "ISB: SCP-173 teleport requested." : "ISB: SCP-173 teleport unavailable.");
        [ConsoleCommand("isb_scp173_goto", description: "Teleports the local player near SCP-173")]
        public static void GoTo() => ConsoleScreen.Log(Scp173Registry.Current?.TeleportPlayerTo(Singleton<GameWorld>.Instance?.MainPlayer) == true ? "ISB: player teleported near SCP-173." : "ISB: SCP-173 teleport unavailable.");
    }
}
