using EFT;
using System;
using System.Reflection;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    internal static class ISBBlackDivisionUtility
    {
        private static readonly Dictionary<Type, Dictionary<string, MemberInfo>> Members = new Dictionary<Type, Dictionary<string, MemberInfo>>();
        private static readonly BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly int[] KnownBlackDivisionRoleIds =
        {
            848420,
            848421,
            848422,
            848423,
        };
        public static bool IsBlackDivisionRole(WildSpawnType role)
        {
            var roleId = (int)role;
            for (var i = 0; i < KnownBlackDivisionRoleIds.Length; i++)
            {
                if (KnownBlackDivisionRoleIds[i] == roleId)
                    return true;
            }

            var roleName = role.ToString();
            return roleName.StartsWith("blackDiv", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsBlackDivAssaultRole(WildSpawnType role)
        {
            return (int)role == 848420 || string.Equals(role.ToString(), "blackDivAssault", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAlive(BotOwner bot)
        {
            return bot != null && !bot.IsDead && bot.GetPlayer != null && bot.GetPlayer.HealthController != null && bot.GetPlayer.HealthController.IsAlive;
        }

        public static BotOwner TryResolveBotOwner(object player)
        {
            if (player == null)
                return null;
            if (player is Player nativePlayer)
                return nativePlayer.AIData?.BotOwner;
            if (player is BotOwner owner)
                return owner;
            try
            {
                var directOwner = GetMemberValue(player, "BotOwner") as BotOwner;
                if (directOwner != null)
                    return directOwner;
                var aiData = GetMemberValue(player, "AIData");
                return GetMemberValue(aiData, "BotOwner") as BotOwner ?? GetMemberValue(aiData, "Owner") as BotOwner;
            }
            catch
            {
                return null;
            }
        }

        public static string GetBotId(BotOwner bot)
        {
            if (bot == null)
                return string.Empty;
            return bot.Profile?.Id ?? bot.Profile?.Nickname ?? bot.GetHashCode().ToString();
        }

        public static bool HasISBGoalEnemy(BotOwner bot)
        {
            if (!IsAlive(bot) || bot.Memory == null)
                return false;
            var settings = bot.Memory.GoalEnemy?.Person?.Profile?.Info?.Settings;
            return settings != null && WildSpawnTypeExtensions.IsISB(settings.Role);
        }

        public static string GetGoalEnemyProfileId(BotOwner bot)
        {
            return IsAlive(bot) ? bot.Memory?.GoalEnemy?.ProfileId : null;
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;
            var type = instance.GetType();
            if (!Members.TryGetValue(type, out var readers))
                Members[type] = readers = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
            if (!readers.TryGetValue(memberName, out var member))
            {
                var property = type.GetProperty(memberName, MemberFlags);
                member = property != null && property.GetIndexParameters().Length == 0 ? (MemberInfo)property : type.GetField(memberName, MemberFlags);
                readers[memberName] = member;
            }

            return member is PropertyInfo prop ? prop.GetValue(instance, null) : (member as FieldInfo)?.GetValue(instance);
        }
    }
}
