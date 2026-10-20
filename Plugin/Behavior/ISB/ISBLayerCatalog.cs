using System;
using System.Collections.Generic;

namespace ISBSpecialForces.Behavior.ISB
{
    internal enum ISBLayerId
    {
        AvoidDanger = 1,
        Assault = 2,
        Patrol = 3,
        Defence = 4,
        SuppressAssault = 5,
        Flank = 6,
        FollowerPatrol = 7,
        DeadBody = 10,
        PatrolDefence = 12,
        Fight = 13,
        Pursuit = 14,
        Hide = 15,
        FightRequest = 16,
        ControlBuilding = 17
    }

    internal readonly struct ISBLayerSpec
    {
        internal ISBLayerId Id { get; }
        internal string Name { get; }
        internal int Priority { get; }
        internal bool ActiveOnStart { get; }

        internal ISBLayerSpec(ISBLayerId id, string name, int priority, bool activeOnStart)
        {
            Id = id;
            Name = name;
            Priority = priority;
            ActiveOnStart = activeOnStart;
        }
    }

    internal static class ISBLayerCatalog
    {
        internal static readonly IReadOnlyList<ISBLayerSpec> Layers = Array.AsReadOnly(new[]
        {
            new ISBLayerSpec(ISBLayerId.AvoidDanger, "ISB Avoid Danger", 120, true),
            new ISBLayerSpec(ISBLayerId.FightRequest, "ISB Fight Request", 110, true),
            new ISBLayerSpec(ISBLayerId.ControlBuilding, "ISB Control Building", 108, true),
            new ISBLayerSpec(ISBLayerId.Pursuit, "ISB Pursuit", 100, true),
            new ISBLayerSpec(ISBLayerId.SuppressAssault, "ISB Suppress Assault", 90, false),
            new ISBLayerSpec(ISBLayerId.Assault, "ISB Assault", 70, false),
            new ISBLayerSpec(ISBLayerId.Flank, "ISB Flank", 65, false),
            new ISBLayerSpec(ISBLayerId.Defence, "ISB Defence", 60, true),
            new ISBLayerSpec(ISBLayerId.Fight, "ISB Fight", 59, true),
            new ISBLayerSpec(ISBLayerId.Hide, "ISB Hide", 40, true),
            new ISBLayerSpec(ISBLayerId.DeadBody, "ISB Dead Body", 28, true),
            new ISBLayerSpec(ISBLayerId.PatrolDefence, "ISB Patrol Defence", 21, true),
            new ISBLayerSpec(ISBLayerId.FollowerPatrol, "ISB Follower Patrol", 18, true),
            new ISBLayerSpec(ISBLayerId.Patrol, "ISB Patrol", 11, true)
        });
    }
}
