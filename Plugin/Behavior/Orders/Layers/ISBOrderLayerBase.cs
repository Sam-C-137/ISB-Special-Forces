using DrakiaXYZ.BigBrain.Brains;
using EFT;
using ISBSpecialForces.Behavior.Orders.Components;
using ISBSpecialForces.Behavior.Orders.State;
using System;

namespace ISBSpecialForces.Behavior.Orders.Layers
{
    internal abstract class ISBOrderLayerBase : CustomLayer
    {
        protected ISBOrderLayerBase(BotOwner botOwner, int priority)
            : base(botOwner, priority)
        {
            Agent = botOwner.GetOrAddOrderAgent();
        }

        protected ISBOrderAgent Agent { get; }
        protected abstract ISBOrderDirective Directive { get; }
        protected abstract Type ActionType { get; }
        protected abstract string ActionReason { get; }
        protected virtual bool RequiresTacticalPoint => false;

        public override bool IsActive()
        {
            return Agent != null
                && Agent.CanRunDirective(Directive, RequiresTacticalPoint);
        }

        public override Action GetNextAction()
        {
            return new Action(ActionType, ActionReason);
        }

        public override bool IsCurrentActionEnding()
        {
            return Agent == null
                || Agent.CurrentDirective != Directive
                || Agent.ShouldEndMovementPulse(Directive)
                || CurrentAction == null
                || CurrentAction.Type != ActionType;
        }

        public override void Stop()
        {
            base.Stop();
        }
    }
}
