using System;
using System.Threading.Tasks;

namespace ISBSSOFExtra
{
    internal static class QuestAcceptanceConfirmation
    {
        internal static async Task<bool> Confirm(Func<int, Task<bool>> ask, Func<bool> valid)
        {
            for (int step = 0; step < 3; step++)
                if (!valid() || !await ask(step) || !valid())
                    return false;
            return true;
        }
    }
}
