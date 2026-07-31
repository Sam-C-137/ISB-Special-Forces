using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ISBSpecialForces.Components
{
    public sealed class ISBAishiRaidResultUiFallback : MonoBehaviour
    {
        private const string MarkerName = "ISB_AISHI_STANDING_RESULT";
        private const float ScanInterval = 1f;

        private static readonly string[] RaidResultTitles =
        {
            "raid ended",
            "raid statistics",
            "incursão encerrada",
            "estatísticas da incursão"
        };

        private static readonly string[] StandingTitles =
        {
            "standing",
            "reputation",
            "reputação"
        };

        private static readonly string[] SourceSectionTitles =
        {
            "looting",
            "combat",
            "saque",
            "combate",
            "health and physical condition",
            "condição física e de saúde"
        };

        private float _nextScanTime;
        private int _scanAttempts;
        private bool _scanAnnounced;
        private bool _inserted;

        private void Update()
        {
            if (!ISBAishiRaidResultState.NeedsLegacyInjection)
            {
                _nextScanTime = 0f;
                _scanAttempts = 0;
                _scanAnnounced = false;
                _inserted = false;
                return;
            }

            if (Time.unscaledTime < _nextScanTime)
                return;

            _nextScanTime = Time.unscaledTime + ScanInterval;

            if (_inserted)
                return;

            var activeTexts = GetActiveTexts();
            if (HasRenderedCaption(activeTexts))
            {
                _inserted = true;
                return;
            }

            if (!_scanAnnounced)
            {
                _scanAnnounced = true;
            }

            if (!LooksLikeRaidResults(activeTexts))
                return;

            if (TryInsert(activeTexts, out var detail))
            {
                _inserted = true;
                return;
            }

            _scanAttempts++;
            if (_scanAttempts == 30 || _scanAttempts == 75)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB AISHI LOYALTY] Raid-results UI fallback is still waiting for a compatible hierarchy " +
                    $"(attempt {_scanAttempts}).");
            }
        }

        private static bool TryInsert(TMP_Text[] texts, out string detail)
        {
            detail = null;

            if (!ISBLocale.TryTranslate("EFenceStandingSource/ISBKill", out var penaltyCaption))
                return false;

            var standingHeader = FindHeader(texts, StandingTitles);
            if (standingHeader != null
                && TryAppendRowToSection(standingHeader, penaltyCaption, out detail))
            {
                return true;
            }

            if (!ISBLocale.TryTranslate("isb_aishi_standing_section", out var standingTitle))
                return false;

            var sourceHeader = FindHeader(texts, SourceSectionTitles);
            if (sourceHeader == null)
                return false;

            var sourceRoot = FindSectionRoot(sourceHeader);
            if (sourceRoot == null || sourceRoot.transform.parent == null)
                return false;

            var clone = UnityEngine.Object.Instantiate(sourceRoot, sourceRoot.transform.parent);
            clone.name = MarkerName;
            clone.SetActive(false);
            DisableBindingScripts(clone);

            var cloneTexts = clone.GetComponentsInChildren<TMP_Text>(true);
            var sourceTitle = Normalize(sourceHeader.text);
            var clonedHeader = cloneTexts.FirstOrDefault(text => Normalize(text.text) == sourceTitle)
                ?? cloneTexts.FirstOrDefault();

            if (clonedHeader == null)
            {
                UnityEngine.Object.Destroy(clone);
                return false;
            }

            clonedHeader.text = standingTitle;
            var rowRoots = FindRowRoots(clone, clonedHeader);

            if (rowRoots.Count == 0)
            {
                if (!SetFallbackTexts(clone, clonedHeader, penaltyCaption))
                {
                    UnityEngine.Object.Destroy(clone);
                    return false;
                }
            }
            else
            {
                var keptRow = rowRoots[0];
                SetRowTexts(keptRow, penaltyCaption);

                for (var index = 1; index < rowRoots.Count; index++)
                {
                    rowRoots[index].SetActive(false);
                    UnityEngine.Object.Destroy(rowRoots[index]);
                }

                foreach (var text in clone.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text == clonedHeader || text.transform.IsChildOf(keptRow.transform))
                        continue;

                    text.gameObject.SetActive(false);
                }
            }

            clone.transform.SetSiblingIndex(0);
            clone.SetActive(true);
            ForceLayout(clone);
            detail = $"new section cloned from '{sourceHeader.text}'";
            return true;
        }

        private static bool TryAppendRowToSection(TMP_Text standingHeader, string penaltyCaption, out string detail)
        {
            detail = null;
            var sectionRoot = FindSectionRoot(standingHeader);
            if (sectionRoot == null)
                return false;

            var rows = FindRowRoots(sectionRoot, standingHeader);
            if (rows.Count == 0)
                return false;

            var template = rows[0];
            var clone = UnityEngine.Object.Instantiate(template, sectionRoot.transform);
            clone.name = MarkerName;
            clone.SetActive(false);
            DisableBindingScripts(clone);
            SetRowTexts(clone, penaltyCaption);
            clone.transform.SetSiblingIndex(template.transform.GetSiblingIndex() + 1);
            clone.SetActive(true);
            ForceLayout(sectionRoot);
            detail = "existing Standing section";
            return true;
        }

        private static TMP_Text[] GetActiveTexts()
        {
            return Resources.FindObjectsOfTypeAll<TMP_Text>()
                .Where(text => text != null
                    && text.gameObject != null
                    && text.gameObject.scene.IsValid()
                    && text.gameObject.activeInHierarchy
                    && !string.IsNullOrWhiteSpace(text.text))
                .ToArray();
        }

        private static bool LooksLikeRaidResults(IEnumerable<TMP_Text> texts)
        {
            var values = texts.Select(text => Normalize(text.text)).ToArray();

            if (values.Any(value => RaidResultTitles.Any(title => value.Contains(title))))
                return true;

            var sectionMatches = values.Count(value =>
                SourceSectionTitles.Any(title => value == title));
            return sectionMatches >= 2;
        }

        private static TMP_Text FindHeader(IEnumerable<TMP_Text> texts, IEnumerable<string> titles)
        {
            var normalizedTitles = titles.Select(Normalize).ToArray();

            foreach (var preferredTitle in normalizedTitles)
            {
                var match = texts.FirstOrDefault(text => Normalize(text.text) == preferredTitle);
                if (match != null)
                    return match;
            }

            return null;
        }

        private static GameObject FindSectionRoot(TMP_Text header)
        {
            var current = header.transform.parent;

            for (var depth = 0; current != null && depth < 7; depth++, current = current.parent)
            {
                var descendants = current.GetComponentsInChildren<TMP_Text>(true)
                    .Where(text => text.gameObject.activeSelf)
                    .ToArray();

                if (descendants.Length < 3 || descendants.Length > 36)
                    continue;

                var childTextBlocks = 0;
                for (var index = 0; index < current.childCount; index++)
                {
                    if (current.GetChild(index).GetComponentsInChildren<TMP_Text>(true).Length > 0)
                        childTextBlocks++;
                }

                if (childTextBlocks >= 2 && descendants.Contains(header))
                    return current.gameObject;
            }

            return null;
        }

        private static List<GameObject> FindRowRoots(GameObject sectionRoot, TMP_Text header)
        {
            var result = new List<GameObject>();
            var seen = new HashSet<GameObject>();

            foreach (var text in sectionRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text == header || !text.gameObject.activeSelf)
                    continue;

                var current = text.transform.parent;
                while (current != null && current != sectionRoot.transform)
                {
                    var rowTexts = current.GetComponentsInChildren<TMP_Text>(true)
                        .Where(candidate => candidate.gameObject.activeSelf)
                        .ToArray();

                    if (rowTexts.Length == 2 && !rowTexts.Contains(header))
                    {
                        if (seen.Add(current.gameObject))
                            result.Add(current.gameObject);
                        break;
                    }

                    current = current.parent;
                }
            }

            return result
                .OrderBy(row => row.transform.GetSiblingIndex())
                .ToList();
        }

        private static void SetRowTexts(GameObject row, string penaltyCaption)
        {
            var texts = row.GetComponentsInChildren<TMP_Text>(true);
            if (texts.Length == 0)
                return;

            TMP_Text valueText = texts.FirstOrDefault(text => LooksNumeric(text.text));
            if (valueText == null && texts.Length > 1)
                valueText = texts[texts.Length - 1];

            var labelText = texts.FirstOrDefault(text => text != valueText) ?? texts[0];
            labelText.text = penaltyCaption;
            EnsureTextVisible(labelText, row.transform);

            if (valueText != null && valueText != labelText)
            {
                valueText.text = FormatDelta();
                EnsureTextVisible(valueText, row.transform);
            }

            foreach (var extra in texts)
            {
                if (extra != labelText && extra != valueText)
                    extra.gameObject.SetActive(false);
            }

            row.SetActive(true);

            var layoutElement = row.GetComponent<LayoutElement>();
            if (layoutElement != null)
                layoutElement.ignoreLayout = false;

            foreach (var canvasGroup in row.GetComponentsInChildren<CanvasGroup>(true))
                canvasGroup.alpha = 1f;
        }

        private static bool SetFallbackTexts(GameObject section, TMP_Text header, string penaltyCaption)
        {
            var remaining = section.GetComponentsInChildren<TMP_Text>(true)
                .Where(text => text != header)
                .ToArray();

            if (remaining.Length < 2)
                return false;

            remaining[0].text = penaltyCaption;
            remaining[1].text = FormatDelta();
            EnsureTextVisible(remaining[0], section.transform);
            EnsureTextVisible(remaining[1], section.transform);

            for (var index = 2; index < remaining.Length; index++)
                remaining[index].gameObject.SetActive(false);

            return true;
        }

        private static void EnsureTextVisible(TMP_Text text, Transform boundary)
        {
            if (text == null)
                return;

            text.enabled = true;

            var current = text.transform;
            while (current != null)
            {
                current.gameObject.SetActive(true);

                if (current == boundary)
                    break;

                current = current.parent;
            }
        }

        private static void DisableBindingScripts(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is Graphic
                    || behaviour is LayoutGroup
                    || behaviour is ContentSizeFitter
                    || behaviour is LayoutElement)
                {
                    continue;
                }

                behaviour.enabled = false;
            }
        }

        private static void ForceLayout(GameObject changedRoot)
        {
            UnityEngine.Canvas.ForceUpdateCanvases();

            var rect = changedRoot.GetComponent<RectTransform>();
            if (rect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

            var parentRect = changedRoot.transform.parent as RectTransform;
            if (parentRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);

            UnityEngine.Canvas.ForceUpdateCanvases();
        }

        private static bool HasRenderedCaption(IEnumerable<TMP_Text> texts)
        {
            if (!ISBLocale.TryTranslate("EFenceStandingSource/ISBKill", out var translatedCaption))
                return false;

            var caption = Normalize(translatedCaption);
            return texts.Any(text => Normalize(text.text) == caption);
        }

        private static string FormatDelta()
        {
            return ISBAishiRaidResultState.StandingDelta.ToString(
                "+0.###;-0.###;0.###",
                CultureInfo.InvariantCulture);
        }

        private static bool LooksNumeric(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var cleaned = Regex.Replace(value, "<.*?>", string.Empty)
                .Replace(" ", string.Empty)
                .Replace(',', '.')
                .TrimEnd('%');

            return double.TryParse(
                cleaned,
                NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _);
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return Regex.Replace(value, "<.*?>", string.Empty)
                .Trim()
                .ToLowerInvariant();
        }
    }
}
