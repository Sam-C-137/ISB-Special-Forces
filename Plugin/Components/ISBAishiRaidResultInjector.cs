using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace ISBSpecialForces.Components
{
    internal static class ISBAishiRaidResultInjector
    {
        private const int StandingGroupFallbackId = 4;
        private const int NumberStatFallbackId = 4;
        private const string StandingSectionLocaleKey = "isb_aishi_standing_section";
        private const string StandingSourceLocaleKey = "EFenceStandingSource/ISBKill";
        private static int _failureCount;
        [ThreadStatic]
        private static bool _injecting;
        private sealed class ListCandidate
        {
            public IList List { get; set; }
            public Type DeclaredType { get; set; }
            public string Source { get; set; }
        }

        public static void TryInject(object statisticsBuilder, object[] arguments = null, string sourceMethod = null)
        {
            if (!ISBAishiRaidResultState.NeedsLegacyInjection || _injecting)
                return;
            _injecting = true;
            try
            {
                var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var owner = statisticsBuilder ?? arguments?.FirstOrDefault(argument => argument != null);
                if (owner == null)
                    throw new InvalidOperationException("StatInfoFactory patch supplied no instance or arguments.");
                var builderType = statisticsBuilder?.GetType() ?? owner.GetType();
                var groupFactory = FindGroupFactory(builderType, flags);
                var statItemAdder = FindStatItemAdder(builderType, flags);
                if ((groupFactory == null || statItemAdder == null) && statisticsBuilder == null)
                {
                    foreach (var argument in arguments ?? Array.Empty<object>())
                    {
                        if (argument == null)
                            continue;
                        var argumentType = argument.GetType();
                        groupFactory = groupFactory ?? FindGroupFactory(argumentType, flags);
                        statItemAdder = statItemAdder ?? FindStatItemAdder(argumentType, flags);
                        if (groupFactory != null && statItemAdder != null)
                        {
                            owner = argument;
                            builderType = argumentType;
                            break;
                        }
                    }
                }

                if (groupFactory == null || statItemAdder == null)
                {
                    throw new MissingMethodException(builderType.FullName, "native standing statistics helper methods");
                }

                var groupType = groupFactory.ReturnType;
                var candidates = CollectListCandidates(statisticsBuilder, arguments, flags).ToList();
                var groupsCandidate = candidates.FirstOrDefault(candidate => IsCompatibleList(candidate, groupType));
                if (groupsCandidate?.List == null)
                {
                    throw new MissingFieldException(builderType.FullName, $"statistics group list (searched {candidates.Count} IList candidate(s))");
                }

                var groups = groupsCandidate.List;
                var penaltyCaption = ISBLocale.LocaleKey(StandingSourceLocaleKey);
                if (ContainsPenalty(groups, penaltyCaption, flags))
                    return;
                var standingGroup = FindStandingGroup(groups, flags);
                var createdGroup = false;
                if (standingGroup == null)
                {
                    var groupParameters = groupFactory.GetParameters();
                    var standingGroupEnum = ResolveEnumValue(groupParameters[1].ParameterType, "Standing", StandingGroupFallbackId);
                    var standingSectionCaption = ISBLocale.LocaleKey(StandingSectionLocaleKey);
                    standingGroup = groupFactory.Invoke(owner, new[] { (object)standingSectionCaption, standingGroupEnum });
                    if (standingGroup == null)
                    {
                        throw new InvalidOperationException("The native standing group factory returned null.");
                    }

                    createdGroup = true;
                }

                var statItems = FindListOnObject(standingGroup, flags);
                if (statItems == null)
                {
                    throw new MissingFieldException(standingGroup.GetType().FullName, "StatItems");
                }

                var itemParameters = statItemAdder.GetParameters();
                var numberStatEnum = ResolveEnumValue(itemParameters[2].ParameterType, "Number", NumberStatFallbackId, "Numeric", "Value");
                var formattedDelta = ISBAishiRaidResultState.StandingDelta.ToString("0.###", CultureInfo.InvariantCulture);
                statItemAdder.Invoke(owner, new object[] { statItems, penaltyCaption, numberStatEnum, formattedDelta, false });
                if (createdGroup)
                {
                    try
                    {
                        groups.Insert(0, standingGroup);
                    }
                    catch (NotSupportedException)
                    {
                        groups.Add(standingGroup);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ISBAishiRaidResultCloneInjector.TryInject(statisticsBuilder, arguments, out var cloneDetail))
                {
                    return;
                }

                _failureCount++;
                if (_failureCount == 1)
                {
                    Plugin.LogSource.LogError($"[ISB AISHI LOYALTY] Standing injection failed " + $"(attempt {_failureCount}, hook={sourceMethod ?? "unknown"}, fallback={cloneDetail}): {ex}");
                }
            }
            finally
            {
                _injecting = false;
            }
        }

        private static IEnumerable<ListCandidate> CollectListCandidates(object statisticsBuilder, object[] arguments, BindingFlags flags)
        {
            var seen = new HashSet<IList>();
            foreach (var candidate in ReadLists(statisticsBuilder, "factory", flags))
            {
                if (candidate.List != null && seen.Add(candidate.List))
                    yield return candidate;
            }

            var index = 0;
            foreach (var argument in arguments ?? Array.Empty<object>())
            {
                if (argument is IList directList && seen.Add(directList))
                {
                    yield return new ListCandidate
                    {
                        List = directList,
                        DeclaredType = argument.GetType(),
                        Source = $"argument[{index}]"};
                }

                foreach (var candidate in ReadLists(argument, $"argument[{index}]", flags))
                {
                    if (candidate.List != null && seen.Add(candidate.List))
                        yield return candidate;
                }

                index++;
            }
        }

        private static IEnumerable<ListCandidate> ReadLists(object instance, string source, BindingFlags flags)
        {
            if (instance == null || instance is string)
                yield break;
            var type = instance.GetType();
            foreach (var field in type.GetFields(flags))
            {
                object value;
                try
                {
                    value = field.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                if (value is IList list)
                {
                    yield return new ListCandidate
                    {
                        List = list,
                        DeclaredType = field.FieldType,
                        Source = $"{source}.{field.Name}"};
                }
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                    continue;
                object value;
                try
                {
                    value = property.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                if (value is IList list)
                {
                    yield return new ListCandidate
                    {
                        List = list,
                        DeclaredType = property.PropertyType,
                        Source = $"{source}.{property.Name}"};
                }
            }
        }

        private static bool IsCompatibleList(ListCandidate candidate, Type itemType)
        {
            if (candidate?.List == null || itemType == null)
                return false;
            if (candidate.List.Count > 0)
            {
                foreach (var item in candidate.List)
                {
                    if (item != null)
                        return itemType.IsInstanceOfType(item);
                }
            }

            var declaredType = candidate.DeclaredType ?? candidate.List.GetType();
            var genericTypes = new[]
            {
                declaredType
            }.Concat(declaredType.GetInterfaces()).Where(type => type.IsGenericType).Select(type => type.GetGenericArguments()).Where(arguments => arguments.Length == 1).Select(arguments => arguments[0]);
            return genericTypes.Any(genericType => genericType == itemType || genericType.IsAssignableFrom(itemType) || itemType.IsAssignableFrom(genericType));
        }

        private static object FindStandingGroup(IList groups, BindingFlags flags)
        {
            foreach (var group in groups)
            {
                if (group == null)
                    continue;
                foreach (var value in ReadSimpleMemberValues(group, flags))
                {
                    if (value is Enum enumValue && enumValue.ToString().IndexOf("Standing", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return group;
                    }

                    if (value is string text && text.IndexOf("Standing", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return group;
                    }
                }
            }

            return null;
        }

        private static bool ContainsPenalty(IList groups, string caption, BindingFlags flags)
        {
            foreach (var group in groups)
            {
                if (group == null)
                    continue;
                var items = FindListOnObject(group, flags);
                if (items == null)
                    continue;
                foreach (var item in items)
                {
                    if (item == null)
                        continue;
                    foreach (var value in ReadSimpleMemberValues(item, flags).OfType<string>())
                    {
                        if (string.Equals(value, StandingSourceLocaleKey, StringComparison.Ordinal) || string.Equals(value, caption, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static IList FindListOnObject(object instance, BindingFlags flags)
        {
            if (instance == null)
                return null;
            if (instance is IList direct)
                return direct;
            return ReadLists(instance, "object", flags).Select(candidate => candidate.List).FirstOrDefault();
        }

        private static IEnumerable<object> ReadSimpleMemberValues(object instance, BindingFlags flags)
        {
            var type = instance.GetType();
            foreach (var field in type.GetFields(flags))
            {
                if (field.FieldType != typeof(string) && !field.FieldType.IsEnum)
                    continue;
                object value;
                try
                {
                    value = field.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                if (value != null)
                    yield return value;
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0 || (property.PropertyType != typeof(string) && !property.PropertyType.IsEnum))
                {
                    continue;
                }

                object value;
                try
                {
                    value = property.GetValue(instance);
                }
                catch
                {
                    continue;
                }

                if (value != null)
                    yield return value;
            }
        }

        private static MethodInfo FindGroupFactory(Type builderType, BindingFlags flags)
        {
            return builderType.GetMethods(flags).FirstOrDefault(method =>
            {
                var parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(string) || !parameters[1].ParameterType.IsEnum || method.ReturnType == typeof(void))
                {
                    return false;
                }

                var returnType = method.ReturnType;
                return returnType.GetFields(flags).Any(field => typeof(IList).IsAssignableFrom(field.FieldType)) || returnType.GetProperties(flags).Any(property => typeof(IList).IsAssignableFrom(property.PropertyType));
            });
        }

        private static MethodInfo FindStatItemAdder(Type builderType, BindingFlags flags)
        {
            return builderType.GetMethods(flags).FirstOrDefault(method =>
            {
                var parameters = method.GetParameters();
                return method.ReturnType == typeof(void) && parameters.Length == 5 && typeof(IEnumerable).IsAssignableFrom(parameters[0].ParameterType) && parameters[1].ParameterType == typeof(string) && parameters[2].ParameterType.IsEnum && parameters[3].ParameterType == typeof(string) && parameters[4].ParameterType == typeof(bool);
            });
        }

        private static object ResolveEnumValue(Type enumType, string preferredName, int fallbackValue, params string[] alternateNames)
        {
            var searchNames = new[]
            {
                preferredName
            }.Concat(alternateNames ?? Array.Empty<string>());
            foreach (var searchName in searchNames)
            {
                var match = Enum.GetNames(enumType).FirstOrDefault(name => name.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match != null)
                    return Enum.Parse(enumType, match);
            }

            return Enum.ToObject(enumType, fallbackValue);
        }
    }
}
