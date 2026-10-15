using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Logging;
using EFT;
using HarmonyLib;

namespace ISBSSOFExtra
{
    internal static class WttResourceCompatibility
    {
        private static readonly HashSet<MethodInfo> Patched = new HashSet<MethodInfo>();
        private static readonly Dictionary<Type, PropertyInfo[]> ManifestCollections = new Dictionary<Type, PropertyInfo[]>();
        private static Harmony _harmony;

        internal static void Enable(ManualLogSource log)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var loader = assembly.GetType("WTTClientCommonLib.ResourceLoader", false);
                var setter = loader?.GetProperty("Manifest", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true);
                Patch(setter, nameof(BeforeManifestAssigned), false, log);
                var start = assembly.GetType("WTTClientCommonLib.Patches.OnGameStarted", false)?.GetMethod("PatchPostfix", BindingFlags.NonPublic | BindingFlags.Static);
                if (start != null && start.ReturnType == typeof(void) && start.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(GameWorld) }))
                    Patch(start, nameof(SkipHeadlessPlayerSpawns), true, log);
            }
        }

        private static void Patch(MethodInfo method, string handler, bool transpiler, ManualLogSource log)
        {
            if (method == null || Patched.Contains(method))
                return;
            try
            {
                if (_harmony == null)
                    _harmony = new Harmony("com.samc137.isbsofextras.wtt-resources");
                var patch = new HarmonyMethod(typeof(WttResourceCompatibility), handler) { priority = Priority.First };
                _harmony.Patch(method, prefix: transpiler ? null : patch, transpiler: transpiler ? patch : null);
                Patched.Add(method);
            }
            catch (Exception e)
            {
                log?.LogError("ISB WTT compatibility failed for " + method.Name + ": " + e.GetBaseException().Message);
            }
        }

        private static void BeforeManifestAssigned(object __0)
        {
            if (__0 == null)
                return;
            var type = __0.GetType();
            if (!ManifestCollections.TryGetValue(type, out var properties))
            {
                properties = new[] { "AudioBundles", "FaceCardMappings", "RadioAudio" }
                    .Select(name => type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
                    .Where(p => p != null && p.CanRead && p.CanWrite && typeof(ICollection).IsAssignableFrom(p.PropertyType) && p.PropertyType.GetConstructor(Type.EmptyTypes) != null)
                    .ToArray();
                ManifestCollections[type] = properties;
            }
            foreach (var property in properties)
                if (property.GetValue(__0, null) == null)
                    property.SetValue(__0, Activator.CreateInstance(property.PropertyType), null);
        }

        private static IEnumerable<CodeInstruction> SkipHeadlessPlayerSpawns(IEnumerable<CodeInstruction> source, ILGenerator generator)
        {
            var code = source.ToList();
            int message = code.FindIndex(i => i.opcode == OpCodes.Ldstr && Equals(i.operand, "MainPlayer is null in OnGameStarted"));
            int getter = code.FindIndex(i =>
                (i.operand is FieldInfo field && field.Name == "MainPlayer" && field.DeclaringType == typeof(GameWorld)) ||
                (i.operand is MethodInfo method && method.Name == "get_MainPlayer" && method.DeclaringType == typeof(GameWorld)));
            if (message < 0 || getter < 1 || getter >= message || code[getter - 1].opcode != OpCodes.Ldarg_0)
                throw new InvalidDataException("WTT player spawn callback has an unsupported layout");
            var exit = code.Skip(message + 1).Take(8).FirstOrDefault(i => i.opcode == OpCodes.Leave || i.opcode == OpCodes.Leave_S);
            if (exit == null || !(exit.operand is Label returnLabel))
                throw new InvalidDataException("WTT player spawn callback has no safe return");
            int insertion = getter - 1;
            var original = code[insertion];
            var continueLabel = generator.DefineLabel();
            var guard = new CodeInstruction(OpCodes.Ldarg_0);
            guard.labels.AddRange(original.labels);
            guard.blocks.AddRange(original.blocks);
            original.labels.Clear();
            original.blocks.Clear();
            original.labels.Add(continueLabel);
            code.InsertRange(insertion, new[]
            {
                guard,
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LocalPlayerModCompatibility), nameof(LocalPlayerModCompatibility.HasHeadlessWithoutPlayer))),
                new CodeInstruction(OpCodes.Brfalse, continueLabel),
                new CodeInstruction(OpCodes.Leave, returnLabel)
            });
            return code;
        }

        internal static void Dispose()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Patched.Clear();
            ManifestCollections.Clear();
        }
    }
}
