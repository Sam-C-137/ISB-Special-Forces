using System.Runtime.CompilerServices;
using EFT;
using UnityEngine;
#if SPT40
using NativeMemory = BotMemoryClass;
#else
using NativeMemory = EFT.BotMemory;
using DamageInfoStruct = EFT.Ballistics.DamageInfo;
#endif

namespace ISBSpecialForces.Behavior.ISB
{
    internal static class ISBThreatMemory
    {
        private sealed class ContactState
        {
            internal EnemyInfo Selected;
            internal float SelectedAt;
            internal IPlayer Attacker;
            internal Vector3 HitPosition;
            internal float HitTime = -10f;
            internal float NextSquadGoal;
        }

        private static readonly ConditionalWeakTable<BotOwner, ContactState> Contacts = new ConditionalWeakTable<BotOwner, ContactState>();
        private static bool Controls(BotOwner bot) => ISBRuntimeBindings.IsAuthority() && ISBNativeBrain.Owns(bot) && !bot.IsDead;

        internal static IPlayer Identity(BotOwner bot, IPlayer source)
        {
            if (source == null) return null;
            if (bot?.EnemiesController?.EnemyInfos != null)
            {
                if (bot.EnemiesController.EnemyInfos.TryGetValue(source, out var known)) return known.Person;
                foreach (var enemy in bot.EnemiesController.EnemyInfos.Values)
                    if (enemy.ProfileId == source.ProfileId) return enemy.Person;
            }
            return source.IsAI && source.AIData?.BotOwner != null ? source.AIData.BotOwner : source;
        }

        internal static void AddEnemyPrefix(NativeMemory __instance, ref IPlayer __0)
        {
#if SPT40
            var bot = __instance.BotOwner_0;
#else
            var bot = __instance._owner;
#endif
            if (Controls(bot) || ISBRuntimeBindings.IsAuthority() && ISBRuntimeBindings.IsEligible(bot)) __0 = Identity(bot, __0);
        }

        internal static void Heard(BotOwner bot, IPlayer source, Vector3 position)
        {
            source = Identity(bot, source);
            if (!Controls(bot) || source?.HealthController?.IsAlive != true || bot.BotsGroup?.IsPlayerEnemy(source) != true)
                return;
            if (!bot.EnemiesController.EnemyInfos.TryGetValue(source, out var enemy))
            {
                bot.BotsGroup.AddEnemy(source, EBotEnemyCause.addPlayer);
                if (!bot.EnemiesController.EnemyInfos.TryGetValue(source, out enemy)) return;
            }
            if (!enemy.IsVisible)
            {
                enemy.Distance = Vector3.Distance(bot.Position, position);
                enemy.GroupInfo.EnemyLastPosition = position;
                enemy.GroupInfo.IsLastPositionChecked = false;
                enemy.GroupInfo.EnemyLastSeenTimeSense = Time.time;
            }
            enemy.SetIgnoreState(false);
            bot.CalcGoal();
        }

        internal static void HitPostfix(NativeMemory __instance, DamageInfoStruct __0)
        {
#if SPT40
            var bot = __instance.BotOwner_0;
#else
            var bot = __instance._owner;
#endif
            var source = __0.Player?.iPlayer;
            source = Identity(bot, source);
            if (!Controls(bot) || source?.HealthController?.IsAlive != true || bot.BotsGroup?.IsPlayerEnemy(source) != true)
                return;
            var state = Contacts.GetValue(bot, _ => new ContactState());
            if (state.Attacker == source && Time.time - state.HitTime < 0.25f) return;
            var position = source.Position;
            float uncertainty = Vector3.Distance(bot.Position, position) / Mathf.Max(1f, bot.Settings.FileSettings.Mind.HIT_POINT_DETECTION);
            position.x += Random.Range(-uncertainty, uncertainty);
            position.z += Random.Range(-uncertainty, uncertainty);
            state.Attacker = source;
            state.HitPosition = position;
            state.HitTime = Time.time;
            Heard(bot, source, position);
            if (bot.EnemiesController.EnemyInfos.TryGetValue(source, out var enemy))
            {
                enemy.LastGetHitTime = Time.time;
                bot.CalcGoal();
            }
        }

        internal static float KnownAt(EnemyInfo enemy) => enemy == null ? 0f :
            Mathf.Max(enemy.PersonalLastSeenTime, Mathf.Max(enemy.LastGetHitTime,
                Mathf.Max(enemy.GroupInfo?.EnemyLastSeenTimeReal ?? 0f, enemy.GroupInfo?.EnemyLastSeenTimeSense ?? 0f)));

        private static bool ImmediateThreat(EnemyInfo enemy, float now) =>
            enemy?.Person?.HealthController?.IsAlive == true &&
            (enemy.IsVisible && enemy.Distance < 12f || enemy.LastGetHitTime > 0f && now - enemy.LastGetHitTime < 2f);

        private static EnemyInfo SharedEnemy(BotOwner bot, float now)
        {
            var contact = (bot.Brain?.BaseBrain as ISBNativeBrain)?.Squad?.Contact;
            if (contact?.Person?.HealthController?.IsAlive != true ||
                !contact.IsVisible && (KnownAt(contact) <= 0f || now - KnownAt(contact) > 20f) ||
                bot.BotsGroup?.IsPlayerEnemy(contact.Person) != true) return null;
            var person = Identity(bot, contact.Person);
            return bot.EnemiesController.EnemyInfos.TryGetValue(person, out var own) && !own.IgnoreUntilAggression ? own : null;
        }

        internal static void ShareContact(BotOwner bot, EnemyInfo contact, float now)
        {
            if (!Controls(bot) || contact?.Person?.HealthController?.IsAlive != true || bot.BotsGroup?.IsPlayerEnemy(contact.Person) != true)
                return;
            var person = Identity(bot, contact.Person);
            if (!bot.EnemiesController.EnemyInfos.TryGetValue(person, out var own))
            {
                bot.BotsGroup.AddEnemy(person, EBotEnemyCause.addPlayer);
                if (!bot.EnemiesController.EnemyInfos.TryGetValue(person, out own)) return;
            }
            if (own.GroupInfo != null && contact.GroupInfo != null && own.GroupInfo != contact.GroupInfo && !own.IsVisible)
            {
                own.GroupInfo.EnemyLastPosition = contact.EnemyLastPositionReal;
                own.GroupInfo.EnemyLastSeenTimeSense = Mathf.Max(own.GroupInfo.EnemyLastSeenTimeSense, KnownAt(contact));
                own.GroupInfo.IsLastPositionChecked = false;
            }
            own.SetIgnoreState(false);
            if (!own.IsVisible) own.Distance = Vector3.Distance(bot.Position, contact.EnemyLastPositionReal);
            var state = Contacts.GetValue(bot, _ => new ContactState());
            if (now < state.NextSquadGoal || bot.Memory?.GoalEnemy?.ProfileId == own.ProfileId || ImmediateThreat(bot.Memory?.GoalEnemy, now)) return;
            state.NextSquadGoal = now + 1f;
            bot.CalcGoal();
        }

        internal static void ChoosePostfix(BotEnemyChooser __instance, ref EnemyInfo __result)
        {
#if SPT40
            var bot = __instance.BotOwner_0;
#else
            var bot = __instance._owner;
#endif
            if (!Controls(bot)) return;
            var current = bot.Memory?.GoalEnemy;
            var state = Contacts.GetValue(bot, _ => new ContactState());
            var shared = SharedEnemy(bot, Time.time);
            bool focus = shared != null && !ImmediateThreat(__result, Time.time) && !ImmediateThreat(current, Time.time);
            if (focus) __result = shared;
            bool valid = current?.Person?.HealthController?.IsAlive == true && current.HaveSeen &&
                current.ShallKnowEnemy() && !current.IgnoreUntilAggression && bot.BotsGroup.IsPlayerEnemy(current.Person);
            if (!focus && valid && __result != current && __result != null && !__result.IsVisible && !current.IsVisible &&
                Time.time - __result.LastGetHitTime > 2f && Time.time - state.SelectedAt < 3f)
                __result = current;
            if (__result != state.Selected)
            {
                state.Selected = __result;
                state.SelectedAt = Time.time;
            }
        }

        internal static bool LookAtRecentAttacker(BotOwner bot)
        {
            if (!Contacts.TryGetValue(bot, out var state) || Time.time - state.HitTime > 2f ||
                state.Attacker?.HealthController?.IsAlive != true) return false;
            if (bot.Memory?.GoalEnemy?.IsVisible == true ||
                (bot.Brain?.BaseBrain as ISBNativeBrain)?.NeedsUrgentAction == true) return false;
            bot.Steering.LookToPoint(state.HitPosition + Vector3.up);
            return true;
        }
    }
}
