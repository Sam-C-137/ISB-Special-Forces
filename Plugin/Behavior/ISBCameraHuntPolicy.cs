using System;

namespace ISBSpecialForces.Behavior
{
    internal static class ISBCameraHuntPolicy
    {
        internal static bool IsTrigger(string audioName)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(audioName ?? string.Empty);
            return string.Equals(name, "CameraEnemies", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "CameraEnemies_RU", StringComparison.OrdinalIgnoreCase);
        }
        internal static int TargetPriority(bool human, bool blackDivision, bool nativeTarget, bool protectedTarget)
        {
            if (protectedTarget)
                return -1;
            if (human || blackDivision)
                return 0;
            return nativeTarget ? 1 : -1;
        }

        internal static bool PreferCandidate(int priority, float distance, bool current, int selectedPriority, float selectedDistance, bool selectedCurrent)
        {
            if (priority < 0 || float.IsNaN(distance) || float.IsInfinity(distance))
                return false;
            if (selectedPriority < 0 || priority < selectedPriority)
                return true;
            if (priority > selectedPriority)
                return false;
            if (current != selectedCurrent)
                return current;
            return distance < selectedDistance;
        }
    }
}
