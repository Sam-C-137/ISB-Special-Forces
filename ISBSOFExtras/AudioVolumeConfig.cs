using BepInEx.Configuration;

namespace ISBSSOFExtra
{
    internal static class AudioVolumeConfig
    {
        internal static ConfigEntry<float> Bind(ConfigFile config, string previousSection, string key, float defaultValue, ConfigDescription description)
        {
            bool save = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                var previous = config.Bind(previousSection, key, defaultValue, description);
                var current = config.Bind("Audio", key, previous.Value, description);
                config.Remove(previous.Definition);
                return current;
            }
            finally
            {
                config.SaveOnConfigSet = save;
                if (save)
                    config.Save();
            }
        }
    }
}
