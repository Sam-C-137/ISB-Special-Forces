using System;
using System.Collections.Generic;

namespace ISBSSOFExtra
{
    internal sealed class AuthorizationQuestActions
    {
        private static readonly string[] Targets =
        {
            "isb_cb_activated_gate_a",
            "isb_cb_activated_gate_b",
            "isb_cb_activated_gate_c",
            "isb_cb_activated_power_a",
            "isb_cb_activated_power_b",
            "isb_cb_activated_power_c",
            "isb_cb_activated_power_d",
            "isb_cb_operated_alarm",
            "isb_cb_operated_lights",
            "isb_cb_repaired_reader",
            "isb_cb_operated_gates",
            "isb_cb_operated_power_restored",
            "isb_cb_office_codes",
            "isb_cb_office_shutdown",
            "isb_cb_muted_map_announcements",
            "isb_cb_muted_decon_announcements"
        };
        internal int Mask { get; private set; }

        private int _reported;
        internal static int Effect(string action)
        {
            switch (action)
            {
                case "GATE_A":
                    return 1;
                case "GATE_B":
                    return 2;
                case "GATE_C":
                    return 4;
                case "POWER_A":
                case "SHUTDOWN_A":
                    return 8;
                case "POWER_B":
                case "SHUTDOWN_B":
                    return 16;
                case "POWER_C":
                case "SHUTDOWN_C":
                    return 32;
                case "POWER_D":
                case "SHUTDOWN_D":
                    return 64;
                case "ALARM":
                    return 128;
                case "EMERGENCY_LIGHTS":
                    return 256;
                case "READER_REPAIR":
                    return 512;
                case "GATES":
                    return 1024;
                case "MASTER":
                    return 2048;
                case "OFFICE_CODE":
                    return 4096;
                case "OFFICE_SHUTDOWN":
                    return 8192;
                case "MAP_ANNOUNCEMENTS":
                    return 16384;
                case "DECON_ANNOUNCEMENTS":
                    return 32768;
                default:
                    return 0;
            }
        }

        internal bool Merge(int mask)
        {
            if (mask < 0 || mask > 65535)
                return false;
            int previous = Mask;
            Mask |= mask;
            return previous != Mask;
        }

        internal IEnumerable<KeyValuePair<int, string>> Pending()
        {
            for (int i = 0; i < Targets.Length; i++)
            {
                int bit = 1 << i;
                if ((Mask & bit) != 0 && (_reported & bit) == 0)
                    yield return new KeyValuePair<int, string>(bit, Targets[i]);
            }
        }

        internal void Reported(int bit) => _reported |= bit & Mask;
        internal void Clear()
        {
            Mask = 0;
            _reported = 0;
        }
    }
}
