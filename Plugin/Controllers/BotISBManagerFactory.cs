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
            if (managers.TryGetValue(botOwner, out var manager))
            {
                return manager;
            }
            return null;
        }

        public static BotISBManager GetOrAddISBManager(this BotOwner botOwner)
        {
            var manager = GetManager(botOwner);

            if (manager != null)
            {
                return manager;
            }

            manager = botOwner.GetPlayer.gameObject.GetOrAddComponent<BotISBManager>();
            managers[botOwner] = manager;
            manager.Init(botOwner);

            return manager;
        }

        public static void ClearManagers()
        {
            managers.Clear();
        }
    }
}
