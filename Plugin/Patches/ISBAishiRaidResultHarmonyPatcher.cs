using EFT;
using HarmonyLib;
using ISBSpecialForces.Components;
using System;
using System.Collections;
using System.Linq;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal static class ISBAishiRaidResultHarmonyPatcher
    {
        private static bool _enabled;
        private static Harmony _harmony;

        public static void Enable()
        {
            if (_enabled)
                return;

            _enabled = true;

            Type[] types;
            try
            {
                types = typeof(Player).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).ToArray();
            }

            var factoryType = types.FirstOrDefault(type =>
                string.Equals(type.FullName, "EFT.UI.StatInfoFactory", StringComparison.Ordinal))
                ?? types.FirstOrDefault(type =>
                    string.Equals(type.Name, "StatInfoFactory", StringComparison.Ordinal));

            if (factoryType == null)
            {
                return;
            }

            var flags = BindingFlags.Instance | BindingFlags.Static |
                        BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly;

            var allMethods = factoryType.GetMethods(flags)
                .Where(method => !method.IsAbstract
                    && !method.ContainsGenericParameters
                    && !method.IsSpecialName)
                .ToArray();

            var methods = allMethods.Where(IsLikelyStatisticsBuildMethod).ToArray();
            if (methods.Length == 0)
                methods = allMethods;

            _harmony = new Harmony(PluginInfo.GUID + ".aishi.raidresults");
            var voidPostfix = new HarmonyMethod(
                typeof(ISBAishiRaidResultHarmonyPatcher).GetMethod(
                    nameof(VoidPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic));
            var resultPostfix = new HarmonyMethod(
                typeof(ISBAishiRaidResultHarmonyPatcher).GetMethod(
                    nameof(ResultPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic));

            var patched = 0;
            foreach (var method in methods)
            {
                try
                {
                    _harmony.Patch(
                        method,
                        postfix: method.ReturnType == typeof(void)
                            ? voidPostfix
                            : resultPostfix);
                    patched++;
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning(
                        $"[ISB AISHI LOYALTY] Could not attach post-raid fallback to " +
                        $"{factoryType.Name}.{method.Name}: {ex.Message}");
                }
            }
        }

        private static bool IsLikelyStatisticsBuildMethod(MethodInfo method)
        {
            var name = method.Name;
            if (name.IndexOf("Stat", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Overall", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Battle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Health", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Loot", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Standing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return method.GetParameters().Any(parameter =>
                typeof(IEnumerable).IsAssignableFrom(parameter.ParameterType));
        }

        private static void VoidPostfix(
            object __instance,
            object[] __args,
            MethodBase __originalMethod)
        {
            ISBAishiRaidResultInjector.TryInject(
                __instance,
                __args,
                __originalMethod?.Name ?? "unknown StatInfoFactory void method");
        }

        private static void ResultPostfix(
            object __instance,
            object[] __args,
            object __result,
            MethodBase __originalMethod)
        {
            var arguments = (__args ?? Array.Empty<object>())
                .Concat(new[] { __result })
                .ToArray();

            ISBAishiRaidResultInjector.TryInject(
                __instance,
                arguments,
                (__originalMethod?.Name ?? "unknown StatInfoFactory method") + "[result]");
        }
    }
}
