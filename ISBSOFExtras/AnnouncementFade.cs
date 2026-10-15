using UnityEngine;

namespace ISBSSOFExtra
{
    internal sealed class AnnouncementFade : MonoBehaviour
    {
        private AudioSource _source;
        private AudioClip _clip;
        private float _volume;
        internal static float Gain(float time, float length)
        {
            if (float.IsNaN(time) || float.IsInfinity(time) || float.IsNaN(length) || float.IsInfinity(length) || length <= 0f)
                return 0f;
            float duration = Mathf.Min(2f, length * .5f);
            return Mathf.Clamp01(Mathf.Min(time, length - time) / duration);
        }

        private static AnnouncementFade Find(AudioSource source)
        {
            foreach (var fade in source.GetComponents<AnnouncementFade>())
                if (fade._source == source)
                    return fade;
            return null;
        }

        internal static void Begin(AudioSource source, AudioClip clip, float volume, float offset)
        {
            if (source == null || clip == null)
                return;
            var fade = Find(source) ?? source.gameObject.AddComponent<AnnouncementFade>();
            fade._source = source;
            fade._clip = clip;
            fade._volume = volume;
            fade.enabled = true;
            source.volume = volume * Gain(offset, clip.length);
        }

        internal static float Volume(AudioSource source)
        {
            if (source == null)
                return 1f;
            var fade = Find(source);
            return fade != null && fade.enabled ? fade._volume : source.volume;
        }

        internal static void SetVolume(AudioSource source, float volume)
        {
            if (source == null)
                return;
            var fade = Find(source);
            if (fade != null && fade.enabled && fade._clip == source.clip)
            {
                fade._volume = volume;
                source.volume = volume * Gain(source.time, source.clip.length);
            }
            else
                source.volume = volume;
        }

        internal static void End(AudioSource source)
        {
            if (source == null)
                return;
            var fade = Find(source);
            if (fade == null || !fade.enabled)
                return;
            source.volume = fade._volume;
            fade.enabled = false;
            fade._clip = null;
        }

        private void LateUpdate()
        {
            if (_source == null)
            {
                enabled = false;
                return;
            }

            if (_source.clip != _clip || _clip == null)
            {
                End(_source);
                return;
            }

            if (_source.isPlaying)
                _source.volume = _volume * Gain(_source.time, _clip.length);
        }
    }
}
