using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace ISBSSOFExtra
{
    internal static class OptionalBallisticsCompatibility
    {
        private static Harmony _harmony;
        private static bool _sainPatched, _hearingPatched, _reviewPatched;
        private static Func<object, object> _weapon, _ammo;
        private static Func<object, object> _weaponTemplate, _templateCaliber, _weaponCaliber;
        internal static void Enable(ManualLogSource log)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var helper = assembly.GetType("SAIN.Helpers.EnumValues", false);
                    var parse = helper == null ? null : AccessTools.Method(helper, "ParseCaliber", new[] { typeof(string) });
                    if (!_sainPatched && parse != null && parse.ReturnType.IsEnum && !Enum.IsDefined(parse.ReturnType, "Caliber58x42") && Enum.IsDefined(parse.ReturnType, "Caliber556x45NATO"))
                    {
                        var prefix = typeof(OptionalBallisticsCompatibility).GetMethod(nameof(ParseCaliber), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(parse.ReturnType);
                        if (_harmony == null)
                            _harmony = new Harmony("com.samc137.isbsofextras.optional-ballistics");
                        _harmony.Patch(parse, prefix: new HarmonyMethod(prefix));
                        _sainPatched = true;
                        log?.LogInfo("ISB compatibility: SAIN uses its 5.56 rifle category for 5.8x42; native ammo properties are preserved.");
                    }

                    var info = assembly.GetType("SAIN.SAINComponent.Classes.Info.WeaponInfo", false);
                    var hearing = info == null ? null : AccessTools.Method(info, "TryGetAmmoCaliber");
                    if (!_hearingPatched && hearing != null && hearing.ReturnType.IsEnum && !Enum.IsDefined(hearing.ReturnType, "Caliber58x42") && Enum.IsDefined(hearing.ReturnType, "Caliber556x45NATO"))
                    {
                        var nativeWeapon = hearing.GetParameters()[0].ParameterType;
                        var template = Member(nativeWeapon, "Template") as PropertyInfo;
                        _weaponTemplate = Getter(nativeWeapon, "Template");
                        _templateCaliber = template == null ? null : Getter(template.PropertyType, "ammoCaliber");
                        _weaponCaliber = Getter(nativeWeapon, "AmmoCaliber");
                        if (_weaponTemplate != null && _templateCaliber != null && _weaponCaliber != null)
                        {
                            var prefix = typeof(OptionalBallisticsCompatibility).GetMethod(nameof(HearingCaliber), BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(hearing.ReturnType);
                            if (_harmony == null)
                                _harmony = new Harmony("com.samc137.isbsofextras.optional-ballistics");
                            _harmony.Patch(hearing, prefix: new HarmonyMethod(prefix));
                            _hearingPatched = true;
                            log?.LogInfo("ISB compatibility: SAIN's native weapon hearing lookup accepts both 5.8x42 spellings.");
                        }
                    }

                    var review = assembly.GetType("RAID_REVIEW.RAID_REVIEW_ClientGameWorld_ShotDelegatePatch", false);
                    var shot = review == null ? null : AccessTools.Method(review, "PatchPostFix");
                    var parameters = shot?.GetParameters();
                    if (!_reviewPatched && parameters != null && parameters.Length == 2)
                    {
                        var bullet = parameters[1].ParameterType;
                        _weapon = Getter(bullet, "Weapon");
                        _ammo = Getter(bullet, "Ammo");
                        if (_weapon != null && _ammo != null)
                        {
                            if (_harmony == null)
                                _harmony = new Harmony("com.samc137.isbsofextras.optional-ballistics");
                            _harmony.Patch(shot, prefix: new HarmonyMethod(typeof(OptionalBallisticsCompatibility), nameof(CompleteShot)));
                            _reviewPatched = true;
                            log?.LogInfo("ISB compatibility: RaidReview skips incomplete ballistic records and retains valid shots.");
                        }
                    }
                }
                catch (Exception error)
                {
                    log?.LogWarning("ISB optional ballistic compatibility unavailable for " + assembly.GetName().Name + ": " + error.GetBaseException().Message);
                }
            }
        }

        private static MemberInfo Member(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var owner = type; owner != null; owner = owner.BaseType)
            {
                var member = (MemberInfo)owner.GetProperty(name, flags) ?? owner.GetField(name, flags);
                if (member != null)
                    return member;
            }

            return null;
        }

        private static Func<object, object> Getter(Type type, string name)
        {
            var member = Member(type, name);
            if (member == null)
                return null;
            var input = Expression.Parameter(typeof(object), "shot");
            var owner = Expression.Convert(input, type);
            var read = member is PropertyInfo property ? (Expression)Expression.Property(owner, property) : Expression.Field(owner, (FieldInfo)member);
            return Expression.Lambda<Func<object, object>>(Expression.Convert(read, typeof(object)), input).Compile();
        }

        private static bool ParseCaliber<T>(string __0, ref T __result)
            where T : struct
        {
            if (__0 != "Caliber58x42")
                return true;
            __result = CaliberCategory<T>.FiveEight;
            return false;
        }

        private static class CaliberCategory<T>
            where T : struct
        {
            internal static readonly T FiveEight = (T)Enum.Parse(typeof(T), "Caliber556x45NATO");
        }

        private static bool HearingCaliber<T>(object __0, ref T __result)
            where T : struct
        {
            if (__0 == null)
                return true;
            var template = _weaponTemplate(__0);
            var caliber = template == null ? null : _templateCaliber(template) as string;
            if (caliber != "Caliber58x42" && caliber != "58x42")
                caliber = _weaponCaliber(__0) as string;
            if (caliber != "Caliber58x42" && caliber != "58x42")
                return true;
            __result = CaliberCategory<T>.FiveEight;
            return false;
        }

        private static bool CompleteShot(object __0, object __1)
        {
            return __0 != null && __1 != null && _weapon(__1) != null && _ammo(__1) != null;
        }

        internal static void Disable()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            _weapon = _ammo = _weaponTemplate = _templateCaliber = _weaponCaliber = null;
            _sainPatched = _hearingPatched = _reviewPatched = false;
        }
    }
}
