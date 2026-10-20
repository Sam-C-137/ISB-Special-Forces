using EFT;
using ISBSpecialForces.Components;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Tactics
{
    internal sealed class TacticalSquad
    {
        internal readonly List<ISBTacticalAgent> Members = new List<ISBTacticalAgent>();
        internal readonly ISBManeuverPolicy Policy = new ISBManeuverPolicy();
        internal readonly BotsGroup Group;
        internal string EnemyId;
        internal Vector3 EnemyPosition, Anchor;
        internal float ContactAge = float.MaxValue;
        private int _previousCount, _assignedRevision = -1;
        private readonly HashSet<ISBTacticalAgent> _moving = new HashSet<ISBTacticalAgent>();
        private readonly Predicate<ISBTacticalAgent> _retired;
        internal TacticalSquad(BotsGroup group)
        {
            Group = group;
            _retired = IsRetired;
        }

        private bool IsRetired(ISBTacticalAgent member) => member == null || !member.Alive || member.Bot.BotsGroup != Group;
        internal bool Advancing => Policy.Mode != ISBManeuverMode.Idle && Policy.Mode != ISBManeuverMode.Defence;

        internal void Tick(float now, bool otherSquadAdvancing)
        {
            Members.RemoveAll(_retired);
            Members.Sort((a, b) =>
            {
                var priority = LeaderPriority(a).CompareTo(LeaderPriority(b));
                return priority != 0 ? priority : string.CompareOrdinal(a.Bot.Profile.Id, b.Bot.Profile.Id);
            });
            if (Members.Count == 0)
                return;
            var casualty = _previousCount > Members.Count;
            _previousCount = Members.Count;
            Anchor = Members[0].Bot.Position;
            ISBTacticalAgent source = null;
            foreach (var member in Members)
            {
                if (!member.HasContact)
                    continue;
                if (source == null || (member.EnemyId == EnemyId && source.EnemyId != EnemyId) || (member.EnemyId == source.EnemyId && member.ContactAge < source.ContactAge))
                    source = member;
            }

            EnemyId = source?.EnemyId;
            ContactAge = source?.ContactAge ?? float.MaxValue;
            if (source != null)
                EnemyPosition = source.EnemyPosition;
            var ready = 0;
            var covering = 0;
            var underFire = false;
            for (var i = 0; i < Members.Count; i++)
            {
                var member = Members[i];
                member.Team = i % 2;
                if (member.EnemyId != EnemyId || !member.HasContact)
                    continue;
                if (member.ReadyToMove)
                    ready++;
                if (member.Visible && member.CanShoot && !member.Urgent)
                    covering++;
                underFire |= member.UnderFire;
            }

            Policy.Update(new TacticalSituation { EnemyId = EnemyId, ContactAge = ContactAge, Distance = Vector3.Distance(Anchor, EnemyPosition), Members = Members.Count, Ready = ready, Covering = covering, UnderFire = underFire, Casualty = casualty, OtherSquadAdvancing = otherSquadAdvancing }, now);
            if (_assignedRevision != Policy.Revision)
            {
                _assignedRevision = Policy.Revision;
                _moving.Clear();
                var budget = Math.Max(1, Members.Count / 2);
                for (var pass = 0; pass < 2 && _moving.Count == 0; pass++)
                    foreach (var member in Members)
                    {
                        var role = (int)member.Bot.Profile.Info.Settings.Role;
                        if (_moving.Count >= budget || member.EnemyId != EnemyId || !member.ReadyToMove || role == 13712 || role == 13714 || (pass == 0 && member.Team != Policy.MovingTeam))
                            continue;
                        _moving.Add(member);
                    }
            }
        }

        internal bool CanMove(ISBTacticalAgent member) => Advancing && member.EnemyId == EnemyId && _moving.Contains(member);
        internal bool IsDestinationFree(ISBTacticalAgent owner, Vector3 point)
        {
            foreach (var member in Members)
                if (member != owner && member.HasDestination && (member.Destination - point).sqrMagnitude < 9f)
                    return false;
            return true;
        }

        private static int LeaderPriority(ISBTacticalAgent member)
        {
            if (member.Bot.Boss?.IamBoss == true)
                return 0;
            switch ((int)member.Bot.Profile.Info.Settings.Role)
            {
                case 13707:
                    return 1;
                case 13709:
                    return 2;
                case 13708:
                    return 3;
                case 13710:
                    return 4;
                default:
                    return 5;
            }
        }
    }

    internal sealed class ISBTacticalController : MonoBehaviourSingleton<ISBTacticalController>
    {
        private readonly List<ISBTacticalAgent> _agents = new List<ISBTacticalAgent>();
        private readonly Dictionary<BotsGroup, TacticalSquad> _squads = new Dictionary<BotsGroup, TacticalSquad>();
        private readonly List<BotsGroup> _empty = new List<BotsGroup>();
        private float _nextTick;
        internal void Register(ISBTacticalAgent agent)
        {
            if (agent != null && !_agents.Contains(agent))
                _agents.Add(agent);
        }

        internal void Remove(ISBTacticalAgent agent)
        {
            _agents.Remove(agent);
            agent.Squad = null;
        }

        public void ResetRaid()
        {
            ISB.ISBNativeSquad.Reset();
            foreach (var agent in _agents)
                if (agent != null)
                {
                    agent.CancelMove();
                    agent.Squad = null;
                }

            _agents.Clear();
            _squads.Clear();
            _nextTick = 0f;
        }

        private void Update()
        {
            if (!ISBFikaAuthority.IsAiAuthority)
                return;
            var now = Time.time;
            if (now < _nextTick)
                return;
            _nextTick = now + 0.25f;
            for (var i = _agents.Count - 1; i >= 0; i--)
            {
                var agent = _agents[i];
                if (agent == null || !agent.Alive)
                {
                    if (agent != null)
                    {
                        agent.CancelMove();
                        agent.Squad = null;
                    }

                    _agents.RemoveAt(i);
                    continue;
                }

                agent.RefreshContact();
                var group = agent.Bot.BotsGroup;
                if (group == null)
                {
                    agent.CancelMove();
                    agent.Squad = null;
                    continue;
                }

                if (!_squads.TryGetValue(group, out var squad))
                    _squads[group] = squad = new TacticalSquad(group);
                if (agent.Squad != squad)
                {
                    agent.CancelMove();
                    agent.Squad = squad;
                    if (!squad.Members.Contains(agent))
                        squad.Members.Add(agent);
                }
            }

            _empty.Clear();
            foreach (var entry in _squads)
            {
                var squad = entry.Value;
                var otherAdvancing = false;
                if (squad.EnemyId != null)
                    foreach (var other in _squads.Values)
                        if (other != squad && squad.EnemyId != null && other.EnemyId == squad.EnemyId && other.Advancing && other.ContactAge <= ISBManeuverPolicy.ContactMemory && (other.Anchor - squad.Anchor).sqrMagnitude <= 80f * 80f)
                        {
                            otherAdvancing = true;
                            break;
                        }

                squad.Tick(now, otherAdvancing);
                if (squad.Members.Count == 0)
                    _empty.Add(entry.Key);
            }

            foreach (var group in _empty)
                _squads.Remove(group);
        }

        public override void OnDestroy()
        {
            ResetRaid();
            base.OnDestroy();
        }
    }
}
