using EFT;
using ISBSpecialForces.Behavior.Tactics;
using ISBSpecialForces.Components;
using MoreBotsAPI.Behavior.Layers;

namespace ISBSpecialForces.Behavior.Layers
{
    internal sealed class ISBHuntLayer : HuntTargetLayer
    {
        private readonly ISBTacticalAgent _agent;
        public ISBHuntLayer(BotOwner bot, int priority) : base(bot, priority)
        {
            _agent = ISBTacticalAgent.GetOrAdd(bot);
        }

        public override string GetName() => BotOwner.Brain?.BaseBrain is ISB.BlackDivBrain ? "blackDiv Hunt" : ISBCameraHuntController.AppliesTo(BotOwner) ? "ISB Camera Hunt" : "ISB Hunt";
        public override bool IsActive()
        {
            if (_agent == null || ISBRevengeHuntController.AppliesTo(BotOwner) || _agent.HasCheckpoint || !_agent.CanRunMission())
                return false;
            if (!ISBCameraHuntController.AppliesTo(BotOwner))
                return base.IsActive();
            if (huntManager == null || huntManager.huntTarget?.HealthController?.IsAlive != true)
                return false;
            ISBCameraHuntController.PrepareRegroup(huntManager);
            getNextAction();
            return true;
        }
    }
}
