using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ISB.Compatibility
{
    public sealed class OfficeKteBriefing
    {
        public const int Count = 5;
        private static readonly int[] Numbers =
        {
            44,
            70,
            76,
            94,
            101,
            111,
            135,
            166,
            199,
            218,
            229,
            292,
            294,
            325,
            430,
            435,
            501,
            540,
            572,
            598,
            611,
            636,
            642,
            652,
            663,
            664,
            665,
            711,
            722,
            728,
            787,
            863,
            876,
            887,
            949,
            952,
            991
        };
        private readonly object _sync = new object ();
        private readonly Dictionary<string, int[]> _active = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> _previous = new Dictionary<string, int[]>(StringComparer.Ordinal);
        private readonly Queue<string> _history = new Queue<string>();
        public static int[] Catalog() => (int[])Numbers.Clone();
        public static bool Valid(int[] selection)
        {
            if (selection == null || selection.Length != Count)
                return false;
            for (int i = 0; i < Count; i++)
            {
                if (Array.IndexOf(Numbers, selection[i]) < 0)
                    return false;
                for (int j = 0; j < i; j++)
                    if (selection[j] == selection[i])
                        return false;
            }

            return true;
        }

        public static bool Same(int[] left, int[] right)
        {
            if (left == null || right == null || left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;
            return true;
        }

        public int[] GetOrCreate(string session, Func<int, int, int> next)
        {
            if (string.IsNullOrWhiteSpace(session))
                throw new ArgumentException(nameof(session));
            if (next == null)
                throw new ArgumentNullException(nameof(next));
            lock (_sync)
            {
                int[] selected;
                if (_active.TryGetValue(session, out selected))
                    return (int[])selected.Clone();
                int[] previous;
                _previous.TryGetValue(session, out previous);
                var pool = new List<int>(Numbers.Length);
                foreach (int number in Numbers)
                    if (previous == null || Array.IndexOf(previous, number) < 0)
                        pool.Add(number);
                selected = new int[Count];
                for (int i = 0; i < Count; i++)
                {
                    int index = next(i, pool.Count - 1);
                    if (index < i || index >= pool.Count)
                        throw new InvalidOperationException("Invalid KTE selection index.");
                    int number = pool[index];
                    pool[index] = pool[i];
                    pool[i] = number;
                    selected[i] = number;
                }

                _active[session] = selected;
                return (int[])selected.Clone();
            }
        }

        public void EndRaid(string session)
        {
            if (string.IsNullOrWhiteSpace(session))
                return;
            lock (_sync)
            {
                int[] selected;
                if (!_active.TryGetValue(session, out selected))
                    return;
                _active.Remove(session);
                if (!_previous.ContainsKey(session))
                    _history.Enqueue(session);
                _previous[session] = selected;
                while (_history.Count > 128)
                    _previous.Remove(_history.Dequeue());
            }
        }
    }

    public sealed class OfficeKteEntry
    {
        public int number { get; set; }
        public string kind { get; set; } = "Object";
        public string classification { get; set; } = "Unclassified";
        public string brief { get; set; } = "";
        public string briefKey { get; set; } = "";
    }

    public sealed class OfficeKteDocument
    {
        public string header { get; set; } = "";
        public string introduction { get; set; } = "";
        public string footer { get; set; } = "";
        public OfficeKteEntry[] entries { get; set; } = new OfficeKteEntry[0];

        public string Render(int[] selection, Func<string, string, string> localize)
        {
            if (!OfficeKteBriefing.Valid(selection) || localize == null || entries == null)
                return null;
            int[] catalog = OfficeKteBriefing.Catalog();
            if (entries.Length != catalog.Length)
                return null;
            var ordered = (OfficeKteEntry[])entries.Clone();
            if (Array.Exists(ordered, entry => entry == null))
                return null;
            Array.Sort(ordered, (left, right) => left.number.CompareTo(right.number));
            for (int i = 0; i < ordered.Length; i++)
                if (ordered[i].number != catalog[i] || string.IsNullOrWhiteSpace(ordered[i].brief))
                    return null;
            var text = new StringBuilder(4096);
            text.Append(localize("ISB/Office/KteHeader", header)).Append("\n\n");
            text.Append(localize("ISB/Office/KteIntroduction", introduction)).Append("\n\n");
            foreach (var entry in ordered)
            {
                bool known = Array.IndexOf(selection, entry.number) >= 0;
                text.Append("KTE ").Append(entry.number.ToString("D3", CultureInfo.InvariantCulture));
                if (known)
                    text.Append(" (").Append(localize("ISB/Office/Kte" + entry.classification, entry.classification)).Append(')');
                text.Append(" - ").Append(localize("ISB/Office/Kte" + entry.kind, entry.kind)).Append(" - ");
                if (known)
                    text.Append(localize("ISB/Office/KteProperties", "Recorded Properties:")).Append(' ').Append(localize(entry.briefKey, entry.brief));
                else
                    text.Append(localize("ISB/Office/KteUnknown", "UNKNOWN."));
                text.Append("\n\n");
            }

            return text.Append(localize("ISB/Office/KteFooter", footer)).ToString();
        }
    }
}
