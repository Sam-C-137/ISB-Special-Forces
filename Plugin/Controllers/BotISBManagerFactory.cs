using EFT;
using System.Collections.Generic;
using ISBSpecialForces.Components;

namespace ISBSpecialForces.Controllers
{
    public static class BotISBManagerFactory
    {
        private static Dictionary<BotOwner, BotISBManager> managers = new Dictionary<BotOwner, BotISBManager>();
        public static BotISBManager GetManager(this BotOwner botOwner)
        {
            if (botOwner == null)
                return null;
            if (managers.TryGetValue(botOwner, out var manager))
            {
                if (manager != null)
                    return manager;
                managers.Remove(botOwner);
            }

            return null;
        }

        public static BotISBManager GetOrAddISBManager(this BotOwner botOwner)
        {
            if (!ISBFikaAuthority.IsAiAuthority || botOwner?.GetPlayer == null)
                return null;
            var manager = GetManager(botOwner);
            if (manager != null)
                return manager;
            manager = botOwner.GetPlayer.gameObject.GetOrAddComponent<BotISBManager>();
            managers[botOwner] = manager;
            manager.Init(botOwner);
            return manager;
        }

        public static void RemoveManager(BotOwner botOwner, BotISBManager manager)
        {
            if (ReferenceEquals(botOwner, null))
                return;
            if (managers.TryGetValue(botOwner, out var current) && ReferenceEquals(current, manager))
                managers.Remove(botOwner);
        }

        public static void ClearManagers()
        {
            managers.Clear();
        }
    }
}
