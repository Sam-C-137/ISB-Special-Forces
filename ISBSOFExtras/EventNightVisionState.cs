using BepInEx.Bootstrap;
using EFT;
using System;
using System.Reflection;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private Action<GameWorld, int> _eventNightVisionPublisher;
        private WeakReference<GameWorld> _eventNightVisionPublishedWorld;
        private int _eventNightVisionPublishedState;
        private bool _eventNightVisionPublished, _eventNightVisionPublishWarning, _eventNightVisionSnapshotApplying;

        public void PublishEventNightVisionSnapshot()
        {
            PublishEventNightVisionStateLocal(force: true);
        }

        private void PublishEventNightVisionStateLocal(bool endingRaid = false, bool force = false)
        {
            if (_eventNightVisionSnapshotApplying && !endingRaid)
                return;
            GameWorld world = _activeRaidWorld;
            bool active = !endingRaid && _raidisActive && IsLaboratoryPresentationAllowed() &&
                (_labsPreBlackoutEventActive || _blackoutSelectedForRaid || _blackoutApplied || _takeoverFacilityRestored);
            int state = active ? (_blackoutApplied ? 2 : 1) : 0;
            bool sameWorld = ReferenceEquals(world, null) ? _eventNightVisionPublishedWorld == null :
                _eventNightVisionPublishedWorld != null && _eventNightVisionPublishedWorld.TryGetTarget(out var previous) && ReferenceEquals(previous, world);
            if (!force && _eventNightVisionPublished && sameWorld && _eventNightVisionPublishedState == state)
                return;
            try
            {
                if (_eventNightVisionPublisher == null)
                {
                    if (!Chainloader.PluginInfos.TryGetValue("com.samc137.isbspecialforces", out var plugin) || plugin?.Instance == null)
                        return;
                    var method = plugin.Instance.GetType().GetMethod("PublishEventNightVisionState", BindingFlags.Public | BindingFlags.Instance,
                        null, new[] { typeof(GameWorld), typeof(int) }, null);
                    if (method == null)
                        throw new MissingMethodException("The ISB client plugin does not support event night vision. Update both ISB DLLs.");
                    _eventNightVisionPublisher = (Action<GameWorld, int>)Delegate.CreateDelegate(typeof(Action<GameWorld, int>), plugin.Instance, method);
                }
                _eventNightVisionPublisher(world, state);
                _eventNightVisionPublishedWorld = ReferenceEquals(world, null) ? null : new WeakReference<GameWorld>(world);
                _eventNightVisionPublishedState = state;
                _eventNightVisionPublished = true;
            }
            catch (Exception ex)
            {
                if (!_eventNightVisionPublishWarning)
                {
                    _eventNightVisionPublishWarning = true;
                    Log.LogWarning("ISB SOF: Event night vision state unavailable: " + ex.GetBaseException().Message);
                }
            }
        }
    }
}
