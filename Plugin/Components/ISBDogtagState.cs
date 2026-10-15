using Newtonsoft.Json;
using SPT.Common.Http;
using System;
using System.Collections.Generic;

namespace ISBSpecialForces.Components
{
    public class ISBDogtagConfigResponse
    {
        public bool enabled { get; set; }
        public string displaySide { get; set; }
        public Dictionary<string, string> dogtagTplByWildType { get; set; } = new Dictionary<string, string>();
    }

    public static class ISBDogtagState
    {
        private static readonly HashSet<string> DogtagTemplates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static DateTime _nextLoadAttemptUtc = DateTime.MinValue;
        private static bool _loaded;
        public static bool Enabled { get; private set; }
        public static string DisplaySide { get; private set; } = "ISB";

        public static void Initialize()
        {
            if (_loaded || DateTime.UtcNow < _nextLoadAttemptUtc)
                return;
            _nextLoadAttemptUtc = DateTime.UtcNow.AddSeconds(5);
            try
            {
                var json = RequestHandler.GetJson("/ISB/dogtags");
                if (string.IsNullOrWhiteSpace(json))
                    return;
                var response = JsonConvert.DeserializeObject<ISBDogtagConfigResponse>(json);
                if (response == null)
                    return;
                DogtagTemplates.Clear();
                Enabled = response.enabled;
                DisplaySide = string.IsNullOrWhiteSpace(response.displaySide) ? "ISB" : response.displaySide.Trim();
                if (response.dogtagTplByWildType != null)
                {
                    foreach (var templateId in response.dogtagTplByWildType.Values)
                    {
                        if (!string.IsNullOrWhiteSpace(templateId))
                            DogtagTemplates.Add(templateId.Trim());
                    }
                }

                _loaded = true;
            }
            catch (Exception)
            {
            }
        }

        public static bool ShouldDisplayAsISB(string templateId)
        {
            if (!_loaded)
                Initialize();
            return Enabled && !string.IsNullOrWhiteSpace(templateId) && DogtagTemplates.Contains(templateId);
        }
    }
}
