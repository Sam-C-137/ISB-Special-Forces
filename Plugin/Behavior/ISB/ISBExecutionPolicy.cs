namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBExecutionPolicy
    {
        internal static bool CanSimulate(bool hasGame, bool fikaLoaded, bool isSoloGame, bool? isServer)
        {
            if (!hasGame)
                return false;
            return !fikaLoaded || isSoloGame || isServer == true;
        }

        internal static bool CanPresent(bool hasGame, bool fikaLoaded, bool isSoloGame, bool? isHeadless)
        {
            if (!hasGame)
                return false;
            return !fikaLoaded || isSoloGame || isHeadless == false;
        }
    }
}
