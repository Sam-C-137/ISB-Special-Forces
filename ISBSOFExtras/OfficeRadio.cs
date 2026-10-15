using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ISBSSOFExtra
{
    public partial class ISBSpecialForcesAudio
    {
        private static readonly Vector3 OfficeRadioPosition = new Vector3(-265.8273f, 4.9565f, -374.303f);
        private GameObject _officeRadio;
        private EFT.Interactive.Switch _officeRadioSwitch;
        private AssetBundle _officeRadioBundle;
        private AudioSource _officeRadioAudio;
        private Coroutine _officeRadioLoad;
        private bool _officeRadioTried, _officeRadioPlayed, _officeRadioOwnsBundle;
        private float _officeRadioNextCheck;
        private void TickOfficeRadio()
        {
            if (!_raidisActive || !_labsPreBlackoutEventActive || _officeAccess?.IsValid() != true)
                return;
            if (IsEventAuthority && !_officeAccess.radioOff && _officeAccess.radioStartedAt < 0d && Time.realtimeSinceStartup >= _officeRadioNextCheck)
            {
                _officeRadioNextCheck = Time.realtimeSinceStartup + .2f;
                if (_activeRaidWorld != null)
                    foreach (var player in _activeRaidWorld.AllAlivePlayersList)
                        if (player != null && !player.IsAI && player.HealthController?.IsAlive == true && (player.Position - OfficeRadioPosition).sqrMagnitude <= 4f)
                        {
                            _officeAccess.radioStartedAt = SynchronizedTime + .3d;
                            _officeAccess.revision++;
                            BroadcastOfficeState();
                            break;
                        }
            }

            if (!CanPresentLocally)
                return;
            if (!_officeRadioTried)
            {
                _officeRadioTried = true;
                try
                {
                    CreateOfficeRadio();
                }
                catch (Exception ex)
                {
                    Log.LogError("ISB: office radio model failed: " + ex.GetBaseException().Message);
                }
            }

            if (_officeRadioSwitch == null && _officeRadio != null)
            {
                _officeRadioSwitch = LabsTakeoverInfrastructure.CreateEmergencySwitch("ISB_OfficeRadio_Off", "Turn Off Radio", _officeRadio.GetComponent<BoxCollider>()?.bounds.center ?? OfficeRadioPosition);
                if (_officeRadioSwitch != null)
                    LabsTakeoverInfrastructure.RegisterEmergencyAction(_officeRadioSwitch, RequestOfficeRadioOff);
            }

            if (_officeRadioSwitch != null)
                _officeRadioSwitch.Operatable = !_officeAccess.radioOff;
            if (_officeAccess.radioOff)
            {
                StopRoutine(ref _officeRadioLoad);
                if (_officeRadioAudio != null)
                    _officeRadioAudio.Stop();
                return;
            }

            if (!_officeRadioPlayed && _officeAccess.radioStartedAt >= 0d)
            {
                _officeRadioPlayed = true;
                _officeRadioLoad = StartCoroutine(PlayOfficeRadio(_officeAccess.radioStartedAt));
            }
        }

        private void RequestOfficeRadioOff()
        {
            var player = _activeRaidWorld?.MainPlayer;
            if (!_labsPreBlackoutEventActive || player == null || _officeAccess?.radioOff != false)
                return;
            string payload = "OQ|" + player.ProfileId;
            if (IsEventAuthority)
                ReceiveOfficePacket(payload.Split('|'));
            else
                NetworkBridge.SendToServer(payload);
        }

        private bool OfficePlayerNearRadio(string profile)
        {
            if (_activeRaidWorld == null || string.IsNullOrEmpty(profile) || profile.Length > 64)
                return false;
            foreach (var player in _activeRaidWorld.AllAlivePlayersList)
                if (player != null && !player.IsAI && player.ProfileId == profile && player.HealthController?.IsAlive == true)
                    return (player.Position - OfficeRadioPosition).sqrMagnitude <= 9f;
            return false;
        }

        private void CreateOfficeRadio()
        {
            foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                if (bundle.name.Equals("isb_office_radio.bundle", StringComparison.OrdinalIgnoreCase))
                {
                    _officeRadioBundle = bundle;
                    break;
                }

            if (_officeRadioBundle == null)
            {
                _officeRadioBundle = AssetBundle.LoadFromFile(Path.Combine(Path.GetDirectoryName(Info.Location), "Office", "isb_office_radio.bundle"));
                _officeRadioOwnsBundle = _officeRadioBundle != null;
            }

            if (_officeRadioBundle == null)
                throw new InvalidOperationException("Missing military radio bundle.");
            var prefab = _officeRadioBundle.LoadAsset<GameObject>("assets/examples/items/radio2/radio2.prefab");
            if (prefab == null)
                throw new InvalidOperationException("Missing military radio model.");
            _officeRadio = new GameObject("ISB_OfficeMilitaryRadio");
            _officeRadio.transform.position = OfficeRadioPosition;
            _officeRadio.transform.rotation = Quaternion.Euler(0f, 0f, 270f);
            CopyRadioGeometry(prefab.transform, _officeRadio.transform, true);
            var renderers = _officeRadio.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                throw new InvalidOperationException("Military radio has no renderable geometry.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            var collider = _officeRadio.AddComponent<BoxCollider>();
            collider.center = _officeRadio.transform.InverseTransformPoint(bounds.center);
            collider.size = _officeRadio.transform.InverseTransformVector(bounds.size);
            collider.size = new Vector3(Mathf.Abs(collider.size.x), Mathf.Abs(collider.size.y), Mathf.Abs(collider.size.z));
        }

        private static void CopyRadioGeometry(Transform source, Transform parent, bool root)
        {
            var part = new GameObject(source.name).transform;
            part.SetParent(parent, false);
            part.localPosition = root ? Vector3.zero : source.localPosition;
            part.localRotation = source.localRotation;
            part.localScale = source.localScale;
            var mesh = source.GetComponent<MeshFilter>();
            var renderer = source.GetComponent<MeshRenderer>();
            if (mesh?.sharedMesh != null && renderer != null)
            {
                part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                part.gameObject.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }

            foreach (Transform child in source)
                CopyRadioGeometry(child, part, false);
        }

        private IEnumerator PlayOfficeRadio(double start)
        {
            AudioClip clip = null;
            yield return LoadAudioClip(Path.Combine(Path.GetDirectoryName(Info.Location), "Office", "43_Franklin_Calls_Dr_Harp.wav"), value => clip = value);
            if (!_raidisActive || _officeAccess?.radioOff == true || !IsAudioClipReady(clip))
            {
                _officeRadioLoad = null;
                yield break;
            }

            while (_raidisActive && SynchronizedTime < start)
                yield return null;
            float offset = (float)Math.Max(0d, SynchronizedTime - start);
            if (!_raidisActive || _officeAccess?.radioOff == true || offset >= clip.length)
            {
                _officeRadioLoad = null;
                yield break;
            }

            _officeRadioAudio = new GameObject("ISB_OfficeRadioAudio").AddComponent<AudioSource>();
            _officeRadioAudio.transform.position = OfficeRadioPosition;
            _officeRadioAudio.playOnAwake = false;
            _officeRadioAudio.loop = false;
            _officeRadioAudio.spatialBlend = 1f;
            _officeRadioAudio.minDistance = 1f;
            _officeRadioAudio.maxDistance = 14f;
            _officeRadioAudio.rolloffMode = AudioRolloffMode.Linear;
            _officeRadioAudio.dopplerLevel = 0f;
            _officeRadioAudio.volume = GetVolumeFactor(_radioVolume);
            _officeRadioAudio.clip = clip;
            _officeRadioAudio.time = offset;
            _officeRadioAudio.Play();
            _officeRadioLoad = null;
        }

        private void ResetOfficeRadio()
        {
            StopRoutine(ref _officeRadioLoad);
            if (_officeRadioAudio != null)
            {
                _officeRadioAudio.Stop();
                Destroy(_officeRadioAudio.gameObject);
            }

            _officeRadioAudio = null;
            if (_officeRadioSwitch != null)
            {
                LabsTakeoverInfrastructure.UnregisterEmergencyAction(_officeRadioSwitch);
                Destroy(_officeRadioSwitch.gameObject);
            }

            _officeRadioSwitch = null;
            Destroy(_officeRadio);
            _officeRadio = null;
            if (_officeRadioOwnsBundle && _officeRadioBundle != null)
                _officeRadioBundle.Unload(true);
            _officeRadioBundle = null;
            _officeRadioOwnsBundle = false;
            _officeRadioPlayed = _officeRadioTried = false;
            _officeRadioNextCheck = 0f;
        }
    }
}
