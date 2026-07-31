using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISBSpecialForces.Prepatch
{
    public static class FenceStandingSourcePatch
    {
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        public static void Patch(ref AssemblyDefinition assembly)
        {
            var enumType = FindTypes(assembly.MainModule.Types)
                .FirstOrDefault(type => string.Equals(
                    type.Name,
                    "EFenceStandingSource",
                    StringComparison.Ordinal));

            if (enumType == null || !enumType.IsEnum)
                return;

            if (enumType.Fields.Any(field => field.Name == "ISBKill"))
                return;

            var usedValues = new HashSet<int>(enumType.Fields
                .Where(field => field.HasConstant && field.Constant != null)
                .Select(field => Convert.ToInt32(field.Constant)));

            var value = 1000;
            while (usedValues.Contains(value))
                value++;

            var fieldDefinition = new FieldDefinition(
                "ISBKill",
                FieldAttributes.Public | FieldAttributes.Static |
                FieldAttributes.Literal | FieldAttributes.HasDefault,
                enumType)
            {
                Constant = value
            };

            enumType.Fields.Add(fieldDefinition);
        }

        private static IEnumerable<TypeDefinition> FindTypes(IEnumerable<TypeDefinition> roots)
        {
            foreach (var type in roots)
            {
                yield return type;

                foreach (var nested in FindTypes(type.NestedTypes))
                    yield return nested;
            }
        }
    }
}
