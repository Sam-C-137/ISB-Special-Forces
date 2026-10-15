using DrakiaXYZ.BigBrain.Brains;
using EFT;
using ISBSpecialForces.Behavior.BlackDivision;
using System;
using ISBSpecialForces.Behavior.Actions;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;

namespace ISBSpecialForces.Behavior.Layers
{
    internal class GoToCheckpointLayer : CustomLayer
    {
        protected BotISBManager isbManager { get; private set; }

        private ISBTacticalAgent _agent;
        public Type lastAction;
        public Type nextAction;
        public string nextActionReason;
        public GoToCheckpointLayer(BotOwner botOwner, int priority) : base(botOwner, priority)
        {
            isbManager = botOwner.GetOrAddISBManager();
            if (WildSpawnTypeExtensions.IsISB(botOwner.Profile.Info.Settings.Role))
                _agent = ISBTacticalAgent.GetOrAdd(botOwner);
        }

        public override void Start()
        {
            BotOwner.PatrollingData?.Unpause();
            base.Start();
        }

        public override void Stop()
        {
            BotOwner.PatrollingData?.Unpause();
            if (isbManager == null)
            {
                base.Stop();
                return;
            }

            isbManager.guardPointDirty = true;
            isbManager.AtCheckpoint = false;
            isbManager.ShouldSwitchCover = false;
            base.Stop();
        }

        public override string GetName()
        {
            return "ISBSoldierHoldCheckpoint";
        }

        public override Action GetNextAction()
        {
            lastAction = nextAction;
            return new Action(lastAction, nextActionReason);
        }

        public override bool IsActive()
        {
            if (!ISBFikaAuthority.IsAiAuthority || isbManager == null || ISBRevengeHuntController.AppliesTo(BotOwner) || !isbManager.CanDoCheckpointActions())
                return false;
            if (_agent == null)
                _agent = ISBTacticalAgent.GetOrAdd(BotOwner, false);
            if (_agent?.CanRunMission() != true)
                return false;
            isbManager.UpdateGuardPoint();
            if (isbManager.guardPoint == null)
                return false;
            GetNextActionInternal();
            return true;
        }

        public void GetNextActionInternal()
        {
            lastAction = nextAction;
            if (isbManager.AtCheckpoint)
            {
                if (isbManager.ShouldSwitchCover)
                {
                    nextAction = typeof(SwitchCheckpointCover);
                    nextActionReason = "ISBSoldierSwitchSpot";
                    return;
                }

                nextAction = typeof(SitAtCheckpoint);
                nextActionReason = "ISBSoldierAtCheckpoint";
                return;
            }

            nextAction = typeof(GoToCheckpoint);
            nextActionReason = "ISBSoldierToCheckpoint";
        }

        public override bool IsCurrentActionEnding()
        {
            return nextAction != lastAction || CurrentAction == null || (CurrentAction.Type != nextAction && CurrentAction.Type != lastAction);
        }
    }
}
