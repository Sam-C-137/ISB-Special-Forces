using System;
using BepInEx.Configuration;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private sealed class ConfigUiOrder
        {
            public int Order;
        }

        private sealed class EventChance
        {
            public float chance;
        }

        private ConfigEntry<float> _containmentChance;
        private bool _readingContainmentChance;
        private float _chanceSaveAt = -1f;
        private float _savedContainmentChance;
        private void ConfigureContainmentChance()
        {
            _containmentChance = Config.Bind("USEC ISB Special Forces Blackout Config", "Containment Breach Event Chance", 15f, new ConfigDescription("Normal event chance for future raids. Saves the server JSON; active quest guarantees still take priority. Change before starting a raid. (Host Only)", new AcceptableValueRange<float>(0f, 100f), new ConfigUiOrder { Order = 99 }));
            try
            {
                var state = JsonConvert.DeserializeObject<EventChance>(RequestHandler.GetJson("/ISB/containment/chance"));
                if (state != null)
                    _containmentChance.Value = state.chance;
            }
            catch (Exception ex)
            {
                Log.LogWarning("ISB SOF: Cannot read ContainmentBreach chance: " + ex.Message);
            }

            _savedContainmentChance = _containmentChance.Value;
            _containmentChance.SettingChanged += OnContainmentChanceChanged;
        }

        private void OnContainmentChanceChanged(object sender, EventArgs args)
        {
            if (_readingContainmentChance)
                return;
            if (_raidisActive)
            {
                SetDisplayedContainmentChance(_savedContainmentChance);
                Log.LogWarning("ISB SOF: Change the ContainmentBreach chance from the menu before starting a raid.");
                return;
            }

            _chanceSaveAt = Time.realtimeSinceStartup + 0.5f;
        }

        private void SetDisplayedContainmentChance(float value)
        {
            _readingContainmentChance = true;
            try
            {
                _containmentChance.Value = value;
            }
            finally
            {
                _readingContainmentChance = false;
            }
        }

        private void TickContainmentChance()
        {
            if (_chanceSaveAt < 0f || Time.realtimeSinceStartup < _chanceSaveAt)
                return;
            _chanceSaveAt = -1f;
            if (_raidisActive)
            {
                SetDisplayedContainmentChance(_savedContainmentChance);
                return;
            }

            try
            {
                var state = JsonConvert.DeserializeObject<EventChance>(RequestHandler.PostJson("/ISB/containment/updateChance", JsonConvert.SerializeObject(new EventChance { chance = _containmentChance.Value })));
                if (state == null)
                    throw new InvalidOperationException("Server returned no event chance.");
                _savedContainmentChance = state.chance;
                SetDisplayedContainmentChance(state.chance);
                if (DiagnosticsEnabled)
                    Log.LogInfo("ISB SOF: ContainmentBreach chance saved to server JSON: " + state.chance + "%.");
            }
            catch (Exception ex)
            {
                SetDisplayedContainmentChance(_savedContainmentChance);
                Log.LogError("ISB SOF: Cannot save ContainmentBreach chance: " + ex.Message);
            }
        }
    }
}
