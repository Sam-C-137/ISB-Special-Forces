using System;

namespace ISBSpecialForces.Behavior.ISB
{
    internal sealed class ISBSuppressionPulse
    {
        internal float NextToggle { get; private set; }
        internal bool IsSuppressing { get; private set; }

        internal bool ShouldAim(float now, bool canShootEnemy, Func<float> nextInterval)
        {
            if (NextToggle < now)
            {
                NextToggle = now + nextInterval();
                IsSuppressing = !IsSuppressing;
            }
            return IsSuppressing || canShootEnemy;
        }
    }
}
