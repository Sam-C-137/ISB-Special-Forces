using DrakiaXYZ.BigBrain.Brains;
using EFT;
using System.Text;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;

namespace ISBSpecialForces.Behavior.Actions
{
    public class GoToCheckpoint : GoToCustomAction
    {
        protected BotISBManager isbManager { get; private set; }

        public GoToCheckpoint(BotOwner botOwner) : base(botOwner)
        {
            isbManager = botOwner.GetOrAddISBManager();
        }

        public override void Start()
        {
            isbManager.UpdateGuardPoint();
            if (isbManager.guardPoint == null)
                return;
            BotOwner.Memory.SetCoverPoints(isbManager.guardPoint, "");
            BotOwner.GetPlayer.MovementContext.SetPatrol(true);
            base.Start();
        }

        public override void Stop()
        {
            BotOwner.GetPlayer.MovementContext.SetPatrol(false);
            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            isbManager.UpdateGuardPoint();
            if (isbManager.guardPoint == null)
                return;
            BotOwner.Memory.SetCoverPoints(isbManager.guardPoint, "");
            base.Update(data);
            if (BotOwner.Mover.IsComeTo(.7f, true, isbManager.guardPoint))
            {
                isbManager.AtCheckpoint = true;
            }
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine($"-- GoToCheckpoint -- {isbManager.guardPoint?.Position}");
            base.BuildDebugText(stringBuilder);
        }

        public override CustomNavigationPoint GetGoToPoint()
        {
            return isbManager.guardPoint;
        }
    }
}
