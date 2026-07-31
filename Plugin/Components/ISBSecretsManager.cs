using Comfort.Common;
using EFT;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public static class ISBSecretsManager
    {
        private static readonly HashSet<string> fired = new HashSet<string>();

        public static void ResetRaid()
        {
            fired.Clear();
        }

        public static bool Fire(string triggerId)
        {
            if (fired.Contains(triggerId))
                return true;

            var eventHandler = Singleton<BotEventHandler>.Instance;

            if (eventHandler == null)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB SECRET] BotEventHandler is unavailable. Trigger '{triggerId}' was not fired and may retry.");
                return false;
            }

            try
            {
                eventHandler.AnyEvent(triggerId);
                fired.Add(triggerId);
                Plugin.LogSource.LogInfo($"[ISB SECRET] Trigger '{triggerId}' fired.");
                return true;
            }
            catch (System.Exception ex)
            {
                Plugin.LogSource.LogWarning(
                    $"[ISB SECRET] Trigger '{triggerId}' failed: {ex.Message}");
                return false;
            }
        }

        public static bool AlreadyFired(string triggerId)
        {
            return fired.Contains(triggerId);
        }
    }
}
