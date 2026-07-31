using EFT;
using SPT.SinglePlayer.Utils.InRaid;
using ISBSpecialForces.Models;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public class BotISBManager : MonoBehaviour
    {
        public BotOwner botOwner;
        public bool AtCheckpoint = false;
        public bool ShouldSwitchCover = false;
        public bool guardPointDirty = true;
        public CustomNavigationPoint guardPoint;

        private ISBCheckpoint assignedCheckpoint;

        public void Init(BotOwner botOwner)
        {
            this.botOwner = botOwner;

            if (!WildSpawnTypeExtensions.IsISB(botOwner.Profile.Info.Settings.Role))
                return;

            if (RaidTimeUtil.GetSecondsSinceSpawning() <= 120)
            {
                Plugin.LogSource.LogInfo($"[ISB] [{botOwner.Profile.Nickname}] Attempting to find and assign checkpoint on spawn.");
                MonoBehaviourSingleton<ISBCheckpointManager>.Instance.FindAndAssignCheckpoint(botOwner);
                Plugin.LogSource.LogInfo($"[ISB] [{botOwner.Profile.Nickname}] Assigned checkpoint: {(HasAssignedCheckpoint() ? GetAssignedCheckpoint().Zone.NameZone : "None")}");

                if (HasAssignedCheckpoint())
                {
                    botOwner.Mover.Teleport(assignedCheckpoint.Position);
                }
            }
        }

        public CustomNavigationPoint GetCheckpointCoverPoint()
        {
            var checkpoint = GetAssignedCheckpoint();
            var patrolPoint = checkpoint.Position;
            var randomDisc = UnityEngine.Random.insideUnitCircle * checkpoint.Radius;
            patrolPoint = patrolPoint + new Vector3(randomDisc.x, 0, randomDisc.y);

            var searchData = new CoverSearchData(patrolPoint, botOwner.CoverSearchInfo, CoverShootType.hide, (checkpoint.Radius * .5f) * (checkpoint.Radius * .5f), 0f, CoverSearchType.distToToCenter, null, null, null, ECheckSHootHide.shootAndHide, new CoverSearchDefenceDataClass(botOwner.Settings.FileSettings.Cover.MIN_DEFENCE_LEVEL), PointsArrayType.byShootType, true);
            return botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
        }

        public void UpdateGuardPoint()
        {
            if (guardPointDirty)
            {
                guardPoint = GetCheckpointCoverPoint();
                guardPointDirty = false;
            }
        }

        public void SetGuardPoint(CustomNavigationPoint point)
        {
            guardPoint = point;
        }

        public bool CanDoCheckpointActions()
        {
            return HasAssignedCheckpoint() && RaidTimeUtil.GetSecondsSinceSpawning() > 0;
        }

        public bool HasAssignedCheckpoint()
        {
            return assignedCheckpoint != null;
        }

        public ISBCheckpoint GetAssignedCheckpoint()
        {
            return assignedCheckpoint;
        }

        public void SetAssignedCheckpoint(ISBCheckpoint checkpoint)
        {
            assignedCheckpoint = checkpoint;
        }
    }
}
