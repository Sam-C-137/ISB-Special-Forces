using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public sealed class ISBAishiReputationWorker : MonoBehaviour
    {
        private const int MaximumActionsPerFrame = 16;
        private static readonly ConcurrentQueue<Action> PendingActions = new ConcurrentQueue<Action>();

        public static void Enqueue(Action action)
        {
            if (action != null)
                PendingActions.Enqueue(action);
        }

        private void Update()
        {
            var processed = 0;

            while (processed < MaximumActionsPerFrame && PendingActions.TryDequeue(out var action))
            {
                processed++;

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Plugin.LogSource?.LogError(
                        $"[ISB AISHI LOYALTY] Deferred reputation action failed: {ex}");
                }
            }
        }

        private void OnDestroy()
        {
            while (PendingActions.TryDequeue(out _))
            {
            }
        }
    }
}
