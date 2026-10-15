using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using ISB.Compatibility;
using ISBSpecialForces.Controllers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ISBSpecialForces.Components
{
    internal sealed class ISBLabsGatePassage : MonoBehaviour
    {
        private readonly HashSet<Collider> _gates = new HashSet<Collider>();
        private readonly Dictionary<BotOwner, Collider> _bots = new Dictionary<BotOwner, Collider>();
        private readonly List<BotOwner> _dead = new List<BotOwner>();
        private readonly Dictionary<Collider, HashSet<Collider>> _changed = new Dictionary<Collider, HashSet<Collider>>();
        private float _nextCheck;
        private bool _subscribed;
        internal static bool Eligible(string location, int role, bool authority) => authority && string.Equals(location, "laboratory", StringComparison.OrdinalIgnoreCase) && (WildSpawnTypeExtensions.IsISB((WildSpawnType)role) || role == 848424 || role == 848426 || ISBBlackDivisionUtility.IsBlackDivisionRole((WildSpawnType)role));
        internal static void Register(BotOwner bot)
        {
            var world = Singleton<GameWorld>.Instance;
            if (world == null || bot?.Profile?.Info?.Settings == null || !Eligible(world.LocationId, (int)bot.Profile.Info.Settings.Role, ISBFikaAuthority.IsAiAuthority))
                return;
            var controller = world.GetComponent<ISBLabsGatePassage>() ?? world.gameObject.AddComponent<ISBLabsGatePassage>();
            controller.Add(bot);
        }

        private void Add(BotOwner bot)
        {
            var collider = bot.GetPlayer?.CharacterController?.GetCollider();
            if (collider == null)
                return;
            if (!_subscribed)
            {
                _subscribed = true;
                SceneManager.sceneLoaded += SceneLoaded;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                    Discover(SceneManager.GetSceneAt(i));
            }

            _bots[bot] = collider;
            foreach (var gate in _gates)
                Ignore(collider, gate);
        }

        private void SceneLoaded(Scene scene, LoadSceneMode mode) => Discover(scene);
        internal static bool GateCollider(string name) => name == "lab_hangar_gates_COLLIDER" || name == "Rollete_gate_gates_COLLIDER" || name == "Transfer_gateways_01_Door_L_COLLIDER" || name == "Transfer_gateways_01_Door_R_COLLIDER" || name == "Transfer_gateways_01_Door_L" || name == "Transfer_gateways_01_Door_R";
        private void Discover(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || !scene.name.StartsWith("Laboratory", StringComparison.OrdinalIgnoreCase))
                return;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    if (GateCollider(collider.name))
                        AddGate(collider);
            }
        }

        private void AddGate(Collider gate)
        {
            if (gate == null || gate.isTrigger || !_gates.Add(gate))
                return;
            foreach (var collider in _bots.Values)
                Ignore(collider, gate);
        }

        private void Ignore(Collider bot, Collider gate)
        {
            if (bot == null || gate == null || PhysicsExtensions.GetIgnoreCollision(bot, gate))
                return;
            PhysicsExtensions.IgnoreCollision(bot, gate, true);
            if (!_changed.TryGetValue(bot, out var gates))
                _changed[bot] = gates = new HashSet<Collider>();
            gates.Add(gate);
        }

        private void Update()
        {
            if (!ISBFikaAuthority.IsAiAuthority || Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + 1f;
            _dead.Clear();
            foreach (var entry in _bots)
            {
                if (entry.Key == null || entry.Key.IsDead || entry.Value == null)
                {
                    _dead.Add(entry.Key);
                    continue;
                }

                foreach (var gate in _gates)
                    Ignore(entry.Value, gate);
            }

            foreach (var bot in _dead)
            {
                var collider = _bots[bot];
                if (!ReferenceEquals(collider, null) && _changed.TryGetValue(collider, out var gates))
                {
                    foreach (var gate in gates)
                        if (collider != null && gate != null)
                            PhysicsExtensions.IgnoreCollision(collider, gate, false);
                    _changed.Remove(collider);
                }

                _bots.Remove(bot);
            }
        }

        private void OnDestroy()
        {
            if (_subscribed)
                SceneManager.sceneLoaded -= SceneLoaded;
            foreach (var pair in _changed)
                foreach (var gate in pair.Value)
                    if (pair.Key != null && gate != null)
                        PhysicsExtensions.IgnoreCollision(pair.Key, gate, false);
            _changed.Clear();
            _gates.Clear();
            _bots.Clear();
            _dead.Clear();
        }
    }
}
