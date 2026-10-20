using EFT;
using ISB.Compatibility;
using ISBSpecialForces.Components;

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBBrainSelection
    {
        internal const int Event = 130;

        internal static bool IsEventBot(BotOwner bot)
        {
            if (bot?.Profile?.Info?.Settings == null || !BlackDivisionCheckpointPolicy.IsRole((int)bot.Profile.Info.Settings.Role) ||
                !BlackDivisionEventCheckpoints.Installed || !ISBBrainBootstrap.BlackDivEnabled ||
                !ISBBrainBootstrap.BlackDivAllMaps && ISBCheckpointManager.Instance?.BlackDivisionContainmentActive != true)
                return false;
            return true;
        }

        internal static int Select(int role, bool eventBot) => eventBot ? Event : ISBNativeBrain.Identifier;
    }

    internal sealed class BlackDivBrain : ISBNativeBrain
    {
        internal new const int Identifier = ISBBrainSelection.Event;
        internal BlackDivBrain(BotOwner bot) : base(bot, "blackDiv") { }
    }
}
