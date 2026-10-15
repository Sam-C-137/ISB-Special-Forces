using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace ISBSSOFExtra
{
    internal static class OptionalScenePresenceCache
    {
        private static Harmony _harmony;
        private static Func<bool> _allowed;
        private static int _mainThread, _sceneCount;
        private static bool _hasValue, _loaded;
        private static string _failure;
        private const string NativeCode = "FgorMgYoAAAAAAsSASgAAAAALB4SASgAAAAALBUSASgAAAAAcgAAAABvAAAAACwCFyoGF1gKBigAAAAAMsYWKg==";
        internal static void Enable(Func<bool> allowed)
        {
            if (_harmony != null)
                return;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType("Manimal.Icebreaker.IcebreakerAcoustics", false);
                var method = type?.GetMethod("IcebreakerLoaded", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (!KnownReader(method))
                    continue;
                _allowed = allowed;
                _mainThread = Thread.CurrentThread.ManagedThreadId;
                var harmony = new Harmony("com.samc137.isbsofextras.scene-presence");
                try
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(OptionalScenePresenceCache), nameof(ReadCached)), postfix: new HarmonyMethod(typeof(OptionalScenePresenceCache), nameof(Store)));
                    SceneManager.sceneLoaded += SceneLoaded;
                    SceneManager.sceneUnloaded += SceneUnloaded;
                    _harmony = harmony;
                }
                catch (Exception exception)
                {
                    _failure = exception.GetBaseException().Message;
                    harmony.UnpatchSelf();
                    _allowed = null;
                }

                return;
            }
        }

        private static bool KnownReader(MethodInfo method)
        {
            if (method == null || method.ReturnType != typeof(bool))
                return false;
            try
            {
                var code = method.GetMethodBody()?.GetILAsByteArray();
                if (code == null || code.Length != 64 || method.Module.ResolveString(BitConverter.ToInt32(code, 37)) != "Icebreaker")
                    return false;
                code = (byte[])code.Clone();
                var offsets = new[]
                {
                    6,
                    14,
                    23,
                    32,
                    42,
                    56
                };
                var names = new[]
                {
                    "GetSceneAt",
                    "get_isLoaded",
                    "get_name",
                    "get_name",
                    "StartsWith",
                    "get_sceneCount"
                };
                var owners = new[]
                {
                    typeof(SceneManager),
                    typeof(Scene),
                    typeof(Scene),
                    typeof(Scene),
                    typeof(string),
                    typeof(SceneManager)
                };
                for (int i = 0; i < offsets.Length; i++)
                {
                    var called = method.Module.ResolveMethod(BitConverter.ToInt32(code, offsets[i]));
                    if (called.DeclaringType != owners[i] || called.Name != names[i])
                        return false;
                    Array.Clear(code, offsets[i], 4);
                }

                Array.Clear(code, 37, 4);
                return Convert.ToBase64String(code) == NativeCode;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadCached(ref bool __result, out bool __state)
        {
            __state = false;
            if (Thread.CurrentThread.ManagedThreadId != _mainThread || _allowed?.Invoke() != true)
            {
                _hasValue = false;
                return true;
            }

            int count = SceneManager.sceneCount;
            if (_hasValue && _sceneCount == count)
            {
                __result = _loaded;
                return false;
            }

            __state = true;
            return true;
        }

        private static void Store(bool __result, bool __state)
        {
            if (!__state)
                return;
            _sceneCount = SceneManager.sceneCount;
            _loaded = __result;
            _hasValue = true;
        }

        private static void SceneLoaded(Scene scene, LoadSceneMode mode) => _hasValue = false;
        private static void SceneUnloaded(Scene scene) => _hasValue = false;
        internal static void Dispose()
        {
            SceneManager.sceneLoaded -= SceneLoaded;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            _harmony?.UnpatchSelf();
            _harmony = null;
            _allowed = null;
            _hasValue = false;
        }
    }
}
