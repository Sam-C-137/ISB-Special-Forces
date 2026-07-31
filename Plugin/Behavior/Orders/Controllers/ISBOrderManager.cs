using EFT;
using ISBSpecialForces.Behavior.Orders.State;
using UnityEngine;

namespace ISBSpecialForces.Behavior.Orders.Controllers
{
    public sealed class ISBOrderManager
        : MonoBehaviourSingleton<ISBOrderManager>
    {
        private float _nextUpdateTime;

        public void ResetRaid()
        {
            ISBOrderSquadRegistry.Clear();
            _nextUpdateTime = 0f;
        }

        private void Update()
        {
            if (!ISBOrderSettings.Enabled)
                return;

            if (Time.time < _nextUpdateTime)
                return;

            _nextUpdateTime = Time.time + ISBOrderSettings.Current.updateInterval;
            ISBOrderSquadRegistry.UpdateAll();
        }
    }
}
