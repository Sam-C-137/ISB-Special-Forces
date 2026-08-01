using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using UnityEngine.Rendering;
using System.Text;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace ISBSSOFExtra
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(FikaPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class ISBSpecialForcesAudio : BaseUnityPlugin
    {
        public const string PluginGuid = "com.samc137.isbsofextras";
        public const string PluginName = "ISB SOF: Immersion Systems Plugin";
        public const string PluginVersion = "1.0.0";
        public const string FikaPluginGuid = "com.fika.core";

        private const string FikaBridgeAssemblyFileName = "ISBSOF_Extras.Fika.dll";
        private const string FikaBridgeAssemblyName = "ISBSOF_Extras.Fika";
        private const string FikaBridgeBootstrapTypeName =
            "ISBSSOFExtra.Fika.FikaBridgeBootstrap";
        public static ManualLogSource Log;

        private ConfigEntry<bool> _enableAnnouncements;
        private ConfigEntry<bool> _enableRadioChatter;
        private ConfigEntry<bool> _enableDeathChatter;
        private ConfigEntry<bool> _enableHeavyDamageChatter;
        private ConfigEntry<bool> _enableAishiRadioChatter;
        private ConfigEntry<bool> _enableAishiDeathChatter;
        private ConfigEntry<bool> _enableAishiHeavyDamageChatter;

        private ConfigEntry<float> _announcementsVolume;
        private ConfigEntry<float> _radioVolume;

        private ConfigEntry<int> _laboratoryBlackoutChance;
        private ConfigEntry<bool> _restrictExfilsDuringBlackout;
        private ConfigEntry<float> _blackoutVolume;

        private ConfigEntry<bool> _enableMapAnnouncementSubtitles;
        private ConfigEntry<string> _subtitleLanguage;

        private ISBSubtitleSystem _subtitleSystem;

        private const string targetmap = "laboratory";
        private const float AnnouncementBaseVolume = 1f;
        private const float AnnouncementMinDistance = 40f;
        private const float AnnouncementMaxDistance = 350f;
        private const float AnnouncementSpatialBlend = 1f;
        private const AudioRolloffMode AnnouncementRolloffMode = AudioRolloffMode.Linear;
        private const float RegularAnnouncementDelayWithoutBlackout = 30f;
        private const float StandaloneBlackoutDelay = 30f;
        private const float BlackoutAnnouncementPostApplyDelay = 0.75f;

        private static readonly HashSet<int> ISBEnums = new HashSet<int>
        {
            13701, 13702, 13703, 13704, 13705, 13706, 13709, 13710
        };

        private static readonly string[][] RegularAnnouncementSequences =
        {
            new[]
            {
                "ISBStart.wav",
                "ISBExits.wav",
                "ISBGateB.wav",
                "CameraStart.wav",
                "CameraEnemies.wav"
            },

            new[]
            {
                "ISB-Hack.wav",
                "ISB-HackReport.wav"
            },

            new[]
            {
                "ISB-O5-Start.wav",
                "ISB-HackReport.wav"
            },

            new[]
            {
                "ISB-A1-Start.wav"
            },

            new[]
            {
                "ISB-NU7-Start.wav"
            }
        };

        private static readonly string[][] BlackoutAnnouncementSequences =
        {
            new[]
            {
                "announcer_lights_out_01.wav"
            },

            new[]
            {
                "announcer_lights_out_02.wav"
            },

            new[]
            {
                "announcer_lights_out_03.wav"
            },

            new[]
            {
                "announcer_lights_out_04.wav"
            }
        };

        private static readonly HashSet<string> LaboratoryPaAnnouncementClips =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "announcer_lights_out_01",
                "announcer_lights_out_02",
                "announcer_lights_out_03",
                "announcer_lights_out_04"
            };

        private const string LaboratoryPaObjectToken = "sound_disclamer";
        private const int LaboratoryPaParentSearchDepth = 6;

        private string[] _chosenSequence;

        private bool _raidisActive;
        private bool _sequenceStarted;
        private bool _fakeblackoutisEnabled;
        private bool _deathChatterOnCooldown;

        private string _dataFolder;
        private string _ISBSpecialForces_RadiosFolder;
        private string _ISBSpecialForces_DeathRadiosFolder;
        private string _ISBSpecialForces_HeavyDamageRadiosFolder;
        private string _ISBSpecialForces_AishiRadiosFolder;
        private string _ISBSpecialForces_AishiDeathRadiosFolder;
        private string _ISBSpecialForces_AishiHeavyDamageRadiosFolder;
        private string _blackoutSoundPath;
        private string _automaticDoorsPath;

        private Dictionary<string, Dictionary<int, List<List<int>>>> _automaticDoors =
            new Dictionary<string, Dictionary<int, List<List<int>>>>();

        private readonly List<string> _botAudioPaths = new List<string>();
        private readonly List<string> _deathAudioPaths = new List<string>();
        private readonly List<string> _heavyDamageAudioPaths = new List<string>();
        private readonly List<string> _AishiBotAudioPaths = new List<string>();
        private readonly List<string> _AishiDeathAudioPaths = new List<string>();
        private readonly List<string> _AishiHeavyDamageAudioPaths = new List<string>();

        private readonly List<AudioClip> _wavsofBots = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofDeaths = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofHeavyDamage = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofAishiBots = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofAishiDeaths = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofAishiHeavyDamage = new List<AudioClip>();

        private readonly Dictionary<string, AudioClip> _audioClipCache = new Dictionary<string, AudioClip>();
        private readonly Dictionary<string, string> _lastRadioPathByCategory =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly List<Player> _listofactiveBots = new List<Player>();
        private readonly HashSet<Player> _deathSubscribedBots = new HashSet<Player>();

        private readonly List<ActiveISBAudio> _activeAudioObjects = new List<ActiveISBAudio>();
        private readonly List<LaboratoryPaSourceSnapshot> _activeLaboratoryPaSources =
            new List<LaboratoryPaSourceSnapshot>();
        private int _laboratoryPaPlaybackVersion;

        private readonly Dictionary<Player, float> _lastKnownHealth = new Dictionary<Player, float>();

        private const float HEAVY_DAMAGE_THRESHOLD = 60f;
        private const float HEAVY_DAMAGE_CHECK_INTERVAL = 1.5f;
        private const float HEAVY_DAMAGE_REACTION_DELAY = 2.5f;
        private const float RADIO_MIN_DISTANCE = 1.5f;
        private const float RADIO_MAX_DISTANCE = 45f;
        private const float RADIO_BASE_VOLUME = 0.26f;
        private const float RADIO_SPATIAL_BLEND = 1f;
        private const AudioRolloffMode RADIO_ROLLOFF_MODE = AudioRolloffMode.Linear;
        private const float RADIO_INITIAL_DELAY = 45f;

        private bool _heavyDamageChatterOnCooldown;
        private bool _heavyDamagePending;
        private Player _heavyDamageTarget;
        private Coroutine _heavyDamageRout;
        private Coroutine _heavyDamageWatcher;

        private bool _audioCacheStarted;
        private bool _announcementCacheReady;

        private float _lastBotCacheTime = 0f;
        private const float BOT_CACHE_INTERVAL = 3f;

        private Coroutine _scanner;
        private Coroutine _radiorout;
        private Coroutine _announcementRout;
        private Coroutine _deathRout;
        private Coroutine _audioCacheRoutine;
        private Coroutine _blackoutRoutine;

        private bool _blackoutApplied;
        private bool _gateBClosed;
        private bool _blackoutChanceResolved;
        private bool _blackoutSelectedForRaid;

        private Action<string> _fikaPayloadHandler;
        private MethodInfo _fikaBridgeShutdownMethod;
        private bool _fikaBridgeInitialized;
        private string _currentNetworkAnnouncement = string.Empty;
        private double _currentNetworkAnnouncementStart;
        private float _currentNetworkAnnouncementLength;
        private string _lastReceivedAnnouncementKey = string.Empty;
        private bool _networkRestrictExfils;
        private bool _networkDisableAutomaticDoors;
        private bool _pendingNetworkBlackout;
        private bool _headlessPresentationDisabled;
        private bool _fikaClientReadyAcknowledged;
        private bool _fikaPresentationClientReady;
        private float _nextFikaReadySendTime;
        private string _currentRaidLocation = string.Empty;
        private string _fikaReportedRaidLocation = string.Empty;
        private GameWorld _activeRaidWorld;
        private bool _fikaRemotePeerSeen;
        private float _fikaRaidStartedObservedAt = -1f;
        private float _fikaRemoteReadyFallbackAt = -1f;
        private const float FIKA_PRESENTATION_GRACE_SECONDS = 6f;
        private const float FIKA_REMOTE_READY_TIMEOUT_SECONDS = 12f;

        private IExtrasNetworkBridge NetworkBridge => ExtrasNetworkBridgeRegistry.Current;
        private bool FikaActive => NetworkBridge.Active;
        private bool IsFikaNetworkSession => FikaActive && NetworkBridge.Ready;
        private bool IsFikaServer => FikaActive && NetworkBridge.IsServer;
        private bool IsEventAuthority => !FikaActive || NetworkBridge.IsServer;
        private bool IsHeadless => FikaActive && NetworkBridge.IsHeadless;

        private bool CanPresentLocally => !IsHeadless;

        private double SynchronizedTime =>
            IsFikaNetworkSession
                ? NetworkBridge.NetworkTime
                : Time.realtimeSinceStartup;

        private void Awake()
        {
            Log = Logger;

            TryInitializeOptionalFikaBridge();
            _fikaPayloadHandler = HandleFikaPayload;

            if (CanPresentLocally)
                LabsBlackoutExfilUiPatcher.Enable(gameObject, Log);
            else
                Log.LogInfo("ISB SOF Extras: Fika headless mode detected. Audio, subtitles and HUD presentation are disabled on this process.");

            _enableAnnouncements = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Enable Laboratory Announcements",
                true,
                "When enabled, upon entering a Labs raid, if ISB operators are present, a series of announcements will play over the facility's intercom system informing of the ISB's presence and the start of its operation in the lab."
            );

            _enableRadioChatter = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Enable ISB Radio Comms",
                true,
                "When enabled, while on a raid where ISB operators are present, you'll be able to hear radio communications between operators, such as Aishi's callouts to them. Great for immersion and for pinpointing their locations."
            );

            _enableDeathChatter = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Enable ISB KIA Comms",
                true,
                "When enabled, while on a raid where ISB operators are present, you'll be able to hear Aishi's and HQ's communication with the other ISB operators when a team member is eliminated in the raid. This is great for immersion, pinpointing positions, and also identifying when a squad member has been taken down."
            );

            _enableHeavyDamageChatter = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Enable ISB Heavy Damage Comms",
                true,
                "When enabled, while on a raid where ISB operators are present, you'll be able to hear Aishi and HQ's communications about operators who have taken a lot of damage. Great for immersion and for pinpointing their current positions."
            );

            _enableAishiRadioChatter = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Enable Aishi ISB Radio Comms",
                true,
                "When enabled, it adds Aishi’s exclusive voice lines to the audio list for coordinating the team, as well as random taunts. Great for immersion and highlighting the bots’ positions."
            );

            _enableAishiDeathChatter = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Enable Aishi ISB KIA Comms",
                true,
                "When enabled, it adds Aishi's exclusive voice lines to the audio list to alert the team when one of the allied operators is eliminated. Great for immersion and revealing the bots' positions."
            );

            _enableAishiHeavyDamageChatter = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Enable Aishi ISB Heavy Damage Comms",
                true,
                "When enabled, it adds Aishi’s exclusive voice lines to the audio list to alert the team when one of the allied operators takes heavy damage but remains alive. Great for immersion and revealing the bots’ positions."
            );

            _announcementsVolume = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "Laboratory Announcements Volume",
                50f,
                new ConfigDescription(
                    "Volume of the Laboratory intercom announcements, from 0 to 100. Changes apply in real time, even while an announcement is already playing.",
                    new AcceptableValueRange<float>(0f, 100f)
                )
            );

            _radioVolume = Config.Bind(
                "USEC ISB Special Forces Audio Plugin Config",
                "ISB Radio Volume",
                80f,
                new ConfigDescription(
                    "Volume of all ISB radio communications, from 0 to 100. Changes apply in real time. even while an radios is already playing.",
                    new AcceptableValueRange<float>(0f, 100f)
                )
            );

            _laboratoryBlackoutChance = Config.Bind(
                "USEC ISB Special Forces Blackout Config",
                "Laboratory Blackout Event Chance",
                50,
                new ConfigDescription(
                    "Chance, from 0 to 100, for the complete Laboratory Blackout Event. In Fika, the host or headless decides for every player.",
                    new AcceptableValueRange<int>(0, 100)
                )
            );

            _restrictExfilsDuringBlackout = Config.Bind(
                "USEC ISB Special Forces Blackout Config",
                "Restrict Laboratory Exits",
                true,
                "Keeps only the Hangar Gate and Parking Gate exfils available when the blackout starts."
            );

            _blackoutVolume = Config.Bind(
                "USEC ISB Special Forces Blackout Config",
                "Blackout Sound Volume",
                100f,
                new ConfigDescription(
                    "Volume of the Blackout Event SFX, from 0 to 100.",
                    new AcceptableValueRange<float>(0f, 100f)
                )
            );

            _enableMapAnnouncementSubtitles = Config.Bind(
                "USEC ISB Special Forces Subtitle Config",
                "Enable Map Announcement Subtitles",
                true,
                "Enables subtitles for the Labs Announcements."
            );

            _subtitleLanguage = Config.Bind(
                "USEC ISB Special Forces Subtitle Config",
                "Subtitle Language",
                "Auto",
                "Subtitle locale code. Use Auto to follow the current EFT locale. Supported: en and pt."
            );

            _announcementsVolume.SettingChanged += OnVolumeSettingChanged;
            _radioVolume.SettingChanged += OnVolumeSettingChanged;
            _blackoutVolume.SettingChanged += OnVolumeSettingChanged;
            _enableAishiRadioChatter.SettingChanged += OnAishiCommsSettingChanged;
            _enableAishiDeathChatter.SettingChanged += OnAishiCommsSettingChanged;
            _enableAishiHeavyDamageChatter.SettingChanged += OnAishiCommsSettingChanged;

            string pluginDir = Path.GetDirectoryName(Info.Location);
            if (pluginDir == null)
            {
                Log.LogError("ISB SOF Audio Plugin: Audio Folder not found");
                return;
            }

            _dataFolder = Path.Combine(pluginDir, "ISBSpecialForces_Audios");
            _ISBSpecialForces_RadiosFolder = Path.Combine(_dataFolder, "ISBSpecialForces_Radios");
            _ISBSpecialForces_DeathRadiosFolder = Path.Combine(_dataFolder, "ISBSpecialForces_DeathRadios");
            _ISBSpecialForces_HeavyDamageRadiosFolder = Path.Combine(_dataFolder, "ISBSpecialForces_HeavyDamageRadios");
            _ISBSpecialForces_AishiRadiosFolder = Path.Combine(_dataFolder, "ISBSpecialForces_AishiRadios");
            _ISBSpecialForces_AishiDeathRadiosFolder = Path.Combine(_dataFolder, "ISBSpecialForces_AishiDeathRadios");
            _ISBSpecialForces_AishiHeavyDamageRadiosFolder = Path.Combine(_dataFolder, "ISBSpecialForces_AishiHeavyDamageRadios");
            _blackoutSoundPath = Path.Combine(_dataFolder, BlackoutBridge.BlackoutSoundFileName);
            _automaticDoorsPath = ResolveOptionalBlackoutDataPath(pluginDir, BlackoutBridge.AutomaticDoorsFileName);

            _automaticDoors = BlackoutBridge.LoadPathMap(
                _automaticDoorsPath,
                Log,
                "automatic door"
            );

            if (CanPresentLocally)
            {
                _subtitleSystem = new ISBSubtitleSystem(
                    this,
                    Log,
                    _dataFolder,
                    _enableMapAnnouncementSubtitles,
                    _subtitleLanguage
                );
            }

            StartCoroutine(RaidMonitor());
        }

        private void TryInitializeOptionalFikaBridge()
        {
            if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid))
                return;

            string pluginDirectory = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                Log.LogWarning(
                    "ISB SOF Extras: Fika is installed, but the Extras plugin directory could not be resolved.");
                return;
            }

            string bridgePath = Path.Combine(
                pluginDirectory,
                FikaBridgeAssemblyFileName);

            if (!File.Exists(bridgePath))
            {
                Log.LogWarning(
                    "ISB SOF Extras: Fika is installed, but " +
                    FikaBridgeAssemblyFileName +
                    " was not found next to the normal Extras DLL. Fika synchronization is disabled.");
                return;
            }

            try
            {
                Assembly bridgeAssembly = FindLoadedAssembly(FikaBridgeAssemblyName) ??
                    Assembly.LoadFrom(bridgePath);

                Type bootstrapType = bridgeAssembly.GetType(
                    FikaBridgeBootstrapTypeName,
                    true);

                MethodInfo initializeMethod = bootstrapType.GetMethod(
                    "Initialize",
                    BindingFlags.Public | BindingFlags.Static);

                _fikaBridgeShutdownMethod = bootstrapType.GetMethod(
                    "Shutdown",
                    BindingFlags.Public | BindingFlags.Static);

                if (initializeMethod == null || _fikaBridgeShutdownMethod == null)
                    throw new MissingMethodException(
                        FikaBridgeBootstrapTypeName,
                        "Initialize/Shutdown");

                initializeMethod.Invoke(null, new object[] { Log });
                _fikaBridgeInitialized = NetworkBridge.Active;

                if (!_fikaBridgeInitialized)
                {
                    Log.LogWarning(
                        "ISB SOF Extras: the optional Fika bridge loaded, but did not become active.");
                }
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Log.LogError(
                    "ISB SOF Extras: optional Fika bridge initialization failed: " +
                    cause.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "ISB SOF Extras: optional Fika bridge initialization failed: " +
                    ex.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loadedAssemblies.Length; i++)
            {
                Assembly assembly = loadedAssemblies[i];
                if (assembly == null)
                    continue;

                AssemblyName name;
                try
                {
                    name = assembly.GetName();
                }
                catch
                {
                    continue;
                }

                if (string.Equals(
                    name.Name,
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return assembly;
                }
            }

            return null;
        }

        private void ShutdownOptionalFikaBridge()
        {
            if (!_fikaBridgeInitialized || _fikaBridgeShutdownMethod == null)
                return;

            try
            {
                _fikaBridgeShutdownMethod.Invoke(null, null);
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Log.LogWarning(
                    "ISB SOF Extras: optional Fika bridge shutdown failed: " +
                    cause.Message);
            }
            catch (Exception ex)
            {
                Log.LogWarning(
                    "ISB SOF Extras: optional Fika bridge shutdown failed: " +
                    ex.Message);
            }
            finally
            {
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        private void Update()
        {
            if (!FikaActive)
                return;

            if (IsFikaNetworkSession && NetworkBridge.RaidStarted && _fikaRaidStartedObservedAt < 0f)
            {
                _fikaRaidStartedObservedAt = Time.realtimeSinceStartup;
                Log.LogInfo(
                    "ISB SOF Extras: FikaRaidStartedEvent observed; presentation READY will open after " +
                    FIKA_PRESENTATION_GRACE_SECONDS.ToString("F1", CultureInfo.InvariantCulture) + "s.");
            }

            if (IsHeadless && !_headlessPresentationDisabled)
            {
                _headlessPresentationDisabled = true;
                LabsBlackoutExfilUiPatcher.Disable();

                if (_subtitleSystem != null)
                {
                    _subtitleSystem.Dispose();
                    _subtitleSystem = null;
                }

                DestroyActiveAudioObjects();
                RestoreLaboratoryPaSources();
                Log.LogInfo("ISB SOF Extras: Headless presentation shutdown completed; authority and synchronization remain active.");
            }

            InvalidateStaleRaidWorldIfNeeded();

            NetworkBridge.DrainReceivedPayloads(_fikaPayloadHandler);

            if (_raidisActive)
            {
                TrySendFikaClientReady();
                TryApplyPendingNetworkBlackout();
            }

            if (NetworkBridge.IsServer && NetworkBridge.ConsumePeerConnected())
            {
                _fikaRemotePeerSeen = true;
                Log.LogInfo("ISB SOF Extras: Fika peer connected; waiting for its post-raid-start READY handshake before sending presentation state.");
            }
        }

        private void HandleFikaPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return;

            string[] parts = payload.Split('|');
            if (parts.Length == 0)
                return;

            if (parts[0] == "Q")
            {
                if (IsFikaNetworkSession && IsFikaServer)
                    HandleFikaClientReady(parts);

                return;
            }

            if (parts[0] == "K")
            {
                if (!IsEventAuthority)
                    _fikaClientReadyAcknowledged = true;

                return;
            }

            if (IsEventAuthority)
                return;

            switch (parts[0])
            {
                case "A":
                    if (!IsLaboratoryPresentationAllowed())
                    {
                        RejectLaboratoryPresentationOutsideLabs("network announcement packet");
                        break;
                    }

                    Log.LogInfo("ISB SOF Fika: Client received synchronized announcement packet.");
                    HandleNetworkAnnouncement(parts);
                    break;

                case "R":
                    HandleNetworkRadio(parts);
                    break;

                case "B":
                    if (!IsLaboratoryPresentationAllowed())
                    {
                        RejectLaboratoryPresentationOutsideLabs("network blackout packet");
                        break;
                    }

                    Log.LogInfo("ISB SOF Fika: Client received synchronized blackout packet.");
                    if (parts.Length >= 3)
                    {
                        bool restrict = parts[1] == "1";
                        bool disableDoors = parts[2] == "1";
                        _networkRestrictExfils = restrict;
                        _networkDisableAutomaticDoors = disableDoors;
                        _pendingNetworkBlackout = true;
                        TryApplyPendingNetworkBlackout();
                    }
                    break;

                case "G":
                    if (IsLaboratoryPresentationAllowed())
                        ApplyNetworkGateBClosed();
                    else
                        RejectLaboratoryPresentationOutsideLabs("network Gate B packet");
                    break;

                case "S":
                    HandleFikaSnapshot(parts);
                    break;
            }
        }

        private void TryApplyPendingNetworkBlackout()
        {
            if (!_pendingNetworkBlackout || _blackoutApplied ||
                !IsLaboratoryPresentationAllowed())
            {
                return;
            }

            if (IsFikaNetworkSession && !IsFikaPresentationWindowOpen())
                return;

            _pendingNetworkBlackout = false;
            _blackoutRoutine = StartCoroutine(DisableLaboratoryBlackout(
                true,
                _networkRestrictExfils,
                _networkDisableAutomaticDoors));
        }

        private void HandleNetworkAnnouncement(string[] parts)
        {
            if (parts.Length < 4 || !IsLaboratoryPresentationAllowed())
                return;

            string clipName = DecodeNetworkString(parts[1]);
            if (!TryParseDouble(parts[2], out double startTime) ||
                !TryParseFloat(parts[3], out float clipLength))
            {
                return;
            }

            StartNetworkAnnouncement(clipName, startTime, clipLength);
        }

        private void HandleNetworkRadio(string[] parts)
        {
            if (parts.Length < 8 || !_raidisActive)
                return;

            string category = parts[1];
            if (!IsRadioCategoryEnabled(category))
                return;

            string clipName = DecodeNetworkString(parts[2]);
            string profileId = DecodeNetworkString(parts[3]);

            if (!TryParseFloat(parts[4], out float x) ||
                !TryParseFloat(parts[5], out float y) ||
                !TryParseFloat(parts[6], out float z) ||
                !TryParseDouble(parts[7], out double startTime))
            {
                return;
            }

            StartCoroutine(PlayNetworkRadio(
                category,
                clipName,
                profileId,
                new Vector3(x, y, z),
                startTime));
        }

        private void TrySendFikaClientReady()
        {
            if (!IsFikaNetworkSession || IsEventAuthority || !CanPresentLocally ||
                _fikaClientReadyAcknowledged ||
                !_raidisActive || !_announcementCacheReady ||
                !IsFikaPresentationWindowOpen())
            {
                return;
            }

            if (!Singleton<GameWorld>.Instantiated ||
                Singleton<GameWorld>.Instance == null ||
                Singleton<GameWorld>.Instance.MainPlayer == null)
            {
                return;
            }

            if (Time.realtimeSinceStartup < _nextFikaReadySendTime)
                return;

            _nextFikaReadySendTime = Time.realtimeSinceStartup + 2f;

            string clientLocation = ResolveRaidLocation(Singleton<GameWorld>.Instance);
            string readyPayload = "Q|READY|" + EncodeNetworkString(clientLocation);

            NetworkBridge.SendToServer(readyPayload);
        }

        private bool IsFikaPresentationWindowOpen()
        {
            if (!IsFikaNetworkSession)
                return true;

            if (!NetworkBridge.RaidStarted)
                return false;

            if (_fikaRaidStartedObservedAt < 0f)
                _fikaRaidStartedObservedAt = Time.realtimeSinceStartup;

            return Time.realtimeSinceStartup - _fikaRaidStartedObservedAt >=
                FIKA_PRESENTATION_GRACE_SECONDS;
        }

        private bool IsLocalPresentationReadyForAuthority()
        {
            if (IsHeadless)
                return true;

            if (!CanPresentLocally || !_announcementCacheReady ||
                !IsFikaPresentationWindowOpen())
            {
                return false;
            }

            return Singleton<GameWorld>.Instantiated &&
                   Singleton<GameWorld>.Instance != null &&
                   Singleton<GameWorld>.Instance.MainPlayer != null;
        }

        private void HandleFikaClientReady(string[] parts)
        {
            if (parts.Length < 2 || !string.Equals(parts[1], "READY", StringComparison.Ordinal))
                return;

            string reportedLocation = parts.Length >= 3
                ? DecodeNetworkString(parts[2])
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(reportedLocation))
            {
                _fikaReportedRaidLocation = reportedLocation.Trim();

                if (IsHeadless)
                {
                    _currentRaidLocation = _fikaReportedRaidLocation;
                    _fakeblackoutisEnabled = IsLaboratoryLocation(_currentRaidLocation);
                }
                else if (!string.IsNullOrWhiteSpace(_currentRaidLocation) &&
                         !string.Equals(
                             _currentRaidLocation,
                             _fikaReportedRaidLocation,
                             StringComparison.OrdinalIgnoreCase))
                {
                    Log.LogWarning(
                        "ISB SOF Extras: Client READY location differs from the authoritative local raid. " +
                        "local='" + _currentRaidLocation + "', remote='" +
                        _fikaReportedRaidLocation + "'. The local map remains authoritative.");
                }
            }

            bool firstReadyClient = !_fikaPresentationClientReady;
            _fikaPresentationClientReady = true;

            NetworkBridge.Broadcast("K|READY");
            NetworkBridge.Broadcast(BuildFikaSnapshot());

            if (firstReadyClient)
            {
                Log.LogInfo(
                    "ISB SOF Extras: A Fika player client reported post-raid-start READY. " +
                    "Announcement authority may now proceed when its local gate is ready. location='" +
                    reportedLocation + "'.");
            }
            else
            {
                Log.LogInfo(
                    "ISB SOF Extras: A late/reconnected Fika client reported raid-ready; " +
                    "current state snapshot was resent. location='" + reportedLocation + "'.");
            }
        }

        private IEnumerator WaitForFikaPresentationReadiness()
        {
            if (!IsFikaNetworkSession || !IsFikaServer)
                yield break;

            float nextLogTime = 0f;

            while (_raidisActive)
            {
                bool localReady = IsLocalPresentationReadyForAuthority();

                bool remotePlayerExpected =
                    _fikaRemotePeerSeen ||
                    !NetworkBridge.IsSinglePlayer;

                if (localReady && !IsHeadless && remotePlayerExpected &&
                    !_fikaPresentationClientReady && _fikaRemoteReadyFallbackAt < 0f)
                {
                    _fikaRemoteReadyFallbackAt =
                        Time.realtimeSinceStartup + FIKA_REMOTE_READY_TIMEOUT_SECONDS;
                }

                bool remoteTimeoutOpen =
                    !IsHeadless &&
                    _fikaRemoteReadyFallbackAt >= 0f &&
                    Time.realtimeSinceStartup >= _fikaRemoteReadyFallbackAt;

                bool remoteReady = IsHeadless
                    ? _fikaPresentationClientReady
                    : (!remotePlayerExpected || _fikaPresentationClientReady || remoteTimeoutOpen);

                if (remoteTimeoutOpen && !_fikaPresentationClientReady)
                {
                    Log.LogWarning(
                        "ISB SOF Extras: Remote presentation READY timed out; " +
                        "the player-host will continue locally and late clients will receive a snapshot.");
                    _fikaRemotePeerSeen = false;
                    _fikaRemoteReadyFallbackAt = -1f;
                    remoteReady = true;
                }

                if (localReady && remoteReady)
                {
                    Log.LogInfo(
                        "ISB SOF Extras: Fika presentation start gate opened. " +
                        "raidStarted=" + NetworkBridge.RaidStarted +
                        ", localReady=" + localReady +
                        ", remotePeerSeen=" + _fikaRemotePeerSeen +
                        ", remoteReady=" + remoteReady +
                        ", headless=" + IsHeadless + ".");
                    yield break;
                }

                if (Time.realtimeSinceStartup >= nextLogTime)
                {
                    nextLogTime = Time.realtimeSinceStartup + 10f;
                    Log.LogInfo(
                        "ISB SOF Extras: Waiting before announcements/blackout. " +
                        "raidStarted=" + NetworkBridge.RaidStarted +
                        ", localReady=" + localReady +
                        ", remotePeerSeen=" + _fikaRemotePeerSeen +
                        ", remoteReady=" + remoteReady +
                        ", graceOpen=" + IsFikaPresentationWindowOpen() +
                        ", singlePlayer=" + NetworkBridge.IsSinglePlayer +
                        ", headless=" + IsHeadless + ".");
                }

                yield return new WaitForSeconds(0.25f);
            }
        }

        private void HandleFikaSnapshot(string[] parts)
        {
            if (parts.Length < 8 || !_raidisActive)
                return;

            bool blackout = parts[1] == "1";
            bool gateB = parts[2] == "1";
            bool restrict = parts[3] == "1";
            bool disableDoors = parts[4] == "1";
            string clipName = DecodeNetworkString(parts[5]);

            if (!IsLaboratoryPresentationAllowed())
            {
                if (blackout || gateB || restrict || disableDoors ||
                    !string.IsNullOrEmpty(clipName))
                {
                    RejectLaboratoryPresentationOutsideLabs("non-empty Fika state snapshot");
                }

                return;
            }

            if (!TryParseDouble(parts[6], out double startTime) ||
                !TryParseFloat(parts[7], out float clipLength))
            {
                return;
            }

            _networkRestrictExfils = restrict;
            _networkDisableAutomaticDoors = disableDoors;

            if (blackout && !_blackoutApplied && _fakeblackoutisEnabled)
            {
                _pendingNetworkBlackout = true;
                TryApplyPendingNetworkBlackout();
            }

            if (gateB)
                ApplyNetworkGateBClosed();

            if (!string.IsNullOrEmpty(clipName))
                StartNetworkAnnouncement(clipName, startTime, clipLength);
        }

        private void StartNetworkAnnouncement(string clipName, double startTime, float clipLength)
        {
            if (!_enableAnnouncements.Value || string.IsNullOrEmpty(clipName) ||
                !IsLaboratoryPresentationAllowed())
            {
                return;
            }

            double elapsed = Math.Max(0d, NetworkBridge.NetworkTime - startTime);
            if (clipLength > 0f && elapsed >= clipLength)
                return;

            string eventKey = clipName + "@" + startTime.ToString("R", CultureInfo.InvariantCulture);
            if (string.Equals(eventKey, _lastReceivedAnnouncementKey, StringComparison.Ordinal))
                return;

            _lastReceivedAnnouncementKey = eventKey;
            StartCoroutine(PlayNetworkAnnouncement(clipName, (float)elapsed));
        }

        private IEnumerator PlayNetworkAnnouncement(string clipName, float startOffset)
        {
            AudioClip clip = null;
            yield return StartCoroutine(ResolveNetworkClip(clipName, null, value => clip = value));

            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning("ISB SOF Fika: Could not resolve synchronized announcement clip '" + clipName + "'.");
                yield break;
            }

            if (ShouldRouteThroughLaboratoryPa(clip.name))
            {
                yield return RouteBlackoutClipIntoLaboratoryPa(clip, startOffset);
                yield break;
            }

            yield return PlayISBSoldiersRadio(
                clip,
                null,
                new Vector3(-170f, 20f, -342f),
                AnnouncementMinDistance,
                AnnouncementMaxDistance,
                AnnouncementBaseVolume,
                _announcementsVolume,
                true,
                startOffset,
                false,
                false,
                AnnouncementSpatialBlend,
                AnnouncementRolloffMode);
        }

        private IEnumerator PlayNetworkRadio(
            string category,
            string clipName,
            string profileId,
            Vector3 fallbackPosition,
            double startTime)
        {
            AudioClip clip = null;
            yield return StartCoroutine(ResolveNetworkClip(clipName, category, value => clip = value));

            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning(
                    "ISB SOF Fika: Could not resolve synchronized radio clip '" + clipName +
                    "' for category '" + category + "'.");
                yield break;
            }

            float startOffset = (float)Math.Max(0d, NetworkBridge.NetworkTime - startTime);
            if (startOffset >= clip.length)
                yield break;

            Player player = FindPlayerByProfileId(profileId);
            Transform parent = null;
            Vector3 position = fallbackPosition;

            if (player != null && player.PlayerBones != null && player.PlayerBones.RootJoint != null)
            {
                parent = player.PlayerBones.RootJoint;
                position = Vector3.up * 1.5f;
            }

            yield return PlayISBSoldiersRadio(
                clip,
                parent,
                position,
                RADIO_MIN_DISTANCE,
                RADIO_MAX_DISTANCE,
                RADIO_BASE_VOLUME,
                _radioVolume,
                false,
                startOffset,
                false,
                false,
                RADIO_SPATIAL_BLEND,
                RADIO_ROLLOFF_MODE);
        }

        private IEnumerator ResolveNetworkClip(
            string clipName,
            string category,
            Action<AudioClip> onResolved)
        {
            AudioClip clip = string.IsNullOrEmpty(category)
                ? FindCachedClipByName(clipName)
                : FindCachedRadioClip(category, clipName);

            if (!IsAudioClipReady(clip))
            {
                string path = string.IsNullOrEmpty(category)
                    ? Path.Combine(_dataFolder, clipName + ".wav")
                    : FindAudioPathByClipName(GetAudioPathsForCategory(category), clipName);

                if (string.IsNullOrEmpty(category) && !File.Exists(path))
                    path = Path.Combine(_dataFolder, clipName);

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    yield return StartCoroutine(LoadAudioClip(path, value => clip = value));
                    if (!string.IsNullOrEmpty(category))
                        AddClipToCategoryList(category, clip);
                }
            }

            onResolved?.Invoke(clip);
        }

        private void ApplyNetworkGateBClosed()
        {
            if (_gateBClosed || !IsLaboratoryPresentationAllowed())
                return;

            _gateBClosed = BlackoutBridge.DisableAndHideExfil(
                BlackoutBridge.GateBExfilName,
                Log);
        }

        private string BuildFikaSnapshot()
        {
            bool labsPresentation = IsLaboratoryPresentationAllowed();

            return string.Join("|", new[]
            {
                "S",
                labsPresentation && _blackoutApplied ? "1" : "0",
                labsPresentation && _gateBClosed ? "1" : "0",
                labsPresentation && _networkRestrictExfils ? "1" : "0",
                labsPresentation && _networkDisableAutomaticDoors ? "1" : "0",
                EncodeNetworkString(labsPresentation ? _currentNetworkAnnouncement : string.Empty),
                (labsPresentation ? _currentNetworkAnnouncementStart : 0d).ToString("R", CultureInfo.InvariantCulture),
                (labsPresentation ? _currentNetworkAnnouncementLength : 0f).ToString("R", CultureInfo.InvariantCulture)
            });
        }

        private void BroadcastFikaAnnouncement(string clipName, float clipLength)
        {
            if (string.IsNullOrEmpty(clipName) || !IsLaboratoryPresentationAllowed())
                return;

            _currentNetworkAnnouncement = clipName;
            _currentNetworkAnnouncementStart = SynchronizedTime;
            _currentNetworkAnnouncementLength = Mathf.Max(clipLength, 0.05f);

            if (!IsFikaServer)
                return;

            bool sent = NetworkBridge.Broadcast(string.Join("|", new[]
            {
                "A",
                EncodeNetworkString(clipName),
                _currentNetworkAnnouncementStart.ToString("R", CultureInfo.InvariantCulture),
                _currentNetworkAnnouncementLength.ToString("R", CultureInfo.InvariantCulture)
            }));

            Log.LogInfo(
                "ISB SOF Fika: Announcement broadcast '" + clipName +
                "' sent=" + sent +
                ", start=" + _currentNetworkAnnouncementStart.ToString("F3", CultureInfo.InvariantCulture) +
                ", duration=" + _currentNetworkAnnouncementLength.ToString("F2", CultureInfo.InvariantCulture) + ".");
        }

        private void BroadcastFikaRadio(string clipName, Player bot, string category)
        {
            if (!IsFikaServer ||
                string.IsNullOrEmpty(clipName) || bot == null ||
                string.IsNullOrEmpty(category))
            {
                return;
            }

            Vector3 position = bot.Position;

            double startTime = SynchronizedTime;
            bool sent = NetworkBridge.Broadcast(string.Join("|", new[]
            {
                "R",
                category,
                EncodeNetworkString(clipName),
                EncodeNetworkString(bot.ProfileId),
                position.x.ToString("R", CultureInfo.InvariantCulture),
                position.y.ToString("R", CultureInfo.InvariantCulture),
                position.z.ToString("R", CultureInfo.InvariantCulture),
                startTime.ToString("R", CultureInfo.InvariantCulture)
            }));

            Log.LogInfo(
                "ISB SOF Fika: Radio broadcast category=" + category +
                ", clip='" + clipName + "', sent=" + sent +
                ", profile='" + bot.ProfileId + "'.");
        }

        private AudioClip FindCachedClipByName(string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return null;

            foreach (AudioClip clip in _audioClipCache.Values)
            {
                if (IsAudioClipReady(clip) &&
                    string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return clip;
                }
            }

            return null;
        }

        private AudioClip FindCachedRadioClip(string category, string clipName)
        {
            if (string.IsNullOrEmpty(clipName))
                return null;

            List<string> paths = GetAudioPathsForCategory(category);
            string exactPath = FindAudioPathByClipName(paths, clipName);

            AudioClip exactClip;
            if (!string.IsNullOrEmpty(exactPath) &&
                _audioClipCache.TryGetValue(exactPath, out exactClip) &&
                IsAudioClipReady(exactClip))
            {
                return exactClip;
            }

            List<AudioClip> categoryClips = GetAudioClipsForCategory(category);
            for (int i = 0; i < categoryClips.Count; i++)
            {
                AudioClip clip = categoryClips[i];
                if (IsAudioClipReady(clip) &&
                    string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    return clip;
                }
            }

            return null;
        }

        private List<string> GetAudioPathsForCategory(string category)
        {
            if (string.Equals(category, "DF", StringComparison.Ordinal))
                return _AishiDeathAudioPaths;

            if (string.Equals(category, "HF", StringComparison.Ordinal))
                return _AishiHeavyDamageAudioPaths;

            if (string.Equals(category, "NF", StringComparison.Ordinal))
                return _AishiBotAudioPaths;

            if (string.Equals(category, "D", StringComparison.Ordinal))
                return _deathAudioPaths;

            if (string.Equals(category, "H", StringComparison.Ordinal))
                return _heavyDamageAudioPaths;

            return _botAudioPaths;
        }

        private List<AudioClip> GetAudioClipsForCategory(string category)
        {
            if (string.Equals(category, "DF", StringComparison.Ordinal))
                return _wavsofAishiDeaths;

            if (string.Equals(category, "HF", StringComparison.Ordinal))
                return _wavsofAishiHeavyDamage;

            if (string.Equals(category, "NF", StringComparison.Ordinal))
                return _wavsofAishiBots;

            if (string.Equals(category, "D", StringComparison.Ordinal))
                return _wavsofDeaths;

            if (string.Equals(category, "H", StringComparison.Ordinal))
                return _wavsofHeavyDamage;

            return _wavsofBots;
        }

        private bool IsRadioCategoryEnabled(string category)
        {
            if (string.Equals(category, "DF", StringComparison.Ordinal))
                return _enableAishiDeathChatter.Value;

            if (string.Equals(category, "HF", StringComparison.Ordinal))
                return _enableAishiHeavyDamageChatter.Value;

            if (string.Equals(category, "NF", StringComparison.Ordinal))
                return _enableAishiRadioChatter.Value;

            if (string.Equals(category, "D", StringComparison.Ordinal))
                return _enableDeathChatter.Value;

            if (string.Equals(category, "H", StringComparison.Ordinal))
                return _enableHeavyDamageChatter.Value;

            return string.Equals(category, "N", StringComparison.Ordinal) &&
                _enableRadioChatter.Value;
        }

        private void AddClipToCategoryList(string category, AudioClip clip)
        {
            if (!IsAudioClipReady(clip))
                return;

            List<AudioClip> list = GetAudioClipsForCategory(category);
            if (!list.Contains(clip))
                list.Add(clip);
        }

        private static string FindAudioPathByClipName(List<string> paths, string clipName)
        {
            if (paths == null || string.IsNullOrEmpty(clipName))
                return null;

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];
                if (string.Equals(
                    Path.GetFileNameWithoutExtension(path),
                    clipName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return path;
                }
            }

            return null;
        }

        private Player FindPlayerByProfileId(string profileId)
        {
            if (string.IsNullOrEmpty(profileId) ||
                !Singleton<GameWorld>.Instantiated ||
                Singleton<GameWorld>.Instance == null ||
                Singleton<GameWorld>.Instance.AllAlivePlayersList == null)
            {
                return null;
            }

            foreach (Player player in Singleton<GameWorld>.Instance.AllAlivePlayersList)
            {
                if (player != null && string.Equals(player.ProfileId, profileId, StringComparison.Ordinal))
                    return player;
            }

            return null;
        }

        private static string EncodeNetworkString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }

        private static string DecodeNetworkString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static bool TryParseDouble(string value, out double result)
        {
            return double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        private string ResolveRaidLocation(GameWorld gameWorld)
        {
            if (gameWorld == null)
                return string.Empty;

            try
            {
                if (gameWorld.MainPlayer != null &&
                    !string.IsNullOrWhiteSpace(gameWorld.MainPlayer.Location))
                {
                    return gameWorld.MainPlayer.Location;
                }
            }
            catch
            {
            }

            try
            {
                Type worldType = gameWorld.GetType();
                string[] memberNames = { "LocationId", "Location", "locationId", "location" };

                foreach (string memberName in memberNames)
                {
                    PropertyInfo property = worldType.GetProperty(
                        memberName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property != null && property.PropertyType == typeof(string))
                    {
                        string value = property.GetValue(gameWorld, null) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }

                    FieldInfo field = worldType.GetField(
                        memberName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null && field.FieldType == typeof(string))
                    {
                        string value = field.GetValue(gameWorld) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (gameWorld.AllAlivePlayersList != null)
                {
                    foreach (Player player in gameWorld.AllAlivePlayersList)
                    {
                        if (player != null && !string.IsNullOrWhiteSpace(player.Location))
                            return player.Location;
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }

        private static bool IsLaboratoryLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
                return false;

            return string.Equals(
                location.Trim(),
                targetmap,
                StringComparison.OrdinalIgnoreCase);
        }

        private bool IsLaboratoryWorld(GameWorld gameWorld, string resolvedLocation)
        {
            if (!string.IsNullOrWhiteSpace(resolvedLocation))
                return IsLaboratoryLocation(resolvedLocation);

            return IsHeadless && IsLaboratoryLocation(_fikaReportedRaidLocation);
        }

        private bool IsActiveRaidWorldCurrent()
        {
            if (!_raidisActive || _activeRaidWorld == null)
                return false;

            return Singleton<GameWorld>.Instantiated &&
                   Singleton<GameWorld>.Instance != null &&
                   ReferenceEquals(Singleton<GameWorld>.Instance, _activeRaidWorld);
        }

        private void InvalidateStaleRaidWorldIfNeeded()
        {
            if (!_raidisActive || IsActiveRaidWorldCurrent())
                return;

            Log.LogWarning(
                "ISB SOF Extras: Active GameWorld changed or was destroyed; clearing the previous raid context before handling the next map. " +
                "previousLocation='" + (_currentRaidLocation ?? string.Empty) + "'.");
            ShutdownRaid();
        }

        private bool IsLaboratoryPresentationAllowed()
        {
            if (!_raidisActive || !_fakeblackoutisEnabled || !IsActiveRaidWorldCurrent())
                return false;

            string liveLocation = ResolveRaidLocation(_activeRaidWorld);
            if (!string.IsNullOrWhiteSpace(liveLocation))
            {
                _currentRaidLocation = liveLocation.Trim();

                if (!IsLaboratoryLocation(_currentRaidLocation))
                {
                    _fakeblackoutisEnabled = false;
                    return false;
                }

                return true;
            }

            if (IsHeadless)
                return IsLaboratoryLocation(_fikaReportedRaidLocation);

            return false;
        }

        private void RejectLaboratoryPresentationOutsideLabs(string source)
        {
            _fakeblackoutisEnabled = false;
            ClearNetworkPresentationState();

            Log.LogWarning(
                "ISB SOF Extras: Ignored Labs-only presentation outside Laboratory. source=" +
                source + ", location='" + (_currentRaidLocation ?? string.Empty) + "'.");
        }

        private void ClearNetworkPresentationState()
        {
            _networkRestrictExfils = false;
            _networkDisableAutomaticDoors = false;
            _pendingNetworkBlackout = false;
            _currentNetworkAnnouncement = string.Empty;
            _currentNetworkAnnouncementStart = 0d;
            _currentNetworkAnnouncementLength = 0f;
            _lastReceivedAnnouncementKey = string.Empty;
        }

        private void ResetFikaHandshake(bool expectRemotePeer)
        {
            _fikaClientReadyAcknowledged = false;
            _fikaPresentationClientReady = false;
            _nextFikaReadySendTime = 0f;
            _fikaReportedRaidLocation = string.Empty;
            _fikaRaidStartedObservedAt = -1f;
            _fikaRemoteReadyFallbackAt = -1f;
            _fikaRemotePeerSeen = expectRemotePeer;
        }

        private void ClearRaidCollections()
        {
            _botAudioPaths.Clear();
            _deathAudioPaths.Clear();
            _heavyDamageAudioPaths.Clear();
            _AishiBotAudioPaths.Clear();
            _AishiDeathAudioPaths.Clear();
            _AishiHeavyDamageAudioPaths.Clear();
            _wavsofBots.Clear();
            _wavsofDeaths.Clear();
            _wavsofHeavyDamage.Clear();
            _wavsofAishiBots.Clear();
            _wavsofAishiDeaths.Clear();
            _wavsofAishiHeavyDamage.Clear();
            _lastRadioPathByCategory.Clear();
            _listofactiveBots.Clear();
            _deathSubscribedBots.Clear();
            _lastKnownHealth.Clear();
        }

        private void StopRoutine(ref Coroutine routine)
        {
            if (routine == null)
                return;

            StopCoroutine(routine);
            routine = null;
        }

        private IEnumerator RaidMonitor()
        {
            while (true)
            {
                GameWorld gameWorld = null;
                string resolvedLocation = string.Empty;
                float nextWaitingLog = 0f;

                while (true)
                {
                    if (Singleton<GameWorld>.Instantiated &&
                        Singleton<GameWorld>.Instance != null)
                    {
                        gameWorld = Singleton<GameWorld>.Instance;
                        resolvedLocation = ResolveRaidLocation(gameWorld);

                        bool headlessAuthorityWaitingForClient = IsFikaServer && IsHeadless;

                        if (!string.IsNullOrWhiteSpace(resolvedLocation) ||
                            headlessAuthorityWaitingForClient)
                        {
                            break;
                        }

                        if (Time.realtimeSinceStartup >= nextWaitingLog)
                        {
                            nextWaitingLog = Time.realtimeSinceStartup + 10f;
                            Log.LogInfo(
                                "ISB SOF Extras: GameWorld detected, waiting for raid location. " +
                                "mainPlayer=" + (gameWorld.MainPlayer != null) +
                                ", fikaActive=" + FikaActive +
                                ", headless=" + IsHeadless + ".");
                        }
                    }

                    yield return null;
                }

                yield return new WaitForSeconds(
                    FikaActive ? 2f : 20f);

                if (!Singleton<GameWorld>.Instantiated ||
                    Singleton<GameWorld>.Instance == null ||
                    !ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld))
                {
                    continue;
                }

                string refreshedLocation = ResolveRaidLocation(gameWorld);
                if (!string.IsNullOrWhiteSpace(refreshedLocation))
                    resolvedLocation = refreshedLocation;

                if (FikaActive)
                {
                    float nextNetworkLog = 0f;
                    while (Singleton<GameWorld>.Instantiated && !NetworkBridge.Ready)
                    {
                        if (Time.realtimeSinceStartup >= nextNetworkLog)
                        {
                            nextNetworkLog = Time.realtimeSinceStartup + 10f;
                            Log.LogWarning(
                                "ISB SOF Extras: Waiting for IFikaNetworkManager before starting synchronized raid logic. " +
                                "server=" + NetworkBridge.IsServer +
                                ", headless=" + IsHeadless +
                                ", raidStarted=" + NetworkBridge.RaidStarted + ".");
                        }

                        yield return new WaitForSeconds(0.5f);
                    }

                    if (!Singleton<GameWorld>.Instantiated)
                        continue;
                }

                bool isLabs = IsLaboratoryWorld(gameWorld, resolvedLocation);

                Log.LogInfo(
                    "ISB SOF Extras: Initial raid context resolved. location='" + resolvedLocation +
                    "', labs=" + isLabs +
                    ", authority=" + IsEventAuthority +
                    ", server=" + IsFikaServer +
                    ", headless=" + IsHeadless +
                    ", networkReady=" + IsFikaNetworkSession + ".");

                InitializeRaid(gameWorld, resolvedLocation, isLabs);

                if (IsFikaNetworkSession && IsFikaServer)
                {
                    yield return StartCoroutine(WaitForFikaPresentationReadiness());

                    if (IsHeadless)
                    {
                        string clientReportedLocation = _fikaReportedRaidLocation;
                        if (!string.IsNullOrWhiteSpace(clientReportedLocation))
                            resolvedLocation = clientReportedLocation;

                        isLabs = IsLaboratoryWorld(gameWorld, resolvedLocation);
                        _currentRaidLocation = resolvedLocation?.Trim() ?? string.Empty;
                        _fakeblackoutisEnabled = isLabs;

                        Log.LogInfo(
                            "ISB SOF Extras: Headless raid context finalized after post-raid-start client READY. " +
                            "location='" + resolvedLocation + "', labs=" + isLabs + ".");
                    }
                }

                if (!Singleton<GameWorld>.Instantiated || !_raidisActive)
                    continue;

                if (IsFikaNetworkSession && IsFikaServer)
                    NetworkBridge.Broadcast(BuildFikaSnapshot());

                if (IsEventAuthority)
                {
                    _radiorout = StartCoroutine(ISBSpecialForces_RadiosLoop());
                    _heavyDamageWatcher = StartCoroutine(HeavyDamageWatcherLoop());

                    if (IsLaboratoryPresentationAllowed())
                    {
                        Log.LogInfo("ISB SOF Audio Plugin: The current raid is in the Lab, enabling the authoritative announcement module via the intercom system. ^-^");
                        _scanner = StartCoroutine(ScanForMainSequence());
                    }
                }
                else
                {
                    Log.LogInfo("ISB SOF Audio Plugin: Fika client mode active. Waiting for host audio and blackout events.");
                }

                while (_raidisActive &&
                       Singleton<GameWorld>.Instantiated &&
                       Singleton<GameWorld>.Instance != null &&
                       ReferenceEquals(Singleton<GameWorld>.Instance, gameWorld))
                {
                    yield return null;
                }

                if (_raidisActive)
                    ShutdownRaid();
            }
        }

        private void InitializeRaid(GameWorld gameWorld, string resolvedLocation, bool isLabs)
        {
            _activeRaidWorld = gameWorld;
            _raidisActive = true;
            _currentRaidLocation = resolvedLocation?.Trim() ?? string.Empty;
            _fakeblackoutisEnabled = isLabs;
            _sequenceStarted = false;
            _deathChatterOnCooldown = false;
            _lastBotCacheTime = 0f;

            _heavyDamageChatterOnCooldown = false;
            _heavyDamagePending = false;
            _heavyDamageTarget = null;
            _blackoutApplied = false;
            _gateBClosed = false;
            _blackoutChanceResolved = false;
            _blackoutSelectedForRaid = false;
            ClearNetworkPresentationState();
            ResetFikaHandshake(FikaActive && !NetworkBridge.IsSinglePlayer);
            BlackoutBridge.ResetHiddenExfils();

            _audioCacheStarted = false;
            _announcementCacheReady = false;

            ClearRaidCollections();

            if (_subtitleSystem != null)
            {
                _subtitleSystem.Reload();
                _subtitleSystem.Prewarm();
            }

            IndexAudioPaths();

            CacheEligibleBots(gameWorld);

            StopRoutine(ref _audioCacheRoutine);
            _audioCacheRoutine = StartCoroutine(PreloadAudioCacheGradually());

            Log.LogInfo("ISB SOF Audio Plugin: Raid initialized.");
        }

        private void ShutdownRaid()
        {
            _raidisActive = false;
            _activeRaidWorld = null;
            _sequenceStarted = false;
            _fakeblackoutisEnabled = false;
            _deathChatterOnCooldown = false;

            _heavyDamageChatterOnCooldown = false;
            _heavyDamagePending = false;
            _heavyDamageTarget = null;

            _audioCacheStarted = false;
            _announcementCacheReady = false;

            StopRoutine(ref _scanner);
            StopRoutine(ref _radiorout);
            StopRoutine(ref _announcementRout);
            StopRoutine(ref _deathRout);
            StopRoutine(ref _heavyDamageRout);
            StopRoutine(ref _heavyDamageWatcher);
            StopRoutine(ref _audioCacheRoutine);
            StopRoutine(ref _blackoutRoutine);

            _blackoutApplied = false;
            _gateBClosed = false;
            _blackoutChanceResolved = false;
            _blackoutSelectedForRaid = false;
            ClearNetworkPresentationState();
            ResetFikaHandshake(false);
            _currentRaidLocation = string.Empty;
            BlackoutBridge.ResetHiddenExfils();

            if (_subtitleSystem != null)
                _subtitleSystem.Stop();

            DestroyActiveAudioObjects();
            RestoreLaboratoryPaSources();
            DestroyAudioClipCache();

            ClearRaidCollections();

            Log.LogInfo("ISB SOF Audio Plugin: Raid ended - Disabling all modules");
        }

        private void IndexAudioPaths()
        {
            IndexWavFolder(
                _ISBSpecialForces_RadiosFolder,
                _botAudioPaths,
                "ISB SOF Audio Plugin: Radio comms folder not found.");
            IndexWavFolder(
                _ISBSpecialForces_DeathRadiosFolder,
                _deathAudioPaths,
                "ISB SOF Audio Plugin: ISB KIA comms folder not found.");
            IndexWavFolder(
                _ISBSpecialForces_HeavyDamageRadiosFolder,
                _heavyDamageAudioPaths,
                "ISB SOF Audio Plugin: Heavy damage comms folder not found.");

            IndexOptionalWavFolder(
                _enableAishiRadioChatter.Value,
                _ISBSpecialForces_AishiRadiosFolder,
                _AishiBotAudioPaths,
                "ISB SOF Audio Plugin: Aishi radio comms folder not found.");
            IndexOptionalWavFolder(
                _enableAishiDeathChatter.Value,
                _ISBSpecialForces_AishiDeathRadiosFolder,
                _AishiDeathAudioPaths,
                "ISB SOF Audio Plugin: Aishi KIA comms folder not found.");
            IndexOptionalWavFolder(
                _enableAishiHeavyDamageChatter.Value,
                _ISBSpecialForces_AishiHeavyDamageRadiosFolder,
                _AishiHeavyDamageAudioPaths,
                "ISB SOF Audio Plugin: Aishi heavy damage comms folder not found.");

            Log.LogInfo($"ISB SOF Audio Plugin: Found {_botAudioPaths.Count} standard and {_AishiBotAudioPaths.Count} Aishi radio comms files.");
            Log.LogInfo($"ISB SOF Audio Plugin: Found {_deathAudioPaths.Count} standard and {_AishiDeathAudioPaths.Count} Aishi KIA comms files.");
            Log.LogInfo($"ISB SOF Audio Plugin: Found {_heavyDamageAudioPaths.Count} standard and {_AishiHeavyDamageAudioPaths.Count} Aishi heavy damage comms files.");
        }

        private void IndexWavFolder(string folder, List<string> target, string missingMessage)
        {
            target.Clear();
            if (!Directory.Exists(folder))
            {
                Log.LogWarning(missingMessage);
                return;
            }

            target.AddRange(Directory.GetFiles(folder, "*.wav"));
        }

        private void IndexOptionalWavFolder(
            bool enabled,
            string folder,
            List<string> target,
            string missingMessage)
        {
            target.Clear();
            if (!enabled)
                return;

            if (!Directory.Exists(folder))
            {
                Log.LogWarning(missingMessage);
                return;
            }

            target.AddRange(Directory.GetFiles(folder, "*.wav"));
        }

        private IEnumerator PreloadAudioCacheGradually()
        {
            if (_audioCacheStarted)
                yield break;

            _audioCacheStarted = true;

            if (IsHeadless)
            {
                _announcementCacheReady = true;
                _audioCacheRoutine = null;
                Log.LogInfo("ISB SOF Extras: Headless authority will use WAV metadata without loading or decoding audio clips.");
                yield break;
            }

            yield return new WaitForSeconds(1f);

            Log.LogInfo("ISB SOF Audio Plugin: Starting gradual audio cache to prevent crashes or performance issues.");

            HashSet<string> announcementFilesToLoad = new HashSet<string>();
            foreach (string[] sequence in RegularAnnouncementSequences)
            {
                foreach (string fileName in sequence)
                    announcementFilesToLoad.Add(fileName);
            }

            foreach (string[] sequence in BlackoutAnnouncementSequences)
            {
                foreach (string fileName in sequence)
                    announcementFilesToLoad.Add(fileName);
            }

            if (File.Exists(_blackoutSoundPath))
                announcementFilesToLoad.Add(BlackoutBridge.BlackoutSoundFileName);

            foreach (string fileName in announcementFilesToLoad)
            {
                if (!_raidisActive)
                    yield break;

                string filePath = Path.Combine(_dataFolder, fileName);

                yield return StartCoroutine(LoadAudioClip(filePath, null));

                yield return new WaitForSeconds(0.25f);
            }

            _announcementCacheReady = true;
            Log.LogInfo("ISB SOF Audio Plugin: Announcement and blackout audio cache ready; Fika client can report raid-ready.");

            foreach (string filePath in _deathAudioPaths)
            {
                if (!_raidisActive)
                    yield break;

                yield return StartCoroutine(LoadAudioClip(filePath, delegate (AudioClip clip)
                {
                    if (clip != null && !_wavsofDeaths.Contains(clip))
                        _wavsofDeaths.Add(clip);
                }));

                yield return new WaitForSeconds(0.25f);
            }

            foreach (string filePath in _AishiDeathAudioPaths)
            {
                if (!_raidisActive)
                    yield break;

                yield return StartCoroutine(LoadAudioClip(filePath, delegate (AudioClip clip)
                {
                    if (clip != null && !_wavsofAishiDeaths.Contains(clip))
                        _wavsofAishiDeaths.Add(clip);
                }));

                yield return new WaitForSeconds(0.25f);
            }

            foreach (string filePath in _heavyDamageAudioPaths)
            {
                if (!_raidisActive)
                    yield break;

                yield return StartCoroutine(LoadAudioClip(filePath, delegate (AudioClip clip)
                {
                    if (clip != null && !_wavsofHeavyDamage.Contains(clip))
                        _wavsofHeavyDamage.Add(clip);
                }));

                yield return new WaitForSeconds(0.25f);
            }

            foreach (string filePath in _AishiHeavyDamageAudioPaths)
            {
                if (!_raidisActive)
                    yield break;

                yield return StartCoroutine(LoadAudioClip(filePath, delegate (AudioClip clip)
                {
                    if (clip != null && !_wavsofAishiHeavyDamage.Contains(clip))
                        _wavsofAishiHeavyDamage.Add(clip);
                }));

                yield return new WaitForSeconds(0.25f);
            }

            foreach (string filePath in _botAudioPaths)
            {
                if (!_raidisActive)
                    yield break;

                yield return StartCoroutine(LoadAudioClip(filePath, delegate (AudioClip clip)
                {
                    if (clip != null && !_wavsofBots.Contains(clip))
                        _wavsofBots.Add(clip);
                }));

                yield return new WaitForSeconds(0.25f);
            }

            foreach (string filePath in _AishiBotAudioPaths)
            {
                if (!_raidisActive)
                    yield break;

                yield return StartCoroutine(LoadAudioClip(filePath, delegate (AudioClip clip)
                {
                    if (clip != null && !_wavsofAishiBots.Contains(clip))
                        _wavsofAishiBots.Add(clip);
                }));

                yield return new WaitForSeconds(0.25f);
            }

            _audioCacheRoutine = null;

            Log.LogInfo(
                $"ISB SOF Audio Plugin: Audio cache ready. " +
                $"Radio: {_wavsofBots.Count} standard + {_wavsofAishiBots.Count} Aishi. " +
                $"KIA: {_wavsofDeaths.Count} standard + {_wavsofAishiDeaths.Count} Aishi. " +
                $"Heavy damage: {_wavsofHeavyDamage.Count} standard + {_wavsofAishiHeavyDamage.Count} Aishi.");
        }

        private IEnumerator LoadAudioClip(string fullPath, Action<AudioClip> onLoaded)
        {
            if (string.IsNullOrEmpty(fullPath))
                yield break;

            AudioClip cachedClip;
            if (_audioClipCache.TryGetValue(fullPath, out cachedClip))
            {
                if (IsAudioClipReady(cachedClip))
                {
                    onLoaded?.Invoke(cachedClip);
                    yield break;
                }

                _audioClipCache.Remove(fullPath);
                if (cachedClip != null)
                    Destroy(cachedClip);
            }

            if (!File.Exists(fullPath))
            {
                Log.LogWarning($"ISB SOF Audio Plugin: Audio file not found: {fullPath}");
                yield break;
            }

            AudioClip clip = LoadPcmWavClip(fullPath);

            if (!IsAudioClipReady(clip))
            {
                if (clip != null)
                {
                    Destroy(clip);
                    clip = null;
                }

                string uri = new Uri(fullPath).AbsoluteUri;
                using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV))
                {
                    DownloadHandlerAudioClip handler =
                        www.downloadHandler as DownloadHandlerAudioClip;
                    if (handler != null)
                        handler.streamAudio = false;

                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        AudioClip requestClip = DownloadHandlerAudioClip.GetContent(www);
                        if (requestClip != null)
                        {
                            requestClip.name = Path.GetFileNameWithoutExtension(fullPath);

                            if (requestClip.loadState == AudioDataLoadState.Unloaded)
                                requestClip.LoadAudioData();

                            float deadline = Time.realtimeSinceStartup + 5f;
                            while (requestClip != null &&
                                   requestClip.loadState == AudioDataLoadState.Loading &&
                                   Time.realtimeSinceStartup < deadline)
                            {
                                yield return null;
                            }

                            clip = CloneAudioClipToOwned(requestClip, fullPath);
                            Destroy(requestClip);
                        }
                    }
                    else
                    {
                        Log.LogWarning(
                            $"ISB SOF Audio Plugin: UnityWebRequest failed to load audio: {fullPath} - {www.error}.");
                    }
                }
            }

            if (!IsAudioClipReady(clip))
            {
                if (clip != null)
                    Destroy(clip);

                Log.LogWarning($"ISB SOF Audio Plugin: Failed to create a stable audio clip: {fullPath}");
                yield break;
            }

            _audioClipCache[fullPath] = clip;
            onLoaded?.Invoke(clip);

            Log.LogInfo(
                $"ISB SOF Audio Plugin: Audio clip ready | clip: {clip.name} | " +
                $"length: {clip.length:F2}s | samples: {clip.samples} | channels: {clip.channels} | " +
                $"frequency: {clip.frequency}Hz | state: {clip.loadState}.");
        }

        private AudioClip CloneAudioClipToOwned(AudioClip source, string fullPath)
        {
            if (!IsAudioClipReady(source))
                return null;

            try
            {
                int sampleValueCount = checked(source.samples * source.channels);
                float[] samples = new float[sampleValueCount];
                if (!source.GetData(samples, 0))
                    throw new InvalidOperationException("AudioClip.GetData failed.");

                AudioClip ownedClip = AudioClip.Create(
                    Path.GetFileNameWithoutExtension(fullPath),
                    source.samples,
                    source.channels,
                    source.frequency,
                    false);

                if (ownedClip == null || !ownedClip.SetData(samples, 0))
                {
                    if (ownedClip != null)
                        Destroy(ownedClip);

                    throw new InvalidOperationException("AudioClip.SetData failed.");
                }

                return ownedClip;
            }
            catch (Exception ex)
            {
                Log.LogWarning(
                    $"ISB SOF Audio Plugin: Failed to clone Unity audio into an owned clip for '{fullPath}': {ex.Message}");
                return null;
            }
        }

        private static bool IsAudioClipReady(AudioClip clip)
        {
            return clip != null &&
                   clip.loadState == AudioDataLoadState.Loaded &&
                   clip.samples > 0 &&
                   clip.channels > 0 &&
                   clip.frequency > 0 &&
                   clip.length > 0.01f;
        }

        private AudioClip LoadPcmWavClip(string fullPath)
        {
            try
            {
                using (FileStream stream = File.OpenRead(fullPath))
                using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII))
                {
                    if (ReadFourCc(reader) != "RIFF")
                        throw new InvalidDataException("Missing RIFF header.");

                    reader.ReadUInt32();

                    if (ReadFourCc(reader) != "WAVE")
                        throw new InvalidDataException("Missing WAVE header.");

                    ushort audioFormat = 0;
                    ushort channels = 0;
                    int sampleRate = 0;
                    ushort bitsPerSample = 0;
                    byte[] audioData = null;

                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunkId = ReadFourCc(reader);
                        uint chunkSizeValue = reader.ReadUInt32();
                        long chunkStart = stream.Position;
                        long chunkSize = chunkSizeValue;

                        if (chunkStart + chunkSize > stream.Length)
                            throw new InvalidDataException("WAV chunk exceeds the file length.");

                        if (chunkId == "fmt ")
                        {
                            if (chunkSize < 16)
                                throw new InvalidDataException("Invalid WAV fmt chunk.");

                            audioFormat = reader.ReadUInt16();
                            channels = reader.ReadUInt16();
                            sampleRate = reader.ReadInt32();
                            reader.ReadInt32();
                            reader.ReadUInt16();
                            bitsPerSample = reader.ReadUInt16();
                        }
                        else if (chunkId == "data")
                        {
                            if (chunkSize > int.MaxValue)
                                throw new InvalidDataException("WAV data chunk is too large.");

                            audioData = reader.ReadBytes((int)chunkSize);
                        }

                        stream.Position = chunkStart + chunkSize;
                        if ((chunkSize & 1L) != 0L && stream.Position < stream.Length)
                            stream.Position++;

                        if (audioData != null && audioFormat != 0)
                            break;
                    }

                    if (audioData == null || audioData.Length == 0)
                        throw new InvalidDataException("WAV data chunk was not found.");

                    if (channels == 0 || sampleRate <= 0)
                        throw new InvalidDataException("Invalid WAV channel count or sample rate.");

                    int bytesPerSample = bitsPerSample / 8;
                    if (bytesPerSample <= 0 || audioData.Length % bytesPerSample != 0)
                        throw new InvalidDataException("Invalid WAV sample size.");

                    int sampleValueCount = audioData.Length / bytesPerSample;
                    int frameCount = sampleValueCount / channels;
                    if (frameCount <= 0)
                        throw new InvalidDataException("WAV contains no complete sample frames.");

                    float[] samples = new float[frameCount * channels];
                    int offset = 0;

                    for (int i = 0; i < samples.Length; i++)
                    {
                        float value;

                        if (audioFormat == 1)
                        {
                            switch (bitsPerSample)
                            {
                                case 8:
                                    value = (audioData[offset] - 128f) / 128f;
                                    break;

                                case 16:
                                    value = BitConverter.ToInt16(audioData, offset) / 32768f;
                                    break;

                                case 24:
                                    int sample24 = audioData[offset] |
                                                   (audioData[offset + 1] << 8) |
                                                   (audioData[offset + 2] << 16);
                                    if ((sample24 & 0x800000) != 0)
                                        sample24 |= unchecked((int)0xFF000000);
                                    value = sample24 / 8388608f;
                                    break;

                                case 32:
                                    value = BitConverter.ToInt32(audioData, offset) / 2147483648f;
                                    break;

                                default:
                                    throw new NotSupportedException(
                                        "Unsupported PCM WAV bit depth: " + bitsPerSample + ".");
                            }
                        }
                        else if (audioFormat == 3 && bitsPerSample == 32)
                        {
                            value = BitConverter.ToSingle(audioData, offset);
                        }
                        else
                        {
                            throw new NotSupportedException(
                                "Unsupported WAV format: format=" + audioFormat +
                                ", bits=" + bitsPerSample + ".");
                        }

                        samples[i] = Mathf.Clamp(value, -1f, 1f);
                        offset += bytesPerSample;
                    }

                    AudioClip decodedClip = AudioClip.Create(
                        Path.GetFileNameWithoutExtension(fullPath),
                        frameCount,
                        channels,
                        sampleRate,
                        false);

                    if (decodedClip == null || !decodedClip.SetData(samples, 0))
                    {
                        if (decodedClip != null)
                            Destroy(decodedClip);

                        throw new InvalidOperationException("AudioClip.SetData failed.");
                    }

                    Log.LogInfo(
                        $"ISB SOF Audio Plugin: PCM WAV decoded '{Path.GetFileName(fullPath)}' " +
                        $"({channels}ch, {sampleRate}Hz, {bitsPerSample}-bit, {decodedClip.length:F2}s).");

                    return decodedClip;
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning(
                    $"ISB SOF Audio Plugin: PCM WAV fallback failed for '{fullPath}': {ex.Message}");
                return null;
            }
        }

        private static string ReadFourCc(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
                throw new EndOfStreamException("Unexpected end of WAV file.");

            return Encoding.ASCII.GetString(bytes);
        }

        private void DestroyAudioClipCache()
        {
            foreach (AudioClip clip in _audioClipCache.Values)
            {
                if (clip != null)
                    Destroy(clip);
            }

            _audioClipCache.Clear();
        }

        private bool IsEligibleISBBot(Player player, bool requireAlive)
        {
            if (player == null)
                return false;

            if (!player.IsAI)
                return false;

            if (player.HealthController == null)
                return false;

            if (requireAlive && !player.HealthController.IsAlive)
                return false;

            if (player.Profile == null || player.Profile.Info == null)
                return false;

            return ISBEnums.Contains((int)player.Profile.Info.Settings.Role);
        }

        private void CacheEligibleBots(GameWorld gameWorld)
        {
            _listofactiveBots.Clear();

            if (gameWorld == null)
                return;

            if (gameWorld.AllAlivePlayersList == null)
                return;

            foreach (Player player in gameWorld.AllAlivePlayersList)
            {
                if (!IsEligibleISBBot(player, true))
                    continue;

                _listofactiveBots.Add(player);
            }

            _lastBotCacheTime = Time.time;

            if (IsEventAuthority)
                SubscribeToDeaths(_listofactiveBots);
        }

        private void CacheEligibleBotsIfNeeded()
        {
            if (Time.time - _lastBotCacheTime < BOT_CACHE_INTERVAL)
                return;

            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return;

            CacheEligibleBots(Singleton<GameWorld>.Instance);
        }

        private void SubscribeToDeaths(List<Player> bots)
        {
            foreach (Player bot in bots)
            {
                if (bot == null)
                    continue;

                if (bot.HealthController == null)
                    continue;

                if (_deathSubscribedBots.Contains(bot))
                    continue;

                _deathSubscribedBots.Add(bot);

                Player captured = bot;
                captured.HealthController.DiedEvent += delegate (EDamageType dmg)
                {
                    OnISBBotDied(captured);
                };

                Log.LogInfo($"ISB SOF Audio Plugin: ISB operator registered with Enum {(int)bot.Profile.Info.Settings.Role}.");
            }
        }

        private void OnISBBotDied(Player bot)
        {
            if (!IsEventAuthority || !_raidisActive)
                return;

            if (_heavyDamagePending)
            {
                Log.LogInfo("ISB SOF Audio Plugin: Operator died before heavy damage reaction fired - suppressing heavy damage comms.");

                _heavyDamagePending = false;
                _heavyDamageTarget = null;

                if (_heavyDamageRout != null)
                {
                    StopCoroutine(_heavyDamageRout);
                    _heavyDamageRout = null;
                }
            }

            if (!_enableDeathChatter.Value && !_enableAishiDeathChatter.Value)
                return;

            if (_deathChatterOnCooldown)
                return;

            if (!HasAvailableAudioForContext("death radio chatter"))
            {
                Log.LogWarning("ISB SOF Audio Plugin: ISB operator KIA detected, but KIA comms clips are not loaded yet.");
                return;
            }

            if (!IsEligibleISBBot(bot, false))
                return;

            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return;

            CacheEligibleBots(Singleton<GameWorld>.Instance);

            if (_listofactiveBots.Count == 0)
            {
                Log.LogInfo("ISB SOF Audio Plugin: ISB operator KIA detected, but no alive ISB operators remains to answer.");
                return;
            }

            Log.LogInfo("ISB SOF Audio Plugin: ISB operator KIA detected - triggering KIA comms.");

            _deathChatterOnCooldown = true;

            if (_deathRout != null)
                StopCoroutine(_deathRout);

            _deathRout = StartCoroutine(DeathChatterRadioRoutine());
        }

        private IEnumerator DeathChatterRadioRoutine()
        {
            yield return new WaitForSeconds(3f);

            yield return PlayRandomISBRadioFromList(
                "death radio chatter"
            );

            float cooldown = UnityEngine.Random.Range(8f, 14f);
            Log.LogInfo($"ISB SOF Audio Plugin: KIA comms cooldown {cooldown:F1}s.");

            yield return new WaitForSeconds(cooldown);

            _deathChatterOnCooldown = false;
            _deathRout = null;
        }

        private IEnumerator HeavyDamageWatcherLoop()
        {
            yield return new WaitForSeconds(3f);

            while (_raidisActive)
            {
                if (_enableHeavyDamageChatter.Value || _enableAishiHeavyDamageChatter.Value)
                    CheckHeavyDamage();

                yield return new WaitForSeconds(HEAVY_DAMAGE_CHECK_INTERVAL);
            }

            _heavyDamageWatcher = null;
        }

        private void CheckHeavyDamage()
        {
            if (!IsEventAuthority || !_raidisActive)
                return;

            CacheEligibleBotsIfNeeded();

            foreach (Player bot in _listofactiveBots)
            {
                if (bot == null || bot.HealthController == null || !bot.HealthController.IsAlive)
                    continue;

                float currentHealth = GetTotalHealth(bot);

                float previous;
                if (_lastKnownHealth.TryGetValue(bot, out previous))
                {
                    float lost = previous - currentHealth;

                    if (lost >= HEAVY_DAMAGE_THRESHOLD &&
                        !_heavyDamageChatterOnCooldown &&
                        !_heavyDamagePending &&
                        HasAvailableAudioForContext("heavy damage radio chatter"))
                    {
                        Log.LogInfo($"ISB SOF Audio Plugin: ISB Operator Heavy damage received detected | ({lost:F0} HP Lost) - scheduling reaction in {HEAVY_DAMAGE_REACTION_DELAY:F1}s.");

                        _heavyDamagePending = true;
                        _heavyDamageTarget = bot;

                        if (_heavyDamageRout != null)
                            StopCoroutine(_heavyDamageRout);

                        _heavyDamageRout = StartCoroutine(HeavyDamageRadioRoutine());
                    }
                }

                _lastKnownHealth[bot] = currentHealth;
            }
        }

        private float GetTotalHealth(Player bot)
        {
            float total = 0f;

            foreach (EBodyPart part in Enum.GetValues(typeof(EBodyPart)))
            {
                if (part == EBodyPart.Common)
                    continue;

                try
                {
                    var hp = bot.HealthController.GetBodyPartHealth(part);
                    total += hp.Current;
                }
                catch
                {
                }
            }

            return total;
        }

        private IEnumerator HeavyDamageRadioRoutine()
        {
            yield return new WaitForSeconds(HEAVY_DAMAGE_REACTION_DELAY);

            if (!_raidisActive ||
                !_heavyDamagePending ||
                _heavyDamageTarget == null ||
                _heavyDamageTarget.HealthController == null ||
                !_heavyDamageTarget.HealthController.IsAlive ||
                !IsEligibleISBBot(_heavyDamageTarget, true))
            {
                Log.LogInfo("ISB SOF Audio Plugin: Heavy damage reaction aborted (operator no longer valid/alive).");

                _heavyDamagePending = false;
                _heavyDamageTarget = null;
                _heavyDamageRout = null;
                yield break;
            }

            _heavyDamagePending = false;
            _heavyDamageChatterOnCooldown = true;

            if (Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null)
                CacheEligibleBotsIfNeeded();

            yield return PlayRandomISBRadioFromList(
                "heavy damage radio chatter"
            );

            _heavyDamageTarget = null;

            float cooldown = UnityEngine.Random.Range(12f, 20f);
            Log.LogInfo($"ISB SOF Audio Plugin: Heavy damage comms cooldown {cooldown:F1}s.");

            yield return new WaitForSeconds(cooldown);

            _heavyDamageChatterOnCooldown = false;
            _heavyDamageRout = null;
        }

        private IEnumerator ScanForMainSequence()
        {
            if (!IsEventAuthority)
                yield break;

            float sequenceTargetTime = Time.realtimeSinceStartup + 25f;
            if (IsFikaNetworkSession && _fikaRaidStartedObservedAt >= 0f)
                sequenceTargetTime = _fikaRaidStartedObservedAt + 25f;

            while (_raidisActive && Time.realtimeSinceStartup < sequenceTargetTime)
                yield return null;

            while (_raidisActive && !_sequenceStarted)
            {
                if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                {
                    yield return new WaitForSeconds(10f);
                    continue;
                }

                CacheEligibleBots(Singleton<GameWorld>.Instance);

                if (_listofactiveBots.Count > 0)
                {
                    Log.LogInfo("ISB SOF Audio Plugin: ISB Operator Detected - Starting announcements sequence");

                    _sequenceStarted = true;

                    if (_announcementRout != null)
                        StopCoroutine(_announcementRout);

                    _announcementRout = StartCoroutine(ISBAnnouncementSequence());

                    yield break;
                }

                yield return new WaitForSeconds(10f);
            }

            _scanner = null;
        }

        private IEnumerator ISBAnnouncementSequence()
        {
            if (!IsEventAuthority || !IsLaboratoryPresentationAllowed())
            {
                _announcementRout = null;
                yield break;
            }

            if (RegularAnnouncementSequences.Length == 0)
            {
                Log.LogWarning("ISB SOF Audio Plugin: No regular announcement sequences set.");
                _announcementRout = null;
                yield break;
            }

            yield return new WaitForSeconds(1.5f);

            if (!IsLaboratoryPresentationAllowed())
            {
                _announcementRout = null;
                yield break;
            }

            ResolveBlackoutChanceOnce();

            bool selectedBlackoutAnnouncement;
            int chosenIndex;
            _chosenSequence = SelectAnnouncementSequence(
                _blackoutSelectedForRaid,
                out selectedBlackoutAnnouncement,
                out chosenIndex);

            if (_chosenSequence == null || _chosenSequence.Length == 0)
            {
                Log.LogWarning("ISB SOF Audio Plugin: No eligible announcement sequence was available.");
                _announcementRout = null;
                yield break;
            }

            Log.LogInfo(
                "ISB SOF Audio Plugin: " +
                (selectedBlackoutAnnouncement ? "Blackout" : "Regular") +
                " announcement sequence selected: " + chosenIndex + ".");

            if (_blackoutSelectedForRaid)
            {
                if (!IsFikaNetworkSession)
                {
                    Log.LogInfo(
                        "ISB SOF Audio Plugin: Standalone Laboratory blackout selected; delaying the event by " +
                        StandaloneBlackoutDelay.ToString("F0", CultureInfo.InvariantCulture) +
                        " seconds.");

                    yield return new WaitForSeconds(StandaloneBlackoutDelay);

                    if (!_raidisActive || !IsLaboratoryPresentationAllowed())
                    {
                        _announcementRout = null;
                        yield break;
                    }
                }

                if (!_blackoutApplied)
                {
                    if (_blackoutRoutine != null)
                        StopCoroutine(_blackoutRoutine);

                    Coroutine activeBlackoutRoutine = StartCoroutine(DisableLaboratoryBlackout());
                    _blackoutRoutine = activeBlackoutRoutine;
                    yield return activeBlackoutRoutine;
                }

                if (!_raidisActive || !IsLaboratoryPresentationAllowed())
                {
                    _announcementRout = null;
                    yield break;
                }

                yield return new WaitForSeconds(BlackoutAnnouncementPostApplyDelay);
            }
            else
            {
                Log.LogInfo(
                    "ISB SOF Audio Plugin: Blackout was not selected; delaying the regular " +
                    "Laboratory announcement by " +
                    RegularAnnouncementDelayWithoutBlackout.ToString("F0", CultureInfo.InvariantCulture) +
                    " seconds.");

                yield return new WaitForSeconds(RegularAnnouncementDelayWithoutBlackout);

                if (!_raidisActive || !IsLaboratoryPresentationAllowed())
                {
                    _announcementRout = null;
                    yield break;
                }
            }

            if (!_enableAnnouncements.Value)
            {
                Log.LogInfo(
                    "ISB SOF Audio Plugin: Laboratory announcements are disabled; " +
                    "the blackout decision and event remain active independently.");
                _announcementRout = null;
                yield break;
            }

            for (int i = 0; i < _chosenSequence.Length; i++)
            {
                yield return PlayISBLeadAnnouncement(_chosenSequence[i]);

                if (i < _chosenSequence.Length - 1)
                    yield return new WaitForSeconds(27f);
            }

            _announcementRout = null;
        }

        private IEnumerator ISBSpecialForces_RadiosLoop()
        {
            if (!IsEventAuthority)
                yield break;

            yield return new WaitForSeconds(RADIO_INITIAL_DELAY);
            Log.LogInfo(
                "ISB SOF Audio Plugin: Normal radio chatter scheduler is active; " +
                "first playback window opened after " + RADIO_INITIAL_DELAY.ToString("F0", CultureInfo.InvariantCulture) + "s.");

            while (_raidisActive)
            {
                if (!_enableRadioChatter.Value && !_enableAishiRadioChatter.Value)
                {
                    yield return new WaitForSeconds(5f);
                    continue;
                }

                CacheEligibleBotsIfNeeded();

                if (_listofactiveBots.Count > 0 && HasAvailableAudioForContext("normal radio chatter"))
                {
                    yield return PlayRandomISBRadioFromList(
                        "normal radio chatter"
                    );
                }

                yield return new WaitForSeconds(UnityEngine.Random.Range(15f, 30f));
            }

            _radiorout = null;
        }

        private IEnumerator PlayRandomISBRadioFromList(
            string contextLog)
        {
            if (!_raidisActive)
                yield break;

            List<string> paths = GetAudioPathsForContext(contextLog);
            List<AudioClip> clips = GetAudioClipsForContext(contextLog);
            if (!HasAvailableAudio(clips, paths))
            {
                Log.LogWarning($"ISB SOF Audio Plugin: No comms audio available for {contextLog}.");
                yield break;
            }

            CacheEligibleBotsIfNeeded();
            _listofactiveBots.RemoveAll(candidateBot =>
                candidateBot == null ||
                candidateBot.HealthController == null ||
                !candidateBot.HealthController.IsAlive ||
                !IsEligibleISBBot(candidateBot, true));

            if (_listofactiveBots.Count == 0)
            {
                Log.LogWarning($"ISB SOF Audio Plugin: No active ISB operators available for {contextLog}.");
                yield break;
            }

            Player bot = _listofactiveBots[UnityEngine.Random.Range(0, _listofactiveBots.Count)];

            if (bot == null ||
                bot.HealthController == null ||
                !bot.HealthController.IsAlive ||
                (!IsHeadless && (bot.PlayerBones == null || bot.PlayerBones.RootJoint == null)))
            {
                Log.LogWarning($"ISB SOF Audio Plugin: Invalid ISB operator selected for {contextLog}.");
                yield break;
            }

            string categoryKey = GetRadioCategoryForContext(contextLog, false);
            string selectedPath = SelectRandomAudioPath(paths, categoryKey);

            if (IsHeadless)
            {
                if (string.IsNullOrEmpty(selectedPath))
                {
                    Log.LogWarning($"ISB SOF Extras: Headless could not select an audio path for {contextLog}.");
                    yield break;
                }

                string clipName = Path.GetFileNameWithoutExtension(selectedPath);
                float clipLength = GetWavDurationSeconds(selectedPath);

                Log.LogInfo(
                    $"ISB SOF Extras: Headless authority selected {contextLog}: {clipName} | " +
                    $"pool {paths.Count} | length {clipLength:F2}s | bot enum {(int)bot.Profile.Info.Settings.Role}");

                string networkCategory = GetRadioCategoryForContext(
                    contextLog,
                    IsAishiAudioPath(selectedPath));
                BroadcastFikaRadio(clipName, bot, networkCategory);
                yield return new WaitForSeconds(Mathf.Max(clipLength, 0.05f));
                yield break;
            }

            clips.RemoveAll(clip => !IsAudioClipReady(clip));

            AudioClip selectedClip = null;
            if (!string.IsNullOrEmpty(selectedPath))
            {
                if (!_audioClipCache.TryGetValue(selectedPath, out selectedClip) ||
                    !IsAudioClipReady(selectedClip))
                {
                    selectedClip = null;
                    yield return StartCoroutine(LoadAudioClip(selectedPath, delegate (AudioClip value)
                    {
                        selectedClip = value;
                    }));
                }

                if (IsAudioClipReady(selectedClip) && !clips.Contains(selectedClip))
                    clips.Add(selectedClip);
            }
            else if (clips.Count > 0)
            {
                selectedClip = clips[UnityEngine.Random.Range(0, clips.Count)];
            }

            if (!IsAudioClipReady(selectedClip))
            {
                Log.LogWarning(
                    $"ISB SOF Audio Plugin: Could not resolve the selected path for {contextLog}. " +
                    $"path='{selectedPath ?? string.Empty}', indexed={paths?.Count ?? 0}, cached={clips.Count}.");
                yield break;
            }

            Log.LogInfo(
                $"ISB SOF Audio Plugin: Playing {contextLog}: {selectedClip.name} | " +
                $"pool {paths?.Count ?? 0} | length {selectedClip.length:F2}s | " +
                $"bot enum {(int)bot.Profile.Info.Settings.Role}");

            string selectedCategory = GetRadioCategoryForContext(
                contextLog,
                !string.IsNullOrEmpty(selectedPath)
                    ? IsAishiAudioPath(selectedPath)
                    : IsAishiAudioClip(selectedClip));
            BroadcastFikaRadio(selectedClip.name, bot, selectedCategory);

            yield return PlayISBSoldiersRadio(
                selectedClip,
                bot.PlayerBones.RootJoint,
                Vector3.up * 1.5f,
                RADIO_MIN_DISTANCE,
                RADIO_MAX_DISTANCE,
                RADIO_BASE_VOLUME,
                _radioVolume,
                false,
                0f,
                false,
                false,
                RADIO_SPATIAL_BLEND,
                RADIO_ROLLOFF_MODE);
        }

        private string SelectRandomAudioPath(List<string> paths, string category)
        {
            if (paths == null || paths.Count == 0)
                return null;

            int selectedIndex = UnityEngine.Random.Range(0, paths.Count);

            string lastPath;
            if (paths.Count > 1 &&
                _lastRadioPathByCategory.TryGetValue(category, out lastPath) &&
                string.Equals(paths[selectedIndex], lastPath, StringComparison.OrdinalIgnoreCase))
            {
                int offset = UnityEngine.Random.Range(1, paths.Count);
                selectedIndex = (selectedIndex + offset) % paths.Count;
            }

            string selectedPath = paths[selectedIndex];
            _lastRadioPathByCategory[category] = selectedPath;
            return selectedPath;
        }

        private static string GetRadioCategoryForContext(
            string contextLog,
            bool Aishi)
        {
            if (!string.IsNullOrEmpty(contextLog) &&
                contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Aishi ? "DF" : "D";
            }

            if (!string.IsNullOrEmpty(contextLog) &&
                contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return Aishi ? "HF" : "H";
            }

            return Aishi ? "NF" : "N";
        }

        private List<string> GetAudioPathsForContext(string contextLog)
        {
            List<string> paths = new List<string>();

            if (contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableDeathChatter.Value)
                    paths.AddRange(_deathAudioPaths);
                if (_enableAishiDeathChatter.Value)
                    paths.AddRange(_AishiDeathAudioPaths);
                return paths;
            }

            if (contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableHeavyDamageChatter.Value)
                    paths.AddRange(_heavyDamageAudioPaths);
                if (_enableAishiHeavyDamageChatter.Value)
                    paths.AddRange(_AishiHeavyDamageAudioPaths);
                return paths;
            }

            if (_enableRadioChatter.Value)
                paths.AddRange(_botAudioPaths);
            if (_enableAishiRadioChatter.Value)
                paths.AddRange(_AishiBotAudioPaths);

            return paths;
        }

        private List<AudioClip> GetAudioClipsForContext(string contextLog)
        {
            List<AudioClip> clips = new List<AudioClip>();

            if (contextLog.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableDeathChatter.Value)
                    clips.AddRange(_wavsofDeaths);
                if (_enableAishiDeathChatter.Value)
                    clips.AddRange(_wavsofAishiDeaths);
                return clips;
            }

            if (contextLog.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (_enableHeavyDamageChatter.Value)
                    clips.AddRange(_wavsofHeavyDamage);
                if (_enableAishiHeavyDamageChatter.Value)
                    clips.AddRange(_wavsofAishiHeavyDamage);
                return clips;
            }

            if (_enableRadioChatter.Value)
                clips.AddRange(_wavsofBots);
            if (_enableAishiRadioChatter.Value)
                clips.AddRange(_wavsofAishiBots);

            return clips;
        }

        private bool HasAvailableAudioForContext(string contextLog)
        {
            return HasAvailableAudio(
                GetAudioClipsForContext(contextLog),
                GetAudioPathsForContext(contextLog));
        }

        private bool IsAishiAudioPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return ContainsAudioPath(_AishiBotAudioPaths, path) ||
                ContainsAudioPath(_AishiDeathAudioPaths, path) ||
                ContainsAudioPath(_AishiHeavyDamageAudioPaths, path);
        }

        private bool IsAishiAudioClip(AudioClip clip)
        {
            if (!IsAudioClipReady(clip))
                return false;

            return _wavsofAishiBots.Contains(clip) ||
                _wavsofAishiDeaths.Contains(clip) ||
                _wavsofAishiHeavyDamage.Contains(clip);
        }

        private static bool ContainsAudioPath(List<string> paths, string path)
        {
            for (int i = 0; i < paths.Count; i++)
            {
                if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private bool HasAvailableAudio(List<AudioClip> clips, List<string> paths)
        {
            if (paths != null && paths.Count > 0)
                return true;

            if (clips == null)
                return false;

            clips.RemoveAll(clip => !IsAudioClipReady(clip));
            return clips.Count > 0;
        }

        private float GetWavDurationSeconds(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (new string(reader.ReadChars(4)) != "RIFF")
                        return 0.05f;

                    reader.ReadInt32();
                    if (new string(reader.ReadChars(4)) != "WAVE")
                        return 0.05f;

                    int byteRate = 0;
                    int dataSize = 0;

                    while (stream.Position + 8 <= stream.Length)
                    {
                        string chunkId = new string(reader.ReadChars(4));
                        int chunkSize = reader.ReadInt32();
                        long nextChunk = stream.Position + chunkSize + (chunkSize & 1);

                        if (chunkId == "fmt " && chunkSize >= 16)
                        {
                            reader.ReadInt16();
                            reader.ReadInt16();
                            reader.ReadInt32();
                            byteRate = reader.ReadInt32();
                        }
                        else if (chunkId == "data")
                        {
                            dataSize = chunkSize;
                        }

                        stream.Position = Math.Min(nextChunk, stream.Length);
                        if (byteRate > 0 && dataSize > 0)
                            break;
                    }

                    if (byteRate > 0 && dataSize > 0)
                        return Mathf.Max((float)dataSize / byteRate, 0.05f);
                }
            }
            catch (Exception ex)
            {
                Log?.LogWarning("ISB SOF Extras: Could not read WAV duration for '" + path + "': " + ex.Message);
            }

            return 0.05f;
        }

        private static string[] SelectAnnouncementSequence(
            bool blackoutSelected,
            out bool selectedBlackoutAnnouncement,
            out int selectedIndex)
        {
            selectedBlackoutAnnouncement = false;
            selectedIndex = -1;

            int regularCount = RegularAnnouncementSequences.Length;
            int blackoutCount = blackoutSelected
                ? BlackoutAnnouncementSequences.Length
                : 0;
            int totalCount = regularCount + blackoutCount;

            if (totalCount <= 0)
                return null;

            int combinedIndex = UnityEngine.Random.Range(0, totalCount);
            if (combinedIndex < regularCount)
            {
                selectedIndex = combinedIndex;
                return RegularAnnouncementSequences[combinedIndex];
            }

            selectedBlackoutAnnouncement = true;
            selectedIndex = combinedIndex - regularCount;
            return BlackoutAnnouncementSequences[selectedIndex];
        }

        private void ResolveBlackoutChanceOnce()
        {
            if (_blackoutChanceResolved)
                return;

            _blackoutChanceResolved = true;

            if (!IsLaboratoryPresentationAllowed())
            {
                _blackoutSelectedForRaid = false;
                return;
            }

            int chance = Mathf.Clamp(_laboratoryBlackoutChance.Value, 0, 100);
            float roll = UnityEngine.Random.value * 100f;
            _blackoutSelectedForRaid = chance >= 100 || (chance > 0 && roll < chance);

            Log.LogInfo(
                "ISB SOF Blackout: Event chance roll " + roll.ToString("F2", CultureInfo.InvariantCulture) +
                " against " + chance + "% -> " +
                (_blackoutSelectedForRaid ? "selected" : "skipped") + ".");
        }

        private IEnumerator DisableLaboratoryBlackout(
            bool networkForced = false,
            bool networkRestrictExfils = false,
            bool networkDisableDoors = false)
        {
            if (!IsLaboratoryPresentationAllowed())
            {
                _blackoutRoutine = null;
                yield break;
            }

            if (_blackoutApplied)
            {
                _blackoutRoutine = null;
                yield break;
            }

            bool restrictExfils = networkForced
                ? networkRestrictExfils
                : _restrictExfilsDuringBlackout.Value;
            bool disableDoors = true;

            if (!IsLaboratoryPresentationAllowed())
            {
                _blackoutRoutine = null;
                yield break;
            }

            _networkRestrictExfils = restrictExfils;
            _networkDisableAutomaticDoors = disableDoors;
            _blackoutApplied = true;

            if (!networkForced && IsFikaServer)
            {
                bool blackoutSent = NetworkBridge.Broadcast(
                    "B|" + (restrictExfils ? "1" : "0") + "|" +
                    (disableDoors ? "1" : "0"));
                Log.LogInfo(
                    "ISB SOF Fika: Blackout packet sent=" + blackoutSent +
                    ", restrictExfils=" + restrictExfils +
                    ", disableDoors=" + disableDoors + ".");
            }

            Log.LogInfo("ISB SOF Blackout: Starting Laboratory Blackout Protocol.");

            if (CanPresentLocally)
                StartCoroutine(PlayLaboratoryBlackoutSound(networkForced));

            if (!IsLaboratoryPresentationAllowed())
            {
                _blackoutApplied = false;
                _blackoutRoutine = null;
                yield break;
            }

            if (restrictExfils)
            {
                LabsBlackout.DisableExfils();
                BlackoutBridge.IndexDisabledExfilsAndTransits(Log);
            }

            if (disableDoors)
                LabsBlackout.DisableAutomaticDoors(_automaticDoors);

            if (CanPresentLocally)
            {
                yield return null;
                LabsBlackout.DisableAutomaticLights();

                yield return null;
                LabsBlackout.DisableAmbientLight();

                yield return null;
                LabsBlackout.DisableElectricSounds();

                yield return null;
                LabsBlackout.DisableLights();

                yield return null;
            }

            Log.LogInfo("ISB SOF Blackout: Laboratory Blackout Protocol completed.");
            _blackoutRoutine = null;
        }

        private IEnumerator PlayLaboratoryBlackoutSound(bool networkForced = false)
        {
            if (!IsLaboratoryPresentationAllowed())
            {
                yield break;
            }

            if (string.IsNullOrEmpty(_blackoutSoundPath) || !File.Exists(_blackoutSoundPath))
            {
                Log.LogInfo(
                    "ISB SOF Blackout: Optional sound '" + BlackoutBridge.BlackoutSoundFileName +
                    "' was not found in the audio folder; continuing without it.");
                yield break;
            }

            AudioClip clip;
            if (!_audioClipCache.TryGetValue(_blackoutSoundPath, out clip) ||
                !IsAudioClipReady(clip))
            {
                if (clip != null)
                {
                    _audioClipCache.Remove(_blackoutSoundPath);
                    Destroy(clip);
                }

                AudioClip loadedClip = null;
                yield return StartCoroutine(LoadAudioClip(
                    _blackoutSoundPath,
                    delegate (AudioClip value)
                    {
                        loadedClip = value;
                    }));
                clip = loadedClip;
            }

            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning("ISB SOF Blackout: The blackout sound clip was invalid at playback time.");
                yield break;
            }

            yield return PlayISBSoldiersRadio(
                clip,
                null,
                new Vector3(-170f, 20f, -342f),
                1f,
                150f,
                4f,
                _blackoutVolume,
                false,
                0f,
                true,
                true
            );
        }

        private string ResolveOptionalBlackoutDataPath(string pluginDir, string fileName)
        {
            string[] candidates =
            {
                Path.Combine(pluginDir, fileName),
                Path.Combine(pluginDir, "Blackout", fileName),
                Path.Combine(_dataFolder, "Blackout", fileName)
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (File.Exists(candidates[i]))
                    return candidates[i];
            }

            return candidates[0];
        }

        private IEnumerator PlayISBLeadAnnouncement(string fileName)
        {
            if (!IsLaboratoryPresentationAllowed())
                yield break;

            string path = Path.Combine(_dataFolder, fileName);

            if (IsHeadless)
            {
                if (!File.Exists(path))
                {
                    Log.LogWarning("ISB SOF Extras: Headless announcement file not found: " + path);
                    yield break;
                }

                string clipName = Path.GetFileNameWithoutExtension(path);
                float clipLength = GetWavDurationSeconds(path);
                BroadcastFikaAnnouncement(clipName, clipLength);

                if (IsEventAuthority &&
                    _restrictExfilsDuringBlackout.Value &&
                    _blackoutApplied &&
                    !_gateBClosed &&
                    string.Equals(clipName, "ISBGateB", StringComparison.OrdinalIgnoreCase))
                {
                    StartCoroutine(DisableGateBAtSynchronizedTime(_currentNetworkAnnouncementStart + 3.70d));
                }

                yield return new WaitForSeconds(Mathf.Max(clipLength, 0.05f));
                yield break;
            }

            AudioClip clip;
            if (!_audioClipCache.TryGetValue(path, out clip) || !IsAudioClipReady(clip))
            {
                if (clip != null)
                {
                    _audioClipCache.Remove(path);
                    Destroy(clip);
                }

                AudioClip loadedClip = null;
                yield return StartCoroutine(LoadAudioClip(
                    path,
                    delegate (AudioClip value)
                    {
                        loadedClip = value;
                    }));
                clip = loadedClip;
            }

            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning($"ISB SOF Audio Plugin: Lead announcement clip is invalid: {fileName}");
                yield break;
            }

            BroadcastFikaAnnouncement(clip.name, clip.length);

            if (IsEventAuthority &&
                _restrictExfilsDuringBlackout.Value &&
                _blackoutApplied &&
                !_gateBClosed &&
                string.Equals(clip.name, "ISBGateB", StringComparison.OrdinalIgnoreCase))
            {
                StartCoroutine(DisableGateBAtSynchronizedTime(_currentNetworkAnnouncementStart + 3.70d));
            }

            if (ShouldRouteThroughLaboratoryPa(clip.name))
            {
                yield return RouteBlackoutClipIntoLaboratoryPa(clip, 0f);
                yield break;
            }

            yield return PlayISBSoldiersRadio(
                clip,
                null,
                new Vector3(-170, 20, -342),
                AnnouncementMinDistance,
                AnnouncementMaxDistance,
                AnnouncementBaseVolume,
                _announcementsVolume,
                true,
                0f,
                true,
                false,
                AnnouncementSpatialBlend,
                AnnouncementRolloffMode
            );
        }

        private static bool ShouldRouteThroughLaboratoryPa(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
                return false;

            string normalized = Path.GetFileNameWithoutExtension(clipName);
            const string legacyEffectSuffix = "_effect";

            if (normalized.EndsWith(
                legacyEffectSuffix,
                StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(
                    0,
                    normalized.Length - legacyEffectSuffix.Length);
            }

            return LaboratoryPaAnnouncementClips.Contains(normalized);
        }

        private IEnumerator RouteBlackoutClipIntoLaboratoryPa(
            AudioClip clip,
            float startOffsetSeconds)
        {
            if (!IsAudioClipReady(clip))
                yield break;

            startOffsetSeconds = Mathf.Clamp(
                startOffsetSeconds,
                0f,
                Mathf.Max(0f, clip.length - 0.05f));

            if (!CanPresentLocally)
            {
                yield return new WaitForSeconds(
                    Mathf.Max(clip.length - startOffsetSeconds, 0.05f));
                yield break;
            }

            List<AudioSource> channels = CollectLaboratoryPaChannels();
            if (channels.Count == 0)
            {
                Log.LogWarning(
                    "ISB SOF Audio Plugin: No Laboratory PA channels associated with '" +
                    LaboratoryPaObjectToken +
                    "' were found. Using the existing announcement source as fallback for '" +
                    clip.name + "'.");

                yield return PlayISBSoldiersRadio(
                    clip,
                    null,
                    new Vector3(-170f, 20f, -342f),
                    AnnouncementMinDistance,
                    AnnouncementMaxDistance,
                    AnnouncementBaseVolume,
                    _announcementsVolume,
                    true,
                    startOffsetSeconds,
                    false,
                    false,
                    AnnouncementSpatialBlend,
                    AnnouncementRolloffMode);
                yield break;
            }

            RestoreLaboratoryPaSources();
            int playbackVersion = _laboratoryPaPlaybackVersion;
            float sliderFactor = GetVolumeFactor(_announcementsVolume);
            AudioSource subtitleClock = null;
            int startedChannels = 0;
            HashSet<int> paObjects = new HashSet<int>();

            for (int i = 0; i < channels.Count; i++)
            {
                AudioSource channel = channels[i];
                if (channel == null)
                    continue;

                LaboratoryPaSourceSnapshot snapshot =
                    LaboratoryPaSourceSnapshot.Capture(channel);
                _activeLaboratoryPaSources.Add(snapshot);

                try
                {
                    channel.Stop();
                    channel.clip = clip;
                    channel.pitch = 1f;
                    channel.loop = false;
                    channel.volume = snapshot.OriginalVolume * sliderFactor;
                    SetAudioSourceOffset(channel, clip, startOffsetSeconds);
                    channel.Play();

                    startedChannels++;
                    paObjects.Add(channel.gameObject.GetInstanceID());

                    if (subtitleClock == null || channel.volume > subtitleClock.volume)
                        subtitleClock = channel;
                }
                catch (Exception exception)
                {
                    Log.LogWarning(
                        "ISB SOF Audio Plugin: A Laboratory PA channel rejected clip '" +
                        clip.name + "': " + exception.Message);
                }
            }

            if (startedChannels == 0)
            {
                RestoreLaboratoryPaSources();

                Log.LogWarning(
                    "ISB SOF Audio Plugin: Laboratory PA channels were detected, but none could " +
                    "start the blackout announcement. Using the existing fallback source.");

                yield return PlayISBSoldiersRadio(
                    clip,
                    null,
                    new Vector3(-170f, 20f, -342f),
                    AnnouncementMinDistance,
                    AnnouncementMaxDistance,
                    AnnouncementBaseVolume,
                    _announcementsVolume,
                    true,
                    startOffsetSeconds,
                    false,
                    false,
                    AnnouncementSpatialBlend,
                    AnnouncementRolloffMode);
                yield break;
            }

            if (_subtitleSystem != null && subtitleClock != null)
                _subtitleSystem.Play(subtitleClock, clip.name);

            Log.LogInfo(
                "ISB SOF Audio Plugin: Routed blackout announcement '" + clip.name +
                "' through " + startedChannels + " Laboratory PA audio channel(s) across " +
                paObjects.Count + " scene object(s). Existing mixer, spatialization and " +
                "environmental channel settings were preserved.");

            float finishAt = Time.realtimeSinceStartup +
                Mathf.Max(clip.length - startOffsetSeconds, 0.05f) + 0.15f;

            while (_raidisActive &&
                   playbackVersion == _laboratoryPaPlaybackVersion &&
                   Time.realtimeSinceStartup < finishAt)
            {
                yield return null;
            }

            if (playbackVersion == _laboratoryPaPlaybackVersion)
                RestoreLaboratoryPaSources();
        }

        private static List<AudioSource> CollectLaboratoryPaChannels()
        {
            AudioSource[] sceneSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
            List<AudioSource> result = new List<AudioSource>();
            HashSet<int> seenSources = new HashSet<int>();

            for (int i = 0; i < sceneSources.Length; i++)
            {
                AudioSource source = sceneSources[i];
                if (source == null || !source.enabled ||
                    source.gameObject == null || !source.gameObject.activeInHierarchy ||
                    !BelongsToLaboratoryPa(source.transform))
                {
                    continue;
                }

                int instanceId = source.GetInstanceID();
                if (seenSources.Add(instanceId))
                    result.Add(source);
            }

            result.Sort(delegate (AudioSource left, AudioSource right)
            {
                float leftVolume = left != null ? left.volume : 0f;
                float rightVolume = right != null ? right.volume : 0f;
                return rightVolume.CompareTo(leftVolume);
            });

            return result;
        }

        private static bool BelongsToLaboratoryPa(Transform sourceTransform)
        {
            Transform current = sourceTransform;
            int remainingDepth = LaboratoryPaParentSearchDepth;

            while (current != null && remainingDepth-- > 0)
            {
                if (!string.IsNullOrEmpty(current.name) &&
                    current.name.IndexOf(
                        LaboratoryPaObjectToken,
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        private static void SetAudioSourceOffset(
            AudioSource source,
            AudioClip clip,
            float startOffsetSeconds)
        {
            if (source == null || clip == null || startOffsetSeconds <= 0f)
                return;

            try
            {
                int maximumSample = Mathf.Max(clip.samples - 1, 0);
                int requestedSample = Mathf.RoundToInt(startOffsetSeconds * clip.frequency);
                source.timeSamples = Mathf.Clamp(requestedSample, 0, maximumSample);
            }
            catch
            {
                try
                {
                    source.time = startOffsetSeconds;
                }
                catch
                {
                }
            }
        }

        private void RestoreLaboratoryPaSources()
        {
            _laboratoryPaPlaybackVersion++;

            for (int i = _activeLaboratoryPaSources.Count - 1; i >= 0; i--)
            {
                LaboratoryPaSourceSnapshot snapshot = _activeLaboratoryPaSources[i];
                if (snapshot != null)
                    snapshot.Restore();
            }

            _activeLaboratoryPaSources.Clear();
        }

        private IEnumerator PlayISBSoldiersRadio(
            AudioClip clip,
            Transform parent,
            Vector3 position,
            float minDistance,
            float maxDistance,
            float baseVolume,
            ConfigEntry<float> volumeEntry,
            bool playMapAnnouncementSubtitle = false,
            float startOffsetSeconds = 0f,
            bool allowGateBTrigger = true,
            bool force2D = false,
            float spatialBlendOverride = -1f,
            AudioRolloffMode rolloffModeOverride = AudioRolloffMode.Logarithmic)
        {
            if (!IsAudioClipReady(clip))
            {
                Log.LogWarning(
                    "ISB SOF Audio Plugin: Refusing to play an invalid audio clip" +
                    (clip != null ? ": " + clip.name : "."));
                yield break;
            }

            startOffsetSeconds = Mathf.Clamp(startOffsetSeconds, 0f, Mathf.Max(0f, clip.length - 0.05f));

            if (!CanPresentLocally)
            {
                float remaining = Mathf.Max(clip.length - startOffsetSeconds, 0.05f);
                yield return new WaitForSeconds(remaining);
                yield break;
            }

            GameObject obj = new GameObject("ISB_Audio");

            if (parent != null)
            {
                obj.transform.SetParent(parent);
                obj.transform.localPosition = position;
            }
            else
            {
                obj.transform.position = position;
            }

            float effectiveVolume = baseVolume * GetVolumeFactor(volumeEntry);

            AudioSource source = obj.AddComponent<AudioSource>();
            source.clip = clip;
            source.spatialBlend = force2D
                ? 0f
                : spatialBlendOverride >= 0f
                    ? Mathf.Clamp01(spatialBlendOverride)
                    : 1f;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = force2D
                ? AudioRolloffMode.Logarithmic
                : rolloffModeOverride;
            source.dopplerLevel = 0f;
            source.spread = force2D ? 0f : 55f;
            source.priority = 48;
            source.volume = effectiveVolume;
            source.loop = false;
            source.playOnAwake = false;

            ActiveISBAudio activeEntry = new ActiveISBAudio
            {
                Obj = obj,
                Source = source,
                BaseVolume = baseVolume,
                VolumeEntry = volumeEntry
            };

            _activeAudioObjects.Add(activeEntry);

            if (startOffsetSeconds > 0f)
                source.time = startOffsetSeconds;

            source.Play();

            if (playMapAnnouncementSubtitle && _subtitleSystem != null)
                _subtitleSystem.Play(source, clip.name);

            Log.LogInfo(
                $"ISB SOF Audio Plugin: AudioSource started | clip: {clip.name} | " +
                $"length: {clip.length:F2}s | " +
                $"volume: {effectiveVolume:F2} (base {baseVolume:F2} x slider {GetVolumeFactor(volumeEntry) * 100f:F0}%) | " +
                $"minDistance: {minDistance:F1} | " +
                $"maxDistance: {maxDistance:F1} | " +
                $"mode: {(force2D ? "2D" : source.spatialBlend < 0.999f ? "Hybrid " + source.spatialBlend.ToString("F2", CultureInfo.InvariantCulture) : "3D")} | " +
                $"rolloff: {source.rolloffMode}"
            );

            float destroyDelay = Mathf.Max((clip.length - startOffsetSeconds) + 0.25f, 0.25f);

            Destroy(obj, destroyDelay);

            yield return new WaitForSeconds(destroyDelay);

            _activeAudioObjects.Remove(activeEntry);
        }

        private IEnumerator DisableGateBAtSynchronizedTime(double closeAt)
        {
            if (!IsLaboratoryPresentationAllowed())
                yield break;

            while (_raidisActive && !_gateBClosed &&
                   IsLaboratoryPresentationAllowed() && SynchronizedTime < closeAt)
            {
                yield return null;
            }

            if (_gateBClosed || !IsLaboratoryPresentationAllowed())
                yield break;

            _gateBClosed = BlackoutBridge.DisableAndHideExfil(
                BlackoutBridge.GateBExfilName,
                Log);

            if (_gateBClosed && IsFikaServer)
                NetworkBridge.Broadcast("G");
        }

        private void DestroyActiveAudioObjects()
        {
            foreach (ActiveISBAudio entry in _activeAudioObjects)
            {
                if (entry != null && entry.Obj != null)
                    Destroy(entry.Obj);
            }

            _activeAudioObjects.Clear();
        }

        private static float GetVolumeFactor(ConfigEntry<float> entry)
        {
            if (entry == null)
                return 1f;

            return Mathf.Clamp01(entry.Value / 100f);
        }

        private void OnVolumeSettingChanged(object sender, EventArgs e)
        {
            for (int i = _activeAudioObjects.Count - 1; i >= 0; i--)
            {
                ActiveISBAudio entry = _activeAudioObjects[i];

                if (entry == null || entry.Obj == null || entry.Source == null)
                {
                    _activeAudioObjects.RemoveAt(i);
                    continue;
                }

                entry.Source.volume = entry.BaseVolume * GetVolumeFactor(entry.VolumeEntry);
            }

            float announcementFactor = GetVolumeFactor(_announcementsVolume);
            for (int i = _activeLaboratoryPaSources.Count - 1; i >= 0; i--)
            {
                LaboratoryPaSourceSnapshot snapshot = _activeLaboratoryPaSources[i];
                if (snapshot == null || snapshot.Source == null)
                {
                    _activeLaboratoryPaSources.RemoveAt(i);
                    continue;
                }

                snapshot.Source.volume = snapshot.OriginalVolume * announcementFactor;
            }
        }

        private void OnAishiCommsSettingChanged(object sender, EventArgs e)
        {
            if (!_raidisActive)
                return;

            IndexAudioPaths();

            if (!_enableAishiRadioChatter.Value)
                _wavsofAishiBots.Clear();
            if (!_enableAishiDeathChatter.Value)
                _wavsofAishiDeaths.Clear();
            if (!_enableAishiHeavyDamageChatter.Value)
                _wavsofAishiHeavyDamage.Clear();

            Log.LogInfo(
                "ISB SOF Audio Plugin: Aishi comms settings changed; " +
                "Aishi audio folders and active pools were refreshed.");
        }

        private void OnDestroy()
        {
            _announcementsVolume.SettingChanged -= OnVolumeSettingChanged;
            _radioVolume.SettingChanged -= OnVolumeSettingChanged;
            _blackoutVolume.SettingChanged -= OnVolumeSettingChanged;
            _enableAishiRadioChatter.SettingChanged -= OnAishiCommsSettingChanged;
            _enableAishiDeathChatter.SettingChanged -= OnAishiCommsSettingChanged;
            _enableAishiHeavyDamageChatter.SettingChanged -= OnAishiCommsSettingChanged;

            if (_subtitleSystem != null)
            {
                _subtitleSystem.Dispose();
                _subtitleSystem = null;
            }

            RestoreLaboratoryPaSources();
            ShutdownOptionalFikaBridge();
        }

        private sealed class LaboratoryPaSourceSnapshot
        {
            public AudioSource Source;
            public AudioClip OriginalClip;
            public float OriginalPitch;
            public float OriginalVolume;
            public bool OriginalLoop;
            public bool WasPlaying;
            public int OriginalTimeSamples;

            public static LaboratoryPaSourceSnapshot Capture(AudioSource source)
            {
                LaboratoryPaSourceSnapshot snapshot = new LaboratoryPaSourceSnapshot
                {
                    Source = source,
                    OriginalClip = source != null ? source.clip : null,
                    OriginalPitch = source != null ? source.pitch : 1f,
                    OriginalVolume = source != null ? source.volume : 1f,
                    OriginalLoop = source != null && source.loop,
                    WasPlaying = source != null && source.isPlaying,
                    OriginalTimeSamples = 0
                };

                if (source != null && source.clip != null)
                {
                    try
                    {
                        snapshot.OriginalTimeSamples = source.timeSamples;
                    }
                    catch
                    {
                    }
                }

                return snapshot;
            }

            public void Restore()
            {
                if (Source == null)
                    return;

                try
                {
                    Source.Stop();
                    Source.clip = OriginalClip;
                    Source.pitch = OriginalPitch;
                    Source.volume = OriginalVolume;
                    Source.loop = OriginalLoop;

                    if (OriginalClip != null && OriginalTimeSamples > 0)
                    {
                        int maximumSample = Mathf.Max(OriginalClip.samples - 1, 0);
                        Source.timeSamples = Mathf.Clamp(
                            OriginalTimeSamples,
                            0,
                            maximumSample);
                    }

                    if (WasPlaying && OriginalClip != null)
                        Source.Play();
                }
                catch
                {
                }
            }
        }

        private sealed class ActiveISBAudio
        {
            public GameObject Obj;

            public AudioSource Source;

            public float BaseVolume;

            public ConfigEntry<float> VolumeEntry;
        }
    }
}