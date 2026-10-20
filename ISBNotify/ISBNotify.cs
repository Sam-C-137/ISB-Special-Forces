using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Communications;
using SPT.Reflection.Patching;
using SPT.Common.Http;
using System.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ISBInfo.Networking;
using UnityEngine;

namespace ISBInfo
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(FikaPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class ISBInfoPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.samc137.ISBinfo";
        public const string PluginName = "ISB SOF Notifier";
        public const string PluginVersion = "1.1.1";
        public const string FikaPluginGuid = "com.fika.core";
        private const string FikaBridgeAssemblyFileName = "ISBNotify.Fika.dll";
        private const string FikaBridgeAssemblyName = "ISBNotify.Fika";
        private const string FikaBridgeBootstrapTypeName = "ISBInfo.Fika.ISBNotifyFikaBootstrap";
        private const float INITIAL_DELAY_FOR_COUNT = 20f;
        private const float DEATH_NOTIFICATION_DELAY = 3f;
        private const float MANUAL_NOTIFICATION_COOLDOWN = 3f;
        private const float PRESENTATION_STATE_REFRESH_INTERVAL = 0.5f;
        private const int ISBCommanderEnum = 13707;
        private const int ISBFollowerCommanderEnum = 13708;
        private static ConfigEntry<bool> EnablePlugin;
        private static ConfigEntry<bool> OnlyInitialAlert;
        private static ConfigEntry<bool> EnableCommanderAlert;
        private static ConfigEntry<bool> EnableCommanderKIA;
        private static bool ISBSpawnedThisRaid;
        private static bool InitialMessageShown;
        private static bool NoAliveMessageShown;
        private static bool CommanderAlertShown;
        private static bool LabsPresenceShown;
        private static bool CurrentRaidIsLaboratory;
        private static bool LabsTakeoverActive;
        private static bool LabsTakeoverStatusResolved;
        private static bool LocalPlayerObservedInRaid;
        private static bool LocalRaidPresentationEnded;
        private static float NextPresentationStateRefreshTime;
        private static readonly HashSet<int> CommanderKIAShown = new HashSet<int>();
        private static readonly HashSet<string> DeathNotificationsScheduled = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, CachedISBOperator> CachedISBOperators = new Dictionary<string, CachedISBOperator>(StringComparer.Ordinal);
        private static readonly Dictionary<int, int> ISBAliveCounts = new Dictionary<int, int>();
        private static readonly AnnouncementEffectNotices EffectNotices = new AnnouncementEffectNotices();
        private readonly Dictionary<string, Coroutine> _pendingEffectNotices = new Dictionary<string, Coroutine>(StringComparer.Ordinal);
        private int _effectNoticeGeneration;
        private static ISBInfoPlugin Instance;
        private IISBNotifyNetworkBridge _networkBridge = NoNetworkBridge.Instance;
        private MethodInfo _fikaBridgeShutdownMethod;
        private bool _fikaBridgeInitialized;
        private bool _networkClientReadyAcknowledged;
        private bool _networkPresentationClientReady;
        private bool _networkRaidStateInitialized;
        private float _nextNetworkReadySendTime;
        private float _nextManualNotificationTime;
        private static int NetworkCommanderCount;
        private static bool NetworkCountsInitialized;
        private const int NetworkReasonStateOnly = 0;
        private const int NetworkReasonCounts = 1;
        private const int NetworkReasonWithdrawal = 2;
        private const int NetworkReasonCommanderAlert = 3;
        private const int NetworkReasonCommanderKia = 4;
        private const int NetworkReasonWhiteTuskKia = 5;
        private const int NetworkReasonOperatorKia = 6;
        private const int NetworkReasonLabsPresence = 7;
        private static readonly Dictionary<int, string> ISBNames = new Dictionary<int, string>
        {
            {
                13702,
                "Firefly (The King of Kings)"
            },
            {
                13703,
                "Vipper (The Riskrunner)"
            },
            {
                13704,
                "Loghan (The Dreadnought)"
            },
            {
                13705,
                "Spearthrower (The Shadow Lord)"
            },
            {
                13715,
                "Evernight (The Chosen One)"
            },
            {
                13716,
                "Nemesis (The Dark Lord)"
            },
            {
                13706,
                "Zofia (The Deadeye)"
            },
            {
                13709,
                "ISB Team Leader"
            },
            {
                13710,
                "ISB Specialist"
            },
            {
                13701,
                "ISB Operative"
            },
            {
                13711,
                "ISB Scout"
            },
            {
                13712,
                "ISB Heavy"
            },
            {
                13713,
                "ISB Assassin"
            },
            {
                13714,
                "ISB Marksman"
            },
            {
                13707,
                "\"The Chosen One\""
            },
            {
                13708,
                "\"The Dark Lord\""
            },
        };
        private static readonly Dictionary<int, string> ISBEmojis = new Dictionary<int, string>
        {
            {
                13702,
                "\u2666"
            },
            {
                13703,
                "\u2666"
            },
            {
                13704,
                "\u2666"
            },
            {
                13705,
                "\u2666"
            },
            {
                13706,
                "\u2666"
            },
            {
                13709,
                "\u2666"
            },
            {
                13710,
                "\u25A0"
            },
            {
                13701,
                "\u25CF"
            },
            {
                13711,
                "\u2666"
            },
            {
                13712,
                "\u25A0"
            },
            {
                13713,
                "\u2666"
            },
            {
                13714,
                "\u2666"
            },
            {
                13715,
                "\u2666"
            },
            {
                13716,
                "\u2666"
            },
            {
                13707,
                "\u2666"
            },
            {
                13708,
                "\u2666"
            },
        };
        private const string DefaultEmoji = "\u25CF";
        private const string TotalBarSymbol = "\u2620";
        private static readonly int[] DisplayOrder =
        {
            13702,
            13703,
            13704,
            13705,
            13715,
            13716,
            13706,
            13709,
            13710,
            13713,
            13711,
            13712,
            13714,
            13701,
        };
        private static readonly int[] WhiteTuskOrder =
        {
            13702,
            13703,
            13704,
            13705,
            13715,
            13716,
            13706,
        };
        private static readonly int[] OperatorOrder =
        {
            13709,
            13710,
            13713,
            13711,
            13712,
            13714,
            13701,
        };
        private static readonly Dictionary<int, string> ISBShortNames = new Dictionary<int, string>
        {
            {
                13702,
                "Firefly"
            },
            {
                13703,
                "Vipper"
            },
            {
                13704,
                "Loghan"
            },
            {
                13705,
                "Spearthrower"
            },
            {
                13715,
                "Evernight"
            },
            {
                13716,
                "Nemesis"
            },
            {
                13706,
                "Zofia"
            },
            {
                13709,
                "Team Leader"
            },
            {
                13710,
                "Specialist"
            },
            {
                13713,
                "Assassin"
            },
            {
                13711,
                "Scout"
            },
            {
                13712,
                "Heavy"
            },
            {
                13714,
                "Marksman"
            },
            {
                13701,
                "Operative"
            },
        };
        private sealed class CachedISBOperator
        {
            public Player Player;
            public int Role;
            public bool Alive;
        }

        private static BepInEx.Configuration.ConfigEntry<bool> _diagnosticLogs;
        public static bool DiagnosticsEnabled => _diagnosticLogs?.Value ?? false;

        private void Awake()
        {
            Instance = this;
            _diagnosticLogs = Config.Bind("Logging", "Diagnostic Logs", false, "Enable informational and debug diagnostics for this plugin. Warnings and errors remain enabled.");
            EnablePlugin = Config.Bind("Settings for the ISB SOF Notifier plugin", "Enable in-raid Notifier", true, "When enabled, if you are in a raid where ISB operations are taking place, you will receive a notification indicating which types of Operatives are active and their respective numbers.");
            OnlyInitialAlert = Config.Bind("Settings for the ISB SOF Notifier plugin", "Only Show Initial Alert", false, "When enabled, you will only receive the single initial notification at the start of the raid, and not the recurring updates that refresh as Operatives are eliminated. You can still press the O key at any time to manually check the current status.");
            EnableCommanderAlert = Config.Bind("Settings for the ISB SOF Notifier plugin", "Enable Commander Alert", true, "When enabled, a special warning is shown when an ISB Commander and his guard are deployed to hunt you. Disable this to remove that specific alert without affecting the regular ISB notifications.");
            EnableCommanderKIA = Config.Bind("Settings for the ISB SOF Notifier plugin", "Enable Commander KIA notification", true, "When enabled, a dedicated notification is shown when an ISB Commander or his guard is eliminated, separate from the regular ISB withdrawal message.");
            new ISBSpawnPatch().Enable();
            new RaidStartPatch().Enable();
            new ISBDeathPatch().Enable();
            TryInitializeOptionalFikaBridge();
            Logger.LogInfo(PluginName + " v" + PluginVersion + " loaded.");
        }

        private void TryInitializeOptionalFikaBridge()
        {
            if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid))
                return;
            string pluginDirectory = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                Logger.LogError("ISB SOF Notifier: Fika is installed, but the plugin directory could not be resolved.");
                return;
            }

            string bridgePath = Path.Combine(pluginDirectory, FikaBridgeAssemblyFileName);
            if (!File.Exists(bridgePath))
            {
                Logger.LogError("ISB SOF Notifier: Fika is installed, but " + FikaBridgeAssemblyFileName + " was not found next to ISBNotify.dll. Synchronization is disabled.");
                return;
            }

            try
            {
                Assembly bridgeAssembly = FindLoadedAssembly(FikaBridgeAssemblyName) ?? Assembly.LoadFrom(bridgePath);
                Type bootstrapType = bridgeAssembly.GetType(FikaBridgeBootstrapTypeName, true);
                MethodInfo initializeMethod = bootstrapType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                _fikaBridgeShutdownMethod = bootstrapType.GetMethod("Shutdown", BindingFlags.Public | BindingFlags.Static);
                if (initializeMethod == null || _fikaBridgeShutdownMethod == null)
                {
                    throw new MissingMethodException(FikaBridgeBootstrapTypeName, "Initialize/Shutdown");
                }

                initializeMethod.Invoke(null, new object[] { Logger });
                _fikaBridgeInitialized = _networkBridge != null && _networkBridge.Active;
                if (!_fikaBridgeInitialized)
                {
                    Logger.LogError("ISB SOF Notifier: the optional Fika bridge loaded, but did not become active.");
                }
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Logger.LogError("ISB SOF Notifier: optional Fika bridge initialization failed: " + cause.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
            catch (Exception ex)
            {
                Logger.LogError("ISB SOF Notifier: optional Fika bridge initialization failed: " + ex.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                    continue;
                try
                {
                    if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    {
                        return assembly;
                    }
                }
                catch
                {
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
            catch (TargetInvocationException)
            {
            }
            catch (Exception ex)
            {
                Logger.LogWarning("ISB SOF Notifier: optional Fika bridge shutdown failed: " + ex.Message);
            }
            finally
            {
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
        }

        public static bool InstallNetworkBridge(IISBNotifyNetworkBridge bridge)
        {
            ISBInfoPlugin instance = Instance;
            if (instance == null || bridge == null || !bridge.Active)
                return false;
            if (instance._networkBridge != null && !ReferenceEquals(instance._networkBridge, NoNetworkBridge.Instance) && !ReferenceEquals(instance._networkBridge, bridge))
            {
                instance.Logger.LogWarning("ISB SOF Notifier: another network bridge is already installed.");
                return false;
            }

            instance._networkBridge = bridge;
            instance.ResetNetworkHandshakeState();
            if (bridge.IsHeadless)
            {
                if (global::ISBInfo.ISBInfoPlugin.DiagnosticsEnabled)
                {
                    instance.Logger.LogInfo("ISB SOF Notifier: Fika headless mode detected. " + "This process will synchronize state without presenting local UI.");
                }
            }

            if (global::ISBInfo.ISBInfoPlugin.DiagnosticsEnabled)
            {
                instance.Logger.LogInfo("ISB SOF Notifier: optional network bridge attached. ready=" + bridge.Ready + ", server=" + bridge.IsServer + ", headless=" + bridge.IsHeadless + ".");
            }

            return true;
        }

        public static void RemoveNetworkBridge(IISBNotifyNetworkBridge bridge)
        {
            ISBInfoPlugin instance = Instance;
            if (instance == null || !ReferenceEquals(instance._networkBridge, bridge))
                return;
            instance._networkBridge = NoNetworkBridge.Instance;
            instance.ResetNetworkHandshakeState();
            if (global::ISBInfo.ISBInfoPlugin.DiagnosticsEnabled)
            {
                instance.Logger.LogInfo("ISB SOF Notifier: optional network bridge detached.");
            }
        }

        private void ResetNetworkHandshakeState()
        {
            _networkClientReadyAcknowledged = false;
            _networkPresentationClientReady = false;
            _nextNetworkReadySendTime = 0f;
            NetworkCommanderCount = 0;
            NetworkCountsInitialized = false;
        }

        private void Update()
        {
            if (IsNetworkActive())
            {
                TrySendNetworkClientReady();
                if (IsEventAuthority() || IsLocalNetworkClientRaidReady())
                    _networkBridge.DrainReceivedPayloads(HandleNetworkPayload);
            }

            if (!IsPluginEnabled() || !CanPresentLocally())
                return;
            if (InitialMessageShown && UnityInput.Current.GetKeyDown(KeyCode.O))
            {
                if (Time.realtimeSinceStartup < _nextManualNotificationTime)
                    return;
                _nextManualNotificationTime = Time.realtimeSinceStartup + MANUAL_NOTIFICATION_COOLDOWN;
                if (IsEventAuthority())
                    RefreshAliveCountsFromCache();
                if (UseLabsSpecialNotifier())
                {
                    if (GetTotalAliveISBCount() > 0 || GetAliveCommanderCount() > 0)
                        ShowLabsPresenceMessage();
                    else
                        ShowLabsNoPresenceMessage();
                    return;
                }

                int totalAlive = GetTotalAliveISBCount();
                int commandersAlive = GetAliveCommanderCount();
                if (totalAlive > 0)
                {
                    ShowCurrentAliveMessage();
                    NoAliveMessageShown = false;
                }

                if (commandersAlive > 0)
                {
                    ShowCommanderStatus();
                }

                if (totalAlive <= 0 && commandersAlive <= 0 && ISBSpawnedThisRaid)
                {
                    ShowNoAliveISBMessage();
                    NoAliveMessageShown = true;
                }
            }
        }

        private static bool IsNetworkActive()
        {
            return Instance != null && Instance._networkBridge != null && ISB.Compatibility.ISBRaidMode.UsesNetworkBridge(Instance._networkBridge.Active, Singleton<AbstractGame>.Instance?.GetType());
        }

        private static bool IsNetworkSession()
        {
            return IsNetworkActive() && Instance._networkBridge.Ready;
        }

        private static bool IsEventAuthority()
        {
            return !IsNetworkActive() || Instance._networkBridge.IsServer;
        }

        private static bool IsHeadless()
        {
            return IsNetworkActive() && Instance._networkBridge.IsHeadless;
        }

        private static bool CanPresentLocally()
        {
            if (IsHeadless())
                return false;
            RefreshLocalRaidPresentationState();
            return !LocalRaidPresentationEnded;
        }

        private static void RefreshLocalRaidPresentationState()
        {
            if (LocalRaidPresentationEnded || IsHeadless())
                return;
            float now = Time.realtimeSinceStartup;
            if (now < NextPresentationStateRefreshTime)
                return;
            NextPresentationStateRefreshTime = now + PRESENTATION_STATE_REFRESH_INTERVAL;
            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
            {
                if (LocalPlayerObservedInRaid)
                    LocalRaidPresentationEnded = true;
                return;
            }

            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            Player mainPlayer = gameWorld.MainPlayer;
            if (mainPlayer == null)
            {
                if (LocalPlayerObservedInRaid)
                    LocalRaidPresentationEnded = true;
                return;
            }

            if (mainPlayer.HealthController != null && !mainPlayer.HealthController.IsAlive)
            {
                LocalPlayerObservedInRaid = true;
                LocalRaidPresentationEnded = true;
                return;
            }

            if (gameWorld.AllAlivePlayersList == null)
                return;
            bool playerStillInRaid = false;
            foreach (Player player in gameWorld.AllAlivePlayersList)
            {
                if (ReferenceEquals(player, mainPlayer))
                {
                    playerStillInRaid = true;
                    break;
                }
            }

            if (playerStillInRaid)
            {
                LocalPlayerObservedInRaid = true;
                return;
            }

            if (LocalPlayerObservedInRaid)
                LocalRaidPresentationEnded = true;
        }

        private static bool ShouldTrackState()
        {
            return IsPluginEnabled() || IsNetworkActive();
        }

        private static void BroadcastNetworkSnapshot(int reason, int commanderRole = 0)
        {
            if (!IsNetworkSession() || !Instance._networkBridge.IsServer)
                return;
            Instance._networkBridge.Broadcast(BuildNetworkSnapshot(reason, commanderRole));
        }

        private void HandleNetworkPayload(string payload)
        {
            if (string.IsNullOrEmpty(payload))
                return;
            string[] parts = payload.Split('|');
            if (parts.Length == 0)
                return;
            if (parts[0] == "F")
            {
                if (parts.Length == 2 && !IsEventAuthority() && Singleton<GameWorld>.Instance != null && IsLaboratoryRaid())
                    ShowAnnouncementEffect(EffectNotices.Record(parts[1]));
                return;
            }

            if (parts[0] == "Q")
            {
                if (IsNetworkSession() && _networkBridge.IsServer)
                    HandleNetworkClientReady(parts);
                return;
            }

            if (parts[0] == "K")
            {
                if (!IsEventAuthority())
                    _networkClientReadyAcknowledged = true;
                return;
            }

            ApplyNetworkSnapshot(payload);
        }

        public static void BeginAnnouncement(string audioName, float notificationDelaySeconds)
        {
            if (Instance == null || !IsEventAuthority() || Singleton<GameWorld>.Instance == null || !IsLaboratoryRaid())
                return;
            EffectNotices.Begin(audioName, Time.realtimeSinceStartup, notificationDelaySeconds);
        }

        public static void ReportAnnouncementEffect(string effectId)
        {
            if (Instance == null || !IsEventAuthority() || Singleton<GameWorld>.Instance == null || !IsLaboratoryRaid())
                return;
            string message = EffectNotices.Record(effectId);
            if (message == null)
                return;
            float delay = EffectNotices.Remaining(effectId, Time.realtimeSinceStartup);
            if (delay > 0f)
            {
                Instance._pendingEffectNotices[effectId] = Instance.StartCoroutine(Instance.DeliverAnnouncementEffectAfterDelay(effectId, message, delay, Singleton<GameWorld>.Instance, Instance._effectNoticeGeneration));
                return;
            }

            DeliverAnnouncementEffect(effectId, message);
        }

        private IEnumerator DeliverAnnouncementEffectAfterDelay(string effectId, string message, float delay, GameWorld world, int generation)
        {
            yield return new WaitForSecondsRealtime(delay);
            if (generation != _effectNoticeGeneration)
                yield break;
            _pendingEffectNotices.Remove(effectId);
            if (!ReferenceEquals(Singleton<GameWorld>.Instance, world) || world == null || !IsEventAuthority() || !IsLaboratoryRaid())
                yield break;
            DeliverAnnouncementEffect(effectId, message);
        }

        private static void DeliverAnnouncementEffect(string effectId, string message)
        {
            if (IsNetworkSession() && Instance._networkBridge.IsServer)
                Instance._networkBridge.Broadcast("F|" + effectId);
            ShowAnnouncementEffect(message);
        }

        private void CancelPendingEffectNotices()
        {
            _effectNoticeGeneration++;
            foreach (var routine in _pendingEffectNotices.Values)
                if (routine != null)
                    StopCoroutine(routine);
            _pendingEffectNotices.Clear();
        }

        private static void ShowAnnouncementEffect(string message)
        {
            if (message == null || !IsPluginEnabled() || !CanPresentLocally())
                return;
            NotificationManagerClass.DisplayNotification(new AnnouncementEffectNotification(ISB.Compatibility.ISBLocalizedText.Get(message, AnnouncementEffectNotices.Fallback(message))));
        }

        private static bool IsLocalNetworkClientRaidReady()
        {
            RefreshLocalRaidPresentationState();
            return !LocalRaidPresentationEnded && Singleton<GameWorld>.Instantiated && Singleton<GameWorld>.Instance != null && Singleton<GameWorld>.Instance.MainPlayer != null;
        }

        private void TrySendNetworkClientReady()
        {
            if (!IsNetworkSession() || IsEventAuthority() || IsHeadless() || _networkClientReadyAcknowledged || !_networkBridge.Ready)
            {
                return;
            }

            if (!IsLocalNetworkClientRaidReady())
                return;
            if (Time.realtimeSinceStartup < _nextNetworkReadySendTime)
                return;
            _nextNetworkReadySendTime = Time.realtimeSinceStartup + 2f;
            string readyLocation = ResolveRaidLocation(Singleton<GameWorld>.Instance);
            string readyPayload = "Q|READY|" + (readyLocation ?? string.Empty).Replace("|", string.Empty);
            _networkBridge.SendToServer(readyPayload);
        }

        private void HandleNetworkClientReady(string[] parts)
        {
            if (parts.Length < 2 || parts[1] != "READY" || !_networkRaidStateInitialized)
                return;
            if (parts.Length >= 3 && IsLaboratoryLocation(parts[2]))
                CurrentRaidIsLaboratory = true;
            bool firstReadyClient = !_networkPresentationClientReady;
            _networkPresentationClientReady = true;
            RefreshAliveCountsFromCache();
            _networkBridge.Broadcast("K|READY");
            _networkBridge.Broadcast(BuildNetworkSnapshot(NetworkReasonStateOnly, 0));
            if (global::ISBInfo.ISBInfoPlugin.DiagnosticsEnabled)
            {
                Logger.LogInfo(firstReadyClient ? "ISB SOF Notifier: A Fika player client is raid-ready. Headless notifier timeline may now start." : "ISB SOF Notifier: A late/reconnected Fika client is raid-ready; current notifier state was resent.");
            }
        }

        private static IEnumerator WaitForHeadlessPresentationClient()
        {
            if (!IsNetworkActive() || !IsHeadless() || !IsEventAuthority())
                yield break;
            while (Instance != null && IsNetworkActive() && !Instance._networkPresentationClientReady)
            {
                yield return new WaitForSeconds(0.25f);
            }
        }

        private static string BuildNetworkSnapshot(int reason, int commanderRole)
        {
            int commanderCount = GetAliveCommanderCountLocal();
            string[] parts = new string[9 + DisplayOrder.Length];
            parts[0] = "N";
            parts[1] = reason.ToString();
            parts[2] = ISBSpawnedThisRaid ? "1" : "0";
            parts[3] = NoAliveMessageShown ? "1" : "0";
            parts[4] = commanderCount.ToString();
            parts[5] = commanderRole.ToString();
            parts[6] = InitialMessageShown ? "1" : "0";
            parts[7] = IsLaboratoryRaid() ? "1" : "0";
            parts[8] = UseLabsSpecialNotifier() ? "1" : "0";
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                int count;
                ISBAliveCounts.TryGetValue(DisplayOrder[i], out count);
                parts[9 + i] = count.ToString();
            }

            return string.Join("|", parts);
        }

        private static void ApplyNetworkSnapshot(string payload)
        {
            if (string.IsNullOrEmpty(payload) || IsEventAuthority())
                return;
            string[] parts = payload.Split('|');
            if (parts.Length < 9 + DisplayOrder.Length || parts[0] != "N")
                return;
            if (!int.TryParse(parts[1], out int reason) || !int.TryParse(parts[4], out int commanderCount) || !int.TryParse(parts[5], out int commanderRole))
            {
                return;
            }

            bool hadCountSnapshot = NetworkCountsInitialized;
            ISBSpawnedThisRaid = parts[2] == "1";
            NoAliveMessageShown = parts[3] == "1";
            InitialMessageShown = parts[6] == "1" || reason != NetworkReasonStateOnly;
            CurrentRaidIsLaboratory = parts[7] == "1";
            LabsTakeoverActive = CurrentRaidIsLaboratory && parts[8] == "1";
            LabsTakeoverStatusResolved = true;
            NetworkCommanderCount = commanderCount;
            if ((reason == NetworkReasonCounts || reason == NetworkReasonWithdrawal) || (reason == NetworkReasonStateOnly && InitialMessageShown))
            {
                NetworkCountsInitialized = true;
            }

            ISBAliveCounts.Clear();
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if (int.TryParse(parts[9 + i], out int count) && count > 0)
                    ISBAliveCounts[DisplayOrder[i]] = count;
            }

            if (!IsPluginEnabled())
                return;
            if (UseLabsSpecialNotifier())
            {
                int totalAliveInLabs = GetTotalAliveISBCount() + NetworkCommanderCount;
                if (InitialMessageShown && totalAliveInLabs > 0 && !LabsPresenceShown)
                {
                    LabsPresenceShown = true;
                    ShowLabsPresenceMessage();
                }

                return;
            }

            switch (reason)
            {
                case NetworkReasonStateOnly:
                    if (!hadCountSnapshot && InitialMessageShown)
                    {
                        int totalAlive = GetTotalAliveISBCount();
                        if (totalAlive > 0)
                            ShowCurrentAliveMessage();
                        else if (ISBSpawnedThisRaid && NetworkCommanderCount <= 0)
                            ShowNoAliveISBMessage();
                        if (NetworkCommanderCount > 0 && !CommanderAlertShown)
                        {
                            CommanderAlertShown = true;
                            ShowCommanderAlert();
                        }
                    }

                    break;
                case NetworkReasonCounts:
                    if (!IsOnlyInitialAlert() || !hadCountSnapshot)
                        ShowCurrentAliveMessage();
                    break;
                case NetworkReasonWithdrawal:
                    if (!IsOnlyInitialAlert() || !hadCountSnapshot)
                        ShowNoAliveISBMessage();
                    break;
                case NetworkReasonCommanderAlert:
                    CommanderAlertShown = true;
                    ShowCommanderAlert();
                    break;
                case NetworkReasonCommanderKia:
                    if (commanderRole == ISBCommanderEnum || commanderRole == ISBFollowerCommanderEnum)
                    {
                        CommanderKIAShown.Add(commanderRole);
                        ShowCommanderKIA((WildSpawnType)commanderRole);
                    }

                    break;
                case NetworkReasonWhiteTuskKia:
                    ShowWhiteTuskKIA((WildSpawnType)commanderRole);
                    break;
                case NetworkReasonOperatorKia:
                    ShowOperatorKIA((WildSpawnType)commanderRole);
                    break;
            }
        }

        public static void MarkLocalRaidPresentationEnded()
        {
            LocalPlayerObservedInRaid = true;
            LocalRaidPresentationEnded = true;
        }

        private static bool IsPluginEnabled()
        {
            return EnablePlugin == null || EnablePlugin.Value;
        }

        private static bool IsOnlyInitialAlert()
        {
            return OnlyInitialAlert != null && OnlyInitialAlert.Value;
        }

        private static void ResetState()
        {
            Instance?.CancelPendingEffectNotices();
            EffectNotices.Reset();
            ISBSpawnedThisRaid = false;
            InitialMessageShown = false;
            NoAliveMessageShown = false;
            CommanderAlertShown = false;
            LabsPresenceShown = false;
            CurrentRaidIsLaboratory = false;
            LabsTakeoverActive = false;
            LabsTakeoverStatusResolved = false;
            LocalPlayerObservedInRaid = false;
            LocalRaidPresentationEnded = false;
            NextPresentationStateRefreshTime = 0f;
            CommanderKIAShown.Clear();
            DeathNotificationsScheduled.Clear();
            CachedISBOperators.Clear();
            NetworkCommanderCount = 0;
            NetworkCountsInitialized = false;
            if (Instance != null)
            {
                Instance.ResetNetworkHandshakeState();
                Instance._networkRaidStateInitialized = false;
                Instance._nextManualNotificationTime = 0f;
            }

            ISBAliveCounts.Clear();
        }

        private static bool IsCommanderRole(WildSpawnType role)
        {
            return role == (WildSpawnType)ISBCommanderEnum || role == (WildSpawnType)ISBFollowerCommanderEnum;
        }

        private static bool IsISBRole(WildSpawnType role)
        {
            return role == (WildSpawnType)13701 || role == (WildSpawnType)13702 || role == (WildSpawnType)13703 || role == (WildSpawnType)13704 || role == (WildSpawnType)13705 || role == (WildSpawnType)13706 || role == (WildSpawnType)13709 || role == (WildSpawnType)13710 || role == (WildSpawnType)13711 || role == (WildSpawnType)13712 || role == (WildSpawnType)13713 || role == (WildSpawnType)13714 || role == (WildSpawnType)13715 || role == (WildSpawnType)13716;
        }

        private static bool IsWhiteTuskRole(WildSpawnType role)
        {
            int value = (int)role;
            for (int i = 0; i < WhiteTuskOrder.Length; i++)
            {
                if (WhiteTuskOrder[i] == value)
                    return true;
            }

            return false;
        }

        private static bool IsOperatorRole(WildSpawnType role)
        {
            int value = (int)role;
            for (int i = 0; i < OperatorOrder.Length; i++)
            {
                if (OperatorOrder[i] == value)
                    return true;
            }

            return false;
        }

        private static string GetCacheKey(Player player)
        {
            if (player == null)
                return string.Empty;
            if (!string.IsNullOrEmpty(player.ProfileId))
                return player.ProfileId;
            return player.GetInstanceID().ToString();
        }

        private static void RegisterCachedISBPlayer(Player player)
        {
            if (!IsEventAuthority() || !ShouldTrackState() || player == null || !player.IsAI)
                return;
            if (player.Profile == null || player.Profile.Info == null || player.Profile.Info.Settings == null)
                return;
            WildSpawnType role = player.Profile.Info.Settings.Role;
            if (!IsISBRole(role) && !IsCommanderRole(role))
                return;
            string key = GetCacheKey(player);
            if (string.IsNullOrEmpty(key))
                return;
            CachedISBOperator cached;
            if (!CachedISBOperators.TryGetValue(key, out cached))
            {
                cached = new CachedISBOperator();
                CachedISBOperators[key] = cached;
            }

            cached.Player = player;
            cached.Role = (int)role;
            cached.Alive = player.HealthController == null || player.HealthController.IsAlive;
            if (IsISBRole(role))
                ISBSpawnedThisRaid = true;
        }

        private static void PopulateInitialCache()
        {
            if (!IsEventAuthority() || !ShouldTrackState())
                return;
            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return;
            GameWorld gameWorld = Singleton<GameWorld>.Instance;
            if (gameWorld.AllAlivePlayersList == null)
                return;
            foreach (Player player in gameWorld.AllAlivePlayersList)
                RegisterCachedISBPlayer(player);
        }

        private static void MarkCachedISBDead(Player player)
        {
            if (player == null)
                return;
            RegisterCachedISBPlayer(player);
            string key = GetCacheKey(player);
            CachedISBOperator cached;
            if (CachedISBOperators.TryGetValue(key, out cached))
                cached.Alive = false;
        }

        private static void RefreshAliveCountsFromCache()
        {
            if (!IsEventAuthority())
                return;
            ISBAliveCounts.Clear();
            if (!ShouldTrackState())
                return;
            foreach (CachedISBOperator cached in CachedISBOperators.Values)
            {
                if (cached == null || !cached.Alive)
                    continue;
                Player player = cached.Player;
                if (player != null && player.HealthController != null && !player.HealthController.IsAlive)
                {
                    cached.Alive = false;
                    continue;
                }

                WildSpawnType role = (WildSpawnType)cached.Role;
                if (!IsISBRole(role))
                    continue;
                int enumValue = cached.Role;
                if (!ISBAliveCounts.ContainsKey(enumValue))
                    ISBAliveCounts[enumValue] = 0;
                ISBAliveCounts[enumValue]++;
            }
        }

        private static int GetAliveCommanderCount()
        {
            if (!IsEventAuthority())
                return NetworkCommanderCount;
            return GetAliveCommanderCountLocal();
        }

        private static int GetAliveCommanderCountLocal()
        {
            int total = 0;
            if (!ShouldTrackState())
                return 0;
            foreach (CachedISBOperator cached in CachedISBOperators.Values)
            {
                if (cached == null || !cached.Alive)
                    continue;
                if (cached.Player != null && cached.Player.HealthController != null && !cached.Player.HealthController.IsAlive)
                {
                    cached.Alive = false;
                    continue;
                }

                if (IsCommanderRole((WildSpawnType)cached.Role))
                    total++;
            }

            return total;
        }

        private static string ResolveRaidLocation(GameWorld gameWorld)
        {
            if (gameWorld == null)
                return string.Empty;
            try
            {
                if (gameWorld.MainPlayer != null && !string.IsNullOrWhiteSpace(gameWorld.MainPlayer.Location))
                    return gameWorld.MainPlayer.Location;
            }
            catch
            {
            }

            try
            {
                Type worldType = gameWorld.GetType();
                string[] memberNames =
                {
                    "LocationId",
                    "Location",
                    "locationId",
                    "location"
                };
                for (int i = 0; i < memberNames.Length; i++)
                {
                    PropertyInfo property = worldType.GetProperty(memberNames[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property != null && property.PropertyType == typeof(string))
                    {
                        string value = property.GetValue(gameWorld, null) as string;
                        if (!string.IsNullOrWhiteSpace(value))
                            return value;
                    }

                    FieldInfo field = worldType.GetField(memberNames[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
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
            return !string.IsNullOrWhiteSpace(location) && string.Equals(location.Trim(), "laboratory", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLaboratoryRaid()
        {
            if (CurrentRaidIsLaboratory)
                return true;
            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return false;
            CurrentRaidIsLaboratory = IsLaboratoryLocation(ResolveRaidLocation(Singleton<GameWorld>.Instance));
            return CurrentRaidIsLaboratory;
        }

        private static bool UseLabsSpecialNotifier()
        {
            return IsLaboratoryRaid() && LabsTakeoverStatusResolved && LabsTakeoverActive;
        }

        private static void ResolveLabsTakeoverStatus()
        {
            if (!IsLaboratoryRaid())
            {
                LabsTakeoverActive = false;
                LabsTakeoverStatusResolved = true;
                return;
            }

            if (!IsEventAuthority())
                return;
            try
            {
                string json = RequestHandler.GetJson("/ISB/labsPreBlackoutEvent/status");
                if (string.IsNullOrWhiteSpace(json))
                    throw new InvalidOperationException("Empty server response.");
                string compact = json.Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Replace("\t", string.Empty);
                LabsTakeoverActive = compact.IndexOf("\"active\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                LabsTakeoverStatusResolved = true;
            }
            catch (Exception)
            {
                LabsTakeoverActive = false;
                LabsTakeoverStatusResolved = false;
            }
        }

        private static int GetTotalAliveISBCount()
        {
            int total = 0;
            foreach (KeyValuePair<int, int> entry in ISBAliveCounts)
            {
                total += entry.Value;
            }

            return total;
        }

        private static List<int> GetOrderedAliveKeys()
        {
            List<int> ordered = new List<int>();
            foreach (int enumValue in DisplayOrder)
            {
                if (ISBAliveCounts.ContainsKey(enumValue))
                    ordered.Add(enumValue);
            }

            List<int> leftovers = new List<int>();
            foreach (int key in ISBAliveCounts.Keys)
            {
                if (!ordered.Contains(key))
                    leftovers.Add(key);
            }

            leftovers.Sort();
            ordered.AddRange(leftovers);
            return ordered;
        }

        private static string GetEmojiFor(int enumValue)
        {
            return ISBEmojis.TryGetValue(enumValue, out string emoji) ? emoji : DefaultEmoji;
        }

        private static string BuildTotalEmojiBar()
        {
            StringBuilder bar = new StringBuilder();
            foreach (int enumValue in GetOrderedAliveKeys())
            {
                int count = ISBAliveCounts[enumValue];
                for (int i = 0; i < count; i++)
                    bar.Append(TotalBarSymbol);
            }

            return bar.ToString();
        }

        private static void ShowLabsPresenceMessage()
        {
            if (!CanPresentLocally() || !IsPluginEnabled())
                return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== ISB SOF ACTIVITY DETECTED ===");
            sb.AppendLine("An ISB Special Operation is currently in progress.");
            sb.AppendLine("A large number of ISB Operatives have been reported in the area..");
            sb.AppendLine("Extreme caution is advised.");
            NotificationManagerClass.DisplayMessageNotification(sb.ToString(), ENotificationDurationType.Long);
        }

        private static void ShowLabsNoPresenceMessage()
        {
            if (!CanPresentLocally() || !IsPluginEnabled())
                return;
            NotificationManagerClass.DisplayMessageNotification("No active ISB presence is currently detected within the Laboratory.", ENotificationDurationType.Long);
        }

        private static void ShowCurrentAliveMessage()
        {
            if (!CanPresentLocally())
                return;
            if (!IsPluginEnabled())
                return;
            if (UseLabsSpecialNotifier())
            {
                ShowLabsPresenceMessage();
                return;
            }

            int total = GetTotalAliveISBCount();
            if (total <= 0)
                return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== ISB SOF ACTIVITY DETECTED ===");
            sb.AppendLine("ISB SOF Operatives have been seen in your current raid.");
            sb.AppendLine("Extreme caution is advised!");
            sb.AppendLine();
            sb.AppendLine($"Total Alive ({total}):  {BuildTotalEmojiBar()}");
            sb.AppendLine("---------------------------------");
            sb.AppendLine();
            foreach (int enumValue in GetOrderedAliveKeys())
            {
                int count = ISBAliveCounts[enumValue];
                string emoji = GetEmojiFor(enumValue);
                string name = ISBNames.TryGetValue(enumValue, out string n) ? n : $"ISB Unknown ({enumValue})";
                sb.AppendLine($"{emoji}  {name}  x{count}");
            }

            NotificationManagerClass.DisplayMessageNotification(sb.ToString(), ENotificationDurationType.Long);
        }

        private static string BuildRemainingGroupList(int[] order)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < order.Length; i++)
            {
                int enumValue = order[i];
                int count;
                if (!ISBAliveCounts.TryGetValue(enumValue, out count) || count <= 0)
                    continue;
                string name = ISBShortNames.TryGetValue(enumValue, out string shortName) ? shortName : enumValue.ToString();
                names.Add(count > 1 ? name + " x" + count : name);
            }

            return string.Join(", ", names.ToArray());
        }

        private static void AppendRemainingWhiteTuskList(StringBuilder sb)
        {
            sb.AppendLine("Remaining White Tusks:");
            for (int i = 0; i < WhiteTuskOrder.Length; i++)
            {
                int enumValue = WhiteTuskOrder[i];
                int count;
                if (!ISBAliveCounts.TryGetValue(enumValue, out count) || count <= 0)
                    continue;
                string emoji = GetEmojiFor(enumValue);
                string name = ISBNames.TryGetValue(enumValue, out string displayName) ? displayName : ISBShortNames.TryGetValue(enumValue, out string shortName) ? shortName : enumValue.ToString();
                sb.AppendLine($"{emoji}  {name}  x{count}");
            }
        }

        private static void ShowWhiteTuskKIA(WildSpawnType role)
        {
            if (!CanPresentLocally() || !IsPluginEnabled())
                return;
            if (UseLabsSpecialNotifier())
                return;
            string who = ISBShortNames.TryGetValue((int)role, out string shortName) ? shortName : role.ToString();
            string remaining = BuildRemainingGroupList(WhiteTuskOrder);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("♦ === WHITE TUSK KIA === ♦");
            sb.AppendLine(who + " has been eliminated.");
            sb.AppendLine();
            if (string.IsNullOrEmpty(remaining))
                sb.AppendLine("All White Tusk operators have been eliminated.");
            else
                AppendRemainingWhiteTuskList(sb);
            NotificationManagerClass.DisplayMessageNotification(sb.ToString(), ENotificationDurationType.Long);
        }

        private static void ShowOperatorKIA(WildSpawnType role)
        {
            if (!CanPresentLocally() || !IsPluginEnabled() || IsOnlyInitialAlert())
                return;
            if (UseLabsSpecialNotifier())
                return;
            string who = ISBShortNames.TryGetValue((int)role, out string shortName) ? shortName : role.ToString();
            string remaining = BuildRemainingGroupList(OperatorOrder);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== ISB MEMBER ELIMINATED ===");
            sb.AppendLine(who + " has been eliminated.");
            sb.AppendLine();
            if (string.IsNullOrEmpty(remaining))
                sb.AppendLine("No standard ISB operators remain active.");
            else
                sb.AppendLine("Remaining ISB operatives: " + remaining);
            NotificationManagerClass.DisplayMessageNotification(sb.ToString(), ENotificationDurationType.Long);
        }

        private static IEnumerator DelayedISBDeathNotification(Player player, WildSpawnType role, string cacheKey)
        {
            yield return new WaitForSeconds(DEATH_NOTIFICATION_DELAY);
            if (!IsEventAuthority() || !ShouldTrackState())
            {
                DeathNotificationsScheduled.Remove(cacheKey);
                yield break;
            }

            RefreshAliveCountsFromCache();
            if (IsWhiteTuskRole(role))
            {
                ShowWhiteTuskKIA(role);
                BroadcastNetworkSnapshot(NetworkReasonWhiteTuskKia, (int)role);
            }
            else if (IsOperatorRole(role))
            {
                ShowOperatorKIA(role);
                BroadcastNetworkSnapshot(NetworkReasonOperatorKia, (int)role);
            }

            if (GetTotalAliveISBCount() <= 0 && GetAliveCommanderCountLocal() <= 0 && ISBSpawnedThisRaid && !NoAliveMessageShown)
            {
                ShowNoAliveISBMessage();
                NoAliveMessageShown = true;
                BroadcastNetworkSnapshot(NetworkReasonWithdrawal);
            }

            DeathNotificationsScheduled.Remove(cacheKey);
        }

        private static void ShowNoAliveISBMessage()
        {
            if (!CanPresentLocally())
                return;
            if (!IsPluginEnabled())
                return;
            if (UseLabsSpecialNotifier())
                return;
            NotificationManagerClass.DisplayMessageNotification("No ISB Operatives remaining in the Current Raid. ISB HQ has ordered a complete withdrawal. You may rest, but remain on alert.", ENotificationDurationType.Long);
        }

        private static void ShowCommanderStatus()
        {
            if (!CanPresentLocally())
                return;
            if (!IsPluginEnabled())
                return;
            if (UseLabsSpecialNotifier())
                return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\u2716 === ISB HIGH-VALUE TARGET DETECTED === \u2716");
            sb.AppendLine("ISB's Operation still active in this raid.");
            sb.AppendLine("Stay alert.");
            NotificationManagerClass.DisplayMessageNotification(sb.ToString(), ENotificationDurationType.Long);
        }

        private static void ShowCommanderAlert()
        {
            if (!CanPresentLocally())
                return;
            if (!IsPluginEnabled())
                return;
            if (UseLabsSpecialNotifier())
                return;
            if (EnableCommanderAlert != null && !EnableCommanderAlert.Value)
                return;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\u2716 === ISB HIGH-VALUE TARGET DETECTED === \u2716");
            sb.AppendLine("Rumors suggest that a high-ranking ISB Commander was");
            sb.AppendLine("deployed to your location. EXTREME CAUTION ADVISED.");
            sb.AppendLine();
            sb.AppendLine("Survive, or make them regret.");
            NotificationManagerClass.DisplayMessageNotification(sb.ToString(), ENotificationDurationType.Long);
        }

        private static void ShowCommanderKIA(WildSpawnType role)
        {
            if (!CanPresentLocally())
                return;
            if (!IsPluginEnabled())
                return;
            if (UseLabsSpecialNotifier())
                return;
            if (EnableCommanderKIA != null && !EnableCommanderKIA.Value)
                return;
            string who = role == (WildSpawnType)ISBCommanderEnum ? "\"The Chosen One\"" : "\"The Dark Lord\"";
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("\u2716 === ISB HIGH-VALUE TARGET DOWN === \u2716");
            sb.AppendLine($"{who} has been eliminated.");
            sb.AppendLine("You'll definitely make it to ISB HQ if you remember that.");
            NotificationManagerClass.DisplayMessageNotification(sb.ToString(), ENotificationDurationType.Long);
        }

        private void OnDestroy()
        {
            CancelPendingEffectNotices();
            ShutdownOptionalFikaBridge();
            if (_networkBridge != null && !ReferenceEquals(_networkBridge, NoNetworkBridge.Instance))
            {
                _networkBridge.Dispose();
            }

            _networkBridge = NoNetworkBridge.Instance;
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        private class ISBDeathPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return typeof(Player).GetMethod(nameof(Player.OnBeenKilledByAggressor), BindingFlags.Public | BindingFlags.Instance);
            }

            [PatchPostfix]
            private static void PatchPostfix(Player __instance)
            {
                if (!IsEventAuthority() || !ShouldTrackState())
                    return;
                if (__instance == null || __instance.Profile == null || __instance.Profile.Info == null || __instance.Profile.Info.Settings == null)
                {
                    return;
                }

                WildSpawnType role = __instance.Profile.Info.Settings.Role;
                if (!IsISBRole(role) && !IsCommanderRole(role))
                    return;
                MarkCachedISBDead(__instance);
                RefreshAliveCountsFromCache();
                if (UseLabsSpecialNotifier())
                {
                    BroadcastNetworkSnapshot(NetworkReasonStateOnly);
                    return;
                }

                int enumValue = (int)role;
                if (IsCommanderRole(role))
                {
                    if (CommanderKIAShown.Contains(enumValue))
                        return;
                    CommanderKIAShown.Add(enumValue);
                    ShowCommanderKIA(role);
                    BroadcastNetworkSnapshot(NetworkReasonCommanderKia, enumValue);
                    if (GetTotalAliveISBCount() <= 0 && GetAliveCommanderCountLocal() <= 0 && ISBSpawnedThisRaid && !NoAliveMessageShown)
                    {
                        ShowNoAliveISBMessage();
                        NoAliveMessageShown = true;
                        BroadcastNetworkSnapshot(NetworkReasonWithdrawal);
                    }

                    return;
                }

                string cacheKey = GetCacheKey(__instance);
                if (string.IsNullOrEmpty(cacheKey) || DeathNotificationsScheduled.Contains(cacheKey))
                    return;
                DeathNotificationsScheduled.Add(cacheKey);
                if (Instance != null)
                    Instance.StartCoroutine(DelayedISBDeathNotification(__instance, role, cacheKey));
            }
        }

        private class ISBSpawnPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return typeof(BotOwner).GetMethod(nameof(BotOwner.method_10), BindingFlags.Public | BindingFlags.Instance);
            }

            [PatchPostfix]
            private static void PatchPostfix(BotOwner __instance)
            {
                if (!IsEventAuthority() || !ShouldTrackState() || __instance == null)
                    return;
                Player player = __instance.GetPlayer;
                if (player == null || player.Profile == null || player.Profile.Info == null || player.Profile.Info.Settings == null)
                    return;
                WildSpawnType role = player.Profile.Info.Settings.Role;
                if (!IsISBRole(role) && !IsCommanderRole(role))
                    return;
                RegisterCachedISBPlayer(player);
                RefreshAliveCountsFromCache();
                if (UseLabsSpecialNotifier())
                {
                    if (InitialMessageShown && !LabsPresenceShown)
                    {
                        LabsPresenceShown = true;
                        ShowLabsPresenceMessage();
                        BroadcastNetworkSnapshot(NetworkReasonLabsPresence);
                    }
                    else if (InitialMessageShown)
                    {
                        BroadcastNetworkSnapshot(NetworkReasonStateOnly);
                    }

                    return;
                }

                if (InitialMessageShown)
                    BroadcastNetworkSnapshot(NetworkReasonStateOnly);
                if (IsCommanderRole(role) && !CommanderAlertShown)
                {
                    CommanderAlertShown = true;
                    ShowCommanderAlert();
                    BroadcastNetworkSnapshot(NetworkReasonCommanderAlert, (int)role);
                }
            }
        }

        private class RaidStartPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return typeof(GameWorld).GetMethod("OnGameStarted");
            }

            [PatchPrefix]
            private static void PatchPrefix()
            {
                ResetState();
            }

            [PatchPostfix]
            private static void PatchPostfix()
            {
                if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                    return;
                GameWorld gameWorld = Singleton<GameWorld>.Instance;
                CurrentRaidIsLaboratory = IsLaboratoryLocation(ResolveRaidLocation(gameWorld));
                if (CurrentRaidIsLaboratory && IsEventAuthority())
                    ResolveLabsTakeoverStatus();
                else if (!CurrentRaidIsLaboratory)
                    LabsTakeoverStatusResolved = true;
                ISBAliveMonitor monitor = gameWorld.GetComponent<ISBAliveMonitor>();
                if (monitor == null)
                    gameWorld.gameObject.AddComponent<ISBAliveMonitor>();
                if (Instance != null)
                    Instance._networkRaidStateInitialized = true;
            }
        }

        private class ISBAliveMonitor : MonoBehaviour
        {
            private Coroutine _monitorRoutine;
            private void Start()
            {
                _monitorRoutine = StartCoroutine(MonitorAliveISB());
            }

            private IEnumerator MonitorAliveISB()
            {
                if (IsNetworkActive() && IsHeadless() && IsEventAuthority())
                    yield return WaitForHeadlessPresentationClient();
                yield return new WaitForSeconds(INITIAL_DELAY_FOR_COUNT);
                if (!IsEventAuthority())
                    yield break;
                if (IsLaboratoryRaid() && !LabsTakeoverStatusResolved)
                    ResolveLabsTakeoverStatus();
                InitialMessageShown = true;
                if (!ShouldTrackState())
                    yield break;
                PopulateInitialCache();
                RefreshAliveCountsFromCache();
                int totalAlive = GetTotalAliveISBCount();
                if (UseLabsSpecialNotifier())
                {
                    int labsAlive = totalAlive + GetAliveCommanderCount();
                    if (labsAlive > 0)
                    {
                        if (!LabsPresenceShown)
                        {
                            LabsPresenceShown = true;
                            ShowLabsPresenceMessage();
                        }

                        BroadcastNetworkSnapshot(NetworkReasonLabsPresence);
                    }
                    else
                    {
                        BroadcastNetworkSnapshot(NetworkReasonStateOnly);
                    }

                    _monitorRoutine = null;
                    yield break;
                }

                if (totalAlive > 0)
                {
                    ShowCurrentAliveMessage();
                    NoAliveMessageShown = false;
                    BroadcastNetworkSnapshot(NetworkReasonCounts);
                }
                else if (ISBSpawnedThisRaid && GetAliveCommanderCount() == 0)
                {
                    ShowNoAliveISBMessage();
                    NoAliveMessageShown = true;
                    BroadcastNetworkSnapshot(NetworkReasonWithdrawal);
                }
                else
                {
                    BroadcastNetworkSnapshot(NetworkReasonStateOnly);
                }

                _monitorRoutine = null;
            }

            private void OnDestroy()
            {
                if (_monitorRoutine != null)
                {
                    StopCoroutine(_monitorRoutine);
                    _monitorRoutine = null;
                }

                ResetState();
            }
        }
    }
}
