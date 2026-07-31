using DrakiaXYZ.BigBrain.Brains;
using EFT;
using ISBSpecialForces.Behavior.Orders.Components;
using ISBSpecialForces.Behavior.Orders.State;
using ISBSpecialForces.Behavior.Orders.Utilities;
using System.Text;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Orders.Actions
{
    public abstract class ISBOrderMoveAction : CustomLogic
    {
        private readonly GClass395 _baseSteeringLogic;
        private readonly GClass212 _goToCoverPoint;
        private GClass31 _goToData;
        private float _startedAt;
        private bool _running;

        protected ISBOrderMoveAction(BotOwner botOwner) : base(botOwner)
        {
            Agent = botOwner.GetOrAddOrderAgent();
            _goToCoverPoint = new GClass212(BotOwner);
            _baseSteeringLogic = new GClass395();
        }

        protected ISBOrderAgent Agent { get; }
        protected abstract ISBOrderDirective Directive { get; }
        protected virtual bool AllowSprint => false;
        protected virtual float ArrivalDistance => 1.5f;
        protected virtual float MovementPulseDuration => 2.25f;

        public override void Start()
        {
            if (_running)
                return;

            _running = true;
            _startedAt = Time.time;
            Agent?.BeginMovementPulse(Directive, MovementPulseDuration);
            SetDataPoint(Agent?.GetTacticalPoint(Directive));

            if (ISBOrderSettings.Logs)
            {
                var pulseKind = Directive == ISBOrderDirective.Pursuit
                    ? "recurring chase pulse"
                    : "one-shot pulse";
            }

            base.Start();
        }

        public override void Stop()
        {
            if (!_running)
                return;

            _running = false;
            BotOwner.Mover?.Sprint(false);
            Agent?.EndMovementPulse(Directive);

            if (ISBOrderSettings.Logs)
            {
                var resumeText = Directive == ISBOrderDirective.Pursuit
                    ? $"native combat window for {ISBOrderSettings.Current.pursuitCombatWindow:F2}s"
                    : "native brain resumed for this order";
            }

            base.Stop();
        }

        public override void Update(CustomLayer.ActionData data)
        {
            if (!_running)
                return;

            if (Directive == ISBOrderDirective.Pursuit && Agent != null && Agent.ShouldStopPursuit())
            {
                Agent.ExpireMovementPulse(Directive);
                return;
            }

            var point = Agent?.GetTacticalPoint(Directive);

            if (point == null)
            {
                Agent?.ExpireMovementPulse(Directive);
                return;
            }

            SetDataPoint(point);
            _goToCoverPoint.UpdateNodeByMain(_goToData);

            BotOwner.SetPose(1f);
            BotOwner.BotLay.GetUp(true);

            var distance = ISBOrderCombatUtility.HorizontalDistance(
                BotOwner.Position,
                point.Position);
            var sprint = AllowSprint && distance > 10f;

            BotOwner.Mover.Sprint(sprint);
            BotOwner.SetTargetMoveSpeed(sprint ? 1f : 0.85f);

            BotOwner.Steering.LookToMovingDirection();
            _baseSteeringLogic.Update(BotOwner);

            if (Directive == ISBOrderDirective.Pursuit && Agent != null)
                Agent.LookTowardEnemy();

            if (BotOwner.Mover.IsComeTo(ArrivalDistance, true, point))
            {
                Agent.InvalidatePoint();
                Agent.ExpireMovementPulse(Directive);
            }
        }

        public override void BuildDebugText(StringBuilder stringBuilder)
        {
            var point = Agent?.GetTacticalPoint(Directive);
            stringBuilder.AppendLine(
                $"-- ISB Order {Directive} -- " +
                $"{(point == null ? "no tactical point" : point.Position.ToString())}");
            base.BuildDebugText(stringBuilder);
        }

        private void SetDataPoint(CustomNavigationPoint point)
        {
            if (point == null)
                return;

            if (_goToData == null)
                _goToData = new GClass31(point);

            _goToData.Point = point;
        }
    }
}
