using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using EFT.Interactive;
using UnityEngine;
using UnityEngine.UI;

namespace ISBSSOFExtra
{
    internal static class LabsBlackoutExfilUiPatcher
    {
        private static LabsBlackoutExfilHudCleaner _cleaner;

        internal static void Enable(GameObject host, ManualLogSource log)
        {
            if (host == null)
                return;

            if (_cleaner == null)
            {
                _cleaner = host.GetComponent<LabsBlackoutExfilHudCleaner>();
                if (_cleaner == null)
                    _cleaner = host.AddComponent<LabsBlackoutExfilHudCleaner>();
            }

            _cleaner.Initialize(log);
        }

        internal static void HideVisibleEntries(ManualLogSource log)
        {
            if (_cleaner != null)
                _cleaner.RequestRefresh();
        }

        internal static void Disable()
        {
            if (_cleaner == null)
                return;

            UnityEngine.Object.Destroy(_cleaner);
            _cleaner = null;
        }
    }

    internal sealed class LabsBlackoutExfilHudCleaner : MonoBehaviour
    {
        private const float NormalScanInterval = 0.20f;
        private const float ForcedScanDuration = 1.00f;
        private const int MaximumRowSearchDepth = 7;
        private const float CanvasRefreshInterval = 0.75f;

        private static readonly Dictionary<Type, ReflectedTextAccessor> TextAccessorCache =
            new Dictionary<Type, ReflectedTextAccessor>();

        private static readonly Dictionary<Type, BindingAccessor> BindingAccessorCache =
            new Dictionary<Type, BindingAccessor>();

        private static Canvas[] _cachedCanvases = new Canvas[0];
        private static float _nextCanvasRefreshTime;

        private float _nextScanTime;
        private float _forcedUntil;

        internal void Initialize(ManualLogSource log)
        {
            RequestRefresh(true);
        }

        internal void RequestRefresh(bool refreshCanvases = false)
        {
            _forcedUntil = Mathf.Max(_forcedUntil, Time.unscaledTime + ForcedScanDuration);
            _nextScanTime = 0f;

            if (refreshCanvases || _cachedCanvases == null || _cachedCanvases.Length == 0)
                _nextCanvasRefreshTime = 0f;
        }

        private void Update()
        {
            if (!BlackoutBridge.HasHiddenHudEntries)
                return;

            float now = Time.unscaledTime;
            if (now < _nextScanTime)
                return;

            _nextScanTime = now + NormalScanInterval;

            int hidden = HideDisabledRows();

            if (now > _forcedUntil && hidden == 0)
                _nextScanTime = now + NormalScanInterval;
        }

        private static int HideDisabledRows()
        {
            int hidden = 0;
            HashSet<int> processedRows = new HashSet<int>();
            Canvas[] canvases = GetActiveCanvases();

            for (int canvasIndex = 0; canvasIndex < canvases.Length; canvasIndex++)
            {
                Canvas canvas = canvases[canvasIndex];
                if (canvas == null || !canvas.isActiveAndEnabled || !canvas.isRootCanvas)
                    continue;

                Component[] components;
                try
                {
                    components = canvas.gameObject.GetComponentsInChildren<Component>(false);
                }
                catch
                {
                    continue;
                }

                for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                {
                    Component component = components[componentIndex];
                    if (component == null)
                        continue;

                    string text;
                    Color color;
                    if (!TryReadText(component, out text, out color))
                        continue;

                    bool isTransit;
                    if (!TryReadEntryCounter(text, out isTransit))
                        continue;

                    bool shouldHide = isTransit
                        ? BlackoutBridge.HideDisabledTransitHudEntries
                        : IsDisabledExfilRow(component.transform, color);

                    if (!shouldHide)
                        continue;

                    GameObject row = FindEntryRowRoot(component.transform, isTransit);
                    if (row == null || !row.activeSelf)
                        continue;

                    int instanceId = row.GetInstanceID();
                    if (!processedRows.Add(instanceId))
                        continue;

                    row.SetActive(false);
                    hidden++;
                }
            }

            return hidden;
        }

        private static Canvas[] GetActiveCanvases()
        {
            float now = Time.unscaledTime;
            if (_cachedCanvases == null ||
                _cachedCanvases.Length == 0 ||
                now >= _nextCanvasRefreshTime)
            {
                try
                {
                    _cachedCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
                }
                catch
                {
                    _cachedCanvases = new Canvas[0];
                }

                _nextCanvasRefreshTime = now + CanvasRefreshInterval;
            }

            return _cachedCanvases;
        }

        private static bool IsDisabledExfilRow(Transform counterTransform, Color counterColor)
        {
            ExfiltrationPoint boundPoint = FindBoundExfiltrationPoint(counterTransform);
            if (boundPoint != null)
            {
                if (boundPoint.Settings != null &&
                    BlackoutBridge.IsExfilHidden(boundPoint.Settings.Name))
                {
                    return true;
                }

                if (boundPoint.Status == EExfiltrationStatus.NotPresent)
                    return true;
            }

            EExfiltrationStatus? boundStatus = FindBoundExfiltrationStatus(counterTransform);
            if (boundStatus.HasValue && boundStatus.Value == EExfiltrationStatus.NotPresent)
                return true;

            return LooksUnavailable(counterColor);
        }

        private static ExfiltrationPoint FindBoundExfiltrationPoint(Transform transform)
        {
            Transform current = transform;
            for (int depth = 0; current != null && depth < MaximumRowSearchDepth; depth++)
            {
                MonoBehaviour[] behaviours;
                try
                {
                    behaviours = current.GetComponents<MonoBehaviour>();
                }
                catch
                {
                    current = current.parent;
                    continue;
                }

                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                        continue;

                    BindingAccessor accessor = GetBindingAccessor(behaviour.GetType());
                    ExfiltrationPoint point = accessor.ReadPoint(behaviour);
                    if (point != null)
                        return point;
                }

                current = current.parent;
            }

            return null;
        }

        private static EExfiltrationStatus? FindBoundExfiltrationStatus(Transform transform)
        {
            Transform current = transform;
            for (int depth = 0; current != null && depth < MaximumRowSearchDepth; depth++)
            {
                MonoBehaviour[] behaviours;
                try
                {
                    behaviours = current.GetComponents<MonoBehaviour>();
                }
                catch
                {
                    current = current.parent;
                    continue;
                }

                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour == null)
                        continue;

                    BindingAccessor accessor = GetBindingAccessor(behaviour.GetType());
                    EExfiltrationStatus? status = accessor.ReadStatus(behaviour);
                    if (status.HasValue)
                        return status;
                }

                current = current.parent;
            }

            return null;
        }

        private static GameObject FindEntryRowRoot(Transform counterTransform, bool isTransit)
        {
            if (counterTransform == null)
                return null;

            Transform current = counterTransform;
            GameObject bestSingleEntryRoot = counterTransform.gameObject;
            GameObject bestTransitRoot = null;

            for (int depth = 0; current != null && depth < MaximumRowSearchDepth; depth++)
            {
                int counterCount;
                int textCount;
                bool containsTimer;
                CountTextEntries(current.gameObject, out counterCount, out textCount, out containsTimer);

                if (counterCount > 1)
                    break;

                if (counterCount == 1)
                {
                    bestSingleEntryRoot = current.gameObject;

                    if (isTransit)
                    {
                        if (containsTimer)
                            bestTransitRoot = current.gameObject;
                    }
                    else if (textCount >= 2)
                    {
                        return current.gameObject;
                    }
                }

                current = current.parent;
            }

            return isTransit && bestTransitRoot != null
                ? bestTransitRoot
                : bestSingleEntryRoot;
        }

        private static void CountTextEntries(
            GameObject root,
            out int counterCount,
            out int textCount,
            out bool containsTimer)
        {
            counterCount = 0;
            textCount = 0;
            containsTimer = false;

            if (root == null)
                return;

            Component[] components;
            try
            {
                components = root.GetComponentsInChildren<Component>(true);
            }
            catch
            {
                return;
            }

            for (int i = 0; i < components.Length; i++)
            {
                string text;
                Color color;
                if (!TryReadText(components[i], out text, out color))
                    continue;

                textCount++;

                bool isTransit;
                if (TryReadEntryCounter(text, out isTransit))
                {
                    counterCount++;
                }
                else if (LooksLikeCountdown(text))
                {
                    containsTimer = true;
                }
            }
        }

        private static bool TryReadEntryCounter(string value, out bool isTransit)
        {
            isTransit = false;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = RemoveRichTextTags(value).Trim();
            string prefix;

            if (text.StartsWith("EXFIL", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "EXFIL";
            }
            else if (text.StartsWith("TRANSIT", StringComparison.OrdinalIgnoreCase))
            {
                prefix = "TRANSIT";
                isTransit = true;
            }
            else
            {
                return false;
            }

            int index = prefix.Length;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            int digitStart = index;
            while (index < text.Length && char.IsDigit(text[index]))
                index++;

            return index > digitStart;
        }

        private static bool LooksLikeCountdown(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string text = RemoveRichTextTags(value).Trim();
            int colonIndex = text.IndexOf(':');
            if (colonIndex <= 0 || colonIndex >= text.Length - 1)
                return false;

            int digitsBefore = 0;
            for (int i = colonIndex - 1; i >= 0 && char.IsDigit(text[i]); i--)
                digitsBefore++;

            int digitsAfter = 0;
            for (int i = colonIndex + 1; i < text.Length && char.IsDigit(text[i]); i++)
                digitsAfter++;

            return digitsBefore >= 1 && digitsAfter >= 2;
        }

        private static string RemoveRichTextTags(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf('<') < 0)
                return value ?? string.Empty;

            char[] buffer = new char[value.Length];
            int length = 0;
            bool insideTag = false;

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == '<')
                {
                    insideTag = true;
                    continue;
                }

                if (character == '>')
                {
                    insideTag = false;
                    continue;
                }

                if (!insideTag)
                    buffer[length++] = character;
            }

            return new string(buffer, 0, length);
        }

        private static bool LooksUnavailable(Color color)
        {
            if (color.a < 0.10f || color.r < 0.38f)
                return false;

            float greenBlueMaximum = Mathf.Max(color.g, color.b);
            return color.r >= greenBlueMaximum + 0.12f &&
                   color.r >= color.g * 1.20f &&
                   color.r >= color.b * 1.20f;
        }

        private static bool TryReadText(Component component, out string text, out Color color)
        {
            text = null;
            color = Color.white;

            if (component == null)
                return false;

            Text unityText = component as Text;
            if (unityText != null)
            {
                text = unityText.text;
                color = unityText.color;
                return !string.IsNullOrEmpty(text);
            }

            Type type = component.GetType();
            ReflectedTextAccessor accessor;
            if (!TextAccessorCache.TryGetValue(type, out accessor))
            {
                accessor = ReflectedTextAccessor.Create(type);
                TextAccessorCache[type] = accessor;
            }

            if (!accessor.IsValid)
                return false;

            return accessor.TryRead(component, out text, out color);
        }

        private static BindingAccessor GetBindingAccessor(Type type)
        {
            BindingAccessor accessor;
            if (!BindingAccessorCache.TryGetValue(type, out accessor))
            {
                accessor = BindingAccessor.Create(type);
                BindingAccessorCache[type] = accessor;
            }

            return accessor;
        }

        private sealed class ReflectedTextAccessor
        {
            private readonly PropertyInfo _textProperty;
            private readonly PropertyInfo _colorProperty;

            private ReflectedTextAccessor(PropertyInfo textProperty, PropertyInfo colorProperty)
            {
                _textProperty = textProperty;
                _colorProperty = colorProperty;
            }

            internal bool IsValid
            {
                get { return _textProperty != null; }
            }

            internal static ReflectedTextAccessor Create(Type type)
            {
                if (type == null)
                    return new ReflectedTextAccessor(null, null);

                string fullName = type.FullName ?? string.Empty;
                if (!fullName.StartsWith("TMPro.", StringComparison.Ordinal) &&
                    fullName.IndexOf("TextMeshPro", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return new ReflectedTextAccessor(null, null);
                }

                PropertyInfo textProperty = type.GetProperty(
                    "text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (textProperty == null ||
                    !textProperty.CanRead ||
                    textProperty.PropertyType != typeof(string))
                {
                    return new ReflectedTextAccessor(null, null);
                }

                PropertyInfo colorProperty = type.GetProperty(
                    "color",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (colorProperty != null &&
                    (!colorProperty.CanRead || colorProperty.PropertyType != typeof(Color)))
                {
                    colorProperty = null;
                }

                return new ReflectedTextAccessor(textProperty, colorProperty);
            }

            internal bool TryRead(Component component, out string text, out Color color)
            {
                text = null;
                color = Color.white;

                try
                {
                    text = _textProperty.GetValue(component, null) as string;
                    if (_colorProperty != null)
                        color = (Color)_colorProperty.GetValue(component, null);

                    return !string.IsNullOrEmpty(text);
                }
                catch
                {
                    return false;
                }
            }
        }

        private sealed class BindingAccessor
        {
            private readonly FieldInfo[] _pointFields;
            private readonly PropertyInfo[] _pointProperties;
            private readonly FieldInfo[] _statusFields;
            private readonly PropertyInfo[] _statusProperties;

            private BindingAccessor(
                FieldInfo[] pointFields,
                PropertyInfo[] pointProperties,
                FieldInfo[] statusFields,
                PropertyInfo[] statusProperties)
            {
                _pointFields = pointFields;
                _pointProperties = pointProperties;
                _statusFields = statusFields;
                _statusProperties = statusProperties;
            }

            internal static BindingAccessor Create(Type type)
            {
                List<FieldInfo> pointFields = new List<FieldInfo>();
                List<PropertyInfo> pointProperties = new List<PropertyInfo>();
                List<FieldInfo> statusFields = new List<FieldInfo>();
                List<PropertyInfo> statusProperties = new List<PropertyInfo>();

                Type current = type;
                while (current != null && current != typeof(MonoBehaviour))
                {
                    try
                    {
                        FieldInfo[] fields = current.GetFields(
                            BindingFlags.Instance |
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly);

                        for (int i = 0; i < fields.Length; i++)
                        {
                            FieldInfo field = fields[i];
                            if (typeof(ExfiltrationPoint).IsAssignableFrom(field.FieldType))
                                pointFields.Add(field);
                            else if (field.FieldType == typeof(EExfiltrationStatus))
                                statusFields.Add(field);
                        }

                        PropertyInfo[] properties = current.GetProperties(
                            BindingFlags.Instance |
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly);

                        for (int i = 0; i < properties.Length; i++)
                        {
                            PropertyInfo property = properties[i];
                            if (!property.CanRead || property.GetIndexParameters().Length != 0)
                                continue;

                            if (typeof(ExfiltrationPoint).IsAssignableFrom(property.PropertyType))
                                pointProperties.Add(property);
                            else if (property.PropertyType == typeof(EExfiltrationStatus))
                                statusProperties.Add(property);
                        }
                    }
                    catch
                    {
                    }

                    current = current.BaseType;
                }

                return new BindingAccessor(
                    pointFields.ToArray(),
                    pointProperties.ToArray(),
                    statusFields.ToArray(),
                    statusProperties.ToArray());
            }

            internal ExfiltrationPoint ReadPoint(object instance)
            {
                for (int i = 0; i < _pointFields.Length; i++)
                {
                    try
                    {
                        ExfiltrationPoint point = _pointFields[i].GetValue(instance) as ExfiltrationPoint;
                        if (point != null)
                            return point;
                    }
                    catch
                    {
                    }
                }

                for (int i = 0; i < _pointProperties.Length; i++)
                {
                    try
                    {
                        ExfiltrationPoint point = _pointProperties[i].GetValue(instance, null) as ExfiltrationPoint;
                        if (point != null)
                            return point;
                    }
                    catch
                    {
                    }
                }

                return null;
            }

            internal EExfiltrationStatus? ReadStatus(object instance)
            {
                for (int i = 0; i < _statusFields.Length; i++)
                {
                    try
                    {
                        return (EExfiltrationStatus)_statusFields[i].GetValue(instance);
                    }
                    catch
                    {
                    }
                }

                for (int i = 0; i < _statusProperties.Length; i++)
                {
                    try
                    {
                        return (EExfiltrationStatus)_statusProperties[i].GetValue(instance, null);
                    }
                    catch
                    {
                    }
                }

                return null;
            }
        }
    }
}
