using EFT;
using System;
using System.Reflection;

namespace ISBSpecialForces.Components
{
    internal static class ISBBlackDivisionUtility
    {
        private static readonly BindingFlags MemberFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
            return (int)role == 848420
                || string.Equals(role.ToString(), "blackDivAssault", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsAlive(BotOwner bot)
        {
            return bot != null
                && !bot.IsDead
                && bot.GetPlayer != null
                && bot.GetPlayer.HealthController != null
                && bot.GetPlayer.HealthController.IsAlive;
        }


        public static BotOwner TryResolveBotOwner(object player)
        {
            if (player == null)
                return null;

            if (player is BotOwner owner)
                return owner;

            try
            {
                var directOwner = GetMemberValue(player, "BotOwner") as BotOwner;

                if (directOwner != null)
                    return directOwner;

                var aiData = GetMemberValue(player, "AIData");
                return GetMemberValue(aiData, "BotOwner") as BotOwner
                    ?? GetMemberValue(aiData, "Owner") as BotOwner;
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

            return bot.Profile?.Id
                ?? bot.Profile?.Nickname
                ?? bot.GetHashCode().ToString();
        }

        public static bool HasISBGoalEnemy(BotOwner bot)
        {
            if (!IsAlive(bot) || bot.Memory == null)
                return false;

            try
            {
                var enemy = GetMemberValue(bot.Memory, "GoalEnemy");
                var person = GetMemberValue(enemy, "Person");
                var profile = GetMemberValue(person, "Profile");
                var info = GetMemberValue(profile, "Info");
                var settings = GetMemberValue(info, "Settings");
                var roleValue = GetMemberValue(settings, "Role");

                if (roleValue is WildSpawnType role)
                    return WildSpawnTypeExtensions.IsISB(role);

                if (roleValue != null && int.TryParse(roleValue.ToString(), out var roleId))
                    return WildSpawnTypeExtensions.IsISB((WildSpawnType)roleId);
            }
            catch
            {
                
            }

            return false;
        }

        public static string GetGoalEnemyProfileId(BotOwner bot)
        {
            if (!IsAlive(bot) || bot.Memory == null)
                return null;

            try
            {
                var enemy = GetMemberValue(bot.Memory, "GoalEnemy");
                var person = GetMemberValue(enemy, "Person");
                var profile = GetMemberValue(person, "Profile");
                var id = GetMemberValue(profile, "Id");
                return id?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static object GetMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrEmpty(memberName))
                return null;

            var type = instance.GetType();
            var property = type.GetProperty(memberName, MemberFlags);

            if (property != null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance, null);

            var field = type.GetField(memberName, MemberFlags);
            return field?.GetValue(instance);
        }
    }
}
