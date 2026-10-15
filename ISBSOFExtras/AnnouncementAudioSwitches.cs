namespace ISBSSOFExtra
{
    internal sealed class AnnouncementAudioSwitches
    {
        internal bool MapMuted { get; private set; }
        internal bool DecontaminationMuted { get; private set; }

        internal bool Muted(bool decontamination, bool deadManActive = false) => !deadManActive && (decontamination ? DecontaminationMuted : MapMuted);
        internal static string FeedbackKey(bool decontamination, bool enabled) => (decontamination ? "isb_decon_pa_" : "isb_map_pa_") + (enabled ? "enabled" : "disabled");
        internal void Set(bool map, bool decontamination)
        {
            MapMuted = map;
            DecontaminationMuted = decontamination;
        }

        internal void Reset() => Set(false, false);
    }

    public partial class ISBSpecialForcesAudio
    {
        private readonly AnnouncementAudioSwitches _announcementSwitches = new AnnouncementAudioSwitches();
        private bool AnnouncementMuted(string name) => _announcementSwitches.Muted(IsDecontaminationClip(name), DeadManActive);
        private void SetAnnouncementMutes(bool map, bool decontamination)
        {
            _announcementSwitches.Set(map, decontamination);
            LabsNativeAnnouncements.SetUserMuted(_raidisActive && map);
            if (_facilityAudio != null)
                _facilityAudio.mute = AnnouncementMuted(_facilityAudio.clip?.name);
            LabsTakeoverInfrastructure.ApplyAnnouncementMutes(map, decontamination);
        }
    }
}
