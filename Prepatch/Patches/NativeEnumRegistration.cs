using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ISBSpecialForces.Prepatch
{
    internal static class NativeEnumRegistration
    {
        private static Dictionary<string, long> entries;
        internal static bool Owns(string enumType, string name, long value)
        {
            if (entries == null)
                entries = ReadEntries();
            if (!entries.TryGetValue(enumType + ":" + name, out var nativeValue))
                return false;
            if (nativeValue != value)
                throw new InvalidOperationException("ISB native enum collision: " + enumType + "." + name);
            return true;
        }

        private static Dictionary<string, long> ReadEntries()
        {
            var result = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "spt-prepatch")
                    continue;
                var method = assembly.GetType("SPT.PrePatch.SptPrePatcher")?.GetMethod("GetCustomEnumEntries", BindingFlags.NonPublic | BindingFlags.Static);
                if (method == null)
                    return result;
                NativeEnumTransport.Enable(method);
                var definitions = method.Invoke(null, null) as IEnumerable ?? throw new InvalidOperationException("SPT returned no native enum definitions.");
                foreach (var entry in definitions)
                {
                    var type = entry.GetType();
                    var enumType = type.GetProperty("EnumType")?.GetValue(entry) as string;
                    var name = type.GetProperty("ConstantName")?.GetValue(entry) as string;
                    var value = type.GetProperty("ConstantValue")?.GetValue(entry);
                    if (enumType == null || name == null || value == null)
                        throw new InvalidOperationException("SPT returned an invalid native enum definition.");
                    result.Add(enumType + ":" + name, Convert.ToInt64(value));
                }

                break;
            }

            return result;
        }
    }
}
