using EFT;
using System;

namespace ISB.Compatibility
{
    internal static class ISBRaidMode
    {
        internal static bool UsesNetworkBridge(bool bridgeActive, Type gameType) => bridgeActive && (gameType == null || !typeof(LocalGame).IsAssignableFrom(gameType));
    }
}
