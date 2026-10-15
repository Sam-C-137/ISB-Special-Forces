using EFT.Communications;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using NativeNotification = EFT.Communications.CustomNotification;

namespace ISBInfo
{
    internal sealed class AnnouncementEffectNotification : NativeNotification
    {
        private const float AnimationSpeed = 0.25f;
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly AccessTools.FieldRef<BaseNotificationView, Animator> GetAnimator = AccessTools.FieldRefAccess<BaseNotificationView, Animator>("_animator");
        internal AnnouncementEffectNotification(string text) : base(text, ENotificationDurationType.Default, ENotificationIconType.Default, null)
        {
        }

        public override BaseNotificationView CreateView(INotificationViewFactory factory)
        {
            var view = base.CreateView(factory);
            var animator = GetAnimator(view);
            if (animator != null)
                animator.SetFloat(SpeedParameter, AnimationSpeed);
            return view;
        }
    }
}
