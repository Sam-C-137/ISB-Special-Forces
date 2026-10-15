using EFT;
using System;
using System.Globalization;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private readonly AuthorizationQuestActions _authorizationQuestActions = new AuthorizationQuestActions();
        private bool _authorizationQuestErrorReported;
        private void ConfirmSharedAuthorizationEffect(string action)
        {
            if (!IsEventAuthority || !_raidisActive || !IsLaboratoryPresentationAllowed())
                return;
            bool changed = _authorizationQuestActions.Merge(AuthorizationQuestActions.Effect(action));
            RecordSharedAuthorizationEffects();
            if (changed && IsFikaServer)
                NetworkBridge.Broadcast("QE|" + _authorizationQuestActions.Mask.ToString(CultureInfo.InvariantCulture));
        }

        private void ReceiveSharedAuthorizationEffects(string value)
        {
            if (IsEventAuthority || !IsFikaNetworkSession || !_raidisActive || !IsLaboratoryPresentationAllowed())
                return;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mask) || mask < 0 || mask > 65535)
                return;
            _authorizationQuestActions.Merge(mask);
            RecordSharedAuthorizationEffects();
        }

        private void RecordSharedAuthorizationEffects()
        {
            if (!_raidisActive || !CanPresentLocally || !IsLaboratoryPresentationAllowed())
                return;
            Player player = _activeRaidWorld?.MainPlayer;
            if (player == null || player.IsAI || player.HealthController?.IsAlive != true)
                return;
            foreach (var effect in _authorizationQuestActions.Pending())
            {
                try
                {
                    player.SpecialPlaceVisited(effect.Value, 0);
                    _authorizationQuestActions.Reported(effect.Key);
                }
                catch (Exception ex)
                {
                    if (_authorizationQuestErrorReported)
                        continue;
                    _authorizationQuestErrorReported = true;
                    Log.LogError("ISB: could not record shared quest effect: " + ex.Message);
                }
            }
        }
    }
}
