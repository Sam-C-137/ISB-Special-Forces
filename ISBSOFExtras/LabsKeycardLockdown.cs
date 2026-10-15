using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using UnityEngine.SceneManagement;

namespace ISBSSOFExtra
{
    internal static partial class LabsKeycardLockdown
    {
        private const string HarmonyId = "com.samc137.isbsofextras.keycard-lockdown";
        private const string FallbackMessage = "Card Reader unpowered. Activate the facility's auxiliary power or install a GreenBat battery or a modified battery with a toolset to restore power to the reader.";
        private static readonly HashSet<KeycardDoor> Doors = new HashSet<KeycardDoor>();
        private static Harmony _harmony;
        private static bool _active;
        internal static void Enable(ManualLogSource log)
        {
            _batteryLog = log;
            if (_harmony != null)
                return;
            try
            {
                _harmony = new Harmony(HarmonyId);
                MethodInfo keycardActions = null;
                foreach (var method in typeof(InteractionContextHelper).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.ReturnType != typeof(AvailableInteractionState))
                        continue;
                    var args = method.GetParameters();
                    if (args.Length != 3 || args[0].ParameterType != typeof(GamePlayerOwner) || args[1].ParameterType != typeof(KeycardDoor) || args[2].ParameterType != typeof(bool))
                        continue;
                    if (keycardActions != null)
                        throw new AmbiguousMatchException("Multiple keycard interaction methods were found.");
                    keycardActions = method;
                }

                if (keycardActions == null)
                    throw new MissingMethodException("Keycard interaction method was not found.");
                Patch(keycardActions, nameof(AvailableActions), true);
                Patch(AccessTools.Method(typeof(KeycardDoor), "Interact", new[] { typeof(InteractionResult) }), nameof(Interact));
                foreach (string name in new[]
                {
                    "Open",
                    "Unlock"
                }

                )
                    Patch(AccessTools.Method(typeof(WorldInteractiveObject), name, Type.EmptyTypes), nameof(CanOperate));
                var unlockArgs = new[]
                {
                    typeof(KeyComponent),
                    typeof(Player),
                    typeof(WorldInteractiveObject)
                };
                Patch(AccessTools.Method(typeof(KeycardDoor), "UnlockOperation", unlockArgs), nameof(CanOperate));
                foreach (var method in typeof(Door).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (method.Name == "KickOpen")
                        Patch(method, nameof(CanOperate));
            }
            catch (Exception ex)
            {
                Disable();
                log?.LogError("ISB SOF: Keycard lockdown patches failed: " + ex.GetBaseException().Message);
            }
        }

        private static void Patch(MethodInfo method, string handler, bool postfix = false)
        {
            if (method == null)
                throw new MissingMethodException(handler);
            var patch = new HarmonyMethod(typeof(LabsKeycardLockdown), handler);
            if (postfix)
            {
                patch.priority = Priority.Last;
                _harmony.Patch(method, postfix: patch);
            }
            else
                _harmony.Patch(method, prefix: patch);
        }

        internal static void Prepare()
        {
            Doors.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var door in root.GetComponentsInChildren<KeycardDoor>(true))
                        Doors.Add(door);
            }

            IndexBatteryReaders();
        }

        internal static void SetActive(bool active)
        {
            active &= _harmony != null;
            if (_active == active)
                return;
            if (active)
                Prepare();
            _active = active;
            Comfort.Common.Singleton<GameWorld>.Instance?.MainPlayer?.ForceInteractionsChanged();
        }

        internal static bool IsBlocked(WorldInteractiveObject door) => _active && door is KeycardDoor && door != null && door.DoorState == EDoorState.Locked && !PoweredReaders.Contains(door.Id ?? string.Empty);
        private static bool CanOperate(WorldInteractiveObject __instance) => !IsBlocked(__instance);
        private static bool Interact(KeycardDoor __instance, InteractionResult __0) => !IsBlocked(__instance) || (__0 != null && __0.InteractionType == EInteractionType.Close);
        private static void AvailableActions(GamePlayerOwner __0, KeycardDoor __1, bool __2, ref AvailableInteractionState __result)
        {
            if (!IsBlocked(__1))
                return;
            RememberReader(__1);
            bool office = IsOfficeReader(__1);
            string message = ISB.Compatibility.ISBLocalizedText.Get("isb_takeover_keycard_disabled", FallbackMessage);
            var notice = new InteractionAction
            {
                Name = Text("isb_reader_no_power", "Card Reader offline."),
                Disabled = true
            };
            if (!__2)
            {
                __result = new AvailableInteractionState
                {
                    Actions = new List<InteractionAction>
                    {
                        notice
                    },
                    SelectedAction = notice,
                    Error = message
                };
                return;
            }

            var action = new InteractionAction
            {
                Name = Text("isb_reader_install_battery", "Install a GreenBat Battery"),
                Disabled = office || _installation != null || FindBattery(__0?.Player) == null,
                Action = () => InstallBattery(__0, __1)
            };
            bool hasModified = FindBattery(__0?.Player, true, office) != null;
            bool hasTools = HasToolset(__0?.Player);
            if (office)
                message = Text("isb_reader_low_frequency_required", "This reader requires a modified low-frequency battery and a toolset.");
            var modified = new InteractionAction
            {
                Name = office ? Text("isb_reader_install_low_frequency", "Use low-frequency battery (1 charge + toolset)") : Text("isb_reader_install_modified", "Use modified battery (1 charge + toolset)"),
                Disabled = _installation != null || !hasModified || !hasTools,
                Action = () => InstallBattery(__0, __1, true)
            };
            if (hasModified && !hasTools)
                message = Text("isb_reader_toolset_required", "A toolset is required. It will not be consumed.");
            __result = new AvailableInteractionState
            {
                Actions = office ? new List<InteractionAction>
                {
                    notice,
                    modified
                }

                : new List<InteractionAction>
                {
                    notice,
                    action,
                    modified
                },
                SelectedAction = !action.Disabled ? action : !modified.Disabled ? modified : notice,
                Error = action.Disabled && modified.Disabled ? message : string.Empty
            };
        }

        internal static void Reset()
        {
            _active = false;
            SetOfficeEvent(false);
            ResetBattery();
            Doors.Clear();
        }

        internal static void Disable()
        {
            Reset();
            _harmony?.UnpatchSelf();
            _harmony = null;
            _authority = null;
            _sendBattery = _broadcastBattery = null;
            _readerRepaired = null;
        }
    }
}
