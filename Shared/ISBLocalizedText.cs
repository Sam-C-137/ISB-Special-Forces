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

        internal static string Format(string key, string fallback, params object[] arguments)
        {
            string value = Get(key, fallback);
            if (arguments == null || arguments.Length == 0)
                return value;
            try
            {
                return string.Format(value, arguments);
            }
            catch (FormatException)
            {
                return string.Format(fallback, arguments);
            }
        }
    }
}
