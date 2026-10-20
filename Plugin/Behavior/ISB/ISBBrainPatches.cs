using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using HarmonyLib;
#if SPT40
using NativeActions = BotActionNodesClass;
using NativeAiming = GClass178;
using NativeNode = BotNodeAbstractClass;
#else
using NativeActions = AIActionsList;
using NativeAiming = Aiming;
using NativeNode = AICoreNode;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBBrainPatches
    {
        private static bool _installed;
        internal static void Install()
        {
            if (_installed)
                return;
            if (Enum.IsDefined(typeof(BotLogicDecision), (int)ISBNativeBrain.SuppressionAdvance) ||
                Enum.IsDefined(typeof(BotLogicDecision), (int)ISBNativeBrain.ContactAdvance))
                throw new InvalidOperationException("ISB action identifier is already in use.");
            var harmony = new Harmony("com.isb.nativebrain");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(StandartBotBrain), "Activate"),
                    transpiler: new HarmonyMethod(typeof(ISBBrainPatches), nameof(ActivateTranspiler)));
                harmony.Patch(AccessTools.Method(typeof(NativeActions), "ActionsList"),
                    postfix: new HarmonyMethod(typeof(ISBBrainPatches), nameof(ActionsPostfix)));
                harmony.Patch(AccessTools.Method(typeof(NativeActions), "CreateNode"),
                    prefix: new HarmonyMethod(typeof(ISBBrainPatches), nameof(CreateNodePrefix)));
                var moreBots = AccessTools.TypeByName("MoreBotsAPI.Patches.StandartBotBrainActivatePatch");
                var replacement = moreBots == null ? null : AccessTools.Method(moreBots, "PatchPrefix");
                if (replacement == null)
                    throw new InvalidOperationException("MoreBots brain activation adapter is unavailable.");
                harmony.Patch(replacement, prefix: new HarmonyMethod(typeof(ISBBrainPatches), nameof(MoreBotsPrefix)));
                var factory = AccessTools.Method(moreBots, "GetBaseBrain");
                if (factory == null)
                    throw new InvalidOperationException("MoreBots legacy brain factory is unavailable.");
                harmony.Patch(factory, prefix: new HarmonyMethod(typeof(ISBBrainPatches), nameof(LegacyBrainPrefix)));
                harmony.Patch(AccessTools.Method(typeof(BotBoss), nameof(BotBoss.OfferSelf)),
                    prefix: new HarmonyMethod(typeof(ISBBrainPatches), nameof(OfferFollowerPrefix)));
#if SPT40
                harmony.Patch(AccessTools.Method(typeof(BotMemoryClass), "AddEnemy"),
#else
                harmony.Patch(AccessTools.Method(typeof(BotMemory), "AddEnemy"),
#endif
                    prefix: new HarmonyMethod(typeof(ISBThreatMemory), nameof(ISBThreatMemory.AddEnemyPrefix)));
#if SPT40
                harmony.Patch(AccessTools.Method(typeof(BotMemoryClass), "method_8"),
#else
                harmony.Patch(AccessTools.Method(typeof(BotMemory), "GetHitInner"),
#endif
                    postfix: new HarmonyMethod(typeof(ISBThreatMemory), nameof(ISBThreatMemory.HitPostfix)));
                harmony.Patch(AccessTools.Method(typeof(BotEnemyChooser), nameof(BotEnemyChooser.FindDangerEnemy)),
                    postfix: new HarmonyMethod(typeof(ISBThreatMemory), nameof(ISBThreatMemory.ChoosePostfix)));
#if SPT40
                harmony.Patch(AccessTools.PropertySetter(typeof(GoalTargetClass), "GoalTarget"),
#else
                harmony.Patch(AccessTools.PropertySetter(typeof(BotGoalTarget), "GoalTarget"),
#endif
                    prefix: new HarmonyMethod(typeof(ISBSearchMemory), nameof(ISBSearchMemory.SelectPrefix)),
                    postfix: new HarmonyMethod(typeof(ISBSearchMemory), nameof(ISBSearchMemory.SelectPostfix)));
#if SPT40
                harmony.Patch(AccessTools.Method(typeof(NativeAiming), "method_0"),
#else
                harmony.Patch(AccessTools.Method(typeof(NativeAiming), "FindPointToShoot"),
#endif
                    prefix: new HarmonyMethod(typeof(ISBFireControl), nameof(ISBFireControl.AimPrefix)));
                harmony.Patch(AccessTools.Method(typeof(ShootData), "Shoot"),
                    prefix: new HarmonyMethod(typeof(ISBFireControl), nameof(ISBFireControl.ShootPrefix)));
                harmony.Patch(AccessTools.Method(typeof(ShootData), "ManualUpdate"),
                    postfix: new HarmonyMethod(typeof(ISBFireControl), nameof(ISBFireControl.UpdatePostfix)));
                harmony.Patch(AccessTools.Method(typeof(BotLay), "TryLay"),
                    prefix: new HarmonyMethod(typeof(ISBFireControl), nameof(ISBFireControl.PronePrefix)));
                _installed = true;
            }
            catch (Exception original)
            {
                try { harmony.UnpatchSelf(); }
                catch (Exception cleanup) { throw new AggregateException(original, cleanup); }
                throw;
            }
        }

        internal static IEnumerable<CodeInstruction> ActivateTranspiler(IEnumerable<CodeInstruction> source, ILGenerator generator)
        {
            var code = source.ToList();
            var getter = AccessTools.PropertyGetter(typeof(StandartBotBrain), nameof(StandartBotBrain.BaseBrain));
            int continuation = code.FindIndex(i => i.Calls(getter));
            if (continuation < 1 || code[continuation - 1].opcode != OpCodes.Ldarg_0 ||
                !code.Any(i => i.Calls(AccessTools.Method(typeof(NativeActions), "ActionsList"))))
                throw new InvalidOperationException("ISB brain activation layout is unsupported.");
            continuation--;
            var fallback = generator.DefineLabel();
            var resume = generator.DefineLabel();
            code[0].labels.Add(fallback);
            code[continuation].labels.Add(resume);
            var prefix = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ISBBrainPatches), nameof(TryCreate))),
                new CodeInstruction(OpCodes.Brfalse, fallback),
                new CodeInstruction(OpCodes.Br, resume)
            };
            return prefix.Concat(code);
        }

        internal static bool TryCreate(StandartBotBrain brain)
        {
#if SPT40
            var owner = brain.BotOwner_0;
#else
            var owner = brain._owner;
#endif
            if (!ISBRuntimeBindings.IsAuthority() || !ISBRuntimeBindings.IsEligible(owner) || ISBSainSquads.Selected(owner))
                return false;
            try
            {
                int role = (int)owner.Profile.Info.Settings.Role;
                int selected = ISBBrainSelection.Select(role, ISBBrainSelection.IsEventBot(owner));
                brain.BaseBrain = selected == ISBBrainSelection.Event ? (ISBNativeBrain)new BlackDivBrain(owner) : new ISBNativeBrain(owner);
                return true;
            }
            catch (Exception exception)
            {
                ISBRuntimeBindings.CriticalFailure(exception);
                return false;
            }
        }

        internal static bool MoreBotsPrefix(StandartBotBrain __0) => !(__0.BaseBrain is ISBNativeBrain);

        internal static void LegacyBrainPrefix(BotOwner __0, ref int __1)
        {
            if (ISBSainSquads.Selected(__0))
            {
                __1 = 9;
                return;
            }
            if (__1 == ISBNativeBrain.Identifier && __0?.Profile?.Info?.Settings != null &&
                WildSpawnTypeExtensions.IsISB(__0.Profile.Info.Settings.Role))
                __1 = 9;
        }

        internal static bool OfferFollowerPrefix(BotBoss __instance, BotOwner __0, ref bool __result)
        {
            if (!ISBNativeBrain.Owns(__instance.Owner) || !ISBNativeBrain.Owns(__0) ||
                ISBNativeSquad.SameFormation(__instance.Owner, __0))
                return true;
            __result = false;
            return false;
        }

        internal static void ActionsPostfix(BotOwner bot, Dictionary<BotLogicDecision, NativeNode> __result)
        {
            if (ISBNativeBrain.Owns(bot))
            {
                __result.Add(ISBNativeBrain.SuppressionAdvance, new ISBSuppressionAdvance(bot));
                __result.Add(ISBNativeBrain.ContactAdvance, new ISBContactAdvance(bot));
                __result[BotLogicDecision.suppressFire] = new ISBSuppressionAdvance(bot, false);
                __result[BotLogicDecision.search] = new ISBSearchNode(bot);
            }
        }

        internal static bool CreateNodePrefix(BotLogicDecision __0, BotOwner __1, ref NativeNode __result)
        {
            if (!ISBNativeBrain.Owns(__1)) return true;
            if (__0 == BotLogicDecision.search) __result = new ISBSearchNode(__1);
            else if (__0 == BotLogicDecision.suppressFire) __result = new ISBSuppressionAdvance(__1, false);
            else if (__0 == ISBNativeBrain.ContactAdvance) __result = new ISBContactAdvance(__1);
            else if (__0 == ISBNativeBrain.SuppressionAdvance) __result = new ISBSuppressionAdvance(__1);
            else return true;
            return false;
        }
    }
}
