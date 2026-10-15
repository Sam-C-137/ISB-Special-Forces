using EFT;
using System;
using ISBSpecialForces.Behavior.BlackDivision;
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
        private float _coverRetryAt;
        public void Init(BotOwner botOwner)
        {
            if (!ISBFikaAuthority.IsAiAuthority || botOwner == null)
                return;
            this.botOwner = botOwner;
            var checkpointManager = MonoBehaviourSingleton<ISBCheckpointManager>.Instance;
            if (checkpointManager == null)
                return;
            if (!WildSpawnTypeExtensions.IsISB(botOwner.Profile.Info.Settings.Role) && !checkpointManager.IsBlackDivisionCheckpointBot(botOwner))
                return;
            if (RaidTimeUtil.GetSecondsSinceSpawning() <= 120 || checkpointManager.CanAssignRuntimeCheckpoint(botOwner))
            {
                checkpointManager.FindAndAssignCheckpoint(botOwner);
                if (global::ISBSpecialForces.Plugin.DiagnosticsEnabled)
                {
                    Plugin.LogSource.LogInfo($"[ISB] [{botOwner.Profile.Nickname}] Assigned checkpoint: {(HasAssignedCheckpoint() ? GetAssignedCheckpoint().Zone.NameZone : "None")}");
                }

                if (HasAssignedCheckpoint())
                {
                    botOwner.Mover.Teleport(assignedCheckpoint.Position);
                }
            }
        }

        public CustomNavigationPoint GetCheckpointCoverPoint()
        {
            var checkpoint = GetAssignedCheckpoint();
            if (checkpoint == null || botOwner?.BotsGroup?.CoverPointMaster == null || botOwner.CoverSearchInfo == null || botOwner.Settings?.FileSettings?.Cover == null)
                return null;
            var patrolPoint = checkpoint.Position;
            var randomDisc = UnityEngine.Random.insideUnitCircle * checkpoint.Radius;
            patrolPoint = patrolPoint + new Vector3(randomDisc.x, 0, randomDisc.y);
            var searchData = new CoverSearchData(patrolPoint, botOwner.CoverSearchInfo, CoverShootType.hide, (checkpoint.Radius * .5f) * (checkpoint.Radius * .5f), 0f, CoverSearchType.distToToCenter, null, null, null, ECheckSHootHide.shootAndHide, new CoverSearchDefenceData(botOwner.Settings.FileSettings.Cover.MIN_DEFENCE_LEVEL), PointsArrayType.byShootType, true);
            return botOwner.BotsGroup.CoverPointMaster.GetCoverPointMain(searchData, true);
        }

        public void UpdateGuardPoint()
        {
            if (!ISBFikaAuthority.IsAiAuthority || !guardPointDirty || Time.time < _coverRetryAt)
                return;
            if (!ISBSpecialForces.Behavior.ISBNavigationBudget.TryBeginSearch(Time.frameCount))
                return;
            _coverRetryAt = Time.time + 1f;
            try
            {
                var candidate = GetCheckpointCoverPoint();
                if (candidate == null || !SainTacticalSensor.IsFinite(candidate.Position))
                    return;
                guardPoint = candidate;
                guardPointDirty = false;
            }
            catch (Exception)
            {
            }
        }

        public void SetGuardPoint(CustomNavigationPoint point)
        {
            guardPoint = point;
        }

        public bool CanDoCheckpointActions()
        {
            return ISBFikaAuthority.IsAiAuthority && HasAssignedCheckpoint() && RaidTimeUtil.GetSecondsSinceSpawning() > 0;
        }

        public bool HasAssignedCheckpoint()
        {
            return assignedCheckpoint != null;
        }

        public ISBCheckpoint GetAssignedCheckpoint()
        {
            return assignedCheckpoint;
        }

        private void OnDestroy()
        {
            assignedCheckpoint?.AssignedBots.Remove(botOwner);
            ISBSpecialForces.Controllers.BotISBManagerFactory.RemoveManager(botOwner, this);
        }

        public void SetAssignedCheckpoint(ISBCheckpoint checkpoint)
        {
            if (assignedCheckpoint == checkpoint)
                return;
            assignedCheckpoint?.AssignedBots.Remove(botOwner);
            assignedCheckpoint = checkpoint;
            guardPoint = null;
            guardPointDirty = true;
            AtCheckpoint = ShouldSwitchCover = false;
            _coverRetryAt = 0f;
        }
    }
}
