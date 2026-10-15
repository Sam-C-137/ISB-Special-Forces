using DrakiaXYZ.BigBrain.Brains;
using EFT;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;
using System.Text;

namespace ISBSpecialForces.Behavior.Actions
{
    internal class SwitchCheckpointCover : GoToCustomAction
    {
        protected BotISBManager isbManager { get; private set; }

        public SwitchCheckpointCover(BotOwner botOwner) : base(botOwner)
        {
            isbManager = botOwner.GetOrAddISBManager();
        }

        public override void Start()
        {
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
            if (BotOwner.Mover.IsComeTo(.5f, true, isbManager.guardPoint))
            {
                isbManager.ShouldSwitchCover = false;
            }
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            stringBuilder.AppendLine($"-- SwitchCheckpointCover -- {isbManager.guardPoint?.Position}");
            base.BuildDebugText(stringBuilder);
        }

        public override CustomNavigationPoint GetGoToPoint()
        {
            return isbManager.guardPoint;
        }
    }
}
