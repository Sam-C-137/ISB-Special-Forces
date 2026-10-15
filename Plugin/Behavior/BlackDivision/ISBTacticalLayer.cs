using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System.Text;

namespace ISBSpecialForces.Behavior.BlackDivision
{
    internal sealed class ISBTacticalLayer : CustomLayer
    {
        private readonly ISBTacticalAgent _agent;
        public ISBTacticalLayer(BotOwner bot, int priority) : base(bot, priority)
        {
            _agent = ISBTacticalAgent.GetOrAdd(bot);
        }

        public override bool IsActive() => _agent?.CanRun() == true;
        public override Action GetNextAction() => new Action(typeof(ISBTacticalMove), "ISB " + _agent.Squad.Policy.Mode);
        public override bool IsCurrentActionEnding() => _agent?.CanRun() != true;
        public override string GetName() => "ISB Tactics";
    }

    internal sealed class ISBTacticalMove : CustomLogic
    {
        private readonly ISBTacticalAgent _agent;
        public ISBTacticalMove(BotOwner bot) : base(bot)
        {
            _agent = ISBTacticalAgent.GetOrAdd(bot);
        }

        public override void Start()
        {
            base.Start();
            _agent?.BeginMove();
        }

        public override void Stop()
        {
            _agent?.CancelMove();
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            _agent?.TickMove();
        }

        public override void BuildDebugText(StringBuilder text)
        {
            if (_agent == null)
            {
                base.BuildDebugText(text);
                return;
            }

            text.AppendLine($"ISB tactics: {_agent.Squad?.Policy.Mode}; contact age {_agent.ContactAge:F1}s; team {_agent.Team}");
            text.AppendLine($"Destination: {(_agent.HasDestination ? _agent.Destination.ToString() : "SAIN control")}");
            base.BuildDebugText(text);
        }
    }
}
