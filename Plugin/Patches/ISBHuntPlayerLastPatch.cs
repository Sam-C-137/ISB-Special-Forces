using ISBSpecialForces.Components;
using MoreBotsAPI.Components;
using SPT.Reflection.Patching;
using System;
using System.Linq;
using System.Reflection;

namespace ISBSpecialForces.Patches
{
    internal abstract class ISBHuntPlayerLastPatchBase : ModulePatch
    {
        protected abstract string TargetMethodName { get; }

        protected override MethodBase GetTargetMethod()
        {
            var method = typeof(HuntManager)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, TargetMethodName, StringComparison.Ordinal)
                    && candidate.GetParameters().Length == 1
                    && candidate.GetParameters()[0].ParameterType == typeof(BotHuntManager));

            if (method == null)
                throw new MissingMethodException(typeof(HuntManager).FullName, TargetMethodName);

            return method;
        }

        protected static void Apply(BotHuntManager hunter)
        {
            ISBHuntTargetPriority.EnforcePlayerLast(hunter);
        }
    }

    internal sealed class ISBHuntFirstTargetPlayerLastPatch : ISBHuntPlayerLastPatchBase
    {
        protected override string TargetMethodName => "FindFirstHuntTarget";

        [PatchPostfix]
        protected static void PatchPostfix(BotHuntManager hunter)
        {
            Apply(hunter);
        }
    }

    internal sealed class ISBHuntNewTargetPlayerLastPatch : ISBHuntPlayerLastPatchBase
    {
        protected override string TargetMethodName => "FindNewHuntTarget";

        [PatchPostfix]
        protected static void PatchPostfix(BotHuntManager hunter)
        {
            Apply(hunter);
        }
    }
}
