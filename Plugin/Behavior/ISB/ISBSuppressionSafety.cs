using System;

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBSuppressionSafety
    {
        internal static bool MayFireAtMemory(float age, float horizontalDistance, float verticalDistance, float forwardProgress,
            bool clearLine, bool launcher)
        {
            return !launcher && clearLine && age >= 0f && age <= 1.5f &&
                !float.IsNaN(horizontalDistance) && !float.IsInfinity(horizontalDistance) &&
                horizontalDistance > 3f && Math.Abs(verticalDistance) < horizontalDistance * 0.75f && forwardProgress > 1f;
        }
    }
}
