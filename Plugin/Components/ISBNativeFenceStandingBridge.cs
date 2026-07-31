using Comfort.Common;
using EFT;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    internal static class ISBNativeFenceStandingBridge
    {
        private const string FenceTraderId = "579dc571d53a0658a154fbec";
        private const string SourceLocaleKey = "EFenceStandingSource/ISBKill";
        private const int MaximumGraphNodes = 1400;
        private const int MaximumGraphDepth = 5;

        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticFlags =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static bool _initialized;
        private static bool _initializationAttempted;
        private static string _unavailableReason;
        private static Type _sourceType;
        private static object _sourceValue;
        private static List<MethodInfo> _staticCandidates = new List<MethodInfo>();
        private static List<MethodInfo> _instanceCandidates = new List<MethodInfo>();

        private sealed class ObjectNode
        {
            public object Value { get; set; }
            public string Path { get; set; }
            public int Depth { get; set; }
        }

        private sealed class MethodCandidate
        {
            public object Owner { get; set; }
            public MethodInfo Method { get; set; }
            public string Path { get; set; }
            public int Score { get; set; }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public new bool Equals(object first, object second)
            {
                return ReferenceEquals(first, second);
            }

            public int GetHashCode(object value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        public static void Initialize(bool forceRetry = false)
        {
            if (_initialized)
                return;

            if (_initializationAttempted && !forceRetry)
                return;

            _initializationAttempted = true;
            _unavailableReason = null;

            try
            {
                var assembly = typeof(Player).Assembly;
                _sourceType = GetAssemblyTypes(assembly)
                    .FirstOrDefault(type => string.Equals(
                        type.Name,
                        "EFenceStandingSource",
                        StringComparison.Ordinal));

                if (_sourceType == null || !_sourceType.IsEnum)
                {
                    _unavailableReason = "EFenceStandingSource was not found";
                    Plugin.LogSource.LogError(
                        "[ISB AISHI LOYALTY] Native Fence bridge could not find EFenceStandingSource.");
                    return;
                }

                var sourceName = Enum.GetNames(_sourceType)
                    .FirstOrDefault(name => string.Equals(name, "ISBKill", StringComparison.Ordinal));

                if (sourceName == null)
                {
                    _unavailableReason = "EFenceStandingSource.ISBKill is missing";
                    return;
                }

                _sourceValue = Enum.Parse(_sourceType, sourceName);
                var assemblyTypes = GetAssemblyTypes(assembly).ToArray();

                _staticCandidates = assemblyTypes
                    .SelectMany(type => SafeGetMethods(type, StaticFlags))
                    .Where(IsStandingMethodCandidate)
                    .OrderByDescending(method => ScoreMethod(method, method.DeclaringType?.FullName))
                    .ToList();

                _instanceCandidates = assemblyTypes
                    .SelectMany(type => SafeGetMethods(type, InstanceFlags))
                    .Where(IsStandingMethodCandidate)
                    .OrderByDescending(method => ScoreMethod(method, method.DeclaringType?.FullName))
                    .ToList();

                _initialized = true;
            }
            catch (Exception ex)
            {
                _unavailableReason = ex.GetType().Name + ": " + ex.Message;
                Plugin.LogSource.LogError(
                    $"[ISB AISHI LOYALTY] Native Fence bridge initialization failed: {ex}");
            }
        }

        public static bool TryRecord(double standingDelta)
        {
            if (!_initialized)
                Initialize(forceRetry: true);

            if (standingDelta >= -0.000001d)
                return false;

            if (!_initialized || _sourceType == null || _sourceValue == null)
            {
                return false;
            }

            try
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                var mainPlayer = gameWorld?.MainPlayer;
                var profile = mainPlayer?.Profile;

                if (mainPlayer == null || profile == null)
                {
                    Plugin.LogSource.LogWarning(
                        "[ISB AISHI LOYALTY] Native Fence event could not be recorded: main player/profile is unavailable.");
                    return false;
                }

                var nodes = BuildObjectGraph(gameWorld, mainPlayer, profile);

                if (TryAppendNativeEvent(nodes, standingDelta, out var collectionDetail))
                {
                    ISBAishiRaidResultState.MarkNativeEventRecorded();
                    return true;
                }

                if (TryInvokeNativeRecorder(nodes, mainPlayer, profile, standingDelta, out var methodDetail))
                {
                    ISBAishiRaidResultState.MarkNativeEventRecorded();
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError(
                    $"[ISB AISHI LOYALTY] Native Fence standing event registration failed: {ex}");
                return false;
            }
        }

        private static List<ObjectNode> BuildObjectGraph(
            GameWorld gameWorld,
            Player mainPlayer,
            object profile)
        {
            var result = new List<ObjectNode>();
            var queue = new Queue<ObjectNode>();
            var visited = new HashSet<object>(ReferenceComparer.Instance);

            Enqueue(queue, gameWorld, "GameWorld", 0);
            Enqueue(queue, mainPlayer, "MainPlayer", 0);
            Enqueue(queue, profile, "Profile", 0);

            while (queue.Count > 0 && result.Count < MaximumGraphNodes)
            {
                var node = queue.Dequeue();
                var value = node.Value;

                if (value == null || IsLeaf(value.GetType()) || !visited.Add(value))
                    continue;

                result.Add(node);

                if (node.Depth >= MaximumGraphDepth)
                    continue;

                foreach (var member in GetReadableMembers(value.GetType())
                    .OrderByDescending(candidate => IsRelevantGraphMember(candidate.Name)))
                {
                    if (node.Depth > 1 && !IsRelevantGraphMember(member.Name))
                        continue;

                    object child;
                    try
                    {
                        child = GetMemberValue(value, member);
                    }
                    catch
                    {
                        continue;
                    }

                    if (child == null || IsLeaf(child.GetType()))
                        continue;

                    var childPath = node.Path + "." + member.Name;
                    Enqueue(queue, child, childPath, node.Depth + 1);

                    if (!(child is IEnumerable enumerable) || child is string)
                        continue;

                    var index = 0;
                    try
                    {
                        foreach (var item in enumerable)
                        {
                            if (item != null && !IsLeaf(item.GetType()))
                            {
                                Enqueue(
                                    queue,
                                    item,
                                    childPath + "[" + index.ToString(CultureInfo.InvariantCulture) + "]",
                                    node.Depth + 1);
                            }

                            index++;
                            if (index >= 24)
                                break;
                        }
                    }
                    catch
                    {
                        
                    }
                }
            }

            return result;
        }

        private static bool TryAppendNativeEvent(
            IEnumerable<ObjectNode> nodes,
            double delta,
            out string detail)
        {
            detail = null;

            var candidates = new List<(ObjectNode Node, MemberInfo Member, IList List, Type ElementType, int Score)>();

            foreach (var node in nodes)
            {
                foreach (var member in GetReadableMembers(node.Value.GetType()))
                {
                    object value;
                    try
                    {
                        value = GetMemberValue(node.Value, member);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!(value is IList list) || list.IsReadOnly || list.IsFixedSize)
                        continue;

                    var elementType = GetListElementType(member, list);
                    if (elementType == null || elementType == typeof(object) || elementType.IsInterface)
                        continue;

                    var enumMember = GetWritableMembers(elementType)
                        .FirstOrDefault(candidate => GetMemberType(candidate) == _sourceType);
                    var numericMember = PickNumericChangeMember(elementType);

                    if (enumMember == null || numericMember == null)
                        continue;

                    var text = node.Path + "." + member.Name + " " + elementType.FullName;
                    var score = ScoreText(text);
                    score += 20;

                    candidates.Add((node, member, list, elementType, score));
                }
            }

            foreach (var candidate in candidates.OrderByDescending(item => item.Score))
            {
                if (candidate.Score < 25)
                    continue;

                object item;
                try
                {
                    item = CreateObject(candidate.ElementType);
                    PopulateStandingEvent(item, delta);
                    candidate.List.Add(item);
                    detail = candidate.Node.Path + "." + candidate.Member.Name +
                        "<" + candidate.ElementType.FullName + ">";
                    return true;
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning(
                        $"[ISB AISHI LOYALTY] Native standing collection candidate " +
                        $"{candidate.Node.Path}.{candidate.Member.Name} rejected the event: {ex.Message}");
                }
            }

            return false;
        }

        private static bool TryInvokeNativeRecorder(
            IEnumerable<ObjectNode> nodes,
            Player mainPlayer,
            object profile,
            double delta,
            out string detail)
        {
            detail = null;
            var candidates = new List<MethodCandidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var node in nodes)
            {
                foreach (var method in SafeGetMethods(node.Value.GetType(), InstanceFlags))
                {
                    if (!IsStandingMethodCandidate(method))
                        continue;

                    var key = node.Path + "|" + method.MetadataToken;
                    if (!seen.Add(key))
                        continue;

                    candidates.Add(new MethodCandidate
                    {
                        Owner = node.Value,
                        Method = method,
                        Path = node.Path,
                        Score = ScoreMethod(method, node.Path)
                    });
                }
            }

            foreach (var method in _instanceCandidates)
            {
                var declaringType = method.DeclaringType;
                if (declaringType == null)
                    continue;

                var existingOwner = nodes
                    .FirstOrDefault(node => declaringType.IsInstanceOfType(node.Value));
                object owner = existingOwner?.Value;
                var path = existingOwner?.Path;

                if (owner == null && typeof(UnityEngine.Object).IsAssignableFrom(declaringType))
                {
                    try
                    {
                        owner = UnityEngine.Object.FindObjectOfType(declaringType);
                        path = "UnityObject:" + declaringType.FullName;
                    }
                    catch
                    {
                        owner = null;
                    }
                }

                if (owner == null)
                    continue;

                var key = path + "|" + method.MetadataToken;
                if (!seen.Add(key))
                    continue;

                candidates.Add(new MethodCandidate
                {
                    Owner = owner,
                    Method = method,
                    Path = path ?? declaringType.FullName,
                    Score = ScoreMethod(method, path ?? declaringType.FullName)
                });
            }

            foreach (var method in _staticCandidates)
            {
                candidates.Add(new MethodCandidate
                {
                    Owner = null,
                    Method = method,
                    Path = method.DeclaringType?.FullName ?? "static",
                    Score = ScoreMethod(method, method.DeclaringType?.FullName) - 2
                });
            }

            foreach (var candidate in candidates
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Method.GetParameters().Length)
                .Take(12))
            {
                if (candidate.Score < 18)
                    continue;

                if (!TryBuildArguments(
                        candidate.Method,
                        mainPlayer,
                        profile,
                        delta,
                        out var arguments))
                {
                    continue;
                }

                var fenceSnapshot = CaptureFenceStanding(profile);

                try
                {
                    var returnValue = candidate.Method.Invoke(candidate.Owner, arguments);

                    if (returnValue is bool accepted && !accepted)
                        continue;

                    detail = candidate.Path + "." + candidate.Method.Name +
                        "(" + string.Join(", ", candidate.Method.GetParameters()
                            .Select(parameter => parameter.ParameterType.Name)) + ")";
                    return true;
                }
                catch (TargetInvocationException ex)
                {
                    var message = ex.InnerException?.Message ?? ex.Message;
                    Plugin.LogSource.LogWarning(
                        $"[ISB AISHI LOYALTY] Native standing method candidate " +
                        $"{candidate.Path}.{candidate.Method.Name} failed: {message}");
                }
                catch (Exception ex)
                {
                    Plugin.LogSource.LogWarning(
                        $"[ISB AISHI LOYALTY] Native standing method candidate " +
                        $"{candidate.Path}.{candidate.Method.Name} failed: {ex.Message}");
                }
                finally
                {
                    RestoreFenceStanding(fenceSnapshot);
                }
            }

            return false;
        }

        private static bool TryBuildArguments(
            MethodInfo method,
            Player mainPlayer,
            object profile,
            double delta,
            out object[] arguments)
        {
            var parameters = method.GetParameters();
            arguments = new object[parameters.Length];
            var sourceAssigned = false;
            var deltaAssigned = false;

            for (var index = 0; index < parameters.Length; index++)
            {
                var parameter = parameters[index];
                var type = parameter.ParameterType;
                var name = parameter.Name ?? string.Empty;

                if (type == _sourceType)
                {
                    arguments[index] = _sourceValue;
                    sourceAssigned = true;
                    continue;
                }

                if (IsNumeric(type) && !deltaAssigned)
                {
                    arguments[index] = Convert.ChangeType(delta, type, CultureInfo.InvariantCulture);
                    deltaAssigned = true;
                    continue;
                }

                if (type.IsInstanceOfType(mainPlayer))
                {
                    arguments[index] = mainPlayer;
                    continue;
                }

                if (profile != null && type.IsInstanceOfType(profile))
                {
                    arguments[index] = profile;
                    continue;
                }

                if (type == typeof(string))
                {
                    arguments[index] = name.IndexOf("trader", StringComparison.OrdinalIgnoreCase) >= 0
                        ? FenceTraderId
                        : SourceLocaleKey;
                    continue;
                }

                if (type == typeof(bool))
                {
                    arguments[index] = name.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("notify", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("result", StringComparison.OrdinalIgnoreCase) >= 0;
                    continue;
                }

                if (parameter.HasDefaultValue)
                {
                    arguments[index] = parameter.DefaultValue;
                    continue;
                }

                if (!type.IsValueType)
                {
                    arguments[index] = null;
                    continue;
                }

                if (Nullable.GetUnderlyingType(type) != null)
                {
                    arguments[index] = null;
                    continue;
                }

                arguments[index] = Activator.CreateInstance(type);
            }

            return sourceAssigned && deltaAssigned;
        }

        private static bool IsStandingMethodCandidate(MethodInfo method)
        {
            if (method == null
                || method.IsAbstract
                || method.ContainsGenericParameters
                || method.IsSpecialName
                || method.GetParameters().Length > 7)
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Any(parameter => parameter.ParameterType == _sourceType)
                && parameters.Any(parameter => IsNumeric(parameter.ParameterType));
        }

        private static int ScoreMethod(MethodInfo method, string path)
        {
            var text = (path ?? string.Empty) + " " +
                (method.DeclaringType?.FullName ?? string.Empty) + " " + method.Name;
            var score = ScoreText(text);
            var name = method.Name;

            if (name.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Apply", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Register", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Change", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Standing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                score += 6;
            }

            score += Math.Max(0, 6 - method.GetParameters().Length);
            return score;
        }

        private static int ScoreText(string text)
        {
            var score = 0;
            score += Contains(text, "Fence") ? 12 : 0;
            score += Contains(text, "Standing") ? 10 : 0;
            score += Contains(text, "Karma") ? 8 : 0;
            score += Contains(text, "Scav") ? 5 : 0;
            score += Contains(text, "Source") ? 5 : 0;
            score += Contains(text, "Change") ? 4 : 0;
            score += Contains(text, "Result") ? 3 : 0;
            score += Contains(text, "Session") ? 2 : 0;
            return score;
        }

        private static void PopulateStandingEvent(object item, double delta)
        {
            var type = item.GetType();
            var writable = GetWritableMembers(type).ToList();
            var sourceMember = writable.First(member => GetMemberType(member) == _sourceType);
            var numericMember = PickNumericChangeMember(type);

            SetMemberValue(item, sourceMember, _sourceValue);
            SetMemberValue(
                item,
                numericMember,
                Convert.ChangeType(delta, GetMemberType(numericMember), CultureInfo.InvariantCulture));

            foreach (var member in writable)
            {
                var memberType = GetMemberType(member);
                var name = member.Name;

                if (member == sourceMember || member == numericMember)
                    continue;

                if (memberType == typeof(string))
                {
                    if (name.IndexOf("trader", StringComparison.OrdinalIgnoreCase) >= 0)
                        SetMemberValue(item, member, FenceTraderId);
                    else if (name.IndexOf("source", StringComparison.OrdinalIgnoreCase) >= 0
                             || name.IndexOf("reason", StringComparison.OrdinalIgnoreCase) >= 0
                             || name.IndexOf("locale", StringComparison.OrdinalIgnoreCase) >= 0)
                        SetMemberValue(item, member, SourceLocaleKey);
                }
                else if (memberType == typeof(bool))
                {
                    var enabled = name.IndexOf("show", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("display", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("valid", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("active", StringComparison.OrdinalIgnoreCase) >= 0;
                    SetMemberValue(item, member, enabled);
                }
            }
        }

        private static MemberInfo PickNumericChangeMember(Type type)
        {
            return GetWritableMembers(type)
                .Where(member => IsNumeric(GetMemberType(member)))
                .OrderByDescending(member =>
                {
                    var name = member.Name;
                    var score = 0;
                    score += Contains(name, "Standing") ? 8 : 0;
                    score += Contains(name, "Delta") ? 8 : 0;
                    score += Contains(name, "Change") ? 7 : 0;
                    score += Contains(name, "Value") ? 4 : 0;
                    score += Contains(name, "Amount") ? 3 : 0;
                    return score;
                })
                .FirstOrDefault();
        }

        private static object CreateObject(Type type)
        {
            try
            {
                return Activator.CreateInstance(type, true);
            }
            catch
            {
                return FormatterServices.GetUninitializedObject(type);
            }
        }

        private static object CaptureFenceStanding(object profile)
        {
            try
            {
                var tradersInfo = GetNamedMemberValue(profile, "TradersInfo") as IDictionary;
                if (tradersInfo == null || !tradersInfo.Contains(FenceTraderId))
                    return null;

                var trader = tradersInfo[FenceTraderId];
                if (trader == null)
                    return null;

                var standing = GetNamedMemberValue(trader, "Standing");
                return standing == null ? null : new object[] { trader, standing };
            }
            catch
            {
                return null;
            }
        }

        private static void RestoreFenceStanding(object snapshot)
        {
            if (!(snapshot is object[] values) || values.Length != 2 || values[0] == null)
                return;

            try
            {
                var trader = values[0];
                var standing = Convert.ToDouble(values[1], CultureInfo.InvariantCulture);
                var setStanding = trader.GetType().GetMethods(InstanceFlags)
                    .FirstOrDefault(method => method.Name == "SetStanding"
                        && method.GetParameters().Length == 1
                        && IsNumeric(method.GetParameters()[0].ParameterType));

                if (setStanding != null)
                {
                    var parameterType = setStanding.GetParameters()[0].ParameterType;
                    setStanding.Invoke(
                        trader,
                        new[] { Convert.ChangeType(standing, parameterType, CultureInfo.InvariantCulture) });
                    return;
                }

                var member = GetWritableMembers(trader.GetType())
                    .FirstOrDefault(candidate => string.Equals(
                        candidate.Name,
                        "Standing",
                        StringComparison.OrdinalIgnoreCase));
                if (member != null)
                {
                    SetMemberValue(
                        trader,
                        member,
                        Convert.ChangeType(standing, GetMemberType(member), CultureInfo.InvariantCulture));
                }
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB AISHI LOYALTY] Temporary local Fence standing could not be restored: {ex.Message}");
            }
        }

        private static IEnumerable<MemberInfo> GetReadableMembers(Type type)
        {
            foreach (var field in type.GetFields(InstanceFlags))
                yield return field;

            foreach (var property in type.GetProperties(InstanceFlags))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    yield return property;
            }
        }

        private static IEnumerable<MemberInfo> GetWritableMembers(Type type)
        {
            foreach (var field in type.GetFields(InstanceFlags))
            {
                if (!field.IsInitOnly && !field.IsLiteral)
                    yield return field;
            }

            foreach (var property in type.GetProperties(InstanceFlags))
            {
                if (property.CanWrite && property.GetIndexParameters().Length == 0)
                    yield return property;
            }
        }

        private static object GetMemberValue(object owner, MemberInfo member)
        {
            if (member is FieldInfo field)
                return field.GetValue(owner);

            return ((PropertyInfo)member).GetValue(owner, null);
        }

        private static object GetNamedMemberValue(object owner, string name)
        {
            if (owner == null)
                return null;

            var type = owner.GetType();
            var property = type.GetProperty(name, InstanceFlags);
            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(owner, null);

            var field = type.GetField(name, InstanceFlags)
                ?? type.GetField("_" + char.ToLowerInvariant(name[0]) + name.Substring(1), InstanceFlags);
            return field?.GetValue(owner);
        }

        private static void SetMemberValue(object owner, MemberInfo member, object value)
        {
            if (member is FieldInfo field)
                field.SetValue(owner, value);
            else
                ((PropertyInfo)member).SetValue(owner, value, null);
        }

        private static Type GetMemberType(MemberInfo member)
        {
            return member is FieldInfo field
                ? field.FieldType
                : ((PropertyInfo)member).PropertyType;
        }

        private static Type GetListElementType(MemberInfo member, IList list)
        {
            var declared = GetMemberType(member);
            if (declared.IsArray)
                return declared.GetElementType();

            if (declared.IsGenericType)
                return declared.GetGenericArguments().FirstOrDefault();

            var runtime = list.GetType();
            if (runtime.IsGenericType)
                return runtime.GetGenericArguments().FirstOrDefault();

            return list.Count > 0 ? list[0]?.GetType() : null;
        }

        private static IEnumerable<MethodInfo> SafeGetMethods(Type type, BindingFlags flags)
        {
            try
            {
                return type.GetMethods(flags);
            }
            catch
            {
                return Array.Empty<MethodInfo>();
            }
        }

        private static IEnumerable<Type> GetAssemblyTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(type => type != null);
            }
        }

        private static void Enqueue(
            Queue<ObjectNode> queue,
            object value,
            string path,
            int depth)
        {
            if (value == null)
                return;

            queue.Enqueue(new ObjectNode
            {
                Value = value,
                Path = path,
                Depth = depth
            });
        }

        private static bool IsRelevantGraphMember(string name)
        {
            return Contains(name, "Profile")
                || Contains(name, "Stat")
                || Contains(name, "Fence")
                || Contains(name, "Standing")
                || Contains(name, "Karma")
                || Contains(name, "Session")
                || Contains(name, "Counter")
                || Contains(name, "Raid")
                || Contains(name, "Result")
                || Contains(name, "Event")
                || Contains(name, "Change")
                || Contains(name, "Source")
                || Contains(name, "Info");
        }

        private static bool IsLeaf(Type type)
        {
            return type.IsPrimitive
                || type.IsEnum
                || type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(TimeSpan)
                || typeof(Delegate).IsAssignableFrom(type)
                || type == typeof(Type);
        }

        private static bool IsNumeric(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type == typeof(float)
                || type == typeof(double)
                || type == typeof(decimal)
                || type == typeof(int)
                || type == typeof(long)
                || type == typeof(short);
        }

        private static bool Contains(string text, string value)
        {
            return text?.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
