using System;
using System.Collections.Generic;
using System.IO;
using System.ComponentModel;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace ISBSpecialForces.Prepatch
{
    internal static class NativeEnumTransport
    {
        private static bool enabled;
        private static OpenDelegate open;
        internal static void Enable(MethodInfo method)
        {
            if (enabled || method.Module.Assembly.GetName().Version != new Version(4, 1, 6, 0))
                return;
            var native = method.Module.Assembly.GetType("SPT.PrePatch.WinHttpClient")?.GetMethod("GetString", BindingFlags.Public | BindingFlags.Static);
            if (native == null || native.ReturnType != typeof(string))
                return;
            var parameters = native.GetParameters();
            if (parameters.Length != 1 || parameters[0].ParameterType != typeof(Uri))
                return;
            new Harmony("com.samc137.isb.native-enum-transport").Patch(native, transpiler: new HarmonyMethod(typeof(NativeEnumTransport), nameof(Transpile)));
            enabled = true;
        }

        private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var result = new List<CodeInstruction>(instructions);
            var replaced = 0;
            foreach (var instruction in result)
            {
                if (instruction.opcode != OpCodes.Call || !(instruction.operand is MethodInfo method) || method.DeclaringType?.FullName != "SPT.PrePatch.WinHttpClient" || method.Name != "WinHttpOpen")
                    continue;
                instruction.operand = typeof(NativeEnumTransport).GetMethod(nameof(Open), BindingFlags.NonPublic | BindingFlags.Static);
                replaced++;
            }

            if (replaced != 1)
                throw new InvalidOperationException("ISB could not verify the native enum transport contract.");
            return result;
        }

        private static IntPtr Open(string agent, uint access, string proxy, string bypass, uint flags)
        {
            if (open == null)
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "winhttp.dll");
                var module = LoadLibraryEx(path, IntPtr.Zero, 2048);
                if (module == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ISB could not load the Windows HTTP library.");
                var function = GetProcAddress(module, "WinHttpOpen");
                if (function == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ISB could not resolve the Windows HTTP entry point.");
                open = (OpenDelegate)Marshal.GetDelegateForFunctionPointer(function, typeof(OpenDelegate));
            }

            return open(agent, access, proxy, bypass, flags);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode, SetLastError = true)]
        private delegate IntPtr OpenDelegate(string agent, uint access, string proxy, string bypass, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
    }
}
