using System;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal sealed class LabsEmergencyElevators
    {
        internal const string ActionPrefix = "emergency_elevator_";
        private static readonly string[] Names =
        {
            "lab_Elevator_Main",
            "lab_Elevator_Cargo",
            "lab_Elevator_Med"
        };
        private static readonly string[] LocaleKeys =
        {
            "isb_emergency_elevator_main",
            "isb_emergency_elevator_cargo",
            "isb_emergency_elevator_medical"
        };
        private static readonly string[] Fallbacks =
        {
            "Emergency Extraction: Main Elevator",
            "Emergency Extraction: Cargo Elevator",
            "Emergency Extraction: Medical Block Elevator"
        };
        private static readonly Vector3[] Positions =
        {
            new Vector3(-282.311f, -2.559f, -335.334f),
            new Vector3(-112.357f, 5.4101f, -407.528f),
            new Vector3(-112.7967f, -2.7321f, -343.1172f)
        };
        private readonly EFT.Interactive.Switch[] _buttons = new EFT.Interactive.Switch[3];
        private readonly ExfiltrationPoint[] _points = new ExfiltrationPoint[3];
        private Action<string> _request;
        private bool _active, _failed;
        internal int Mask { get; private set; }

        internal static bool PlayerNear(string action, Vector3 player)
        {
            int index = ActionIndex(action);
            return index >= 0 && (player - Positions[index]).sqrMagnitude <= 16f;
        }

        internal static int ActionIndex(string action)
        {
            for (int i = 0; i < Names.Length; i++)
                if (string.Equals(action, ActionPrefix + i, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        internal void SetActive(bool active, bool presentLocally, Action<string> request, ManualLogSource log)
        {
            if (!active)
            {
                Reset();
                return;
            }

            _active = true;
            _request = request;
            var controller = CommonAssets.Scripts.Game.ExfiltrationController.Instance;
            if (controller?.ExfiltrationPoints == null)
                return;
            for (int i = 0; i < Names.Length; i++)
            {
                if (_points[i] == null)
                    foreach (var point in controller.ExfiltrationPoints)
                        if (point?.Settings != null && point.Settings.Name == Names[i])
                        {
                            _points[i] = point;
                            break;
                        }

                if (!presentLocally || _buttons[i] != null)
                    continue;
                try
                {
                    var sw = LabsTakeoverInfrastructure.CreateEmergencySwitch("ISB_EmergencyElevator_" + i, ISB.Compatibility.ISBLocalizedText.Get(LocaleKeys[i], Fallbacks[i]), Positions[i]);
                    if (sw == null)
                        throw new InvalidOperationException("Native switch template unavailable.");
                    _buttons[i] = sw;
                    sw.OnDoorStateChanged += OnPressed;
                }
                catch (Exception ex)
                {
                    if (!_failed)
                        log?.LogError("ISB SOF: Emergency elevator button unavailable: " + ex.Message);
                    _failed = true;
                }
            }

            RefreshButtons();
        }

        private void OnPressed(WorldInteractiveObject interactive, EDoorState previous, EDoorState next)
        {
            if (!_active || next != EDoorState.Open)
                return;
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (!ReferenceEquals(_buttons[i], interactive))
                    continue;
                _request?.Invoke(ActionPrefix + i);
                if ((Mask & (1 << i)) == 0)
                {
                    _buttons[i].DoorState = EDoorState.Shut;
                    _buttons[i].Operatable = true;
                }

                break;
            }
        }

        internal bool Activate(int index, ManualLogSource log)
        {
            if (!_active || index < 0 || index >= Names.Length)
                return false;
            if ((Mask & (1 << index)) != 0)
                return true;
            var point = _points[index];
            if (point == null)
                return false;
            try
            {
                point.Status = EExfiltrationStatus.RegularMode;
                var box = point.GetComponent<BoxCollider>();
                var world = Singleton<GameWorld>.Instance;
                if (box != null && world != null)
                {
                    foreach (var entry in world.RegisteredPlayers)
                    {
                        if (!(entry is Player player) || player.HealthController == null || !player.HealthController.IsAlive || !point.InfiltrationMatch(player) || point.Entered.Contains(player))
                            continue;
                        Vector3 local = box.transform.InverseTransformPoint(player.Position + Vector3.up * 0.5f) - box.center;
                        Vector3 half = box.size * 0.5f;
                        if (Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y || Mathf.Abs(local.z) > half.z)
                            continue;
                        point.Entered.Add(player);
                        point.Proceed(player, false);
                    }
                }

                BlackoutBridge.ShowExfil(Names[index], log);
                Mask |= 1 << index;
                RefreshButtons();
                return true;
            }
            catch (Exception ex)
            {
                if (!_failed)
                    log?.LogError("ISB SOF: Emergency extraction failed: " + ex.Message);
                _failed = true;
                return false;
            }
        }

        internal void ApplyMask(int mask, ManualLogSource log)
        {
            if (mask < 0 || mask > 7)
                return;
            for (int i = 0; i < Names.Length; i++)
                if ((mask & (1 << i)) != 0)
                    Activate(i, log);
        }

        private void RefreshButtons()
        {
            for (int i = 0; i < _buttons.Length; i++)
                if (_buttons[i] != null)
                    _buttons[i].gameObject.SetActive(_active && (Mask & (1 << i)) == 0);
        }

        internal void Reset()
        {
            _active = false;
            Mask = 0;
            _request = null;
            _failed = false;
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i] != null)
                {
                    _buttons[i].OnDoorStateChanged -= OnPressed;
                    _buttons[i].gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(_buttons[i].gameObject);
                }

                _buttons[i] = null;
                _points[i] = null;
            }
        }
    }
}
