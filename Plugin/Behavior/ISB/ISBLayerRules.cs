namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBLayerRules
    {
        internal static bool CanAttack(bool hasEnemy, bool enemyIsAI) => hasEnemy;

        internal static bool CanDefend(bool hasEnemy, bool hasLeader, bool hasDefensivePlace) =>
            hasEnemy && hasLeader && hasDefensivePlace;

        internal static bool CanFight(bool hasEnemy, bool hasLeader, bool hasDefensivePlace,
            bool enemyIsAI, bool isVisible, float groupSeenAge, float personalSeenAge)
        {
            if (!hasEnemy)
                return false;
            if (!hasLeader)
                return true;
            return !hasDefensivePlace;
        }

        internal static bool CanHide(bool hasEnemy, bool wasHit, bool squadWasHit) =>
            !hasEnemy && (wasHit || squadWasHit);

        internal static bool CanPatrolDefence(bool hasLeader, bool hasDefensivePlace, int areaId) =>
            hasLeader && hasDefensivePlace && areaId > 0;

        internal static bool ShouldHeal(float timeSinceGroupSeen, int delay, bool firstAidInUse,
            bool firstAidCancelling, bool needsFirstAid, bool hasFirstAid, bool firstAidReady,
            bool needsSurgery, bool hasSurgery)
        {
            if (timeSinceGroupSeen < delay)
                return false;
            return firstAidInUse && !firstAidCancelling || needsFirstAid && hasFirstAid && firstAidReady ||
                needsSurgery && hasSurgery;
        }
    }
}
