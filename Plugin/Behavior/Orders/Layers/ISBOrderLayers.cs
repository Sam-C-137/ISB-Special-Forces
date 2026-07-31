using EFT;
using ISBSpecialForces.Behavior.Orders.Actions;
using ISBSpecialForces.Behavior.Orders.State;
using System;

namespace ISBSpecialForces.Behavior.Orders.Layers
{

    internal sealed class ISBOrderFlankLayer : ISBOrderLayerBase
    {
        public ISBOrderFlankLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        protected override ISBOrderDirective Directive => ISBOrderDirective.Flank;
        protected override Type ActionType => typeof(ISBOrderFlankAction);
        protected override string ActionReason => "ISBOrderFlank";
        protected override bool RequiresTacticalPoint => true;
        public override string GetName() => "ISBOrderFlankLayer";
    }

    internal sealed class ISBOrderAssaultLayer : ISBOrderLayerBase
    {
        public ISBOrderAssaultLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        protected override ISBOrderDirective Directive => ISBOrderDirective.Assault;
        protected override Type ActionType => typeof(ISBOrderAssaultAction);
        protected override string ActionReason => "ISBOrderAssault";
        protected override bool RequiresTacticalPoint => true;
        public override string GetName() => "ISBOrderAssaultLayer";
    }

    internal sealed class ISBOrderPursuitLayer : ISBOrderLayerBase
    {
        public ISBOrderPursuitLayer(BotOwner botOwner, int priority) : base(botOwner, priority) { }
        protected override ISBOrderDirective Directive => ISBOrderDirective.Pursuit;
        protected override Type ActionType => typeof(ISBOrderPursuitAction);
        protected override string ActionReason => "ISBOrderPursuit";
        protected override bool RequiresTacticalPoint => true;
        public override string GetName() => "ISBOrderPursuitLayer";
    }
}
