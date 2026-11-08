using EFT;
using ISBSpecialForces.Behavior.Tactics;
using ISBSpecialForces.Components;
using MoreBotsAPI.Behavior.Layers;

namespace ISBSpecialForces.Behavior.Layers
{
    internal sealed class ExternalChaosHuntLayer : HuntTargetLayer
    {
        private readonly ISBTacticalAgent _agent;
        public ExternalChaosHuntLayer(BotOwner bot, int priority) : base(bot, priority) => _agent = ISBTacticalAgent.GetOrAdd(bot, false);
        public override string GetName() => "Chaos Insurgency Hunt";
        internal static bool CanRun(int role, string spawnId, bool missionReady, bool checkpoint) => role == global::ISB.Compatibility.GhostBotVariant.ChaosValue
            && spawnId?.StartsWith("isb_external_chaos_hunt_", System.StringComparison.Ordinal) == true && missionReady && !checkpoint;
        public override bool IsActive()
        {
            if (_agent == null || BotOwner?.Profile?.Info?.Settings == null ||
                !CanRun((int)BotOwner.Profile.Info.Settings.Role, BotOwner.SpawnProfileData?.SpawnParams?.Id_spawn, true, false))
                return false;
            return !_agent.HasCheckpoint && _agent.CanRunMission() && base.IsActive();
        }
    }

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
            if (_agent == null || ISBEventPopulationQueue.TryGetHuntSquad(BotOwner, out _, out _) ||
                ISBRevengeHuntController.AppliesTo(BotOwner) || _agent.HasCheckpoint || !_agent.CanRunMission())
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
