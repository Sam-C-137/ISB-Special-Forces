using System;

namespace ISB.Compatibility
{
    public static class ISBDogtagLevels
    {
        public static bool Range(string template, out int min, out int max)
        {
            switch (template)
            {
                case "6a8f0a4350e8e920dd389fd7":
                    min = 30;
                    max = 40;
                    return true;
                case "6a8f540c650b4dbe25badff8":
                    min = 55;
                    max = 60;
                    return true;
                case "6a8f1e073b022db2a04c1b76":
                    min = 45;
                    max = 55;
                    return true;
                case "6a8f0e0be1730357d098267b":
                    min = 61;
                    max = 67;
                    return true;
                case "6a8f1e1f60f1022c46406c5b":
                    min = 69;
                    max = 75;
                    return true;
                case "6a8f1e3beb9811b096e224c3":
                    min = 76;
                    max = 79;
                    return true;
                default:
                    min = max = 0;
                    return false;
            }
        }

        public static int Choose(string template, string identity, int? current)
        {
            if (!Range(template, out int min, out int max))
                return current ?? 1;
            if (current.HasValue && current >= min && current <= max)
                return current.Value;
            uint hash = 2166136261;
            unchecked
            {
                foreach (char c in identity ?? template)
                    hash = (hash ^ c) * 16777619;
            }

            return min + (int)(hash % (uint)(max - min + 1));
        }
    }
}
