namespace ISBSpecialForces.Behavior.ISB
{
    internal enum ISBAction
    {
        Hold, Cover, Shoot, Heal, Advance, Suppress, SuppressAdvance, Flank, FiringPosition, Patrol, Follow, Body
    }

    internal struct ISBCombatInput
    {
        internal bool HasEnemy, Visible, CanShoot, InCover, NeedsHeal, GroupSeesEnemy;
        internal bool ReadyToAssault, HasFlankPoint, CanSuppressAdvance, HasFiringPoint, CanReachFiringPoint, CoverPartner;
        internal bool HasBoss, IsLeader, CanAdvanceToCover;
        internal float EnemyDistance;
    }

    internal static class ISBCombatPlan
    {
        internal static ISBAction Select(ISBLayerId layer, ISBCombatInput input)
        {
            if (layer == ISBLayerId.DeadBody)
                return input.NeedsHeal ? ISBAction.Heal : ISBAction.Body;
            if (layer == ISBLayerId.Hide)
                return input.InCover ? input.NeedsHeal ? ISBAction.Heal : ISBAction.Hold : ISBAction.Cover;
            if (layer == ISBLayerId.Patrol || layer == ISBLayerId.FollowerPatrol)
                return input.NeedsHeal ? ISBAction.Heal : input.HasBoss && !input.IsLeader ? ISBAction.Follow : ISBAction.Patrol;
            if (!input.HasEnemy)
                return input.InCover ? ISBAction.Hold : ISBAction.Cover;
            if (input.Visible && input.CanShoot)
                return ISBAction.Shoot;
            if (input.NeedsHeal && layer != ISBLayerId.Flank && layer != ISBLayerId.SuppressAssault)
                return ISBAction.Heal;
            if (layer == ISBLayerId.SuppressAssault)
                return input.CanSuppressAdvance ? ISBAction.SuppressAdvance : ISBAction.Advance;
            if (layer == ISBLayerId.Flank)
                return input.EnemyDistance < 10f ? ISBAction.Advance : input.HasFlankPoint ? ISBAction.Flank : ISBAction.Cover;
            if (layer == ISBLayerId.Assault)
            {
                if (input.GroupSeesEnemy)
                    return ISBAction.Advance;
                if (input.EnemyDistance < 30f)
                    return input.ReadyToAssault ? input.CoverPartner ? ISBAction.Suppress : ISBAction.Advance : ISBAction.Hold;
                return input.CanAdvanceToCover ? ISBAction.Cover : input.InCover ? ISBAction.Hold : ISBAction.Cover;
            }
            if (layer == ISBLayerId.Pursuit || layer == ISBLayerId.ControlBuilding)
                return ISBAction.Advance;
            if (input.HasFiringPoint && input.CanReachFiringPoint)
                return ISBAction.FiringPosition;
            return input.InCover ? ISBAction.Hold : ISBAction.Cover;
        }
    }
}
