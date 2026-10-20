using DrakiaXYZ.BigBrain.Brains;
using EFT;
using UnityEngine;
using System.Text;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;

namespace ISBSpecialForces.Behavior.Actions
{
    public class GoToCheckpoint : GoToCustomAction
    {
        protected BotISBManager isbManager { get; private set; }
        private bool _fallbackStarted;
        private float _nextFallbackPath;

        public GoToCheckpoint(BotOwner botOwner) : base(botOwner)
        {
            isbManager = botOwner.GetOrAddISBManager();
        }

        public override void Start()
        {
            _fallbackStarted = false;
            isbManager.UpdateGuardPoint();
            if (isbManager.guardPoint != null) BotOwner.Memory.SetCoverPoints(isbManager.guardPoint, "");
            BotOwner.AimingManager.CurrentAiming.LoseTarget();
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
            {
                var checkpoint = isbManager.GetAssignedCheckpoint();
                if (checkpoint == null) return;
                if ((BotOwner.Position - checkpoint.Position).sqrMagnitude <= .49f)
                {
                    BotOwner.StopMove();
                    isbManager.AtCheckpoint = true;
                    return;
                }
                if (!_fallbackStarted || !BotOwner.Mover.HasPathAndNoComplete && Time.time >= _nextFallbackPath)
                {
                    _fallbackStarted = true;
                    _nextFallbackPath = Time.time + 1f;
                    BotOwner.GoToPoint(checkpoint.Position, mustHaveWay: true);
                }
                UpdateBotMovement();
                UpdateSteering();
                return;
            }
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
