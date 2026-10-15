using System;

namespace ISBSpecialForces.Behavior.BlackDivision
{
    internal enum BlackDivisionMode
    {
        Idle,
        Defence,
        Assault,
        Flank,
        SuppressAssault
    }

    internal struct TacticalSituation
    {
        internal string EnemyId;
        internal float ContactAge, Distance;
        internal int Members, Ready, Covering;
        internal bool UnderFire, Casualty, OtherSquadAdvancing;
    }

    internal sealed class BlackDivisionTactics
    {
        internal const float ContactMemory = 40f;
        internal const float QuietAssaultDelay = 9f;
        internal const float CloseCombatDistance = 18f;
        internal const float PhaseDuration = 8f;
        internal BlackDivisionMode Mode { get; private set; }
        internal int Revision { get; private set; }
        internal int MovingTeam { get; private set; }

        private string _enemy;
        private float _prepareUntil, _defendUntil, _phaseUntil, _changeAfter;
        internal void Update(TacticalSituation situation, float now)
        {
            if (string.IsNullOrEmpty(situation.EnemyId) || float.IsNaN(situation.ContactAge) || float.IsInfinity(situation.ContactAge) || float.IsNaN(situation.Distance) || float.IsInfinity(situation.Distance) || situation.ContactAge < 0f || situation.ContactAge > ContactMemory || situation.Members == 0)
            {
                _enemy = null;
                Change(BlackDivisionMode.Idle, now);
                return;
            }

            if (_enemy != situation.EnemyId)
            {
                _enemy = situation.EnemyId;
                _prepareUntil = now + 2.5f;
                _defendUntil = now;
                Change(BlackDivisionMode.Defence, now);
                Revision++;
            }

            if (situation.Casualty)
                _defendUntil = now + 6f;
            if (now < _prepareUntil || now < _defendUntil || situation.Distance <= CloseCombatDistance || situation.Ready == 0 || situation.Members < 2)
            {
                Change(BlackDivisionMode.Defence, now);
                return;
            }

            var next = BlackDivisionMode.Defence;
            if (situation.Covering > 0)
                next = situation.UnderFire ? BlackDivisionMode.SuppressAssault : BlackDivisionMode.Flank;
            else if (situation.ContactAge >= QuietAssaultDelay)
                next = BlackDivisionMode.Assault;
            else if (situation.OtherSquadAdvancing)
                next = BlackDivisionMode.Flank;
            if (next == BlackDivisionMode.Defence || now >= _changeAfter)
                Change(next, now);
            if (Mode != BlackDivisionMode.Defence && Mode != BlackDivisionMode.Idle && now >= _phaseUntil)
            {
                MovingTeam = 1 - MovingTeam;
                Revision++;
                _phaseUntil = now + PhaseDuration;
            }
        }

        private void Change(BlackDivisionMode mode, float now)
        {
            if (mode == Mode)
                return;
            Mode = mode;
            Revision++;
            _changeAfter = now + 3f;
            _phaseUntil = now + PhaseDuration;
        }

        internal static bool CanAssignCheckpoint(string spawnId) => spawnId == null || spawnId.IndexOf("hunt", StringComparison.OrdinalIgnoreCase) < 0;
        internal static bool NeedsCombat(bool visible, bool canShoot, bool urgent, bool underFire, bool contact, float age, float distance) => visible || canShoot || urgent || underFire || (contact && age >= 0f && (age <= 3f || (age <= 6f && distance <= CloseCombatDistance)));
        internal static bool CanManeuver(bool active, bool visible, bool canShoot, bool urgent, bool underFire, float distance, float contactAge) => active && !visible && !canShoot && !urgent && !underFire && distance > CloseCombatDistance && contactAge >= 0f && contactAge <= ContactMemory;
    }
}
