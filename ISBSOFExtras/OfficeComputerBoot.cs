using System;
using System.IO;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed class OfficeComputerBoot : IDisposable
    {
        private readonly GameObject _root;
        private readonly VideoPlayer _video;
        private readonly RenderTexture _frame;
        private readonly AudioSource _sound;
        private bool _started, _failed, _endReached, _disposed;
        private readonly ManualLogSource _log;
        private float _finishedAt = -1f;
        private readonly float _prepareAt = Time.realtimeSinceStartup;
        internal bool Complete => _finishedAt >= 0f && Time.realtimeSinceStartup >= _finishedAt + 3f;
        internal bool VideoFinished => _finishedAt >= 0f;
        internal float LoadingProgress => VideoFinished ? Mathf.Clamp01((Time.realtimeSinceStartup - _finishedAt) / 3f) : 0f;
        internal Texture Frame => !_disposed && _video != null && _video.isPrepared && !_failed ? _frame : null;

        internal OfficeComputerBoot(string path, ManualLogSource log)
        {
            _log = log;
            try
            {
                _root = new GameObject("ISB_OfficeComputerStartup");
                _root.transform.position = new Vector3(-266.547f, 4.936f, -375.567f);
                _frame = DecontaminationScreenFrame.CreateTexture(1280, 720, "ISB_ComputerStartup");
                _sound = _root.AddComponent<AudioSource>();
                _sound.playOnAwake = false;
                _sound.spatialBlend = 1f;
                _sound.minDistance = 1f;
                _sound.maxDistance = 12f;
                _sound.dopplerLevel = 0f;
                _video = _root.AddComponent<VideoPlayer>();
                _video.playOnAwake = false;
                _video.source = VideoSource.Url;
                _video.url = new Uri(path).AbsoluteUri;
                _video.renderMode = VideoRenderMode.RenderTexture;
                _video.targetTexture = _frame;
                _video.audioOutputMode = VideoAudioOutputMode.AudioSource;
                _video.controlledAudioTrackCount = 1;
                _video.SetTargetAudioSource(0, _sound);
                _video.EnableAudioTrack(0, true);
                _video.isLooping = false;
                _video.skipOnDrop = false;
                _video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
                _video.waitForFirstFrame = true;
                _video.loopPointReached += OnFinished;
                _video.errorReceived += OnError;
                _video.Prepare();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Tick(double elapsed)
        {
            if (_disposed || _finishedAt >= 0f)
                return;
            if (_failed || (!_video.isPrepared && Time.realtimeSinceStartup - _prepareAt > 60f))
            {
                _finishedAt = Time.realtimeSinceStartup;
                return;
            }

            if (!_video.isPrepared || elapsed < 0d)
                return;
            if (!_started)
            {
                _video.Play();
                _started = true;
            }

            if (CanFinishVideo(_endReached, _video.length, _video.time + (_video.frameRate > 0f ? 1d / _video.frameRate : 0d)))
                _finishedAt = Time.realtimeSinceStartup;
        }

        internal static bool CanFinishVideo(bool reachedEnd, double duration, double playedFor) => reachedEnd && duration > 0d && !double.IsNaN(duration) && !double.IsInfinity(duration) && playedFor + .01d >= duration;
        private void OnFinished(VideoPlayer player) => _endReached = true;
        private void OnError(VideoPlayer player, string error)
        {
            if (!_failed)
                _log?.LogError("ISB: computer startup video failed: " + error);
            _failed = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_video != null)
            {
                _video.loopPointReached -= OnFinished;
                _video.errorReceived -= OnError;
                _video.Stop();
                _video.targetTexture = null;
            }

            if (_sound != null)
                _sound.Stop();
            if (_root != null)
            {
                _root.SetActive(false);
                Object.Destroy(_root);
            }

            DecontaminationScreenFrame.Release(_frame);
        }
    }
}
