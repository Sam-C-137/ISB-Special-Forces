using System;
using System.Runtime.CompilerServices;

namespace ISBSpecialForces.Behavior.ISB
{
    internal readonly struct ISBSquadKey : IEquatable<ISBSquadKey>
    {
        private readonly object _identity;
        private readonly bool _event;
        internal ISBSquadKey(object group, object formation, bool eventBrain = false)
        {
            _identity = formation ?? group;
            _event = eventBrain;
        }
        public bool Equals(ISBSquadKey other) => _event == other._event && ReferenceEquals(_identity, other._identity);
        public override bool Equals(object obj) => obj is ISBSquadKey other && Equals(other);
        public override int GetHashCode() => _identity == null ? 0 : RuntimeHelpers.GetHashCode(_identity) ^ (_event ? 130 : 0);
    }
}
