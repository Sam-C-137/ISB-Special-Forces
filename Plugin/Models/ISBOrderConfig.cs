namespace ISBSpecialForces.Models
{
    public sealed class ISBOrderConfig
    {
        public bool enabled { get; set; } = true;
        public bool logs { get; set; } = false;
        public float updateInterval { get; set; } = 0.35f;
        public float decisionInterval { get; set; } = 0.8f;
        public float minimumOrderDuration { get; set; } = 8f;
        public float maximumOrderDuration { get; set; } = 12f;
        public float enemyMemoryDuration { get; set; } = 8f;
        public float casualtyDefenceDuration { get; set; } = 12f;
        public float regroupEnterDistance { get; set; } = 32f;
        public float regroupExitDistance { get; set; } = 18f;
        public float closeCombatDistance { get; set; } = 18f;
        public float assaultEnterDistance { get; set; } = 20f;
        public float assaultExitDistance { get; set; } = 30f;
        public float pursuitEnterDistance { get; set; } = 35f;
        public float pursuitExitDistance { get; set; } = 20f;
        public float pursuitOrderDuration { get; set; } = 6f;
        public float pursuitCooldownDuration { get; set; } = 8f;
        public float pursuitPulseDuration { get; set; } = 3.75f;
        public float pursuitCombatWindow { get; set; } = 2.5f;
        public float pursuitPointRefreshInterval { get; set; } = 0.6f;
        public float pursuitEnemyMoveRefreshDistance { get; set; } = 2.5f;
        public float pursuitPointSearchRadius { get; set; } = 8f;
        public float pursuitAdvanceStep { get; set; } = 28f;
        public float assaultStopDistance { get; set; } = 13f;
        public float pursuitStopDistance { get; set; } = 12f;
        public float flankDistance { get; set; } = 20f;
        public float tacticalPointSearchRadius { get; set; } = 20f;
        public float navMeshSampleRadius { get; set; } = 12f;
        public float pointRefreshInterval { get; set; } = 4f;
    }
}
