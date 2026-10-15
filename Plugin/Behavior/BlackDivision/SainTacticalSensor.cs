using BepInEx.Bootstrap;
using EFT;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace ISBSpecialForces.Behavior.BlackDivision
{
    internal static class SainTacticalSensor
    {
        private static Type _componentType;
        private static readonly Dictionary<Type, Func<object, bool>> UrgencyReaders = new Dictionary<Type, Func<object, bool>>();
        private static class Readers<T>
        {
            internal static readonly Dictionary<(Type Type, string Name), Func<object, T>> Properties = new Dictionary<(Type, string), Func<object, T>>();
        }

        internal static object Find(BotOwner bot)
        {
            if (_componentType == null && Chainloader.PluginInfos.TryGetValue("me.sol.sain", out var info))
                _componentType = info.Instance?.GetType().Assembly.GetType("SAIN.Components.BotComponent");
            return _componentType == null ? null : bot.GetComponent(_componentType);
        }

        private static T Read<T>(object instance, string name)
        {
            if (instance == null)
                return default;
            var type = instance.GetType();
            var key = (type, name);
            if (!Readers<T>.Properties.TryGetValue(key, out var reader))
            {
                var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property == null)
                    throw new MissingMemberException(type.FullName, name);
                var argument = Expression.Parameter(typeof(object), "instance");
                var value = Expression.Property(Expression.Convert(argument, type), property);
                reader = Expression.Lambda<Func<object, T>>(Expression.Convert(value, typeof(T)), argument).Compile();
                Readers<T>.Properties.Add(key, reader);
            }

            return reader(instance);
        }

        private static bool IsUrgent(object decision)
        {
            if (decision == null)
                return true;
            var type = decision.GetType();
            if (!UrgencyReaders.TryGetValue(type, out var reader))
            {
                var argument = Expression.Parameter(typeof(object), "decision");
                var instance = Expression.Convert(argument, type);
                var self = Expression.Property(instance, "CurrentSelfDecision");
                var combat = Expression.Property(instance, "CurrentCombatDecision");
                Expression urgent = Expression.NotEqual(self, DecisionValue(self.Type, "None"));
                if (!combat.Type.IsValueType)
                    urgent = Expression.OrElse(urgent, Expression.Equal(combat, Expression.Constant(null, combat.Type)));
                foreach (var name in new[]
                {
                    "Retreat",
                    "RunAway",
                    "AvoidGrenade",
                    "ThrowGrenade",
                    "DogFight",
                    "SeekCover",
                    "Freeze",
                    "MeleeAttack"
                }

                )
                    urgent = Expression.OrElse(urgent, Expression.Equal(combat, DecisionValue(combat.Type, name)));
                reader = Expression.Lambda<Func<object, bool>>(urgent, argument).Compile();
                UrgencyReaders.Add(type, reader);
            }

            return reader(decision);
        }

        private static ConstantExpression DecisionValue(Type type, string name)
        {
            return Expression.Constant(type.IsEnum ? Enum.Parse(type, name) : (object)name, type);
        }

        internal static bool ReadContact(object component, out string enemyId, out Vector3 position, out float age, out bool visible, out bool canShoot, out bool urgent)
        {
            enemyId = null;
            position = default;
            age = float.MaxValue;
            visible = canShoot = false;
            urgent = true;
            if (component == null || Read<object>(component, "BotActivation") == null || Read<object>(component, "EnemyController") == null || !Read<bool>(component, "BotActive") || Read<bool>(component, "IsDead"))
                return false;
            urgent = IsUrgent(Read<object>(component, "Decision"));
            var enemy = Read<object>(component, "GoalEnemy");
            if (enemy == null || !Read<bool>(enemy, "EnemyKnown"))
                return false;
            var known = Read<Vector3?>(enemy, "LastKnownPosition");
            if (!known.HasValue)
                return false;
            age = Read<float>(enemy, "TimeSinceLastKnownUpdated");
            if (float.IsNaN(age) || age < 0f || age > BlackDivisionTactics.ContactMemory)
                return false;
            enemyId = Read<string>(enemy, "EnemyProfileId");
            position = known.Value;
            visible = Read<bool>(enemy, "IsVisible");
            canShoot = Read<bool>(enemy, "CanShoot");
            return !string.IsNullOrEmpty(enemyId) && IsFinite(position);
        }

        internal static bool IsFinite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);
    }
}
