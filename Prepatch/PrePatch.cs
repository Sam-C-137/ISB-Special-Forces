using BepInEx;
using MoreBotsAPI;
using ISBSpecialForces;
using System.Collections.Generic;

namespace ISBSpecialForces
{
    [BepInDependency("com.morebotsapiprepatch.tacticaltoaster", BepInDependency.DependencyFlags.HardDependency)]
    [BepInPlugin(ClientInfo.PreLoadGUID, ClientInfo.PreLoadName, ClientInfo.Version)]
    public class ISBSpecialForces : BaseUnityPlugin
    {
        public static ISBSpecialForces Instance;

        public void Awake()
        {
            Instance = this;
        }
    }
}
