using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ISBSSOFExtra
{
    internal sealed class TakeoverAlarmAudio : IDisposable
    {
        private UnityWebRequest _request;
        private bool _disposed;
        private AlarmLightPulse _pulse;
        internal AudioClip Clip { get; private set; }

        internal IEnumerator Load(string path)
        {
            if (_disposed || _request != null || !File.Exists(path))
                yield break;
            var analysis = Task.Run(() => AlarmLightPulse.Read(path));
            var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, AudioType.WAV);
            _request = request;
            ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = true;
            var operation = request.SendWebRequest();
            while (!_disposed && !operation.isDone)
            {
                if (_disposed)
                    yield break;
                yield return null;
            }

            if (_disposed || request.result != UnityWebRequest.Result.Success)
                yield break;
            while (!_disposed && !analysis.IsCompleted)
                yield return null;
            if (_disposed)
                yield break;
            if (analysis.Status == TaskStatus.RanToCompletion)
                _pulse = analysis.Result;
            else
            {
                var failure = analysis.Exception;
                Debug.LogError("[ISB] Unable to analyze takeover alarm audio: " + failure);
            }

            Clip = DownloadHandlerAudioClip.GetContent(request);
            if (Clip == null)
                yield break;
            Clip.name = Path.GetFileNameWithoutExtension(path);
            if (Clip.loadState == AudioDataLoadState.Unloaded)
                Clip.LoadAudioData();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Clip != null && Clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline)
            {
                if (_disposed)
                    yield break;
                yield return null;
            }
        }

        internal float Pulse(AudioSource source)
        {
            if (_disposed || _pulse == null || source == null || source.clip != Clip || !source.isPlaying || Clip.frequency <= 0)
                return 0f;
            return _pulse.Value(source.timeSamples / (double)Clip.frequency);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (Clip != null)
                UnityEngine.Object.Destroy(Clip);
            Clip = null;
            _pulse = null;
            if (_request == null)
                return;
            if (!_request.isDone)
                _request.Abort();
            _request.Dispose();
            _request = null;
        }
    }
}
