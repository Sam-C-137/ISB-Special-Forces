using ISBSpecialForces.Models;

namespace ISBSpecialForces.Behavior.Orders.State
{
    public static class ISBOrderSettings
    {
        public static ISBOrderConfig Current { get; } = new ISBOrderConfig
        {
            enabled = true,
            logs = true,
            updateInterval = 0.35f,
            decisionInterval = 0.8f,
            minimumOrderDuration = 6f,
            maximumOrderDuration = 9f,
            enemyMemoryDuration = 8f,
            casualtyDefenceDuration = 12f,
            regroupEnterDistance = 32f,
            regroupExitDistance = 18f,
            closeCombatDistance = 18f,
            assaultEnterDistance = 20f,
            assaultExitDistance = 30f,
            pursuitEnterDistance = 38f,
            pursuitExitDistance = 22f,
            pursuitOrderDuration = 6f,
            pursuitCooldownDuration = 8f,
            pursuitPulseDuration = 3.75f,
            pursuitCombatWindow = 2.5f,
            pursuitPointRefreshInterval = 0.6f,
            pursuitEnemyMoveRefreshDistance = 2.5f,
            pursuitPointSearchRadius = 8f,
            pursuitAdvanceStep = 28f,
            assaultStopDistance = 13f,
            pursuitStopDistance = 12f,
            flankDistance = 20f,
            tacticalPointSearchRadius = 20f,
            navMeshSampleRadius = 12f,
            pointRefreshInterval = 4f
        };

        public static bool Enabled => Current.enabled;
        public static bool Logs => Current.logs;

        public static void InitRaid()
        {
            if (!Logs)
                return;
        }
    }
}
