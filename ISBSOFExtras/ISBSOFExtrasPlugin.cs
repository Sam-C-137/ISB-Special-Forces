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
using UnityEngine.SceneManagement;
using System.Text;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace ISBSSOFExtra
{
    internal sealed class LabsPreBlackoutEventStatus
    {
        public string variant { get; set; } = "legacy";
        public ISB.Compatibility.OfficeAccessState officeAccess { get; set; }
        public bool active { get; set; }
        public bool blackDivisionInstalled { get; set; }
        public bool labsChaosEnabled { get; set; }
        public bool disableFire { get; set; }
        public bool disableSirens { get; set; }
        public bool disableDestroyedVehicles { get; set; }
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(FikaPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ManimalLabsBlackoutCompatibility.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public partial class ISBSpecialForcesAudio : BaseUnityPlugin
    {
        public const string PluginGuid = "com.samc137.isbsofextras";
        public const string PluginName = "ISB SOF: Immersion Systems Plugin";
        public const string PluginVersion = "2.0.2";
        public const string FikaPluginGuid = "com.fika.core";
        private const string FikaBridgeAssemblyFileName = "ISBSOF_Extras.Fika.dll";
        private const string FikaBridgeAssemblyName = "ISBSOF_Extras.Fika";
        private const string FikaBridgeBootstrapTypeName = "ISBSSOFExtra.Fika.FikaBridgeBootstrap";
        public static ManualLogSource Log;
        private ConfigEntry<bool> _enableAnnouncements;
        private ConfigEntry<bool> _enableRadioChatter;
        private ConfigEntry<bool> _enableDeathChatter;
        private ConfigEntry<bool> _enableHeavyDamageChatter;
        private ConfigEntry<bool> _enableAishiRadioChatter;
        private ConfigEntry<bool> _enableAishiDeathChatter;
        private ConfigEntry<bool> _enableAishiHeavyDamageChatter;
        private ConfigEntry<bool> _enableWhiteTusksCommon;
        private ConfigEntry<bool> _enableWhiteTuskAishi;
        private ConfigEntry<float> _announcementsVolume;
        private ConfigEntry<float> _alarmVolume;
        private ConfigEntry<float> _deadManSwitchVolume;
        private ConfigEntry<float> _radioVolume;
        private ConfigEntry<int> _laboratoryBlackoutChance;
        private ConfigEntry<bool> _restrictExfilsDuringBlackout;
        private ConfigEntry<float> _blackoutVolume;
        private ConfigEntry<bool> _enablePreBlackoutEvent;
        private ConfigEntry<float> _preBlackoutVolume;
        private ConfigEntry<bool> _enableMapAnnouncementSubtitles;
        private ConfigEntry<string> _subtitleLanguage;
        private ISBSubtitleSystem _subtitleSystem;
        private const string targetmap = "laboratory";
        private const float AnnouncementBaseVolume = 1f;
        private const float AnnouncementMinDistance = 12f;
        private const float AnnouncementMaxDistance = 350f;
        private const float AnnouncementSpatialBlend = 1f;
        private const AudioRolloffMode AnnouncementRolloffMode = AudioRolloffMode.Linear;
        private const float StandaloneBlackoutDelay = 30f;
        private const int AnnouncementPreloadLimit = 32;
        private static readonly HashSet<int> ISBEnums = new HashSet<int>
        {
            13701,
            13702,
            13703,
            13704,
            13705,
            13706,
            13709,
            13710,
            13711,
            13712,
            13713,
            13714,
            13715,
            13716
        };
        private static readonly HashSet<string> LaboratoryPaAnnouncementClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SCPWarn",
            "SCPWarnRepeat",
            "announcer_lights_out_01",
            "announcer_lights_out_02",
            "announcer_lights_out_03",
            "announcer_lights_out_04"
        };
        private const string LaboratoryPaObjectToken = "sound_disclamer";
        private const int LaboratoryPaParentSearchDepth = 6;
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
        private string _ISBSpecialForces_WhiteTusksCommonRadiosFolder;
        private string _ISBSpecialForces_WhiteTusksCommonDeathRadiosFolder;
        private string _ISBSpecialForces_WhiteTusksCommonHeavyDamageRadiosFolder;
        private string _ISBSpecialForces_WhiteTuskAishiRadiosFolder;
        private string _ISBSpecialForces_WhiteTuskAishiDeathRadiosFolder;
        private string _ISBSpecialForces_WhiteTuskAishiHeavyDamageRadiosFolder;
        private string _blackoutSoundPath;
        private string _automaticDoorsPath;
        private string _preBlackoutSequencePath;
        private string _labsAnnouncementsPath;
        private Action<string> _announcementEffectHandler;
        private bool _announcementEffectResolved;
        private bool _announcementEffectFailed;
        private LabsAnnouncementsDefinition _labsAnnouncements = new LabsAnnouncementsDefinition();
        private bool _labsChaosEnabled;
        private bool _labsChaosScheduled;
        private Coroutine _labsChaosRoutine;
        private Coroutine _cameraHuntRoutine;
        private readonly LabsEmergencyElevators _emergencyElevators = new LabsEmergencyElevators();
        private int _emergencyElevatorMask;
        private string _takeoverInfrastructurePath;
        private TakeoverInfrastructureDefinition _takeoverInfrastructure;
        private readonly TakeoverFire _takeoverFire = new TakeoverFire();
        private readonly TakeoverSirens _takeoverSirens = new TakeoverSirens();
        private string _takeoverSirensPath;
        private GameWorld _takeoverSceneryWorld;
        private TakeoverScenerySettings _scenerySettings;
        private bool _disableTakeoverFire;
        private bool _disableTakeoverSirens;
        private bool _disableTakeoverDestroyedVehicles;
        private string _takeoverFirePath;
        private PreBlackoutSequenceDefinition[] _preBlackoutSequences = Array.Empty<PreBlackoutSequenceDefinition>();
        private bool _labsPreBlackoutEventActive;
        private string _selectedLabsVariant = "legacy";
        private bool _takeoverAlarmStarted;
        private bool _takeoverAlarmDisabled;
        private bool _takeoverEmergencyLightsStarted;
        private bool _takeoverEmergencyLightsDisabled;
        private bool _takeoverGatesOpened;
        private bool _takeoverPowerA;
        private bool _takeoverPowerB;
        private bool _takeoverPowerC;
        private bool _takeoverPowerD;
        private bool _takeoverGateA;
        private bool _takeoverGateB;
        private bool _takeoverGateC;
        private bool _takeoverGatesAppliedLocally;
        private bool _blackoutTransitionInProgress;
        private bool _takeoverFacilityRestored;
        private bool _takeoverPanelsInitialized;
        private LabsFacilityControlMode _facilityControlMode;
        private bool _manualShutdownA;
        private bool _manualShutdownB;
        private bool _manualShutdownC;
        private bool _manualShutdownD;
        private bool _manualShutdownCompleted;
        private bool _labsBlackoutSpawnEventTriggered;
        private bool _labsPowerRestoredSpawnEventTriggered;
        private Coroutine _labsBlackoutSpawnEventRoutine;
        private Coroutine _labsPowerRestoredSpawnEventRoutine;
        private Coroutine _facilityPanelEnsureRoutine;
        private const string TakeoverAlarmObjectName = "ISB_Takeover_Facility_Alarm";
        private const string LabsBlackoutPatrolTriggerId = "isb_labs_blackout_patrol";
        private const string LabsBlackoutCheckpointTriggerId = "isb_labs_blackout_checkpoint";
        private const string LabsBlackoutHuntTriggerId = "isb_labs_blackout_hunt";
        private const string LabsPowerRestoredPatrolTriggerId = "isb_labs_power_restored_patrol";
        private const string LabsPowerRestoredCheckpointTriggerId = "isb_labs_power_restored_checkpoint";
        private const string LabsPowerRestoredHuntTriggerId = "isb_labs_power_restored_hunt";
        private GameObject _takeoverAlarmObject;
        private AudioSource _takeoverAlarmSource;
        private Coroutine _takeoverAlarmRoutine;
        private Coroutine _nativeSwitchLockRoutine;
        private int _takeoverAlarmGeneration;
        private float _nextTakeoverAlarmHealthCheck;
        private Dictionary<string, Dictionary<int, List<List<int>>>> _automaticDoors = new Dictionary<string, Dictionary<int, List<List<int>>>>();
        private readonly List<string> _botAudioPaths = new List<string>();
        private readonly List<string> _deathAudioPaths = new List<string>();
        private readonly List<string> _heavyDamageAudioPaths = new List<string>();
        private readonly List<string> _AishiBotAudioPaths = new List<string>();
        private readonly List<string> _AishiDeathAudioPaths = new List<string>();
        private readonly List<string> _AishiHeavyDamageAudioPaths = new List<string>();
        private readonly List<string> _whiteTusksCommonBotAudioPaths = new List<string>();
        private readonly List<string> _whiteTusksCommonDeathAudioPaths = new List<string>();
        private readonly List<string> _whiteTusksCommonHeavyDamageAudioPaths = new List<string>();
        private readonly List<string> _whiteTuskAishiBotAudioPaths = new List<string>();
        private readonly List<string> _whiteTuskAishiDeathAudioPaths = new List<string>();
        private readonly List<string> _whiteTuskAishiHeavyDamageAudioPaths = new List<string>();
        private readonly List<AudioClip> _wavsofBots = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofDeaths = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofHeavyDamage = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofAishiBots = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofAishiDeaths = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofAishiHeavyDamage = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofWhiteTusksCommonBots = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofWhiteTusksCommonDeaths = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofWhiteTusksCommonHeavyDamage = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofWhiteTuskAishiBots = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofWhiteTuskAishiDeaths = new List<AudioClip>();
        private readonly List<AudioClip> _wavsofWhiteTuskAishiHeavyDamage = new List<AudioClip>();
        private readonly Dictionary<string, AudioClip> _audioClipCache = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _loadingAudio = new HashSet<string>();
        private readonly Dictionary<AudioClip, float> _audioLastUsed = new Dictionary<AudioClip, float>();
        private CancellationTokenSource _audioCancellation = new CancellationTokenSource();
        private int _audioGeneration;
        private float _nextAudioCacheTrim;
        private const long AudioCacheBudget = 128L * 1024 * 1024;
        private readonly Dictionary<string, string> _lastRadioPathByCategory = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<Player> _listofactiveBots = new List<Player>();
        private readonly HashSet<Player> _deathSubscribedBots = new HashSet<Player>();
        private readonly List<ActiveISBAudio> _activeAudioObjects = new List<ActiveISBAudio>();
        private readonly List<LaboratoryPaSourceSnapshot> _activeLaboratoryPaSources = new List<LaboratoryPaSourceSnapshot>();
        private int _laboratoryPaPlaybackVersion;
        private readonly Dictionary<Player, float> _lastKnownHealth = new Dictionary<Player, float>();
        private static readonly EBodyPart[] TrackedBodyParts = (EBodyPart[])Enum.GetValues(typeof(EBodyPart));
        private const float HEAVY_DAMAGE_THRESHOLD = 60f;
        private const float HEAVY_DAMAGE_CHECK_INTERVAL = 1.5f;
        private const float HEAVY_DAMAGE_REACTION_DELAY = 2.5f;
        private const float RADIO_MIN_DISTANCE = 1.0f;
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
        private bool _pendingNetworkManualBlackoutTransition;
        private bool _headlessPresentationDisabled;
        private bool _fikaClientReadyAcknowledged;
        private bool _fikaSceneryStateReceived;
        private float _nextFikaSceneryRequest;
        private bool _fikaStartupFailed, _fikaContentRejected;
        private readonly HashSet<string> _fikaCompatiblePlayers = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _fikaReadyPlayers = new HashSet<string>(StringComparer.Ordinal);
        private float _nextFikaReadySendTime;
        private string _currentRaidLocation = string.Empty;
        private string _fikaReportedRaidLocation = string.Empty;
        private GameWorld _activeRaidWorld;
        private bool _fikaRemotePeerSeen;
        private float _fikaRaidStartedObservedAt = -1f;
        private const float FIKA_PRESENTATION_GRACE_SECONDS = 6f;
        private IExtrasNetworkBridge NetworkBridge => ExtrasNetworkBridgeRegistry.Current;
        private bool FikaActive => ISB.Compatibility.ISBRaidMode.UsesNetworkBridge(NetworkBridge.Active, Singleton<AbstractGame>.Instance?.GetType());
        private bool IsFikaNetworkSession => FikaActive && NetworkBridge.Ready;
        private bool IsFikaServer => FikaActive && NetworkBridge.IsServer;
        private bool FikaRequired => Chainloader.PluginInfos.ContainsKey(FikaPluginGuid) && !(Singleton<AbstractGame>.Instance is LocalGame);
        private bool IsEventAuthority => !FikaRequired || FikaActive && NetworkBridge.IsServer;
        private bool IsHeadless => FikaActive && NetworkBridge.IsHeadless;
        private bool CanPresentLocally => !IsHeadless;
        private double SynchronizedTime => IsFikaNetworkSession ? NetworkBridge.NetworkTime : Time.realtimeSinceStartup;

        private static BepInEx.Configuration.ConfigEntry<bool> _diagnosticLogs;
        public static bool DiagnosticsEnabled => _diagnosticLogs?.Value ?? false;

        private void Start()
        {
            Scp173DebugBridge.Enable(Log);
            EFT.UI.ConsoleScreen.Processor.RegisterCommandGroup<Scp173Commands>();
            OptionalBallisticsCompatibility.Enable(Log);
            OptionalScenePresenceCache.Enable(() => _raidisActive && IsLaboratoryPresentationAllowed());
            LocalPlayerModCompatibility.Enable(Log);
            AishiGasMaskCompatibility.Enable(Log);
            WttResourceCompatibility.Enable(Log);
        }

        private void Awake()
        {
            Log = Logger;


            WttResourceCompatibility.Enable(Log);
            _diagnosticLogs = Config.Bind("Logging", "Diagnostic Logs", false, "Enable informational and debug diagnostics for this plugin. Warnings and errors remain enabled.");
            OptionalBallisticsCompatibility.Enable(Log);
            LabsKeycardLockdown.Enable(Log);
            LabsContainmentQuestWarning.Enable(Log);
            LabsVariantMenu.Enable(Log);
            ContainmentAchievementIcons.Enable(Log);
            DecontaminationSurvivalQuest.Enable(CanRecordDecontaminationSurvival, Log);
            DecontaminationSurvivalQuest.OfficeExtraction = RecordOfficeExtraction;
            DecontaminationSurvivalQuest.TenMinuteEligibility = CanRecordTenMinuteSurvival;
            DecontaminationSurvivalQuest.FiveMinuteEligibility = CanRecordFiveMinuteSurvival;
            LabsNativeAnnouncements.Enable(Log);
            LabsTakeoverInfrastructure.EnableControlMenu(Log);
            TakeoverFlameTrigger.EnableImmunity(Log);
            TryInitializeOptionalFikaBridge();
            _fikaPayloadHandler = HandleFikaPayload;
            ManimalLabsBlackoutCompatibility.Enable(Log);
            LabsRenderTextureCompatibility.Enable();
            ManimalLabsCullingCompatibility.Enable(Log);
            if (CanPresentLocally)
            {
                LabsBlackoutExfilUiPatcher.Enable(gameObject, Log);
            }

            _enableAnnouncements = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable Laboratory Announcements", true, "When enabled, upon entering a Labs raid, if ISB operators are present, a series of announcements will play over the facility's intercom system informing of the ISB's presence and the start of its operation in the lab.");
            _enableRadioChatter = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable ISB Radio Comms", true, "When enabled, while on a raid where ISB operators are present, you'll be able to hear radio communications between operators, such as Aishi's callouts to them. Great for immersion and for pinpointing their locations.");
            _enableDeathChatter = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable ISB KIA Comms", true, "When enabled, while on a raid where ISB operators are present, you'll be able to hear Aishi's and HQ's communication with the other ISB operators when a team member is eliminated in the raid. This is great for immersion, pinpointing positions, and also identifying when a squad member has been taken down.");
            _enableHeavyDamageChatter = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable ISB Heavy Damage Comms", true, "When enabled, while on a raid where ISB operators are present, you'll be able to hear Aishi and HQ's communications about operators who have taken a lot of damage. Great for immersion and for pinpointing their current positions.");
            _enableAishiRadioChatter = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable Aishi ISB Radio Comms", true, "When enabled, it adds Aishi's exclusive voice lines to the audio list for coordinating the team, as well as random taunts. Great for immersion and highlighting the bots' positions.");
            _enableAishiDeathChatter = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable Aishi ISB KIA Comms", true, "When enabled, it adds Aishi's exclusive voice lines to the audio list to alert the team when one of the allied operators is eliminated. Great for immersion and revealing the bots' positions.");
            _enableAishiHeavyDamageChatter = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable Aishi ISB Heavy Damage Comms", true, "When enabled, it adds Aishi's exclusive voice lines to the audio list to alert the team when one of the allied operators takes heavy damage but remains alive. Great for immersion and revealing the bots' positions.");
            _enableWhiteTusksCommon = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable White Tusks Common Comms", true, "When enabled, White Tusks use their dedicated common radio, KIA and heavy-damage audio branches when files are available, falling back to the regular ISB pools when they are not.");
            _enableWhiteTuskAishi = Config.Bind("USEC ISB Special Forces Audio Plugin Config", "Enable White Tusk Aishi Comms", true, "When enabled, White Tusks use their dedicated Aishi radio, KIA and heavy-damage audio branches when files are available, falling back to the regular Aishi pools when they are not.");
            _announcementsVolume = AudioVolumeConfig.Bind(Config, "USEC ISB Special Forces Audio Plugin Config", "Laboratory Announcements Volume", 35f, new ConfigDescription("Volume of the Laboratory intercom announcements, from 0 to 100. Changes apply in real time, even while an announcement is already playing.", new AcceptableValueRange<float>(0f, 100f)));
            _alarmVolume = Config.Bind("Audio", "Containment Breach Alarm Volume", 100f, new ConfigDescription("Alarm volume, from 0 to 100. Changes apply immediately.", new AcceptableValueRange<float>(0f, 100f)));
            _alarmVolume.SettingChanged += OnVolumeSettingChanged;
            _deadManSwitchVolume = Config.Bind("Audio", "Dead Man Switch Alarm Volume", _announcementsVolume.Value, new ConfigDescription("Volume of the Dead Man Switch alarm and warning track, from 0 to 100. The voice and alarm share one recording. Changes apply immediately; other announcements and EventEnd keep their own announcement volume.", new AcceptableValueRange<float>(0f, 100f)));
            _deadManSwitchVolume.SettingChanged += OnVolumeSettingChanged;
            _radioVolume = AudioVolumeConfig.Bind(Config, "USEC ISB Special Forces Audio Plugin Config", "ISB Radio Volume", 60f, new ConfigDescription("Volume of all ISB radio communications, from 0 to 100. Changes apply in real time, even while a radio communication is already playing.", new AcceptableValueRange<float>(0f, 100f)));
            _laboratoryBlackoutChance = Config.Bind("USEC ISB Special Forces Blackout Config", "Laboratory Blackout Event Chance", 50, new ConfigDescription("Chance, from 0 to 100, for a normal Laboratory Blackout when the server ContainmentBreach event was not selected. In Fika, the host or headless decides for every player.", new AcceptableValueRange<int>(0, 100), new ConfigUiOrder { Order = 100 }));
            ConfigureContainmentChance();
            _restrictExfilsDuringBlackout = Config.Bind("USEC ISB Special Forces Blackout Config", "Restrict Laboratory Exits", true, "Keeps only the Hangar Gate and Parking Gate exfils available when the blackout starts.");
            _blackoutVolume = AudioVolumeConfig.Bind(Config, "USEC ISB Special Forces Blackout Config", "Blackout Sound Volume", 100f, new ConfigDescription("Volume of black_out_huge.wav, from 0 to 100. Normal blackouts use it when power fails; during a ContainmentBreach it is reserved for the Master emergency power restoration control.", new AcceptableValueRange<float>(0f, 100f)));
            _enablePreBlackoutEvent = Config.Bind("USEC ISB Special Forces Blackout Config", "Enable ContainmentBreach Event", true, "Plays the configured lockdown trigger audio when the server ContainmentBreach event is active for the current Laboratory raid.");
            _preBlackoutVolume = AudioVolumeConfig.Bind(Config, "USEC ISB Special Forces Blackout Config", "ContainmentBreach Volume", 100f, new ConfigDescription("Volume of the ContainmentBreach trigger audio, from 0 to 100.", new AcceptableValueRange<float>(0f, 100f)));
            _enableMapAnnouncementSubtitles = Config.Bind("USEC ISB Special Forces Subtitle Config", "Enable Map Announcement Subtitles", true, "Enables subtitles for the Labs Announcements.");
            _subtitleLanguage = Config.Bind("USEC ISB Special Forces Subtitle Config", "Subtitle Language", "Auto", "Subtitle locale code. Use Auto to follow the current EFT locale. Supported: en, pt, ru and ch (Simplified Chinese; zh-CN is also accepted).");
            _announcementsVolume.SettingChanged += OnVolumeSettingChanged;
            _radioVolume.SettingChanged += OnVolumeSettingChanged;
            _blackoutVolume.SettingChanged += OnVolumeSettingChanged;
            _preBlackoutVolume.SettingChanged += OnVolumeSettingChanged;
            _enableAishiRadioChatter.SettingChanged += OnAishiCommsSettingChanged;
            _enableAishiDeathChatter.SettingChanged += OnAishiCommsSettingChanged;
            _enableAishiHeavyDamageChatter.SettingChanged += OnAishiCommsSettingChanged;
            _enableWhiteTusksCommon.SettingChanged += OnAishiCommsSettingChanged;
            _enableWhiteTuskAishi.SettingChanged += OnAishiCommsSettingChanged;
            string pluginDir = Path.GetDirectoryName(Info.Location);
            _eventContentCheck = new EventContentCheck(pluginDir);
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
            string whiteTusksCommonRoot = Path.Combine(_dataFolder, "ISBSpecialForces_WhiteTusksCommon");
            _ISBSpecialForces_WhiteTusksCommonRadiosFolder = Path.Combine(whiteTusksCommonRoot, "Radios");
            _ISBSpecialForces_WhiteTusksCommonDeathRadiosFolder = Path.Combine(whiteTusksCommonRoot, "DeathRadios");
            _ISBSpecialForces_WhiteTusksCommonHeavyDamageRadiosFolder = Path.Combine(whiteTusksCommonRoot, "HeavyDamageRadios");
            string whiteTuskAishiRoot = Path.Combine(_dataFolder, "ISBSpecialForces_WhiteTuskAishi");
            _ISBSpecialForces_WhiteTuskAishiRadiosFolder = Path.Combine(whiteTuskAishiRoot, "Radios");
            _ISBSpecialForces_WhiteTuskAishiDeathRadiosFolder = Path.Combine(whiteTuskAishiRoot, "DeathRadios");
            _ISBSpecialForces_WhiteTuskAishiHeavyDamageRadiosFolder = Path.Combine(whiteTuskAishiRoot, "HeavyDamageRadios");
            _blackoutSoundPath = Path.Combine(_dataFolder, BlackoutBridge.BlackoutSoundFileName);
            _automaticDoorsPath = ResolveOptionalBlackoutDataPath(pluginDir, BlackoutBridge.AutomaticDoorsFileName);
            _preBlackoutSequencePath = ResolveOptionalBlackoutDataPath(pluginDir, "pre_blackout_sequences.json");
            _labsAnnouncementsPath = ResolveOptionalBlackoutDataPath(pluginDir, "labs_announcements.json");
            _takeoverInfrastructurePath = ResolveOptionalBlackoutDataPath(pluginDir, "takeover_infrastructure.json");
            _takeoverFirePath = ResolveOptionalBlackoutDataPath(pluginDir, "takeover_fire.json");
            _takeoverSirensPath = ResolveOptionalBlackoutDataPath(pluginDir, "takeover_sirens.json");
            _scenerySettings = new TakeoverScenerySettings(_takeoverFirePath, _takeoverSirensPath, Log);
            LabsSpawnSafety.Configure(_takeoverFirePath, () =>
            {
                var status = JsonConvert.DeserializeObject<LabsPreBlackoutEventStatus>(RequestHandler.GetJson("/ISB/labsPreBlackoutEvent/status"));
                return status?.active == true && !status.disableFire;
            }, Log);
            LoadTakeoverInfrastructure();
            Log.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
            _automaticDoors = BlackoutBridge.LoadPathMap(_automaticDoorsPath, Log, "automatic door");
            if (CanPresentLocally)
            {
                _subtitleSystem = new ISBSubtitleSystem(this, Log, _dataFolder, _enableMapAnnouncementSubtitles, _subtitleLanguage);
            }

            StartCoroutine(RaidMonitor());
        }

        private readonly CorpseLightBudget _corpseLights = new CorpseLightBudget();
        private void Update()
        {
            TickScp173();
            _emergencyLighting.Tick(_takeoverAlarmAudio != null ? _takeoverAlarmAudio.Pulse(_takeoverAlarmSource) : 0f);
            if (CanPresentLocally && _raidisActive && IsLaboratoryPresentationAllowed())
                _corpseLights.Tick(_activeRaidWorld, Time.realtimeSinceStartup);
            LabsVariantMenu.Tick(_raidisActive, CanPresentLocally);
            TickContainmentChance();
            LabsKeycardLockdown.TickBattery();
            TickDecontamination();
            TickDecontaminationVideo();
            TickOfficeAccess();
            TickQuestProgressNotifications();
            TickFacilitySequence();
            TickOfficeMonitor();
            _takeoverSirens.Tick(Time.deltaTime, Time.realtimeSinceStartup);
            _takeoverFire.Tick(Time.realtimeSinceStartup, SynchronizedTime);
            MaintainTakeoverAlarmLoopLocal();
            TrimAudioClipCache();
            if (!FikaActive)
                return;
            if (IsFikaNetworkSession && NetworkBridge.RaidStarted && _fikaRaidStartedObservedAt < 0f)
            {
                _fikaRaidStartedObservedAt = Time.realtimeSinceStartup;
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Extras: FikaRaidStartedEvent observed; presentation READY will open after " + FIKA_PRESENTATION_GRACE_SECONDS.ToString("F1", CultureInfo.InvariantCulture) + "s.");
                }
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
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Extras: Headless presentation shutdown completed; authority and synchronization remain active.");
                }
            }

            InvalidateStaleRaidWorldIfNeeded();
            NetworkBridge.DrainReceivedPayloads(_fikaPayloadHandler);
            if (_raidisActive)
            {
                TrySendFikaSceneryRequest();
                TrySendFikaClientReady();
                TryApplyPendingNetworkBlackout();
            }

            if (NetworkBridge.IsServer && NetworkBridge.ConsumePeerConnected())
            {
                _fikaRemotePeerSeen = true;
                if (global::ISBSSOFExtra.ISBSpecialForcesAudio.DiagnosticsEnabled)
                {
                    Log.LogInfo("ISB SOF Extras: Fika peer connected; waiting for its post-raid-start READY handshake before sending presentation state.");
                }
            }
        }

        private void LoadLabsAnnouncements()
        {
            if (!File.Exists(_labsAnnouncementsPath))
            {
                _labsAnnouncements = new LabsAnnouncementsDefinition();
                return;
            }

            try
            {
                var definition = JsonConvert.DeserializeObject<LabsAnnouncementsDefinition>(File.ReadAllText(_labsAnnouncementsPath));
                if (definition == null)
                    throw new InvalidDataException("Expected an object containing regular and blackout sequences.");
                _labsAnnouncements = LabsAnnouncementConfig.Normalize(definition, AnnouncementAudioExists);
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF: Failed to read Labs announcements '" + _labsAnnouncementsPath + "': " + ex.Message);
            }
        }

        private void LoadPreBlackoutSequences()
        {
            if (!File.Exists(_preBlackoutSequencePath))
            {
                _preBlackoutSequences = Array.Empty<PreBlackoutSequenceDefinition>();
                return;
            }

            try
            {
                var definitions = JsonConvert.DeserializeObject<PreBlackoutSequenceDefinition[]>(File.ReadAllText(_preBlackoutSequencePath));
                if (definitions == null)
                    throw new InvalidDataException("Expected an array of ContainmentBreach sequences.");
                _preBlackoutSequences = LabsAnnouncementConfig.NormalizeEvents(definitions, AnnouncementAudioExists);
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF Blackout: Failed to read pre-blackout sequence config '" + _preBlackoutSequencePath + "': " + ex.Message);
            }
        }

        private bool AnnouncementAudioExists(string fileName)
        {
            return File.Exists(Path.Combine(_dataFolder, fileName));
        }

        private void LoadTakeoverInfrastructure()
        {
            _takeoverInfrastructure = null;
            if (string.IsNullOrEmpty(_takeoverInfrastructurePath) || !File.Exists(_takeoverInfrastructurePath))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(_takeoverInfrastructurePath);
                TakeoverInfrastructureDefinition definition = JsonConvert.DeserializeObject<TakeoverInfrastructureDefinition>(json);
                if (definition == null || !definition.enabled)
                    return;
                if (definition.alarm == null)
                    definition.alarm = new TakeoverAlarmDefinition();
                if (definition.gateOverride == null)
                    definition.gateOverride = new TakeoverGateOverrideDefinition();
                if (definition.facilityPower == null)
                    definition.facilityPower = new TakeoverFacilityPowerDefinition();
                if (definition.normalBlackoutPower == null)
                    definition.normalBlackoutPower = new NormalBlackoutPowerDefinition();
                if (definition.manualBlackout == null)
                    definition.manualBlackout = new ManualBlackoutDefinition();
                if (definition.powerRestoration == null)
                    definition.powerRestoration = new TakeoverPowerRestorationAudioDefinition();
                definition.alarm.volume = Mathf.Clamp(definition.alarm.volume, 0f, 100f);
                definition.powerRestoration.volume = Mathf.Clamp(definition.powerRestoration.volume, 0f, 10f);
                if (!string.IsNullOrWhiteSpace(definition.powerRestoration.audio))
                    definition.powerRestoration.audio = definition.powerRestoration.audio.Trim();
                if (definition.gateOverride.gates == null)
                    definition.gateOverride.gates = Array.Empty<string>();
                _takeoverInfrastructure = definition;
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF Takeover: Failed to read infrastructure config '" + _takeoverInfrastructurePath + "': " + ex.Message);
            }
        }

        private bool LoadLabsPreBlackoutEventStatus()
        {
            if (!IsEventAuthority)
                return false;
            try
            {
                string json = RequestHandler.GetJson("/ISB/labsPreBlackoutEvent/status");
                LabsPreBlackoutEventStatus status = JsonConvert.DeserializeObject<LabsPreBlackoutEventStatus>(json);
                _selectedLabsVariant = status?.variant ?? "legacy";
                bool active = status?.active == true;
                if (active)
                    SetOfficeStatus(status.officeAccess);
                _labsChaosEnabled = active && status.labsChaosEnabled;
                SetTakeoverSceneryOptions(status?.disableFire == true, status?.disableSirens == true, status?.disableDestroyedVehicles == true);
                return active;
            }
            catch (Exception ex)
            {
                Log.LogError("ISB SOF Blackout: Could not read server ContainmentBreach status: " + ex.GetType().Name);
                return false;
            }
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

                AnnouncementFade.SetVolume(entry.Source, entry.BaseVolume * GetVolumeFactor(entry.VolumeEntry));
            }

            if (_takeoverAlarmSource != null)
                _takeoverAlarmSource.volume = Mathf.Clamp01((_takeoverInfrastructure?.alarm?.volume ?? 100f) / 100f) * GetVolumeFactor(_alarmVolume);
            if (_officeRadioAudio != null)
                _officeRadioAudio.volume = GetVolumeFactor(_radioVolume);
            if (_facilityAudio != null)
                AnnouncementFade.SetVolume(_facilityAudio, AnnouncementBaseVolume * GetVolumeFactor(_facilityVolume));
            float announcementFactor = GetVolumeFactor(_announcementsVolume);
            for (int i = _activeLaboratoryPaSources.Count - 1; i >= 0; i--)
            {
                LaboratoryPaSourceSnapshot snapshot = _activeLaboratoryPaSources[i];
                if (snapshot == null || snapshot.Source == null)
                {
                    _activeLaboratoryPaSources.RemoveAt(i);
                    continue;
                }

                AnnouncementFade.SetVolume(snapshot.Source, snapshot.OriginalVolume * announcementFactor);
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
            if (!_enableWhiteTusksCommon.Value)
            {
                _wavsofWhiteTusksCommonBots.Clear();
                _wavsofWhiteTusksCommonDeaths.Clear();
                _wavsofWhiteTusksCommonHeavyDamage.Clear();
            }

            if (!_enableWhiteTuskAishi.Value)
            {
                _wavsofWhiteTuskAishiBots.Clear();
                _wavsofWhiteTuskAishiDeaths.Clear();
                _wavsofWhiteTuskAishiHeavyDamage.Clear();
            }
        }

        private void OnDestroy()
        {


            _scp173?.Dispose();
            Scp173DebugBridge.Dispose();
            ResetOfficeAccess();
            DisposeDecontamination();
            StopAllCoroutines();
            _emergencyLighting.Dispose();
            _corpseLights.Clear();
            OptionalBallisticsCompatibility.Disable();
            LabsKeycardLockdown.Disable();
            LabsContainmentQuestWarning.Disable();
            LabsVariantMenu.Disable();
            ContainmentAchievementIcons.Disable();
            DecontaminationSurvivalQuest.Disable();
            LabsNativeAnnouncements.Disable();
            LabsFinalLockdown.Disable();
            LabsTakeoverInfrastructure.DisableControlMenu();
            TakeoverFlameTrigger.DisableImmunity();
            LabsSpawnSafety.Disable();
            _emergencyElevators.Reset();
            DestroyActiveAudioObjects();
            StopTakeoverAlarmLocal();
            RestoreLaboratoryPaSources();
            DestroyAudioClipCache();
            _audioCancellation.Dispose();
            ManimalLabsBlackoutCompatibility.Dispose();
            LabsRenderTextureCompatibility.Dispose();
            ManimalLabsCullingCompatibility.Dispose();
            OptionalScenePresenceCache.Dispose();
            LocalPlayerModCompatibility.Dispose();
            AishiGasMaskCompatibility.Dispose();
            WttResourceCompatibility.Dispose();
            _takeoverFire.Dispose();
            _takeoverSirens.Dispose();
            _announcementsVolume.SettingChanged -= OnVolumeSettingChanged;
            _alarmVolume.SettingChanged -= OnVolumeSettingChanged;
            _deadManSwitchVolume.SettingChanged -= OnVolumeSettingChanged;
            _radioVolume.SettingChanged -= OnVolumeSettingChanged;
            _blackoutVolume.SettingChanged -= OnVolumeSettingChanged;
            _preBlackoutVolume.SettingChanged -= OnVolumeSettingChanged;
            _enableAishiRadioChatter.SettingChanged -= OnAishiCommsSettingChanged;
            _enableAishiDeathChatter.SettingChanged -= OnAishiCommsSettingChanged;
            _enableAishiHeavyDamageChatter.SettingChanged -= OnAishiCommsSettingChanged;
            _enableWhiteTusksCommon.SettingChanged -= OnAishiCommsSettingChanged;
            _enableWhiteTuskAishi.SettingChanged -= OnAishiCommsSettingChanged;
            if (_subtitleSystem != null)
            {
                _subtitleSystem.Dispose();
                _subtitleSystem = null;
            }

            RestoreLaboratoryPaSources();
            ShutdownOptionalFikaBridge();
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
