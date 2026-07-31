using Comfort.Common;
using EFT;
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public static class ISBSecretHuntActivator
    {
        public static void Activate(string triggerId, float delaySeconds)
        {
            GameObject host = new GameObject("ISBSecretHuntActivator_" + triggerId);
            ISBSecretHuntRunner runner = host.AddComponent<ISBSecretHuntRunner>();
            runner.Run(triggerId, delaySeconds);
        }
    }

    internal class ISBSecretHuntRunner : MonoBehaviour
    {
        public void Run(string triggerId, float delaySeconds)
        {
            Plugin.LogSource.LogInfo("[ISB SECRET] Wave '" + triggerId + "' activation scheduled in " + delaySeconds + "s.");
            StartCoroutine(ActivateAfterDelay(triggerId, delaySeconds));
        }

        private IEnumerator ActivateAfterDelay(string triggerId, float delaySeconds)
        {
            yield return new WaitForSeconds(delaySeconds);

            LocalGame localGame = Singleton<AbstractGame>.Instance as LocalGame;
            if (localGame == null)
            {
                Plugin.LogSource.LogWarning("[ISB SECRET] LocalGame unavailable, aborting activation of '" + triggerId + "'.");
                Destroy(gameObject);
                yield break;
            }

            BossLocationSpawn wave = localGame.Location_0.BossLocationSpawn.FirstOrDefault(w => w.TriggerId == triggerId);
            if (wave == null)
            {
                Plugin.LogSource.LogWarning("[ISB SECRET] No armed wave with TriggerId '" + triggerId + "' found in location data.");
                Destroy(gameObject);
                yield break;
            }

            IBotGame botGame = Singleton<IBotGame>.Instance;
            if (botGame == null || botGame.BotsController == null)
            {
                Plugin.LogSource.LogWarning("[ISB SECRET] BotsController unavailable, aborting activation of '" + triggerId + "'.");
                Destroy(gameObject);
                yield break;
            }

            botGame.BotsController.ActivateBotsByWave(wave);
            Plugin.LogSource.LogInfo("[ISB SECRET] Wave '" + triggerId + "' activated, spawning in zone '" + wave.BossZone + "'.");
            Destroy(gameObject);
        }
    }
}
