using EFT;
using ISBSpecialForces.Behavior.Tactics;
using ISBSpecialForces.Components;
using MoreBotsAPI.Behavior.Layers;

namespace ISBSpecialForces.Behavior.Layers
{
    internal sealed class RevengeHunt : HuntTargetLayer
    {
        private readonly ISBTacticalAgent _agent;
        public RevengeHunt(BotOwner bot, int priority) : base(bot, priority)
        {
            _agent = ISBTacticalAgent.GetOrAdd(bot);
        }

        public override string GetName() => "RevengeHunt";
        public override bool IsActive()
        {
            if (_agent == null || !ISBRevengeHuntController.AppliesTo(BotOwner) || !_agent.CanRunMission() || huntManager?.huntTarget?.HealthController?.IsAlive != true)
                return false;
            ISBRevengeHuntController.TryRetarget(huntManager);
            getNextAction();
            return true;
        }
    }
}
