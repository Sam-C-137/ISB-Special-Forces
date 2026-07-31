using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace ISBSpecialForces.Components
{
    internal static class ISBAishiRaidResultCloneInjector
    {
        private const string CaptionKey = "EFenceStandingSource/ISBKill";
        private const string SectionKey = "isb_aishi_standing_section";

        private sealed class NestedList
        {
            public object Owner;
            public MemberInfo Member;
            public IList List;
        }

        public static bool TryInject(object factory, object[] arguments, out string detail)
        {
            detail = null;
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var roots = new List<object>();

            if (factory != null)
                roots.Add(factory);

            if (arguments != null)
                roots.AddRange(arguments.Where(argument => argument != null));

            var lists = CollectLists(roots, flags).Distinct().ToList();

            if (lists.Any(list => ContainsStringDeep(list, CaptionKey, flags)))
            {
                detail = "entry already present";
                return true;
            }

            var outer = lists
                .Where(list => list.Count > 0)
                .Select(list => new
                {
                    List = list,
                    Nested = list.Cast<object>()
                        .Where(item => item != null)
                        .Select(item => FindNestedList(item, flags))
                        .Where(item => item != null)
                        .ToList()
                })
                .Where(candidate => candidate.Nested.Count > 0)
                .OrderByDescending(candidate => candidate.Nested.Count)
                .ThenByDescending(candidate => candidate.List.Count)
                .FirstOrDefault();

            if (outer == null)
            {
                detail = $"no populated statistics-group list among {lists.Count} IList candidate(s)";
                return false;
            }

            var templateNested = outer.Nested
                .Where(candidate => candidate.List != null && candidate.List.Count > 0)
                .OrderByDescending(candidate => candidate.List.Count)
                .FirstOrDefault();

            if (templateNested == null)
            {
                detail = "statistics groups were found, but none contained a template stat item";
                return false;
            }

            var templateGroup = templateNested.Owner;
            var templateItem = templateNested.List.Cast<object>().FirstOrDefault(item => item != null);

            if (templateItem == null)
            {
                detail = "template stat item was null";
                return false;
            }

            var newGroup = Clone(templateGroup);
            var newItem = Clone(templateItem);

            if (newGroup == null || newItem == null)
            {
                detail = "MemberwiseClone failed";
                return false;
            }

            var newItems = CreateEmptyList(templateNested.List);
            if (newItems == null)
            {
                detail = $"could not create list of type {templateNested.List.GetType().FullName}";
                return false;
            }

            if (!SetMemberValue(newGroup, templateNested.Member, newItems))
            {
                detail = $"could not replace nested list member {templateNested.Member.Name}";
                return false;
            }

            SetCaptionMember(newGroup, SectionKey, flags, false);
            SetEnumMembers(newGroup, "Standing", flags);

            var formattedDelta = ISBAishiRaidResultState.StandingDelta.ToString(
                "0.###",
                CultureInfo.InvariantCulture);

            SetItemMembers(newItem, CaptionKey, formattedDelta, flags);
            newItems.Add(newItem);

            try
            {
                outer.List.Insert(0, newGroup);
            }
            catch
            {
                outer.List.Add(newGroup);
            }

            detail = $"cloned {templateGroup.GetType().Name}/{templateItem.GetType().Name}";
            return true;
        }

        private static IEnumerable<IList> CollectLists(IEnumerable<object> roots, BindingFlags flags)
        {
            var seenObjects = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var seenLists = new HashSet<IList>(ReferenceEqualityComparer<IList>.Instance);
            var queue = new Queue<Tuple<object, int>>();

            foreach (var root in roots)
                queue.Enqueue(Tuple.Create(root, 0));

            while (queue.Count > 0)
            {
                var entry = queue.Dequeue();
                var instance = entry.Item1;
                var depth = entry.Item2;

                if (instance == null || instance is string || !seenObjects.Add(instance))
                    continue;

                if (instance is IList direct && seenLists.Add(direct))
                    yield return direct;

                if (depth >= 2)
                    continue;

                var type = instance.GetType();

                foreach (var field in type.GetFields(flags))
                {
                    object value;
                    try { value = field.GetValue(instance); }
                    catch { continue; }

                    if (value == null || value is string)
                        continue;

                    if (value is IList list && seenLists.Add(list))
                        yield return list;

                    if (!field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                        queue.Enqueue(Tuple.Create(value, depth + 1));
                }

                foreach (var property in type.GetProperties(flags))
                {
                    if (!property.CanRead || property.GetIndexParameters().Length != 0)
                        continue;

                    object value;
                    try { value = property.GetValue(instance, null); }
                    catch { continue; }

                    if (value == null || value is string)
                        continue;

                    if (value is IList list && seenLists.Add(list))
                        yield return list;
                }
            }
        }

        private static NestedList FindNestedList(object owner, BindingFlags flags)
        {
            var type = owner.GetType();

            foreach (var field in type.GetFields(flags))
            {
                object value;
                try { value = field.GetValue(owner); }
                catch { continue; }

                if (value is IList list)
                    return new NestedList { Owner = owner, Member = field, List = list };
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    continue;

                object value;
                try { value = property.GetValue(owner, null); }
                catch { continue; }

                if (value is IList list)
                    return new NestedList { Owner = owner, Member = property, List = list };
            }

            return null;
        }

        private static object Clone(object instance)
        {
            try
            {
                return typeof(object)
                    .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(instance, null);
            }
            catch
            {
                return null;
            }
        }

        private static IList CreateEmptyList(IList template)
        {
            try
            {
                if (!template.GetType().IsAbstract)
                    return Activator.CreateInstance(template.GetType()) as IList;
            }
            catch { }

            var listInterface = template.GetType().GetInterfaces()
                .Concat(new[] { template.GetType() })
                .FirstOrDefault(type =>
                    type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>));

            if (listInterface == null)
                return null;

            var elementType = listInterface.GetGenericArguments()[0];
            return Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType)) as IList;
        }

        private static void SetItemMembers(
            object item,
            string caption,
            string value,
            BindingFlags flags)
        {
            var stringMembers = GetWritableMembers(item.GetType(), typeof(string), flags).ToList();
            var captionMember = PickMember(stringMembers, "caption", "title", "name", "locale", "text", "description");
            var valueMember = PickMember(stringMembers, "value", "number", "count", "data", "result");

            if (captionMember == null)
            {
                captionMember = stringMembers.FirstOrDefault(member =>
                {
                    var current = GetMemberValue(item, member) as string;
                    return !string.IsNullOrEmpty(current)
                        && !double.TryParse(current, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
                }) ?? stringMembers.FirstOrDefault();
            }

            if (valueMember == null || valueMember == captionMember)
            {
                valueMember = stringMembers.FirstOrDefault(member =>
                {
                    if (member == captionMember)
                        return false;

                    var current = GetMemberValue(item, member) as string;
                    return !string.IsNullOrEmpty(current)
                        && double.TryParse(current, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
                }) ?? stringMembers.FirstOrDefault(member => member != captionMember);
            }

            if (captionMember != null)
                SetMemberValue(item, captionMember, caption);

            if (valueMember != null)
                SetMemberValue(item, valueMember, value);

            foreach (var member in GetWritableMembers(item.GetType(), null, flags))
            {
                var memberType = GetMemberType(member);

                if (memberType.IsEnum)
                    SetMemberValue(item, member, ResolveEnum(memberType, "Number", "Numeric", "Value"));
                else if (memberType == typeof(bool))
                    SetMemberValue(item, member, false);
                else if (IsNumeric(memberType)
                         && member.Name.IndexOf("value", StringComparison.OrdinalIgnoreCase) >= 0)
                    SetMemberValue(item, member, Convert.ChangeType(ISBAishiRaidResultState.StandingDelta, memberType, CultureInfo.InvariantCulture));
            }
        }

        private static void SetCaptionMember(object instance, string value, BindingFlags flags, bool all)
        {
            var members = GetWritableMembers(instance.GetType(), typeof(string), flags).ToList();
            var preferred = PickMember(members, "caption", "title", "name", "locale", "text", "description")
                ?? members.FirstOrDefault();

            if (preferred != null)
                SetMemberValue(instance, preferred, value);

            if (!all)
                return;

            foreach (var member in members.Where(member => member != preferred))
                SetMemberValue(instance, member, value);
        }

        private static void SetEnumMembers(object instance, string preferred, BindingFlags flags)
        {
            foreach (var member in GetWritableMembers(instance.GetType(), null, flags))
            {
                var type = GetMemberType(member);
                if (type.IsEnum)
                    SetMemberValue(instance, member, ResolveEnum(type, preferred));
            }
        }

        private static object ResolveEnum(Type enumType, params string[] names)
        {
            foreach (var preferred in names)
            {
                var name = Enum.GetNames(enumType).FirstOrDefault(candidate =>
                    candidate.IndexOf(preferred, StringComparison.OrdinalIgnoreCase) >= 0);

                if (name != null)
                    return Enum.Parse(enumType, name);
            }

            return Enum.ToObject(enumType, 0);
        }

        private static IEnumerable<MemberInfo> GetWritableMembers(Type type, Type exactType, BindingFlags flags)
        {
            foreach (var field in type.GetFields(flags))
            {
                if (!field.IsInitOnly && (exactType == null || field.FieldType == exactType))
                    yield return field;
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (property.CanWrite
                    && property.GetIndexParameters().Length == 0
                    && (exactType == null || property.PropertyType == exactType))
                {
                    yield return property;
                }
            }
        }

        private static MemberInfo PickMember(IEnumerable<MemberInfo> members, params string[] fragments)
        {
            foreach (var fragment in fragments)
            {
                var match = members.FirstOrDefault(member =>
                    member.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);

                if (match != null)
                    return match;
            }

            return null;
        }

        private static Type GetMemberType(MemberInfo member)
        {
            return member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        }

        private static object GetMemberValue(object instance, MemberInfo member)
        {
            try
            {
                return member is FieldInfo field
                    ? field.GetValue(instance)
                    : ((PropertyInfo)member).GetValue(instance, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool SetMemberValue(object instance, MemberInfo member, object value)
        {
            try
            {
                if (member is FieldInfo field)
                    field.SetValue(instance, value);
                else
                    ((PropertyInfo)member).SetValue(instance, value, null);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsStringDeep(object instance, string value, BindingFlags flags, int depth = 0)
        {
            if (instance == null || depth > 3)
                return false;

            if (instance is string text)
                return string.Equals(text, value, StringComparison.Ordinal);

            if (instance is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (ContainsStringDeep(item, value, flags, depth + 1))
                        return true;
                }

                return false;
            }

            var type = instance.GetType();
            if (type.IsPrimitive || type.IsEnum)
                return false;

            foreach (var field in type.GetFields(flags))
            {
                object child;
                try { child = field.GetValue(instance); }
                catch { continue; }

                if (ContainsStringDeep(child, value, flags, depth + 1))
                    return true;
            }

            return false;
        }

        private static bool IsNumeric(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(byte) || type == typeof(sbyte)
                || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint)
                || type == typeof(long) || type == typeof(ulong)
                || type == typeof(float) || type == typeof(double)
                || type == typeof(decimal);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T> where T : class
        {
            public static readonly ReferenceEqualityComparer<T> Instance = new ReferenceEqualityComparer<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }
}
