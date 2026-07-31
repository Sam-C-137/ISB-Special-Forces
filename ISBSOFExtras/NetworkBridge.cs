using System;

namespace ISBSSOFExtra
{
    public interface IExtrasNetworkBridge
    {
        bool Active { get; }

        bool Ready { get; }

        bool IsServer { get; }

        bool IsHeadless { get; }

        bool IsSinglePlayer { get; }

        bool RaidStarted { get; }

        double NetworkTime { get; }

        void DrainReceivedPayloads(Action<string> handler);

        bool ConsumePeerConnected();

        bool Broadcast(string payload);

        bool SendToServer(string payload);
    }

    public static class ExtrasNetworkBridgeRegistry
    {
        private static readonly IExtrasNetworkBridge EmptyBridge = new NoNetworkBridge();
        private static IExtrasNetworkBridge _current = EmptyBridge;

        public static IExtrasNetworkBridge Current => _current ?? EmptyBridge;

        public static void Register(IExtrasNetworkBridge bridge)
        {
            _current = bridge ?? EmptyBridge;
        }

        public static void Unregister(IExtrasNetworkBridge bridge)
        {
            if (ReferenceEquals(_current, bridge))
                _current = EmptyBridge;
        }

        private sealed class NoNetworkBridge : IExtrasNetworkBridge
        {
            public bool Active => false;

            public bool Ready => false;

            public bool IsServer => false;

            public bool IsHeadless => false;

            public bool IsSinglePlayer => true;

            public bool RaidStarted => false;

            public double NetworkTime => 0d;

            public void DrainReceivedPayloads(Action<string> handler)
            {
            }

            public bool ConsumePeerConnected()
            {
                return false;
            }

            public bool Broadcast(string payload)
            {
                return false;
            }

            public bool SendToServer(string payload)
            {
                return false;
            }
        }
    }
}
