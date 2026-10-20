using System.Collections.Generic;

namespace ISBSSOFExtra.Fika
{
    internal sealed class FikaEventQueue<T>
    {
        private sealed class Pending
        {
            internal T Message;
            internal string StateKey;
        }

        private readonly LinkedList<Pending> _messages = new LinkedList<Pending>();
        private readonly Dictionary<string, LinkedListNode<Pending>> _replaceable = new Dictionary<string, LinkedListNode<Pending>>();

        internal void Enqueue(T message, string stateKey)
        {
            if (stateKey == null)
                _replaceable.Clear();
            else if (_replaceable.TryGetValue(stateKey, out var previous))
                _messages.Remove(previous);
            var added = _messages.AddLast(new Pending { Message = message, StateKey = stateKey });
            if (stateKey != null)
                _replaceable[stateKey] = added;
        }

        internal bool TryDequeue(out T message)
        {
            if (_messages.First == null)
            {
                message = default(T);
                return false;
            }
            var first = _messages.First;
            message = first.Value.Message;
            if (first.Value.StateKey != null && _replaceable.TryGetValue(first.Value.StateKey, out var current) && ReferenceEquals(first, current))
                _replaceable.Remove(first.Value.StateKey);
            _messages.RemoveFirst();
            return true;
        }

        internal void Clear()
        {
            _messages.Clear();
            _replaceable.Clear();
        }

        internal static string StateKey(string payload, string player)
        {
            if (payload.StartsWith("S|"))
                return "scenery";
            if (payload.StartsWith("SC173|O|"))
                return "observer:" + player;
            if (payload.StartsWith("SC173|S|") && payload.Contains("\"config\":null") && payload.Contains("\"victim\":null") && payload.Contains("\"sound\":null"))
                return "entity";
            return null;
        }
    }
}
