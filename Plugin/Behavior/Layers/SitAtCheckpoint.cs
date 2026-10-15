using DrakiaXYZ.BigBrain.Brains;
using EFT;
using ISBSpecialForces.Components;
using ISBSpecialForces.Controllers;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Actions
{
    internal class SitAtCheckpoint : CustomLogic
    {
        protected BotISBManager isbManager { get; private set; }

        private GClass278 holdPosition;
        private GClass395 baseSteeringLogic;
        private float sitStart = -1f;
        private float sitDuration = 0f;
        public SitAtCheckpoint(BotOwner botOwner) : base(botOwner)
        {
            isbManager = botOwner.GetOrAddISBManager();
            holdPosition = new GClass278(BotOwner);
            baseSteeringLogic = new GClass395();
        }

        public override void Start()
        {
            sitStart = Time.time;
            sitDuration = UnityEngine.Random.Range(10f, 30f);
            if (GClass856.IsTrue100(50))
                BotOwner.SetPose(1);
            else
                BotOwner.SetPose(0);
            BotOwner.GetPlayer.MovementContext.SetPatrol(true);
        }

        public override void Stop()
        {
            sitStart = -1f;
            BotOwner.GetPlayer.MovementContext.SetPatrol(false);
        }

        public override void Update(CustomLayer.ActionData data)
        {
            isbManager.UpdateGuardPoint();
            if (isbManager.guardPoint == null)
                return;
            holdPosition.UpdateNodeByMain(data);
            baseSteeringLogic.Update(BotOwner);
            if (Time.time > sitStart + sitDuration)
            {
                isbManager.ShouldSwitchCover = true;
                isbManager.guardPointDirty = true;
                sitDuration = UnityEngine.Random.Range(10f, 30f);
                sitStart = Time.time;
            }
        }
    }
}
