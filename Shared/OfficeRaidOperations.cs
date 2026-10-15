using System;
using System.Collections.Generic;

namespace ISB.Compatibility
{
    public sealed class OfficeRaidOperator
    {
        public int actions { get; set; }
        public string rewardId { get; set; } = "";
    }

    public sealed class OfficePlantReservation
    {
        public string profile { get; set; } = "";
        public string token { get; set; } = "";
        public string item { get; set; } = "";
        public double start { get; set; }
    }

    public sealed class OfficeRaidOperations
    {
        public const int Hack = 1, Code = 2, Shutdown = 4, Ssd = 8, Download = 16, Reader = 32;
        public bool codeUploaded { get; set; }
        public int disk { get; set; }
        public bool diskInstalled { get; set; }
        public string downloader { get; set; } = "";
        public int downloaded { get; set; }
        public int downloading { get; set; } = -1;
        public double downloadAt { get; set; } = -1;
        public int fileCount { get; set; } = 2;
        public Dictionary<string, OfficeRaidOperator> operators { get; set; } = new Dictionary<string, OfficeRaidOperator>();
        public Dictionary<string, string> awards { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> awardLocations { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, OfficePlantReservation> reservations { get; set; } = new Dictionary<string, OfficePlantReservation>();

        private static bool Profile(string value) => !string.IsNullOrEmpty(value) && value.Length <= 64 && value.IndexOf('|') < 0;
        public bool Valid()
        {
            if (operators == null || awards == null || awards.Count > 64 || awardLocations == null || awardLocations.Count > 64 || reservations == null || operators.Count > 64 || reservations.Count > 3 || fileCount < 1 || fileCount > 20 || disk < 0 || downloaded < 0 || downloaded >= (1 << fileCount) || downloading < -1 || downloading >= fileCount || double.IsNaN(downloadAt) || double.IsInfinity(downloadAt) || downloadAt < -1 || downloader == null || downloader.Length > 64)
                return false;
            foreach (var op in operators)
                if (!Profile(op.Key) || op.Value == null || op.Value.actions < 0 || op.Value.actions > 63 || op.Value.rewardId == null || (op.Value.rewardId.Length != 0 && op.Value.rewardId.Length != 24))
                    return false;
            foreach (var r in reservations)
                if (!Kind(r.Key) || r.Value == null || !Profile(r.Value.profile) || r.Value.token?.Length != 32 || r.Value.item?.Length != 24 || double.IsNaN(r.Value.start) || double.IsInfinity(r.Value.start))
                    return false;
            foreach (var award in awards)
                if (award.Key.Length != 24 || !Profile(award.Value))
                    return false;
            foreach (var location in awardLocations)
                if (!awards.ContainsKey(location.Key) || string.IsNullOrEmpty(location.Value) || location.Value.Length > 1024)
                    return false;
            return true;
        }

        public static bool Kind(string kind) => kind == "HACK" || kind == "UPLOAD" || kind == "SSD";
        public bool Record(string profile, int action)
        {
            if (!Profile(profile) || action <= 0 || action > 63)
                return false;
            if (!operators.TryGetValue(profile, out var value))
            {
                if (operators.Count >= 64)
                    return false;
                operators[profile] = value = new OfficeRaidOperator();
            }

            int old = value.actions;
            value.actions |= action;
            return old != value.actions;
        }

        public int Actions(string profile) => profile != null && operators.TryGetValue(profile, out var value) ? value.actions : 0;
        public bool CanPlant(string kind, string profile, bool hasItem, bool hackQuestActive) => Kind(kind) && hasItem && (kind == "SSD" ? !diskInstalled : kind == "UPLOAD" ? !codeUploaded : hackQuestActive && (Actions(profile) & Hack) == 0);
        public bool Reserve(string kind, string profile, string token, string item, double now)
        {
            if (!Kind(kind) || !Profile(profile) || token?.Length != 32 || item?.Length != 24 || double.IsNaN(now) || double.IsInfinity(now) || (!operators.ContainsKey(profile) && operators.Count >= 64) || (kind == "SSD" && diskInstalled) || (kind == "UPLOAD" && codeUploaded))
                return false;
            if (reservations.TryGetValue(kind, out var pending) && now - pending.start < 90)
                return pending.profile == profile && pending.token == token && pending.item == item;
            reservations[kind] = new OfficePlantReservation
            {
                profile = profile,
                token = token,
                item = item,
                start = now
            };
            return true;
        }

        public bool Commit(string kind, string profile, string token, double now)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || !reservations.TryGetValue(kind, out var value) || value.profile != profile || value.token != token || now - value.start < 9.5 || now - value.start > 90)
                return false;
            if (kind == "HACK")
                Record(profile, Hack);
            if (kind == "UPLOAD")
                codeUploaded = true;
            if (kind == "SSD")
            {
                if (diskInstalled)
                    return false;
                disk++;
                diskInstalled = true;
                downloader = "";
                downloaded = 0;
                downloading = -1;
                downloadAt = -1;
                Record(profile, Ssd);
            }

            reservations.Remove(kind);
            return true;
        }

        public bool Cancel(string kind, string profile, string token)
        {
            if (!reservations.TryGetValue(kind, out var value) || value.profile != profile || value.token != token)
                return false;
            return reservations.Remove(kind);
        }

        public bool CanDownload(string profile, int file, int expectedDisk) => Profile(profile) && (operators.ContainsKey(profile) || operators.Count < 64) && diskInstalled && disk == expectedDisk && file >= 0 && file < fileCount && downloading < 0 && (downloader == "" || downloader == profile) && (downloaded & (1 << file)) == 0;
        public bool BeginDownload(string profile, int file, int expectedDisk, double now)
        {
            if (!CanDownload(profile, file, expectedDisk) || double.IsNaN(now) || double.IsInfinity(now))
                return false;
            downloader = profile;
            downloading = file;
            downloadAt = now + 3d;
            return true;
        }

        public bool Tick(double now)
        {
            if (downloading < 0 || double.IsNaN(now) || double.IsInfinity(now) || now < downloadAt)
                return false;
            downloaded |= 1 << downloading;
            downloading = -1;
            downloadAt = -1;
            return true;
        }

        public bool CanEject(string profile) => diskInstalled && downloader == profile && downloading < 0 && downloaded == (1 << fileCount) - 1;
        public bool Eject(string profile, int expectedDisk, string reward, string location = "")
        {
            if (location == null || location.Length > 1024)
                return false;
            if (disk != expectedDisk || !CanEject(profile) || reward?.Length != 24 || awards.Count >= 64 || awards.ContainsKey(reward))
                return false;
            Record(profile, Download);
            operators[profile].rewardId = reward;
            awards[reward] = profile;
            if (location.Length > 0)
                awardLocations[reward] = location;
            diskInstalled = false;
            return true;
        }

        public bool CanFinishHackRaid(string profile, bool powerRestoredThisRaid, bool survived) => survived && powerRestoredThisRaid && (Actions(profile) & Hack) != 0;
        public bool CompleteRaid(string profile) => (Actions(profile) & (Code | Shutdown | Ssd | Download)) == (Code | Shutdown | Ssd | Download);
    }
}
