using System;

namespace ISB.Compatibility
{
    public sealed class OfficePoint
    {
        public float x { get; set; }
        public float y { get; set; }
        public float z { get; set; }
        public float rotationY { get; set; }

        public OfficePoint()
        {
        }

        public OfficePoint(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool IsValid() => Finite(x) && Finite(y) && Finite(z) && Finite(rotationY);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public sealed class LabsOfficeConfig
    {
        public bool enabled { get; set; } = true;
        public OfficePoint keypad { get; set; } = new OfficePoint(-267.7555f, 4.6325f, -376.0247f);
        public OfficePoint shutdownButton { get; set; } = new OfficePoint(-266.909f, 4.946f, -375.567f);
        public OfficePoint pendingFlashDrive { get; set; } = new OfficePoint(-267.744f, 4.118f, -374.304f);
        public string pendingFlashDriveItem { get; set; } = "";
        public OfficePoint cassieFlashDrive { get; set; } = new OfficePoint(-134.091f, 1.368f, -323.890f);
        public string cassieFlashDriveItem { get; set; } = "4fef2f1523c2754ade168cfa";
        public DeadManSwitchConfig deadManSwitch { get; set; } = new DeadManSwitchConfig();

        public bool IsValid() => deadManSwitch?.IsValid() == true && keypad?.IsValid() == true && shutdownButton?.IsValid() == true;
    }

    public sealed class DeadManSwitchConfig
    {
        public string audio { get; set; } = "DeadManSwitchV2.wav";
        public string image { get; set; } = "DeadManSwitchImage.jpg";
        public float countdownAt { get; set; } = 16.64f;
        public float countdownSeconds { get; set; } = 90f;
        public float endEffectSeconds { get; set; } = 3f;
        public string endAudio { get; set; } = "EventEnd.wav";
        public float endAudioSeconds { get; set; } = 23.613542f;
        public float fadeSeconds { get; set; } = 1.5f;
        public double FadeAt => countdownAt + countdownSeconds + Math.Max(2f, endEffectSeconds) + 2d;
        public double EndAudioAt => FadeAt + fadeSeconds;
        public double PlayersDieAt => EndAudioAt + endAudioSeconds;

        public bool IsValid() => !string.IsNullOrWhiteSpace(audio) && !string.IsNullOrWhiteSpace(image) && System.IO.Path.GetFileName(audio) == audio && System.IO.Path.GetFileName(image) == image && countdownAt >= 0f && countdownAt <= 300f && countdownSeconds > 0f && countdownSeconds <= 1800f && endEffectSeconds >= 0f && endEffectSeconds <= 10f && !string.IsNullOrWhiteSpace(endAudio) && System.IO.Path.GetFileName(endAudio) == endAudio && endAudioSeconds > 0f && endAudioSeconds <= 300f && fadeSeconds > 0f && fadeSeconds <= 10f;
    }

    public sealed class OfficeAccessState
    {
        public const string FixedCode = "7816";
        public LabsOfficeConfig config { get; set; } = new LabsOfficeConfig();
        public string raidCode { get; set; } = "";
        public int[] kteBriefing { get; set; } = new int[0];
        public OfficeRaidOperations operations { get; set; } = new OfficeRaidOperations();
        public int stage { get; set; }
        public double bootStartedAt { get; set; } = -1d;
        public double radioStartedAt { get; set; } = -1d;
        public bool radioOff { get; set; }
        public bool consoleShown { get; set; }

        public bool ConsoleReady(double now) => stage >= 1 && bootStartedAt >= 0d && now >= bootStartedAt;
        public int revision { get; set; }
        public bool aborted { get; set; }
        public double pausedAt { get; set; } = -1d;
        public double pausedSeconds { get; set; }
        public double commandReadyAt { get; set; }

        public double Clock(double now) => (aborted && pausedAt >= 0d ? pausedAt : now) - pausedSeconds;
        public double destructionStart { get; set; } = -1d;
        public bool DestructionActive => destructionStart >= 0d;

        public bool IsValid() => kteBriefing != null && (kteBriefing.Length == 0 || OfficeKteBriefing.Valid(kteBriefing)) && operations?.Valid() == true && config?.enabled == true && config.IsValid() && ValidCode(raidCode) && (destructionStart == -1d || (!double.IsNaN(destructionStart) && !double.IsInfinity(destructionStart) && destructionStart >= 0d)) && !double.IsNaN(pausedSeconds) && !double.IsInfinity(pausedSeconds) && pausedSeconds >= 0d && !double.IsNaN(pausedAt) && !double.IsInfinity(pausedAt) && pausedAt >= -1d && !double.IsNaN(commandReadyAt) && !double.IsInfinity(commandReadyAt) && commandReadyAt >= 0d && !double.IsNaN(radioStartedAt) && !double.IsInfinity(radioStartedAt) && radioStartedAt >= -1d && !double.IsNaN(bootStartedAt) && !double.IsInfinity(bootStartedAt) && bootStartedAt >= -1d && raidCode != FixedCode && stage >= 0 && stage <= 2 && revision >= 0 && (!aborted || stage == 2);
        public bool Submit(string code, int expectedStage)
        {
            if (!IsValid() || aborted || DestructionActive || stage == 2 || expectedStage != stage || !ValidCode(code))
                return false;
            if (code != (stage == 0 ? FixedCode : raidCode))
                return false;
            stage++;
            revision++;
            return true;
        }

        public bool ArmOnWrongCode(string code, int expectedStage, double now)
        {
            if (!IsValid() || aborted || DestructionActive || stage == 2 || expectedStage != stage || !ValidCode(code) || double.IsNaN(now) || double.IsInfinity(now) || now < 0d || code == (stage == 0 ? FixedCode : raidCode))
                return false;
            destructionStart = now;
            revision++;
            return true;
        }

        public bool SetDecontamination(bool enabled, double now)
        {
            if (!IsValid() || stage != 2 || DestructionActive || double.IsNaN(now) || double.IsInfinity(now) || now < commandReadyAt || enabled != aborted)
                return false;
            if (enabled)
            {
                pausedSeconds += Math.Max(0d, now - pausedAt);
                pausedAt = -1d;
            }
            else
                pausedAt = now;
            aborted = !enabled;
            commandReadyAt = now + 8d;
            revision++;
            return true;
        }

        public static bool ValidCode(string code)
        {
            if (code == null || code.Length != 4)
                return false;
            for (int i = 0; i < code.Length; i++)
                if (code[i] < '0' || code[i] > '9')
                    return false;
            return true;
        }

        public static bool Available(bool eventActive, bool officeInstalled, LabsOfficeConfig config) => eventActive && officeInstalled && config?.enabled == true && config.IsValid();
    }
}
