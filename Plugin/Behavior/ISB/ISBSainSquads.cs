using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using DrakiaXYZ.BigBrain.Brains;
using HarmonyLib;
using ISBSpecialForces.Components;

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBSainSquads
    {
        private static readonly Dictionary<ISBSquadKey, bool> Choices = new Dictionary<ISBSquadKey, bool>();
        private static readonly Dictionary<string, bool> Bots = new Dictionary<string, bool>(StringComparer.Ordinal);
        private static readonly Random Random = new Random();
        private static bool _installed;
        private static double _chance;
        internal static bool Available { get; private set; }

        internal static void Configure(Assembly sain, double chance)
        {
            Available = false;
            _chance = double.IsNaN(chance) ? 0 : Math.Max(0, Math.Min(100, chance));
            if (sain == null || _chance <= 0)
                return;
            if (!_installed)
            {
                var type = sain.GetType("SAIN.SAINEnableClass", true);
                var method = AccessTools.Method(type, "IsBotExcluded", new[] { typeof(BotOwner) });
                if (method == null)
                    throw new MissingMethodException(type.FullName, "IsBotExcluded(BotOwner)");
                var harmony = new Harmony("com.isb.sainsquads");
                var extract = AccessTools.Method(sain.GetType("SAIN.Layers.ExtractLayer", true), "IsActive");
                if (extract == null) throw new MissingMethodException("SAIN ExtractLayer.IsActive");
                harmony.Patch(extract, prefix: new HarmonyMethod(typeof(ISBSainSquads), nameof(ExtractPrefix)));
                harmony.Patch(method,
                    prefix: new HarmonyMethod(typeof(ISBSainSquads), nameof(ExcludedPrefix)));
                GameWorld.OnDispose += Clear;
                _installed = true;
            }
            Available = true;
        }

        internal static void Disable() => Available = false;

        internal static bool Selected(BotOwner bot)
        {
            if (!Available || !ISBFikaAuthority.IsAiAuthority || bot?.Profile == null)
                return false;
            if (Bots.TryGetValue(bot.ProfileId, out var selected))
                return selected;
            if (!ISBRuntimeBindings.IsEligible(bot))
                return false;
            var formation = ISBNativeSquad.Formation(bot);
            var key = new ISBSquadKey((object)bot.BotsGroup ?? bot, formation,
                !WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role));
            if (!Choices.TryGetValue(key, out selected))
            {
                selected = Roll(_chance, Random.NextDouble());
                Choices.Add(key, selected);
            }
            Bots[bot.ProfileId] = selected;
            return selected;
        }

        internal static bool Roll(double chance, double sample) =>
            !double.IsNaN(chance) && chance > 0 && (chance >= 100 || sample * 100 < chance);

        private static bool ExcludedPrefix(BotOwner __0, ref bool __result)
        {
            if (!Available || !ISBFikaAuthority.IsAiAuthority || !ISBRuntimeBindings.IsEligible(__0))
                return true;
            if (!Selected(__0)) return true;
            __result = false;
            return false;
        }

        private static bool ExtractPrefix(CustomLayer __instance, ref bool __result)
        {
            if (!Selected(__instance.BotOwner)) return true;
            __result = false;
            return false;
        }

        private static void Clear()
        {
            Choices.Clear();
            Bots.Clear();
        }
    }
}
