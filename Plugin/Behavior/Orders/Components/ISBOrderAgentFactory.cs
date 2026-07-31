using EFT;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Orders.Components
{
    public static class ISBOrderAgentFactory
    {
        public static ISBOrderAgent GetOrderAgent(this BotOwner botOwner)
        {
            if (botOwner?.GetPlayer == null)
                return null;

            return botOwner.GetPlayer.gameObject.GetComponent<ISBOrderAgent>();
        }

        public static ISBOrderAgent GetOrAddOrderAgent(this BotOwner botOwner)
        {
            if (botOwner?.GetPlayer == null)
                return null;

            var agent = botOwner.GetPlayer.gameObject.GetComponent<ISBOrderAgent>();

            if (agent == null)
                agent = botOwner.GetPlayer.gameObject.AddComponent<ISBOrderAgent>();

            agent.Init(botOwner);
            return agent;
        }
    }
}
