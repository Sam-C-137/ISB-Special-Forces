namespace ISBSpecialForces.Behavior
{
    internal static class ISBNavigationBudget
    {
        private static int _frame = -1;
        private static int _searches;
        internal static bool TryBeginSearch(int frame)
        {
            if (_frame != frame)
            {
                _frame = frame;
                _searches = 0;
            }

            if (_searches >= 2)
                return false;
            _searches++;
            return true;
        }
    }
}
