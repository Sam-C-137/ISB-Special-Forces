using System.Collections.Generic;
using EFT;
using EFT.Interactive;
using UnityEngine;

namespace ISBSSOFExtra
{
    internal sealed class CorpseLightBudget
    {
        private sealed class Entry
        {
            internal Light Light;
            internal Transform Corpse;
            internal bool Enabled;
        }

        private readonly HashSet<Corpse> _known = new HashSet<Corpse>();
        private readonly List<Corpse> _removed = new List<Corpse>();
        private readonly List<Light> _scratch = new List<Light>();
        private readonly List<Entry> _lights = new List<Entry>();
        private float _nextScan;
        internal void Tick(GameWorld world, float now)
        {
            if (world == null || world.LootList == null || now < _nextScan)
                return;
            _nextScan = now + .5f;
            for (int i = 0; i < world.LootList.Count; i++)
            {
                var corpse = world.LootList[i] as Corpse;
                if (corpse == null || !_known.Add(corpse))
                    continue;
                corpse.GetComponentsInChildren(true, _scratch);
                for (int j = 0; j < _scratch.Count; j++)
                {
                    var light = _scratch[j];
                    if (light != null)
                        _lights.Add(new Entry { Light = light, Corpse = corpse.transform, Enabled = light.enabled });
                }

                _scratch.Clear();
            }

            for (int i = _lights.Count - 1; i >= 0; i--)
            {
                var entry = _lights[i];
                if (entry.Light == null)
                {
                    _lights.RemoveAt(i);
                    continue;
                }

                if (entry.Corpse == null || !entry.Light.transform.IsChildOf(entry.Corpse))
                {
                    if (entry.Enabled)
                        entry.Light.enabled = true;
                    _lights.RemoveAt(i);
                    continue;
                }

                entry.Enabled |= entry.Light.enabled;
                if (entry.Light.enabled)
                    entry.Light.enabled = false;
            }

            foreach (var corpse in _known)
                if (corpse == null)
                    _removed.Add(corpse);
            for (int i = 0; i < _removed.Count; i++)
                _known.Remove(_removed[i]);
            _removed.Clear();
        }

        internal void Clear()
        {
            for (int i = 0; i < _lights.Count; i++)
                if (_lights[i].Light != null && _lights[i].Enabled)
                    _lights[i].Light.enabled = true;
            _lights.Clear();
            _known.Clear();
            _removed.Clear();
            _scratch.Clear();
            _nextScan = 0f;
        }
    }
}
