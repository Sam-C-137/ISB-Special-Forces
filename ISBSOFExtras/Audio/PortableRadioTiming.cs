using System;

namespace ISBSSOFExtra
{
    internal sealed class PortableRadioTiming
    {
        internal const float IdleInterval = 300f;
        internal const float OperatorInterval = 60f;
        internal bool Equipped { get; private set; }
        internal bool Playing { get; private set; }

        private float _lastTransmission;
        internal void SetEquipped(bool equipped, float now)
        {
            if (equipped && !Equipped)
                _lastTransmission = now;
            Equipped = equipped;
        }

        internal bool IsDue(float now, bool hasOperators)
        {
            return Equipped && !Playing && now - _lastTransmission >= (hasOperators ? OperatorInterval : IdleInterval);
        }

        internal void Started(float now)
        {
            _lastTransmission = now;
            Playing = true;
        }

        internal void Finished()
        {
            Playing = false;
        }

        internal static bool IsSpecialSlot(string id)
        {
            return string.Equals(id, "SpecialSlot1", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "SpecialSlot2", StringComparison.OrdinalIgnoreCase) || string.Equals(id, "SpecialSlot3", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsPortableCategory(string category)
        {
            return category == "PN" || category == "PNF" || category == "PWN" || category == "PWNF";
        }

        internal static string AudioCategory(string category)
        {
            return IsPortableCategory(category) ? category.Substring(1) : category;
        }
    }
}
