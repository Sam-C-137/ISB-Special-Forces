using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ISBSpecialForces.Components
{
    internal static class ISBLocale
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<MethodInfo> LocalizedMethods = new List<MethodInfo>();
        private static MethodInfo _workingMethod;
        private static bool _initialized;

        public static void Initialize()
        {
            lock (SyncRoot)
            {
                if (_initialized)
                    return;

                _initialized = true;

                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    AddAssemblyMethods(assembly);

                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            }
        }

        public static string LocaleKey(string key)
        {
            return key;
        }

        public static bool TryTranslate(string key, out string translated)
        {
            translated = null;

            if (string.IsNullOrWhiteSpace(key))
                return false;

            Initialize();

            MethodInfo workingMethod;
            MethodInfo[] methods;

            lock (SyncRoot)
            {
                workingMethod = _workingMethod;
                methods = LocalizedMethods.ToArray();
            }

            if (TryInvoke(workingMethod, key, out translated))
                return true;

            foreach (var method in methods)
            {
                if (ReferenceEquals(method, workingMethod))
                    continue;

                if (!TryInvoke(method, key, out translated))
                    continue;

                lock (SyncRoot)
                    _workingMethod = method;

                return true;
            }

            translated = null;
            return false;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (args?.LoadedAssembly == null)
                return;

            lock (SyncRoot)
                AddAssemblyMethods(args.LoadedAssembly);
        }

        private static void AddAssemblyMethods(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
                return;

            Type[] types;

            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(type => type != null).ToArray();
            }
            catch
            {
                return;
            }

            var candidates = types
                .SelectMany(GetStaticMethodsSafe)
                .Where(IsLocalizedMethod)
                .OrderByDescending(ScoreLocalizedMethod);

            foreach (var candidate in candidates)
            {
                if (!LocalizedMethods.Contains(candidate))
                    LocalizedMethods.Add(candidate);
            }

            LocalizedMethods.Sort((left, right) =>
                ScoreLocalizedMethod(right).CompareTo(ScoreLocalizedMethod(left)));
        }

        private static IEnumerable<MethodInfo> GetStaticMethodsSafe(Type type)
        {
            if (type == null)
                return Array.Empty<MethodInfo>();

            try
            {
                return type.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch
            {
                return Array.Empty<MethodInfo>();
            }
        }

        private static bool IsLocalizedMethod(MethodInfo method)
        {
            if (method == null
                || !string.Equals(method.Name, "Localized", StringComparison.Ordinal)
                || method.ReturnType != typeof(string)
                || method.ContainsGenericParameters)
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length == 1
                && parameters[0].ParameterType == typeof(string);
        }

        private static bool TryInvoke(MethodInfo method, string key, out string translated)
        {
            translated = null;

            if (method == null)
                return false;

            try
            {
                translated = method.Invoke(null, new object[] { key }) as string;
                return IsValidTranslation(translated, key);
            }
            catch
            {
                translated = null;
                return false;
            }
        }

        private static bool IsValidTranslation(string translated, string key)
        {
            return !string.IsNullOrWhiteSpace(translated)
                && !string.Equals(translated, key, StringComparison.Ordinal);
        }

        private static int ScoreLocalizedMethod(MethodInfo method)
        {
            var typeName = method.DeclaringType?.FullName ?? string.Empty;
            var assemblyName = method.DeclaringType?.Assembly.GetName().Name ?? string.Empty;
            var score = 0;

            if (typeName.IndexOf("Localization", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 60;
            if (typeName.IndexOf("Locale", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 40;
            if (assemblyName.IndexOf("CommonExtensions", StringComparison.OrdinalIgnoreCase) >= 0)
                score += 35;
            if (typeName.StartsWith("EFT", StringComparison.Ordinal))
                score += 20;
            if (method.IsPublic)
                score += 5;

            return score;
        }
    }
}
