using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using EFT;
using EFT.Interactive;
using ISB.Compatibility;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ISBSSOFExtra
{
    internal sealed partial class LabsOfficeScene : IDisposable
    {
        private readonly ManualLogSource _log;
        private readonly Action<string, int> _submit;
        private readonly Action _shutdown;
        private EFT.Interactive.Switch _keypadDoor, _shutdownButton;
        private GameWorld _world;
        private OfficeAccessState _state;
        private GameObject _readerModel;
        internal Vector3 KeypadPosition => _keypadDoor != null ? _keypadDoor.transform.position : Point(_state.config.keypad);

        private readonly List<Texture2D> _textures = new List<Texture2D>();
        private float _nextSetup, _pendingUntil, _keypadResultUntil;
        private int _setupFailures;
        private bool _open, _pending, _keypadGranted, _reported, _terminal;
        internal Func<string> TerminalStatus;
        internal Func<string> GeneratorStatus;
        internal Func<string, bool> ControlState;
        internal void CommandResult(bool success)
        {
            PlayKeypadSound(success ? "granted" : "denied");
        }

        internal Func<double> TerminalClock;
        internal Func<bool> BootReady;
        internal Action<string> TerminalCommand;
        private int _terminalPage;
        private float _shutdownPendingUntil;
        private string _keypadEntry = "";
        private string _raidCode => _state.raidCode;

        private CursorLockMode _previousCursor;
        private bool _previousVisible;
        internal LabsOfficeScene(ManualLogSource log, Action<string, int> submit, Action shutdown)
        {
            _log = log;
            _submit = submit;
            _shutdown = shutdown;
        }

        internal void Tick(GameWorld world, OfficeAccessState state)
        {
            _world = world;
            _state = state;
            if (state?.IsValid() != true || world == null)
            {
                CloseKeypad();
                return;
            }

            var player = world.MainPlayer;
            if (_open && (player == null || player.HealthController?.IsAlive != true || _keypadDoor == null || (player.Position - (_terminal ? Point(state.config.shutdownButton) : KeypadPosition)).sqrMagnitude > 16f || state.DestructionActive))
                CloseKeypad();
            if (_pending && Time.unscaledTime >= _pendingUntil)
                Result(false);
            if (_keypadDoor != null)
                _keypadDoor.Operatable = state.stage < 2 && !state.DestructionActive && !_open;
            if (_shutdownButton != null)
                _shutdownButton.Operatable = state.ConsoleReady(TerminalClock?.Invoke() ?? 0d) && (BootReady?.Invoke() ?? true) && !state.DestructionActive && !_open;
            if (Time.unscaledTime < _nextSetup)
                return;
            _nextSetup = Time.unscaledTime + (_setupFailures < 10 ? 3f : 30f);
            try
            {
                if (_keypadDoor == null)
                {
                    _keypadDoor = LabsTakeoverInfrastructure.CreateEmergencySwitch("ISB_Office_Keypad", Text("Keypad", "Enter access codes"), Point(state.config.keypad));
                    if (_keypadDoor != null)
                    {
                        _keypadDoor.transform.rotation = Quaternion.Euler(0f, state.config.keypad.rotationY, 0f);
                        LabsTakeoverInfrastructure.RegisterEmergencyAction(_keypadDoor, OpenKeypad);
                    }
                }

                if (state.stage >= 1 && _shutdownButton == null)
                {
                    _shutdownButton = LabsTakeoverInfrastructure.CreateEmergencySwitch("ISB_Office_StopDecontamination", Text("Terminal", "Access C.A.S.S.I.E. terminal"), Point(state.config.shutdownButton));
                    if (_shutdownButton != null)
                        LabsTakeoverInfrastructure.RegisterEmergencyAction(_shutdownButton, ShutdownPressed);
                }

                if (_keypadDoor != null && _readerModel == null)
                    CreateReaderModel();
            }
            catch (Exception ex)
            {
                if (!_reported)
                    _log.LogError("ISB: office controls or code board could not be prepared: " + ex.Message);
                _reported = true;
                _setupFailures++;
            }
        }

        internal static Vector3 Point(OfficePoint point) => new Vector3(point.x, point.y, point.z);
        private static string Text(string key, string fallback) => ISBLocalizedText.Get("ISB/Office/" + key, fallback);
        private void OpenKeypad()
        {
            if (_state == null || _state.stage >= 2 || _state.DestructionActive || _open || _world?.MainPlayer?.HealthController?.IsAlive != true || (_world.MainPlayer.Position - KeypadPosition).sqrMagnitude > 16f)
                return;
            _terminal = false;
            _keypadEntry = "";
            _pending = false;
            _keypadGranted = false;
            _keypadResultUntil = 0f;
            _previousCursor = Cursor.lockState;
            _previousVisible = Cursor.visible;
            _open = true;
            _keypadDoor.Operatable = false;
            GamePlayerOwner.SetIgnoreInput(true);
            Application.onBeforeRender += EnforceKeypadCursor;
        }

        private void ShutdownPressed()
        {
            if (_open || !(BootReady?.Invoke() ?? true) || _state == null || !_state.ConsoleReady(TerminalClock?.Invoke() ?? 0d) || _state.DestructionActive || _world?.MainPlayer?.HealthController?.IsAlive != true || (_world.MainPlayer.Position - Point(_state.config.shutdownButton)).sqrMagnitude > 16f)
                return;
            _previousCursor = Cursor.lockState;
            _previousVisible = Cursor.visible;
            _terminal = true;
            _open = true;
            _terminalPage = 0;
            TerminalCommand?.Invoke("VIEW");
            if (_shutdownButton != null)
                _shutdownButton.Operatable = false;
            GamePlayerOwner.SetIgnoreInput(true);
            Application.onBeforeRender += EnforceKeypadCursor;
        }

        private bool _emergencyNotice;
        private void ConfirmTerminalShutdown()
        {
            if (!_open || !_terminal || _state == null || _state.DestructionActive || Time.unscaledTime < _shutdownPendingUntil)
                return;
            if (_state.stage < 2)
            {
                _emergencyNotice = true;
                return;
            }

            _shutdownPendingUntil = Time.unscaledTime + .2f;
            _shutdown();
        }

        private void SendTerminalCommand(string action)
        {
            if (!_open || !_terminal || _state == null || _state.DestructionActive || Time.unscaledTime < _shutdownPendingUntil)
                return;
            _shutdownPendingUntil = Time.unscaledTime + .2f;
            TerminalCommand?.Invoke(action);
        }

        private void CloseKeypad()
        {
            if (!_open)
                return;
            _open = false;
            _pending = false;
            _terminal = false;
            if (_keypadDoor != null)
                _keypadDoor.Operatable = _state != null && _state.stage < 2 && !_state.DestructionActive;
            Application.onBeforeRender -= EnforceKeypadCursor;
            GamePlayerOwner.SetIgnoreInput(false);
            Cursor.lockState = _previousCursor;
            Cursor.visible = _previousVisible;
        }

        private void EnforceKeypadCursor()
        {
            if (_open)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void SubmitKeypad()
        {
            if (!_open || _pending || _keypadEntry.Length != 4 || Time.unscaledTime < _keypadResultUntil)
                return;
            _pending = true;
            _pendingUntil = Time.unscaledTime + 5f;
            _submit(_keypadEntry, _state.stage);
        }

        internal void Result(bool accepted)
        {
            if (!_open || !_pending)
                return;
            PlayKeypadSound(accepted ? "granted" : "denied");
            _pending = false;
            _keypadGranted = accepted;
            _keypadResultUntil = Time.unscaledTime + .8f;
            _keypadEntry = "";
        }

        internal void Draw()
        {
            if (!_open)
                return;
            if (_terminal)
            {
                TerminalGui();
                return;
            }

            if (_keypadGranted && Time.unscaledTime >= _keypadResultUntil && _state.stage >= 1)
            {
                CloseKeypad();
                return;
            }

            bool enabled = GUI.enabled;
            Color color = GUI.color;
            try
            {
                GUI.enabled = enabled && !_pending;
                KeypadGui();
            }
            finally
            {
                GUI.enabled = enabled;
                GUI.color = color;
            }
        }

        private Texture2D LoadEmbeddedTexture(string resource)
        {
            using (var stream = typeof(LabsOfficeScene).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                    throw new InvalidOperationException("Office texture is missing: " + resource);
                using (var bytes = new MemoryStream())
                {
                    stream.CopyTo(bytes);
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!ImageConversion.LoadImage(texture, bytes.ToArray()))
                    {
                        Object.Destroy(texture);
                        throw new InvalidOperationException(resource);
                    }

                    texture.name = resource;
                    _textures.Add(texture);
                    return texture;
                }
            }
        }

        public void Dispose()
        {
            CloseKeypad();
            if (_keypadDoor != null)
            {
                LabsTakeoverInfrastructure.UnregisterEmergencyAction(_keypadDoor);
                Object.Destroy(_keypadDoor.gameObject);
            }

            if (_shutdownButton != null)
            {
                LabsTakeoverInfrastructure.UnregisterEmergencyAction(_shutdownButton);
                Object.Destroy(_shutdownButton.gameObject);
            }

            DisposeTerminal();
            DisposeKeypadModel();
            foreach (var texture in _textures)
                Object.Destroy(texture);
            _textures.Clear();
        }
    }
}
