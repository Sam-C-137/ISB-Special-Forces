using System;

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBTacticLayers
    {
        internal static void Apply(ISBTacticalState status, Action<ISBLayerId, bool> setActive, Action reportEnemy)
        {
            setActive(ISBLayerId.Assault, status == ISBTacticalState.Assault);
            setActive(ISBLayerId.SuppressAssault, status == ISBTacticalState.SuppressAssault);
            setActive(ISBLayerId.Flank, status == ISBTacticalState.Flank);
            reportEnemy?.Invoke();
        }
    }
}
