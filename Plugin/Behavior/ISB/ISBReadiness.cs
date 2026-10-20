using System.Collections.Generic;

namespace ISBSpecialForces.Behavior.ISB
{
    internal sealed class ISBReadiness<T>
    {
        private readonly Dictionary<T, float> _readyAt = new Dictionary<T, float>();

        internal void Register(T member)
        {
            if (!_readyAt.ContainsKey(member))
                _readyAt.Add(member, 0f);
        }
        internal void Remove(T member) => _readyAt.Remove(member);
        internal void Clear() => _readyAt.Clear();

        internal void ReadyToAttack(T member, float now)
        {
            if (_readyAt.ContainsKey(member))
                _readyAt[member] = now + 5f;
        }

        internal bool IsReadyToAssault(T member, float now)
        {
            return _readyAt.TryGetValue(member, out float deadline) && deadline < now;
        }

        internal bool TryGetDeadline(T member, out float deadline) => _readyAt.TryGetValue(member, out deadline);
    }
}
