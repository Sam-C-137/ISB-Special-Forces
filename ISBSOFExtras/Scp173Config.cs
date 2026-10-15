using System;
using System.IO;
using System.Collections.Generic;

namespace ISBSSOFExtra
{
    internal sealed class Scp173Config
    {
        public bool enabled = true;
        public bool allowClientDebug = false;
        public float chancePercent = 5f;
        public string[] maps =
        {
            "laboratory"
        };
        public float spawnDelaySeconds = 45f;
        public bool spawnOnContainmentBreach = true;
        public bool activateWhenSeen = true;
        public float activationLookSeconds = 5f;
        public float activationBlackoutSeconds = 2f;
        public float warningDelaySeconds = 30f;
        public float automaticContainmentChancePercent = 25f;
        public float automaticContainmentMinSeconds = 360f;
        public float automaticContainmentMaxSeconds = 600f;
        public float containmentSeconds = 6f;
        public string containmentGasAudio = "GasLeak.wav";
        public float containmentGasVolume = .8f;
        public float containmentGasFadeInSeconds = .3f;
        public float containmentGasFadeOutSeconds = 1f;
        public Scp173SpawnPoint containmentButton = new Scp173SpawnPoint { x=-134.5208f, y=1.2125f, z=-358.4231f };
        public Scp173SpawnPoint containmentButtonRotation = new Scp173SpawnPoint { x=90f, y=270f, z=0f };
        public int chasesBeforeRetarget = 3;
        public float chaseDurationSeconds = 6f;
        public float chaseRetrySeconds = 30f;
        public float chaseStepHeight = .3f;

        public bool fixedSpawn = false;
        public Dictionary<string, Scp173SpawnPoint> fixedSpawnPoints = new Dictionary<string, Scp173SpawnPoint>
        {
            ["laboratory"] = new Scp173SpawnPoint
            {
                x = -130.75f,
                y = .1f,
                z = -358.4f
            }
        };
        public float initialMinDistance = 18f;
        public float initialMaxDistance = 32f;
        public float teleportTriggerDistance = 20f;
        public float teleportMinDistance = 8f;
        public float teleportMaxDistance = 15f;
        public float teleportCooldownSeconds = 180f;
        public bool periodicTeleport = true;
        public float killDistance = 1.6f;
        public float killCooldownSeconds = 0.75f;
        public float unobservedGraceSeconds = 0.25f;
        public float afterTeleportGraceSeconds = 0.75f;
        public float moveSpeed = 9.1f;
        public float height = 2f;
        public float bodyRadius = 0.28f;
        public float observationDistance = 100f;
        public float botViewAngle = 110f;
        public bool botsObserve = true;
        public bool killBots = true;
        public bool killPlayers = true;
        public float logicHz = 20f;
        public float observationReportHz = 20f;
        public float observerTimeoutSeconds = 0.75f;
        public float pathRefreshSeconds = 0.5f;
        public int spawnAttempts = 24;
        public string moveAudio = "move.wav";
        public string teleportAudio = "teleport.wav";
        public string killAudio = "kill.wav";
        public string activationAudio = "activation.wav";
        public string[] moveAudioVariants =
        {
            "move-02.wav",
            "move-03.wav"
        };
        public string[] teleportAudioVariants =
        {
            "teleport-02.wav",
            "teleport-03.wav",
            "teleport-04.wav"
        };
        public string[] killAudioVariants =
        {
            "kill-02.wav",
            "kill-03.wav"
        };
        public float audioVolume = 0.225f;
        public float audioMaxDistance = 35f;
        public float moveAudioCooldownSeconds = 2f;
        public float audioFadeInSeconds = 0.15f;
        public float audioFadeOutSeconds = 0.25f;
        internal void Normalize()
        {
            activationLookSeconds = Clamp(activationLookSeconds, .1f, 60f, 5f);
            activationBlackoutSeconds = Clamp(activationBlackoutSeconds, 1f, 15f, 2f);
            warningDelaySeconds = Clamp(warningDelaySeconds, 0f, 600f, 30f);
            automaticContainmentChancePercent = Clamp(automaticContainmentChancePercent, 0f, 100f, 25f);
            automaticContainmentMinSeconds = Clamp(automaticContainmentMinSeconds, 1f, 3600f, 360f);
            automaticContainmentMaxSeconds = Clamp(automaticContainmentMaxSeconds, automaticContainmentMinSeconds, 7200f, Math.Max(600f, automaticContainmentMinSeconds));
            containmentSeconds = Clamp(containmentSeconds, 1f, 30f, 6f);
            containmentGasAudio = AudioFile(containmentGasAudio);
            containmentGasVolume = Clamp(containmentGasVolume, 0f, 1f, .8f);
            containmentGasFadeInSeconds = Clamp(containmentGasFadeInSeconds, 0f, 5f, .3f);
            containmentGasFadeOutSeconds = Clamp(containmentGasFadeOutSeconds, 0f, 5f, 1f);
            chaseDurationSeconds = Clamp(chaseDurationSeconds, 1f, 60f, 6f);
            chaseRetrySeconds = Clamp(chaseRetrySeconds, 1f, 600f, 30f);
            chaseStepHeight = Clamp(chaseStepHeight, .1f, .5f, .3f);
            chasesBeforeRetarget = Math.Max(1, Math.Min(20, chasesBeforeRetarget));
            if (containmentButtonRotation == null || !containmentButtonRotation.Valid) throw new InvalidDataException("Nightmare containment button rotation must be finite.");
            if (containmentButton == null || !containmentButton.Valid) throw new InvalidDataException("Nightmare containment button coordinates must be finite.");
            chancePercent = Clamp(chancePercent, 0f, 100f, 5f);
            spawnDelaySeconds = Clamp(spawnDelaySeconds, 0f, 1800f, 45f);
            killDistance = Clamp(killDistance, 0.5f, 3f, 1.6f);
            initialMinDistance = Clamp(initialMinDistance, killDistance + 2f, 200f, 18f);
            initialMaxDistance = Clamp(initialMaxDistance, initialMinDistance, 250f, Math.Max(32f, initialMinDistance));
            teleportTriggerDistance = Clamp(teleportTriggerDistance, 5f, 250f, 20f);
            teleportMinDistance = Clamp(teleportMinDistance, killDistance + 2f, 100f, 8f);
            teleportMaxDistance = Clamp(teleportMaxDistance, teleportMinDistance, 150f, Math.Max(15f, teleportMinDistance));
            teleportCooldownSeconds = Clamp(teleportCooldownSeconds, 1f, 3600f, 180f);
            killCooldownSeconds = Clamp(killCooldownSeconds, 0.2f, 60f, 0.75f);
            unobservedGraceSeconds = Clamp(unobservedGraceSeconds, 0.1f, 5f, 0.25f);
            afterTeleportGraceSeconds = Clamp(afterTeleportGraceSeconds, 0.25f, 10f, 0.75f);
            moveSpeed = Clamp(moveSpeed, 0.1f, 20f, 9.1f);
            height = Clamp(height, 1f, 3f, 2f);
            bodyRadius = Clamp(bodyRadius, 0.15f, 0.6f, 0.28f);
            observationDistance = Clamp(observationDistance, teleportMaxDistance + 5f, 500f, 100f);
            botViewAngle = Clamp(botViewAngle, 30f, 180f, 110f);
            logicHz = Clamp(logicHz, 5f, 30f, 20f);
            observationReportHz = Clamp(observationReportHz, 10f, 30f, 20f);
            observerTimeoutSeconds = Clamp(observerTimeoutSeconds, 0.25f, 3f, 0.75f);
            pathRefreshSeconds = Clamp(pathRefreshSeconds, 0.2f, 5f, 0.5f);
            spawnAttempts = Math.Max(1, Math.Min(48, spawnAttempts));
            audioVolume = Clamp(audioVolume, 0f, 1f, 0.225f);
            audioMaxDistance = Clamp(audioMaxDistance, 3f, 150f, 35f);
            moveAudioCooldownSeconds = Clamp(moveAudioCooldownSeconds, 0.5f, 60f, 2f);
            audioFadeInSeconds = Clamp(audioFadeInSeconds, 0f, 5f, 0.15f);
            audioFadeOutSeconds = Clamp(audioFadeOutSeconds, 0f, 5f, 0.25f);
            moveAudio = AudioFile(moveAudio);
            teleportAudio = AudioFile(teleportAudio);
            killAudio = AudioFile(killAudio);
            activationAudio = AudioFile(activationAudio);
            moveAudioVariants = AudioFiles(moveAudioVariants);
            teleportAudioVariants = AudioFiles(teleportAudioVariants);
            killAudioVariants = AudioFiles(killAudioVariants);
            if (maps == null)
                maps = Array.Empty<string>();
            if (fixedSpawnPoints == null)
                fixedSpawnPoints = new Dictionary<string, Scp173SpawnPoint>();
            if (fixedSpawnPoints.Count > 128)
                throw new InvalidDataException("Nightmare fixed spawn list exceeds 128 maps.");
            foreach (var point in fixedSpawnPoints.Values)
                if (point == null || !point.Valid)
                    throw new InvalidDataException("Nightmare fixed spawn coordinates must be finite and inside the map bounds.");
        }

        internal static string AudioFile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";
            if (value.Length > 120 || Path.GetFileName(value) != value || value.Contains("..") || !string.Equals(Path.GetExtension(value), ".wav", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Nightmare audio must be a WAV filename inside the Nightmare folder.");
            return value;
        }

        private static string[] AudioFiles(string[] values)
        {
            if (values == null)
                return Array.Empty<string>();
            if (values.Length > 8)
                throw new InvalidDataException("Nightmare permits at most eight audio variants per action.");
            var result = new List<string>();
            foreach (var value in values)
            {
                var name = AudioFile(value);
                if (!string.IsNullOrEmpty(name) && !result.Contains(name))
                    result.Add(name);
            }

            return result.ToArray();
        }

        private static float Clamp(float v, float min, float max, float fallback) => Math.Max(min, Math.Min(max, float.IsNaN(v) || float.IsInfinity(v) ? fallback : v));
    }

    internal sealed class Scp173SpawnPoint
    {
        public float x, y, z;
        internal bool Valid => ValidValue(x) && ValidValue(y) && ValidValue(z);

        private static bool ValidValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 100000f;
    }
}
