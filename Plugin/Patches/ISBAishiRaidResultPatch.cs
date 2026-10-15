using EFT;
using ISBSpecialForces.Components;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal abstract class ISBAishiRaidResultPatchBase : ModulePatch
    {
        protected abstract string TargetMethodName { get; }

        protected override MethodBase GetTargetMethod()
        {
            Type[] types;
            try
            {
                types = typeof(Player).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).ToArray();
            }

            var factoryType = types.FirstOrDefault(type => type.FullName == "EFT.UI.StatInfoFactory");
            var target = factoryType?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => string.Equals(method.Name, TargetMethodName, StringComparison.Ordinal)).OrderBy(method => method.GetParameters().Length).FirstOrDefault();
            if (target == null)
            {
                throw new MissingMethodException($"Could not find EFT.UI.StatInfoFactory.{TargetMethodName}.");
            }

            return target;
        }

        protected static void Inject(object instance, object[] arguments, string sourceMethod)
        {
            if (ISBAishiRaidResultState.NeedsLegacyInjection)
            {
                if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
                {
                    Plugin.LogSource.LogInfo($"[ISB AISHI LOYALTY] Post-raid standing injector reached {sourceMethod}().");
                }
            }

            ISBAishiRaidResultInjector.TryInject(instance, arguments, sourceMethod);
        }
    }

    internal sealed class ISBAishiRaidResultPatch : ISBAishiRaidResultPatchBase
    {
        protected override string TargetMethodName => "AddOverallStats";

        [PatchPostfix]
        protected static void PatchPostfix(object __instance, object[] __args)
        {
            Inject(__instance, __args, "AddOverallStats");
        }
    }

    internal sealed class ISBAishiRaidResultHealthFallbackPatch : ISBAishiRaidResultPatchBase
    {
        protected override string TargetMethodName => "AddStandingStats";

        [PatchPostfix]
        protected static void PatchPostfix(object __instance, object[] __args)
        {
            Inject(__instance, __args, "AddStandingStats");
        }
    }

    internal sealed class ISBAishiRaidResultLootFallbackPatch : ISBAishiRaidResultPatchBase
    {
        protected override string TargetMethodName => "AddBattleStats";

        [PatchPostfix]
        protected static void PatchPostfix(object __instance, object[] __args)
        {
            Inject(__instance, __args, "AddBattleStats");
        }
    }
}
