using System.Collections.Generic;

namespace ISB.MixedAmmo
{
    public class CaliberIntDefinition
    {
        public int chance { get; set; } = 100;
        public string mode { get; set; } = "blocks";
        public int interleaveBlockSize { get; set; } = 4;
        public int maxStacksPerMagazine { get; set; } = 12;
        public List<MixEntry> entries { get; set; } = new();
    }

    public class MixEntry
    {
        public string tpl { get; set; } = string.Empty;
        public int ratio { get; set; } = 1;
        public string position { get; set; } = "auto";
    }
}
