using System;

namespace ISBInfo
{
    internal sealed class AnnouncementEffectNotices
    {
        private int _shown;
        private double _gateDue, _cameraDue, _chaosDue, _decontaminationDue, _evacuationDue, _lockdownDue, _cameraTrackingDue;
        internal void Begin(string audio, double now, float delay)
        {
            if (double.IsNaN(now) || double.IsInfinity(now))
                return;
            if (float.IsNaN(delay) || float.IsInfinity(delay))
                delay = 0f;
            double due = now + Math.Max(0f, Math.Min(300f, delay));
            if ((_shown & 64) == 0 && (string.Equals(audio, "CameraStart", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "CameraStart.wav", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "CameraStart_RU", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "CameraStart_RU.wav", StringComparison.OrdinalIgnoreCase)))
                _cameraTrackingDue = due;
            if ((_shown & 1) == 0 && (string.Equals(audio, "ISBGateB", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "ISBGateB.wav", StringComparison.OrdinalIgnoreCase)))
                _gateDue = due;
            if ((_shown & 2) == 0 && (string.Equals(audio, "CameraEnemies", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "CameraEnemies.wav", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "CameraEnemies_RU", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "CameraEnemies_RU.wav", StringComparison.OrdinalIgnoreCase)))
                _cameraDue = due;
            if ((_shown & 4) == 0 && (string.Equals(audio, "ISB-LabsChaos", StringComparison.OrdinalIgnoreCase) || string.Equals(audio, "ISB-LabsChaos.wav", StringComparison.OrdinalIgnoreCase)))
                _chaosDue = due;
            string name = System.IO.Path.GetFileNameWithoutExtension(audio ?? string.Empty);
            if ((_shown & 8) == 0 && (string.Equals(name, "DecontaminationProcess01", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "DecontaminationProcess01_RU", StringComparison.OrdinalIgnoreCase)))
                _decontaminationDue = due;
            if ((_shown & 16) == 0 && (name == "DecontaminationProcess05" || name == "DecontaminationProcess05_RU"))
                _evacuationDue = due;
            if ((_shown & 32) == 0 && (name == "DecontaminationProcess07" || name == "DecontaminationProcess07_RU"))
                _lockdownDue = due;
        }

        internal float Remaining(string effect, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now))
                return 0f;
            double due = effect == "gate_b_closed" ? _gateDue : effect == "camera_hunt" ? _cameraDue : effect == "labs_chaos" ? _chaosDue : effect == "decontamination_warning" ? _decontaminationDue : effect == "decontamination_gates" ? _evacuationDue : effect == "decontamination_lockdown" ? _lockdownDue : effect == "camera_tracking" ? _cameraTrackingDue : 0d;
            return (float)Math.Max(0d, Math.Min(300d, due - now));
        }

        internal string Record(string effectId)
        {
            int flag;
            string message;
            switch (effectId)
            {
                case "camera_tracking":
                    flag = 64;
                    message = "isb_notice_camera_tracking";
                    break;
                case "gate_b_closed":
                    flag = 1;
                    message = "isb_notice_gate_b_closed";
                    break;
                case "camera_hunt":
                    flag = 2;
                    message = "isb_notice_camera_hunt";
                    break;
                case "labs_chaos":
                    flag = 4;
                    message = "isb_notice_labs_chaos";
                    break;
                case "decontamination_warning":
                    flag = 8;
                    message = "isb_notice_decontamination_warning";
                    break;
                case "decontamination_gates":
                    flag = 16;
                    message = "isb_notice_decontamination_gates";
                    break;
                case "decontamination_lockdown":
                    flag = 32;
                    message = "isb_notice_decontamination_lockdown";
                    break;
                default:
                    return null;
            }

            if ((_shown & flag) != 0)
                return null;
            _shown |= flag;
            return message;
        }

        internal static string Fallback(string key)
        {
            switch (key)
            {
                case "isb_notice_camera_tracking":
                    return "ISB surveillance is active. Unauthorized intruders and escapees are being marked on ISB navigation devices.";
                case "isb_notice_gate_b_closed":
                    return "ISB forces have completely locked down the exits through the \"Hangar Gate\". This extraction option is unavailable.";
                case "isb_notice_camera_hunt":
                    return "External ISB Forces have completed the activation and full tracking of the facility's sensors and surveillance cameras. You and all targets have been located and are exposed.";
                case "isb_notice_labs_chaos":
                    return "The containment breach is spiraling out of control. ISB reinforcements have been dispatched to the facility to expel any intruders.";
                case "isb_notice_decontamination_warning":
                    return "Decontamination System activated. After the countdown ends, the toxin will be released and will begin to fill the facility. Equip a Gas Mask or evacuate the facility immediately.";
                case "isb_notice_decontamination_gates":
                    return "The Decontamination System is active. The facility gates have been opened automatically. Evacuate IMMEDIATELY.";
                case "isb_notice_decontamination_lockdown":
                    return "ContainmentBreach is active. All exits have been restricted and decontamination will begin. Look for an alternative exit.";
                default:
                    return key;
            }
        }

        internal void Reset()
        {
            _shown = 0;
            _gateDue = _cameraDue = _chaosDue = _decontaminationDue = _evacuationDue = _lockdownDue = _cameraTrackingDue = 0d;
        }
    }
}
