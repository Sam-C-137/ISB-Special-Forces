using System;
using System.Security.Cryptography;
using System.Text;

namespace ISB.Compatibility
{
    public static class VipperPoisonIdentity
    {
        public const string Prefix = "13703ffe";
        public const string Marker = "ISBVipperPoison:";
        public static string TemplateId(string original)
        {
            using (var hash = SHA256.Create())
                return Prefix + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Marker + original))).Replace("-", "").ToLowerInvariant().Substring(0, 16);
        }
        public static bool IsPoisoned(string id)
        {
            if (id == null || id.Length != 24 || !id.StartsWith(Prefix, StringComparison.Ordinal))
                return false;
            foreach (var character in id)
                if (!Uri.IsHexDigit(character))
                    return false;
            return true;
        }
    }
}
