using System;
using System.IO;
using System.Collections.Generic;

namespace ISBSSOFExtra
{
    internal sealed class FacilityAudioCue
    {
        public string audio;
        public double start;
        public float duration;
        internal bool Valid => !string.IsNullOrEmpty(audio) && Path.GetFileName(audio) == audio && !double.IsNaN(start) && !double.IsInfinity(start) && start >= 0 && !float.IsNaN(duration) && !float.IsInfinity(duration) && duration > 0 && duration <= 600;
    }

    internal static class FacilityPowerTiming
    {
        internal static double RestoreAt(double start, float fifthDuration, float countdownAt, float seconds) => Math.Max(start + fifthDuration, start + countdownAt + seconds);
    }

    internal sealed class FacilityRemoteOperation
    {
        public string cell = "";
        public double finishAt;
        public bool shutdown;
        internal string Action => cell.StartsWith("P:", StringComparison.Ordinal) ? "GATE_" + cell.Substring(2) : (shutdown ? "SHUTDOWN_" : "POWER_") + cell;
        internal bool Valid => !double.IsNaN(finishAt) && !double.IsInfinity(finishAt) && finishAt >= FacilitySequenceState.RemoteDurationSeconds && (cell == "A" || cell == "B" || cell == "C" || cell == "D" || (!shutdown && (cell == "P:A" || cell == "P:B" || cell == "P:C")));
    }

    internal sealed class FacilitySequenceState
    {
        internal const double RemoteDurationSeconds = 120d;
        internal const int RemoteConcurrentLimit = 3;
        public int revision;
        public bool masterStarted;
        public bool powerCommissioned;
        public bool shutdownCommissioned;
        public double countdownFrom = -1d;
        internal bool FreeToggle => powerCommissioned && shutdownCommissioned;

        public double restoreAt = -1d;
        public List<FacilityRemoteOperation> remoteOperations = new List<FacilityRemoteOperation>();
        internal bool Schedule(string cell, bool shutdown, double now)
        {
            var operation = new FacilityRemoteOperation
            {
                cell = cell,
                shutdown = shutdown,
                finishAt = now + RemoteDurationSeconds
            };
            if (!Valid || remoteOperations.Count >= RemoteConcurrentLimit || double.IsNaN(now) || double.IsInfinity(now) || now < 0d || !operation.Valid || remoteOperations.Exists(item => item.cell == cell))
                return false;
            remoteOperations.Add(operation);
            return true;
        }

        internal bool CancelForManualAction(string action)
        {
            return remoteOperations.RemoveAll(item => item.Action == action) != 0;
        }

        internal bool TryTakeDue(double now, out FacilityRemoteOperation operation)
        {
            operation = null;
            if (double.IsNaN(now) || double.IsInfinity(now))
                return false;
            for (int i = 0; i < remoteOperations.Count; i++)
            {
                if (now < remoteOperations[i].finishAt)
                    continue;
                operation = remoteOperations[i];
                remoteOperations.RemoveAt(i);
                return true;
            }

            return false;
        }

        private bool UniqueRemoteCells()
        {
            for (int i = 0; i < remoteOperations.Count; i++)
                for (int j = i + 1; j < remoteOperations.Count; j++)
                    if (remoteOperations[i].cell == remoteOperations[j].cell)
                        return false;
            return true;
        }

        public List<FacilityAudioCue> cues = new List<FacilityAudioCue>();
        internal bool Valid => revision >= 0 && remoteOperations != null && remoteOperations.Count <= RemoteConcurrentLimit && remoteOperations.TrueForAll(item => item != null && item.Valid) && UniqueRemoteCells() && (!shutdownCommissioned || powerCommissioned) && !double.IsNaN(countdownFrom) && !double.IsInfinity(countdownFrom) && countdownFrom >= -1d && cues != null && cues.Count <= 16 && cues.TrueForAll(c => c != null && c.Valid) && !double.IsNaN(restoreAt) && !double.IsInfinity(restoreAt) && restoreAt >= -1d;
    }
}
