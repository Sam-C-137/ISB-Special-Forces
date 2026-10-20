using EFT;

namespace ISBSpecialForces.Behavior.ISB
{
    internal sealed class ISBEvacuationLayer : DeadManSwitchPanic
    {
        public ISBEvacuationLayer(BotOwner bot, int priority) : base(bot, priority) { }
        public override string GetName() => "ISB Evacuation";
    }
}
