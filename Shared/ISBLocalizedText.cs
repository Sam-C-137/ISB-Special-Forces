using System;

namespace ISB.Compatibility
{
    internal static class ISBLocalizedText
    {
        internal static string Get(string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(key))
                return fallback ?? string.Empty;
            try
            {
                string value = EFT.LocalizationExtensions.Localized(key, null);
                if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, key, StringComparison.Ordinal))
                    return value;
            }
            catch (Exception)
            {
            }

            return fallback ?? key;
        }
    }
}
