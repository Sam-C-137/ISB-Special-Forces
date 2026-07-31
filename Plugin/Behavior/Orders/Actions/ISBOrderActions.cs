using EFT;
using ISBSpecialForces.Behavior.Orders.State;

namespace ISBSpecialForces.Behavior.Orders.Actions
{

    public sealed class ISBOrderAssaultAction : ISBOrderMoveAction
    {
        public ISBOrderAssaultAction(BotOwner botOwner) : base(botOwner) { }
        protected override ISBOrderDirective Directive => ISBOrderDirective.Assault;
        protected override bool AllowSprint => true;
        protected override float ArrivalDistance => 2f;
        protected override float MovementPulseDuration => 2.5f;
    }

    public sealed class ISBOrderFlankAction : ISBOrderMoveAction
    {
        public ISBOrderFlankAction(BotOwner botOwner) : base(botOwner) { }
        protected override ISBOrderDirective Directive => ISBOrderDirective.Flank;
        protected override bool AllowSprint => true;
        protected override float ArrivalDistance => 2f;
        protected override float MovementPulseDuration => 3.75f;
    }

    public sealed class ISBOrderPursuitAction : ISBOrderMoveAction
    {
        public ISBOrderPursuitAction(BotOwner botOwner) : base(botOwner) { }
        protected override ISBOrderDirective Directive => ISBOrderDirective.Pursuit;
        protected override bool AllowSprint => true;
        protected override float ArrivalDistance => 3f;
        protected override float MovementPulseDuration => ISBOrderSettings.Current.pursuitPulseDuration;
    }
}
