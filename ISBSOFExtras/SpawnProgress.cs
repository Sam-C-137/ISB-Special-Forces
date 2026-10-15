using System;
using System.Linq;
using System.Reflection;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private static Type _spawnProgressType;
        internal static void ReportScp173Spawn()
        {
            if (_spawnProgressType == null)
                _spawnProgressType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ISBSpecialForces.Components.ISBSpawnProgressBridge", false)).FirstOrDefault(t => t != null);
            _spawnProgressType?.GetMethod("ReportScp173Spawn", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }

        private bool UpdateSpawnProgress(double chance)
        {
            if (!IsEventAuthority)
                return false;
            if (_spawnProgressType == null)
                _spawnProgressType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("ISBSpecialForces.Components.ISBSpawnProgressBridge", false)).FirstOrDefault(t => t != null);
            if (_spawnProgressType == null)
                return false;
            _spawnProgressType.GetMethod("ReportLabs", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { _labsPreBlackoutEventActive && _raidisActive, _blackoutApplied && !_labsPreBlackoutEventActive, chance });
            if (_spawnProgressType.GetProperty("BlackoutDecisionReady")?.GetValue(null)is bool ready && ready)
            {
                _blackoutSelectedForRaid = (bool)_spawnProgressType.GetProperty("BlackoutSelected").GetValue(null);
                return true;
            }

            return false;
        }
    }
}
