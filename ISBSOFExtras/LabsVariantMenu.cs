using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.Matchmaker;
using ISB.Compatibility;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Common.Http;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
#if ISB_SPT41
using Location = JsonType.LocationSettings.Location;
using Banner = JsonType.LocationSettings.Location.LocationBanner;
using Notifications = EFT.Communications.NotificationManager;

#else
using Location = LocationSettingsClass.Location;
using Banner = LocationSettingsClass.Location.GClass1428;
using Notifications = NotificationManagerClass;
#endif
namespace ISBSSOFExtra
{
    internal sealed class LabsMenuState
    {
        public bool enabled, unlocked, campaignActive, accepted = true;
        public string selected = "standard", profile, lastQuest, message;
    }

    internal static class LabsVariantMenu
    {
        private static readonly string[] Modes =
        {
            "standard",
            "containment",
            "blackout"
        };
        private static readonly Dictionary<string, Sprite> Covers = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, Task<Sprite>> CoverRequests = new Dictionary<string, Task<Sprite>>();
        private static Sprite _controlsBackground, _emptyCover;
        private static readonly HashSet<string> Notified = new HashSet<string>();
        private static readonly FieldInfo BannerField = AccessTools.Field(typeof(LocationInfoPanel), "_banner");
        private static readonly FieldInfo NameField = AccessTools.Field(typeof(LocationInfoPanel), "_name");
        private static readonly FieldInfo DescriptionField = AccessTools.Field(typeof(LocationInfoPanel), "_description");
        private static readonly MethodInfo SetPanel = AccessTools.GetDeclaredMethods(typeof(LocationInfoPanel)).Single(m => m.Name == "Set" && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Location));
        private static readonly MethodInfo CloneLocation = AccessTools.Method(typeof(object), "MemberwiseClone");
        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static LabsMenuState _state;
        private static Task<LabsMenuState> _request;
        private static bool _selecting, _reported, _menuResetPending, _stateRefreshPending;
        private static readonly LabsMenuRequestRetry Retry = new LabsMenuRequestRetry();
        private static int _generation;
        private static LocationButton _labsButton;
        private static DefaultUIButton _selector;
        private static GameObject _choices;
        private static bool _choicesOpen;
        private static readonly List<DefaultUIButton> ChoiceButtons = new List<DefaultUIButton>();
        private static Sprite _menuIcon;
        private static MatchMakerSelectionLocationScreen _screen;
        private static string _mapLabel;
        private static string _loadingMode;
        private static object[] _buttonArgs;
        private static readonly MethodInfo ShowButton = AccessTools.Method(typeof(LocationButton), "Show");
        private static bool BossMarker => Customize && Mode == "containment";

        private static LocationInfoPanel _panel;
        private static object[] _panelArgs;
        private static string SeenPath => Path.Combine(BepInEx.Paths.ConfigPath, "ISB.LabsVariants.seen.json");
        private static string Mode => _state?.enabled == true && _state.unlocked ? ValidMode(_state.selected) : _state?.campaignActive == true ? "containment" : "standard";
        private static bool Customize => _state?.campaignActive == true || _state?.enabled == true;

        private static bool IsLabs(Location location) => location != null && string.Equals(location.Id, "laboratory", StringComparison.OrdinalIgnoreCase);
        internal static void Enable(ManualLogSource log)
        {
            _log = log;
            if (_harmony != null)
                return;
            try
            {
                if (File.Exists(SeenPath))
                    foreach (var s in JsonConvert.DeserializeObject<string[]>(File.ReadAllText(SeenPath)) ?? Array.Empty<string>())
                        Notified.Add(s);
                _harmony = new Harmony("com.samc137.isbsofextras.labs-variants");
                _harmony.Patch(ShowButton, prefix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(BeforeButton)), postfix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(ButtonShown)));
                _harmony.Patch(SetPanel, prefix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(BeforePanel)), postfix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(AfterPanel)));
                _harmony.Patch(ThreeArgumentShow(typeof(MatchMakerSelectionLocationScreen)), prefix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(SelectionOpening)), postfix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(SelectionShown)));
                _harmony.Patch(AccessTools.Method(typeof(MatchMakerSelectionLocationScreen), "Close"), prefix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(SelectionClosed)));
                _harmony.Patch(ThreeArgumentShow(typeof(MatchmakerTimeHasCome)), prefix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(BeforeLoading)), postfix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(LoadingShown)));
                _harmony.Patch(AccessTools.Method(typeof(MatchmakerBannersPanel), "Show"), prefix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(BeforeLoadingBanners)));
                var createBanner = AccessTools.GetDeclaredMethods(typeof(MatchmakerBannersPanel)).Single(m => m.ReturnType == typeof(MatchmakerBanner) && m.GetParameters().Length == 3);
                _harmony.Patch(createBanner, prefix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(ControlsBeforeCover)), postfix: new HarmonyMethod(typeof(LabsVariantMenu), nameof(CoverAfterControls)));
            }
            catch (Exception ex)
            {
                _log.LogError("ISB Labs selector: " + ex.GetBaseException().Message);
                Disable();
            }
        }

        internal static void RequestStateRefresh()
        {
            if (_harmony == null)
                return;
            _stateRefreshPending = true;
            Retry.Reset();
        }

        internal static void Tick(bool inRaid, bool canPresent)
        {
            if (_harmony == null || inRaid || !canPresent)
                return;
            if (_request != null && _request.IsCompleted)
            {
                try
                {
                    var state = _request.GetAwaiter().GetResult();
                    if (state == null)
                        throw new InvalidDataException("Empty Labs menu response.");
                    if (state != null && !_stateRefreshPending)
                    {
                        bool changed = _state == null || _state.enabled != state.enabled || _state.unlocked != state.unlocked || _state.campaignActive != state.campaignActive || _state.selected != state.selected || _state.profile != state.profile;
                        _state = state;
                        _reported = false;
                        Retry.Succeeded(Time.realtimeSinceStartup);
                        if (!state.accepted)
                            Notify(state.message ?? "Não foi possível selecionar esta Labs.");
                        if (state.unlocked)
                        {
                            string key = state.profile + ":" + state.lastQuest;
                            if (Notified.Add(key))
                            {
                                Notify("By completing the Containment Breach event, you have new The Labs options available.\nThe Lab · Containment Breach · Blackout");
                                File.WriteAllText(SeenPath, JsonConvert.SerializeObject(Notified.ToArray()));
                            }
                        }

                        if (changed)
                        {
                            _log?.LogInfo("ISB Labs menu state: enabled=" + state.enabled + ", unlocked=" + state.unlocked + ", campaign=" + state.campaignActive + ", selected=" + state.selected);
                            Refresh();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Retry.Failed(Time.realtimeSinceStartup);
                    if (!_reported)
                    {
                        _reported = true;
                        _log.LogWarning("ISB Labs menu: " + ex.GetBaseException().Message);
                    }
                }
                finally
                {
                    _request = null;
                    _selecting = false;
                    UpdateButton();
                }
            }

            UpdateButton();
            if (_request != null || PreloaderUI.Instance == null || _loadingMode != null)
                return;
            if (_menuResetPending || _stateRefreshPending)
            {
                bool opening = _menuResetPending;
                _menuResetPending = false;
                _stateRefreshPending = false;
                _selecting = opening;
                UpdateButton();
                _request = Task.Run(() => JsonConvert.DeserializeObject<LabsMenuState>(opening ? RequestHandler.PostJson("/ISB/labsVariants/openSelection", "{}") : RequestHandler.GetJson("/ISB/labsVariants/status")));
                return;
            }

            if (!Retry.Take(Time.realtimeSinceStartup, _screen != null && _screen.gameObject.activeInHierarchy))
                return;
            _request = Task.Run(() => JsonConvert.DeserializeObject<LabsMenuState>(RequestHandler.GetJson("/ISB/labsVariants/status")));
        }

        private static void Notify(string text) => Notifications.DisplayMessageNotification(text, EFT.Communications.ENotificationDurationType.Long);
        private static void Choose(string mode)
        {
            if (_request != null || _selecting || _menuResetPending || _state?.unlocked != true || mode == Mode)
                return;
            _choicesOpen = false;
            _selecting = true;
            UpdateButton();
            _request = Task.Run(() => JsonConvert.DeserializeObject<LabsMenuState>(RequestHandler.PostJson("/ISB/labsVariants/select", JsonConvert.SerializeObject(new { mode }))));
        }

        private static void BeforeButton(LocationButton __instance, Location __0, object[] __args)
        {
            if (!IsLabs(__0) || __instance.GetComponentInParent<MatchMakerSelectionLocationScreen>(true) != _screen || _screen == null)
                return;
            _labsButton = __instance;
            _buttonArgs = (object[])__args.Clone();
            if (_state != null && (_state.enabled || _state.campaignActive))
                __args[8] = BossMarker;
        }

        private static void ButtonShown(LocationButton __instance, Location __0)
        {
            if (!IsLabs(__0) || __instance != _labsButton)
                return;
            _mapLabel = null;
            ApplyMapName();
            ApplyNativeBossMarker();
            _menuResetPending = true;
            Retry.Reset();
        }

        private static MethodInfo ThreeArgumentShow(Type type) => AccessTools.GetDeclaredMethods(type).Single(m => m.Name == "Show" && m.GetParameters().Length == 3);
        private static void ClearClonedEvent(UnityEvent evt)
        {
            evt.RemoveAllListeners();
            for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                evt.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        private static string ValidMode(string mode) => Modes.Contains(mode) ? mode : "standard";
        private static string Title(string mode) => ISBLocalizedText.Get("isb_labs_" + mode + " Name", mode == "containment" ? "Containment Breach The Labs" : mode == "blackout" ? "Blackout The Labs" : "The Lab");
        private static void ApplyNativeBossMarker()
        {
            if (_labsButton == null || !Customize)
                return;
            GameObject Icon(string field) => AccessTools.Field(typeof(LocationButton), field).GetValue(_labsButton) as GameObject;
            bool locked = Icon("_lockedIcon")?.activeSelf == true;
            bool boss = BossMarker && !locked;
            Icon("_bossIcon")?.SetActive(boss);
            Icon("_availableIcon")?.SetActive(!locked && !boss && Icon("_overloadedIcon")?.activeSelf != true && Icon("_newIcon")?.activeSelf != true);
        }

        private static UISpawnableToggle MapToggle => _labsButton == null ? null : AccessTools.Field(typeof(LocationButton), "_spawnableToggle").GetValue(_labsButton) as UISpawnableToggle;

        private static void SelectionOpening(MatchMakerSelectionLocationScreen __instance)
        {
            if (_screen != null && _screen != __instance)
                SelectionClosed(_screen);
            _screen = __instance;
            _request = null;
            _selecting = false;
            _panel = null;
            _panelArgs = null;
            _mapLabel = null;
            _loadingMode = null;
            _menuResetPending = true;
            Retry.Reset();
        }

        private static void SelectionShown(MatchMakerSelectionLocationScreen __instance)
        {
            _screen = __instance;
            _loadingMode = null;
            _menuResetPending = true;
            Retry.Reset();
            UpdateButton();
        }

        private static void SelectionClosed(MatchMakerSelectionLocationScreen __instance)
        {
            if (__instance != _screen)
                return;
            _request = null;
            _selecting = false;
            ++_generation;
            Retry.Reset();
            _menuResetPending = false;
            _screen = null;
            _panel = null;
            _panelArgs = null;
            _labsButton = null;
            _buttonArgs = null;
            _mapLabel = null;
            if (_selector != null)
                UnityEngine.Object.Destroy(_selector.gameObject);
            _selector = null;
            if (_choices != null)
                UnityEngine.Object.Destroy(_choices);
            _choices = null;
            _choicesOpen = false;
            ChoiceButtons.Clear();
        }

        private static void ApplyMapName()
        {
            var toggle = MapToggle;
            if (toggle == null || _buttonArgs == null)
                return;
            string text = Customize ? Title(Mode).ToUpperInvariant() : ((Location)_buttonArgs[0]).LocalizedName.ToUpperInvariant();
            if (_mapLabel == text)
                return;
            _mapLabel = text;
            var nameRect = (RectTransform)toggle.transform;
            var left = nameRect.TransformPoint(new Vector3(nameRect.rect.xMin, nameRect.rect.center.y, 0));
            foreach (var field in new[]
            {
                "_headerLabel",
                "_sizeLabel"
            }

            )
                if (AccessTools.Field(typeof(UISpawnableToggle), field).GetValue(toggle)is TMP_Text label)
                    label.text = text;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_labsButton.transform);
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)toggle.transform);
            nameRect.position += left - nameRect.TransformPoint(new Vector3(nameRect.rect.xMin, nameRect.rect.center.y, 0));
        }

        private static void UpdateButton()
        {
            ApplyMapName();
            ApplyNativeBossMarker();
            var toggle = MapToggle;
            bool visible = _screen != null && _screen.gameObject.activeInHierarchy && _panel != null && _panel.gameObject.activeInHierarchy && _state?.enabled == true && toggle != null;
            if (!visible)
            {
                if (_selector != null)
                    _selector.gameObject.SetActive(false);
                if (_choices != null)
                    _choices.SetActive(false);
                _choicesOpen = false;
                return;
            }

            if (_selector == null)
            {
                var template = AccessTools.Field(typeof(MatchMakerSelectionLocationScreen), "_acceptButton").GetValue(_screen) as DefaultUIButton;
                if (template == null)
                    template = AccessTools.Field(typeof(MatchMakerSelectionLocationScreen), "_pocketMapButton").GetValue(_screen) as DefaultUIButton;
                var parent = AccessTools.Field(typeof(MatchMakerSelectionLocationScreen), "_locationButtonsContainer").GetValue(_screen) as Transform;
                if (template == null || parent == null)
                    return;
                _selector = UnityEngine.Object.Instantiate(template, parent, false);
                _selector.name = "ISB Labs variant";
                ShowNativeButton(_selector);
                ClearClonedEvent(_selector.OnClick);
                ClearClonedEvent(_selector.OnMouseOver);
                ClearClonedEvent(_selector.OnMouseOut);
                _selector.OnClick.AddListener(() =>
                {
                    _choicesOpen = !_choicesOpen;
                    UpdateChoices();
                });
                AccessTools.Field(typeof(DefaultUIButton), "_minWidth").SetValue(_selector, 36f);
                _selector.SetIcon(null, null);
                _selector.SetRawText("", 24);
                var icon = new GameObject("ISB menu icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                icon.GetComponent<LayoutElement>().ignoreLayout = true;
                var iconRect = (RectTransform)icon.transform;
                iconRect.SetParent(_selector.transform, false);
                iconRect.anchorMin = iconRect.anchorMax = iconRect.pivot = new Vector2(.5f, .5f);
                iconRect.sizeDelta = new Vector2(18, 18);
                iconRect.anchoredPosition = Vector2.zero;
                icon.GetComponent<Image>().sprite = MenuIcon();
                icon.GetComponent<Image>().raycastTarget = false;
                var layout = _selector.GetComponent<LayoutElement>() ?? _selector.gameObject.AddComponent<LayoutElement>();
                layout.ignoreLayout = true;
                layout.minWidth = 36;
                layout.preferredWidth = 36;
                var fitter = _selector.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                    fitter.enabled = false;
                var rect = (RectTransform)_selector.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.pivot = new Vector2(0, .5f);
                rect.sizeDelta = new Vector2(36, 32);
                rect.localScale = Vector3.one;
                rect.SetAsLastSibling();
            }

            ShowNativeButton(_selector);
            _selector.Interactable = _state.unlocked && !_selecting && !_menuResetPending;
#if ISB_SPT41
            _selector.SetTooltips(ISBLocalizedText.Get("ISB/Labs/ChooseVariant", "Choose Labs version"), _state.unlocked ? "Confirmando a versão da Labs..." : "Conclua a última quest da colaboração com o Manimal Lab para liberar as versões da Labs.");
#else
            _selector.method_2(ISBLocalizedText.Get("ISB/Labs/ChooseVariant", "Choose Labs version"), _state.unlocked ? "Confirmando a versão da Labs..." : "Conclua a última quest da colaboração com o Manimal Lab para liberar as versões da Labs.");
#endif
            var mapRect = (RectTransform)toggle.transform;
            _selector.transform.position = mapRect.TransformPoint(new Vector3(mapRect.rect.xMax + 8, mapRect.rect.center.y, 0));
            UpdateChoices();
            if (_choicesOpen && (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0) && !PointerInside(_selector.transform) && !PointerInside(_choices.transform)))
            {
                _choicesOpen = false;
                _choices.SetActive(false);
            }
        }

        private static void ShowNativeButton(DefaultUIButton button)
        {
            button.enabled = true;
            button.gameObject.SetActive(true);
            var group = button.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }
        }

        private static bool PointerInside(Transform target)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            return RectTransformUtility.RectangleContainsScreenPoint((RectTransform)target, Input.mousePosition, canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null);
        }

        private static Sprite MenuIcon()
        {
            if (_menuIcon != null)
                return _menuIcon;
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            texture.name = "ISB Labs menu icon";
            texture.filterMode = FilterMode.Point;
            var pixels = new Color32[256];
            for (int y = 3; y < 14; y++)
                for (int x = 2; x < 14; x++)
                    if (y == 3 || y == 4 || y == 7 || y == 8 || y == 11 || y == 12)
                        pixels[y * 16 + x] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply();
            return _menuIcon = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 100);
        }

        private static void UpdateChoices()
        {
            if (!_choicesOpen || _selector == null || !_selector.Interactable)
            {
                if (_choices != null)
                    _choices.SetActive(false);
                return;
            }

            if (_choices == null)
            {
                _choices = new GameObject("ISB Labs choices", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                var rect = (RectTransform)_choices.transform;
                rect.SetParent(_selector.transform.parent, false);
                rect.pivot = new Vector2(0, 1);
                rect.sizeDelta = new Vector2(274, 116);
                var surface = _choices.GetComponent<Image>();
                var dropdown = Resources.FindObjectsOfTypeAll<DropDownBox>().FirstOrDefault(d => d != null);
                var native = dropdown == null ? null : AccessTools.Field(typeof(DropDownBox), "_background").GetValue(dropdown) as Image;
                if (native == null || native.sprite == null)
                    native = _selector.GetComponentsInChildren<Image>(true).FirstOrDefault(i => i.sprite != null && i.name != "ISB menu icon");
                if (native != null && native.sprite != null)
                {
                    surface.sprite = native.sprite;
                    surface.type = native.type;
                    surface.material = native.material;
                    surface.color = new Color(.13f, .15f, .15f, .98f);
                }
                else
                    surface.color = new Color(.18f, .20f, .19f, .80f);
                surface.raycastTarget = true;
                _choices.GetComponent<LayoutElement>().ignoreLayout = true;
                for (int i = 0; i < Modes.Length; i++)
                {
                    var choice = UnityEngine.Object.Instantiate(_selector, rect, false);
                    string mode = Modes[i];
                    choice.name = "Select Labs " + mode;
                    var copiedIcon = choice.transform.Find("ISB menu icon");
                    if (copiedIcon != null)
                        UnityEngine.Object.Destroy(copiedIcon.gameObject);
                    ShowNativeButton(choice);
                    ClearClonedEvent(choice.OnClick);
                    choice.OnClick.AddListener(() =>
                    {
                        if (Mode == mode)
                        {
                            _choicesOpen = false;
                            UpdateChoices();
                        }
                        else
                            Choose(mode);
                    });
                    choice.SetIcon(null, null);
                    choice.SetRawText(ISBLocalizedText.Get("isb_labs_" + mode + " Option", mode == "containment" ? "Containment Breach" : mode == "blackout" ? "Blackout" : "The Lab"), 19);
                    var cr = (RectTransform)choice.transform;
                    cr.anchorMin = cr.anchorMax = new Vector2(0, 1);
                    cr.pivot = new Vector2(0, 1);
                    cr.anchoredPosition = new Vector2(6, -6 - i * 35);
                    cr.sizeDelta = new Vector2(262, 34);
                    choice.gameObject.SetActive(true);
                    ChoiceButtons.Add(choice);
                }
            }

            _choices.SetActive(true);
            _choices.transform.SetAsLastSibling();
            var selectorRect = (RectTransform)_selector.transform;
            _choices.transform.position = selectorRect.TransformPoint(new Vector3(selectorRect.rect.xMax + 6, selectorRect.rect.yMax, 0));
            foreach (var button in ChoiceButtons)
                button.Interactable = !_selecting && !_menuResetPending;
        }

        private static Sprite EmptyCover()
        {
            if (_emptyCover != null)
                return _emptyCover;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.name = "ISB pending cover";
            texture.SetPixels32(new Color32[1]);
            texture.Apply();
            return _emptyCover = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
        }

        private static void BeforePanel(LocationInfoPanel __instance, ref Location __0, out Location __state)
        {
            __state = __0;
            if (_screen == null || __instance.GetComponentInParent<MatchMakerSelectionLocationScreen>(true) != _screen)
                return;
            var image = (Image)BannerField.GetValue(__instance);
            if (!IsLabs(__0) || !Customize)
            {
                image.overrideSprite = null;
                image.enabled = true;
                return;
            }

            Covers.TryGetValue(Mode, out var cached);
            image.overrideSprite = cached ?? EmptyCover();
            image.enabled = cached != null;
            __0 = (Location)CloneLocation.Invoke(__0, null);
            __0.Banners = Array.Empty<Banner>();
        }

        private static void AfterPanel(LocationInfoPanel __instance, object[] __args, Location __state)
        {
            if (_screen == null || __instance.GetComponentInParent<MatchMakerSelectionLocationScreen>(true) != _screen)
                return;
            var location = __state;
            ++_generation;
            if (!IsLabs(location))
            {
                _panel = null;
                _panelArgs = null;
                UpdateButton();
                return;
            }

            _panel = __instance;
            _panelArgs = (object[])__args.Clone();
            _panelArgs[0] = location;
            if (_state != null && (_state.enabled || _state.campaignActive))
            {
                var boss = AccessTools.Field(typeof(LocationInfoPanel), "_boss").GetValue(__instance) as GameObject;
                if (boss != null)
                    boss.SetActive(BossMarker);
            }

            UpdateButton();
            if (!Customize)
                return;
            var title = (TMP_Text)NameField.GetValue(__instance);
            var description = (TMP_Text)DescriptionField.GetValue(__instance);
            var mode = Mode;
            title.text = Title(mode);
            description.text = ISBLocalizedText.Get("isb_labs_" + mode + " Description", "");
            title.enableAutoSizing = true;
            title.fontSizeMin = 19;
            title.fontSizeMax = 36;
            if (mode == "containment")
            {
                SetInfoText(__instance, "_playTime", "15–40 MIN");
                SetInfoText(__instance, "_difficulty", "CHAOS");
                SetInfoText(__instance, "_players", "20–30");
            }
            else if (mode == "blackout")
            {
                SetInfoText(__instance, "_difficulty", "HARD");
                SetInfoText(__instance, "_players", "10–20");
            }

            LoadCover(__instance, __args[__args.Length - 1], mode, _generation);
        }

        private static void SetInfoText(LocationInfoPanel panel, string field, string text)
        {
            if (AccessTools.Field(typeof(LocationInfoPanel), field).GetValue(panel)is TMP_Text label)
                label.text = text;
        }

        private static async void LoadCover(LocationInfoPanel panel, object session, string mode, int generation)
        {
            try
            {
                if (!Covers.TryGetValue(mode, out var sprite))
                {
                    var banner = CreateCover(mode);
                    if (!CoverRequests.TryGetValue(mode, out var task))
                    {
                        task = (Task<Sprite>)AccessTools.Method(typeof(Banner), "LoadBannerSprite").Invoke(banner, new[] { session });
                        CoverRequests[mode] = task;
                    }

                    try
                    {
                        sprite = await task;
                    }
                    finally
                    {
                        CoverRequests.Remove(mode);
                    }

                    if (sprite != null && _harmony != null)
                        Covers[mode] = sprite;
                }

                if (sprite != null && panel != null && panel == _panel && generation == _generation && mode == Mode)
                {
                    var image = (Image)BannerField.GetValue(panel);
                    image.overrideSprite = sprite;
                    image.enabled = true;
                }
            }
            catch (Exception ex)
            {
                if (panel != null && panel == _panel && generation == _generation)
                {
                    var image = (Image)BannerField.GetValue(panel);
                    image.overrideSprite = null;
                    image.enabled = true;
                }

                _log?.LogWarning("ISB Labs banner: " + ex.GetBaseException().Message);
            }
        }

        private static Banner CreateCover(string mode) => JsonConvert.DeserializeObject<Banner>("{\"id\":\"isb_labs_" + mode + "\",\"pic\":{\"file\":\"isb_labs_" + mode + "_v4.png\",\"path\":\"banners/isb_labs_" + mode + "_v4.png\",\"rcid\":\"\",\"type\":\"png\"}}");
        private static void BeforeLoading(RaidSettings __1)
        {
            _loadingMode = IsLabs(__1.SelectedLocation) && Customize ? Mode : null;
            _menuResetPending = false;
        }

        private static void LoadingShown(MatchmakerTimeHasCome __instance)
        {
            if (_loadingMode != null && AccessTools.Field(typeof(MatchmakerTimeHasCome), "_locationName").GetValue(__instance)is TMP_Text name)
                name.text = Title(_loadingMode);
        }

        private static void BeforeLoadingBanners(ref Location __0)
        {
            if (!IsLabs(__0) || _loadingMode == null)
                return;
            var copy = (Location)CloneLocation.Invoke(__0, null);
            copy.Banners = __0.Banners != null && __0.Banners.Length > 0 ? (Banner[])__0.Banners.Clone() : new Banner[1];
            copy.Banners[0] = CreateCover(_loadingMode);
            __0 = copy;
        }

        private static IList BannerPages(MatchmakerBannersPanel panel)
        {
#if ISB_SPT41
            return AccessTools.Field(typeof(MatchmakerBannersPanel), "_locationBanners").GetValue(panel) as IList;
#else
            return AccessTools.Field(typeof(MatchmakerBannersPanel), "list_0").GetValue(panel) as IList;
#endif
        }

        private static bool IsLabsCover(string key) => Modes.Any(m => key == "isb_labs_" + m + " Name");
        private static void PlaceBanner(IList pages, MatchmakerBanner banner, int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                var entry = pages[i];
                if (!ReferenceEquals(AccessTools.Field(entry.GetType(), "Banner").GetValue(entry), banner))
                    continue;
                pages.RemoveAt(i);
                pages.Insert(Math.Min(index, pages.Count), entry);
                if (AccessTools.Field(entry.GetType(), "Toggle").GetValue(entry)is Component toggle)
                    toggle.transform.SetSiblingIndex(index);
                banner.transform.SetSiblingIndex(index);
                return;
            }
        }

        private static void ControlsBeforeCover(MatchmakerBannersPanel __instance, string __0)
        {
            if (!IsLabsCover(__0))
                return;
            var pages = BannerPages(__instance);
            if (pages == null)
                return;
            foreach (var entry in pages)
                if (AccessTools.Field(entry.GetType(), "Banner").GetValue(entry)is MatchmakerBanner existing && existing.BannerName == "KeyCombination_Title")
                    return;
            var create = AccessTools.GetDeclaredMethods(typeof(MatchmakerBannersPanel)).Single(m => m.ReturnType == typeof(MatchmakerBanner) && m.GetParameters().Length == 3);
            var texture = Texture2D.blackTexture;
            if (_controlsBackground == null)
                _controlsBackground = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero);
            var banner = (MatchmakerBanner)create.Invoke(__instance, new object[] { "KeyCombination_Title", "KeyCombination_Description", _controlsBackground });
            __instance.KeyBannerGenerator.GetKeyBindingBanner(banner.transform);
            PlaceBanner(pages, banner, 0);
        }

        private static void CoverAfterControls(MatchmakerBannersPanel __instance, string __0, MatchmakerBanner __result)
        {
            if (__result != null && IsLabsCover(__0))
                PlaceBanner(BannerPages(__instance), __result, 1);
        }

        private static void Refresh()
        {
            ApplyMapName();
            ApplyNativeBossMarker();
            UpdateButton();
            if (_panel != null && _panel.gameObject.activeInHierarchy && _panelArgs != null)
                SetPanel.Invoke(_panel, _panelArgs);
        }

        internal static void Disable()
        {
            ++_generation;
            Retry.Reset();
            _harmony?.UnpatchSelf();
            _harmony = null;
            _stateRefreshPending = false;
            _request = null;
            _state = null;
            if (_controlsBackground != null)
            {
                UnityEngine.Object.Destroy(_controlsBackground);
                _controlsBackground = null;
            }

            Covers.Clear();
            CoverRequests.Clear();
            if (_emptyCover != null)
            {
                UnityEngine.Object.Destroy(_emptyCover.texture);
                UnityEngine.Object.Destroy(_emptyCover);
                _emptyCover = null;
            }

            if (_selector != null)
                UnityEngine.Object.Destroy(_selector.gameObject);
            if (_choices != null)
                UnityEngine.Object.Destroy(_choices);
            _choices = null;
            _choicesOpen = false;
            ChoiceButtons.Clear();
            if (_menuIcon != null)
            {
                UnityEngine.Object.Destroy(_menuIcon.texture);
                UnityEngine.Object.Destroy(_menuIcon);
                _menuIcon = null;
            }

            _screen = null;
            _loadingMode = null;
            _selector = null;
            _panel = null;
            _panelArgs = null;
            _labsButton = null;
            _buttonArgs = null;
        }
    }
}
