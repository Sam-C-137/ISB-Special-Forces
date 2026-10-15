using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Game.Spawning;
using EFT.Interactive;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;
using NativeSpawnSystem = EFT.Game.Spawning.SpawnSystem;
using NativeSpawnPoints = EFT.Game.Spawning.ISpawnPointsCollection;

namespace ISBSSOFExtra
{
    internal static class LabsSpawnSafety
    {
        internal const float FireMargin = 4f;
        internal const float GraceSeconds = 25f;
        private static Harmony _harmony;
        private static FieldInfo _spawnPoints;
        private static string _firePath;
        private static Func<bool> _enabledForRaid;
        private static ManualLogSource _log;
        private static AbstractGame _game;
        private static bool _loaded, _enabled, _playerChecked;
        private static readonly List<Bounds> Hazards = new List<Bounds>();
        private static ISpawnPoint[] _candidates = Array.Empty<ISpawnPoint>();
        private sealed class SafePoints : NativeSpawnPoints
        {
            internal readonly NativeSpawnPoints Original;
            internal readonly ISpawnPoint[] Points;
            internal SafePoints(NativeSpawnPoints original, ISpawnPoint[] points)
            {
                Original = original;
                Points = points;
            }

            public int Count => Points.Length;

            public IEnumerator<ISpawnPoint> GetEnumerator() => ((IEnumerable<ISpawnPoint>)Points).GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            public ISpawnPoint CreateSpawnPoint(SpawnPointParams value) => Original.CreateSpawnPoint(value);
            public void DestroySpawnPoint(ISpawnPoint value) => Original.DestroySpawnPoint(value);
            public void Validate() => Original.Validate();
        }

        internal static void Configure(string firePath, Func<bool> enabledForRaid, ManualLogSource log)
        {
            Disable();
            _firePath = firePath;
            _enabledForRaid = enabledForRaid;
            _log = log;
            try
            {
                var type = typeof(NativeSpawnSystem);
                var select = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single(m => m.Name.EndsWith("SelectSpawnPoint", StringComparison.Ordinal) && m.GetParameters().Length == 7);
                _spawnPoints = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Single(f => f.FieldType == typeof(NativeSpawnPoints));
                _harmony = new Harmony("com.samc137.isbsofextras.safe-player-spawn");
                _harmony.Patch(select, prefix: new HarmonyMethod(typeof(LabsSpawnSafety), nameof(BeforeSelection)), postfix: new HarmonyMethod(typeof(LabsSpawnSafety), nameof(AfterSelection)) { priority = Priority.Last }, finalizer: new HarmonyMethod(typeof(LabsSpawnSafety), nameof(RestoreSelection)));
                _harmony.Patch(AccessTools.PropertySetter(typeof(AbstractGame), nameof(AbstractGame.Status)), prefix: new HarmonyMethod(typeof(LabsSpawnSafety), nameof(BeforeRaidStarted)) { priority = Priority.First });
            }
            catch (Exception ex)
            {
                Disable();
                log?.LogError("[ISB Spawn] Player spawn protection could not be installed: " + ex.Message);
            }
        }

        internal static void Disable()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            _game = null;
            _loaded = false;
            _enabled = false;
            _playerChecked = false;
            Hazards.Clear();
            _candidates = Array.Empty<ISpawnPoint>();
        }

        private static bool EnsureLoaded()
        {
            var game = Singleton<AbstractGame>.Instance;
            if (game == null || !(string.Equals(game.LocationObjectId, "laboratory", StringComparison.OrdinalIgnoreCase) || SceneManager.GetSceneByName("Laboratory_Collector_Exit").isLoaded))
                return false;
            if (!ReferenceEquals(_game, game))
            {
                _game = game;
                _loaded = false;
                _enabled = false;
                _playerChecked = false;
                Hazards.Clear();
                _candidates = Array.Empty<ISpawnPoint>();
            }

            if (_loaded)
                return _enabled;
            _loaded = true;
            try
            {
                if (_enabledForRaid?.Invoke() != true || string.IsNullOrEmpty(_firePath))
                    return false;
                var data = JsonConvert.DeserializeObject<TakeoverFireDefinition>(File.ReadAllText(_firePath));
                if (data?.enabled != true || data.fires == null)
                    return false;
                foreach (var point in data.fires.Take(TakeoverFire.MaxFirePoints))
                {
                    if (point?.damage != true)
                        continue;
                    var size = new Vector3(point.damageWidth, point.damageHeight, point.damageDepth) * point.scale;
                    Hazards.Add(WorldBounds(new Vector3(point.x, point.y, point.z), Quaternion.Euler(point.rotationX, point.rotationY, point.rotationZ), new Vector3(0, size.y * .5f, 0), size, FireMargin));
                }

                foreach (var trigger in Resources.FindObjectsOfTypeAll<FlameDamageTrigger>())
                {
                    if (trigger == null || !trigger.isActiveAndEnabled || trigger is TakeoverFlameTrigger)
                        continue;
                    var collider = trigger.GetComponent<Collider>();
                    if (collider == null)
                        continue;
                    var bounds = collider.bounds;
                    bounds.Expand(new Vector3(FireMargin * 2f, .2f, FireMargin * 2f));
                    Hazards.Add(bounds);
                }

                _enabled = Hazards.Count > 0;
            }
            catch (Exception ex)
            {
                _log?.LogError("[ISB Spawn] Fire exclusion areas could not be loaded: " + ex.Message);
            }

            return _enabled;
        }

        internal static Bounds WorldBounds(Vector3 position, Quaternion rotation, Vector3 center, Vector3 size, float margin)
        {
            var bounds = new Bounds(position + rotation * center, Vector3.zero);
            for (int i = 0; i < 8; i++)
                bounds.Encapsulate(position + rotation * (center + new Vector3(((i & 1) == 0 ? -.5f : .5f) * size.x, ((i & 2) == 0 ? -.5f : .5f) * size.y, ((i & 4) == 0 ? -.5f : .5f) * size.z)));
            bounds.Expand(new Vector3(margin * 2f, .2f, margin * 2f));
            return bounds;
        }

        internal static bool IsSafe(Vector3 position, IEnumerable<Bounds> hazards)
        {
            if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) || float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                return false;
            var player = new Bounds(position + Vector3.up * .9f, new Vector3(.7f, 1.8f, .7f));
            foreach (var hazard in hazards)
                if (hazard.Intersects(player))
                    return false;
            return true;
        }

        internal static bool IsHumanCategory(ESpawnCategory category, IPlayer person) => person?.IsAI != true && (category == ESpawnCategory.Player || category == ESpawnCategory.Coop || category == ESpawnCategory.Group);
        private static bool ClearBody(Vector3 position)
        {
            int layer = LayerMask.NameToLayer("LowPolyCollider");
            return layer >= 0 && !Physics.CheckCapsule(position + Vector3.up * .45f, position + Vector3.up * 1.45f, .3f, 1 << layer, QueryTriggerInteraction.Ignore);
        }

        private static void BeforeSelection(object __instance, ESpawnCategory __0, EPlayerSide __1, IPlayer __4, out object __state)
        {
            __state = null;
            if (!IsHumanCategory(__0, __4) || !EnsureLoaded())
                return;
            var original = (NativeSpawnPoints)_spawnPoints.GetValue(__instance);
            var safe = original.Where(p => p != null && IsSafe(p.Position, Hazards) && ClearBody(p.Position)).ToArray();
            if (!safe.Any(p => MatchesPlayer(p, __1)))
            {
                _log?.LogError("[ISB Spawn] No unobstructed player spawn outside the fire exclusion areas.");
                return;
            }

            _candidates = safe;
            __state = original;
            _spawnPoints.SetValue(__instance, new SafePoints(original, safe));
        }

        private static bool MatchesPlayer(ISpawnPoint point, EPlayerSide side)
        {
            var mask = side == EPlayerSide.Usec ? EPlayerSideMask.Usec : side == EPlayerSide.Bear ? EPlayerSideMask.Bear : EPlayerSideMask.Savage;
            return !point.SpawnBlocked && (point.Sides & mask) != 0 && (point.Categories & (ESpawnCategoryMask.Player | ESpawnCategoryMask.Coop | ESpawnCategoryMask.Group)) != 0;
        }

        private static ISpawnPoint FindReplacement(EPlayerSide side, Vector3 origin)
        {
            return _candidates.Where(p => MatchesPlayer(p, side) && IsSafe(p.Position, Hazards) && ClearBody(p.Position)).OrderBy(p => (p.Position - origin).sqrMagnitude).FirstOrDefault();
        }

        private static void AfterSelection(ESpawnCategory __0, EPlayerSide __1, IPlayer __4, ref ISpawnPoint __result)
        {
            if (!IsHumanCategory(__0, __4) || !EnsureLoaded() || __result == null || IsSafe(__result.Position, Hazards))
                return;
            var replacement = FindReplacement(__1, __result.Position);
            if (replacement != null)
                __result = replacement;
        }

        private static void RestoreSelection(object __instance, object __state)
        {
            if (__state != null)
                _spawnPoints.SetValue(__instance, __state);
        }

        private static void BeforeRaidStarted(GameStatus __0)
        {
            if (__0 != GameStatus.Started || !EnsureLoaded() || _playerChecked)
                return;
            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            if (player == null || !player.IsYourPlayer || player.IsAI)
                return;
            _playerChecked = true;
            if (IsSafe(player.Position, Hazards))
                return;
            if (_candidates.Length == 0)
                _candidates = Resources.FindObjectsOfTypeAll<SpawnPointMarker>().Where(m => m != null && m.gameObject.scene.isLoaded).Select(m => m.SpawnPoint).Where(p => p != null).ToArray();
            var replacement = FindReplacement(player.Side, player.Position);
            if (replacement == null)
            {
                _log?.LogError("[ISB Spawn] Could not relocate the player away from fire before raid start.");
                return;
            }

            player.Teleport(replacement.Position, true);
        }

        internal static bool IsGracePeriod(GameStatus status, float elapsed) => status != GameStatus.Started || float.IsNaN(elapsed) || float.IsInfinity(elapsed) || elapsed < GraceSeconds;
        internal static bool FireDamageSuspended
        {
            get
            {
                if (!TakeoverFlameTrigger.EventActive)
                    return false;
                var game = Singleton<AbstractGame>.Instance;
                return game == null || game.GameTimer == null || IsGracePeriod(game.Status, game.PastTime);
            }
        }
    }
}
