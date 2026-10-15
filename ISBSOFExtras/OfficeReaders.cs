using System;
using System.Collections.Generic;
using EFT.Interactive;
using UnityEngine.SceneManagement;

namespace ISBSSOFExtra
{
    internal static partial class LabsKeycardLockdown
    {
        private const string OfficeBatteryTemplate = "264f194ae30547c55dfbf0d9";
        private static readonly HashSet<string> OfficeReaders = new HashSet<string>(StringComparer.Ordinal);
        private static bool _officeEvent;
        private static float _officeReaderScanAt;
        private static string _officeAdmin;
        internal static bool OfficeAdminPowered => _officeEvent && _officeAdmin != null && PoweredReaders.Contains(_officeAdmin);

        private static bool IsOfficeReader(WorldInteractiveObject door) => _officeEvent && door != null && OfficeReaders.Contains(door.Id ?? "");
        internal static void SetOfficeEvent(bool active)
        {
            if (_officeEvent == active && (!active || OfficeReaders.Count == 2 || UnityEngine.Time.unscaledTime < _officeReaderScanAt))
                return;
            _officeReaderScanAt = UnityEngine.Time.unscaledTime + 3f;
            _officeEvent = active;
            OfficeReaders.Clear();
            _officeAdmin = null;
            if (!active)
                return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || !scene.name.StartsWith("Laboratory_Office_Abov", StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var proxy in root.GetComponentsInChildren<InteractiveProxy>(true))
                    {
                        var parent = proxy.transform.parent;
                        if (parent == null || parent.parent == null || parent.parent.name != "Quest_doors" || parent.parent.parent == null || parent.parent.parent.name.TrimStart('_') != "GRAFT_DM_questdoors" || !(proxy.Link is KeycardDoor))
                            continue;
                        bool admin = parent.name == "security_pass_card (2)" && Math.Abs(proxy.transform.position.x + 261.2713f) < .5f;
                        bool safe = parent.name == "security_pass_card (1)" && Math.Abs(proxy.transform.position.x + 268.8578f) < .5f;
                        if ((!admin && !safe) || Math.Abs(proxy.transform.position.y - 5.624f) > .5f)
                            continue;
                        OfficeReaders.Add(proxy.Link.Id);
                        RememberReader(proxy.Link);
                        if (admin)
                            _officeAdmin = proxy.Link.Id;
                    }
            }
        }
    }
}
