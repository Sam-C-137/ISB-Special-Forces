using System;
using System.IO;
using BepInEx.Logging;
using Newtonsoft.Json;

namespace ISBSSOFExtra
{
    internal sealed class TakeoverScenerySettings
    {
        private readonly string _firePath, _sirensPath;
        private readonly ManualLogSource _log;
        internal TakeoverScenerySettings(string firePath, string sirensPath, ManualLogSource log)
        {
            _firePath = firePath;
            _sirensPath = sirensPath;
            _log = log;
        }

        internal void ApplyPresentation(TakeoverFire fire, TakeoverSirens sirens)
        {
            var flames = Read<TakeoverFireDefinition>(_firePath);
            var lights = Read<TakeoverSirensDefinition>(_sirensPath);
            lights.Normalize();
            fire.SetPresentation(flames.maxVisibleFires, flames.visualDistance, flames.maxFireLights, flames.lightDistance, flames.particleDensity, flames.showSmoke);
            sirens.SetPresentation(lights.maxLitSirens, lights.lightActivationDistance, lights.rotationSpeed, lights.spotIntensity, lights.spotRange, lights.pointIntensity, lights.pointRange);
        }

        private T Read<T>(string path)
            where T : class, new()
        {
            try
            {
                if (File.Exists(path))
                    return JsonConvert.DeserializeObject<T>(File.ReadAllText(path)) ?? new T();
            }
            catch (Exception ex)
            {
                _log.LogError("ISB SOF: Cannot read scenery settings from " + path + ": " + ex.Message);
            }

            return new T();
        }
    }
}
