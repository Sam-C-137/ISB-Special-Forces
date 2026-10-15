using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Diz.Utils;
using EFT.UI;
using Fika.Core;
using Fika.Core.UI;
using HarmonyLib;

namespace ISBSSOFExtra.Fika
{
    internal static class FikaStartupUiCompatibility
    {
        private static Harmony _harmony;

        internal static void Enable()
        {
            if (_harmony != null)
                return;
            var harmony = new Harmony("com.samc137.isbsofextras.fika.startup-ui");
            try
            {
                harmony.Patch(AccessTools.Method(typeof(FikaConfig), nameof(FikaConfig.SetupConfig)),
                    transpiler: new HarmonyMethod(typeof(FikaStartupUiCompatibility), nameof(DispatchStartupMessage)));
                _harmony = harmony;
            }
            catch
            {
                harmony.UnpatchSelf();
                throw;
            }
        }

        internal static void Disable()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private static IEnumerable<CodeInstruction> DispatchStartupMessage(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            MethodInfo replacement = AccessTools.Method(typeof(FikaStartupUiCompatibility), nameof(ShowStartupMessage));
            for (int i = 0; i < code.Count; i++)
            {
                if (!(code[i].operand is MethodInfo method) || method.DeclaringType != typeof(FikaUIGlobals) || method.Name != "ShowFikaMessage")
                    continue;
                if (i + 1 >= code.Count || code[i + 1].opcode != OpCodes.Pop || method.GetParameters().Length != 7)
                    throw new InvalidOperationException("Unsupported Fika startup message layout.");
                code[i].opcode = OpCodes.Call;
                code[i].operand = replacement;
            }
            return code;
        }

        private static object ShowStartupMessage(PreloaderUI ui, string header, string message,
            ErrorScreen.EButtonType buttonType, float waitingTime, Action accept, Action timeout)
        {
            if (AsyncWorker.CheckIsMainThread())
                return FikaUIGlobals.ShowFikaMessage(ui, header, message, buttonType, waitingTime, accept, timeout);
            AsyncWorker.RunInMainTread(() =>
            {
                if (ui != null)
                    FikaUIGlobals.ShowFikaMessage(ui, header, message, buttonType, waitingTime, accept, timeout);
            });
            return null;
        }
    }
}
