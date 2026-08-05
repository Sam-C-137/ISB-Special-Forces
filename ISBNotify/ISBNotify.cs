using BepInEx;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Communications;
using SPT.Reflection.Patching;
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
        public const string PluginVersion = "1.0.2";
        public const string FikaPluginGuid = "com.fika.core";

        private const string FikaBridgeAssemblyFileName = "ISBNotify.Fika.dll";
        private const string FikaBridgeAssemblyName = "ISBNotify.Fika";
        private const string FikaBridgeBootstrapTypeName =
            "ISBInfo.Fika.ISBNotifyFikaBootstrap";

        private const float INITIAL_DELAY_FOR_COUNT = 20f;
        private const float BOTS_SCAN = 15f;
        private const float MANUAL_NOTIFICATION_COOLDOWN = 3f;

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
        private static readonly HashSet<int> CommanderKIAShown = new HashSet<int>();

        private static readonly Dictionary<int, int> ISBAliveCounts = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> LastShownCounts = new Dictionary<int, int>();

        private static ISBInfoPlugin Instance;
        private IISBNotifyNetworkBridge _networkBridge = NoNetworkBridge.Instance;
        private MethodInfo _fikaBridgeShutdownMethod;
        private bool _fikaBridgeInitialized;
        private bool _networkClientReadyAcknowledged;
        private bool _networkPresentationClientReady;
        private bool _networkRaidStateInitialized;
        private bool _networkReadyRequestLogged;
        private float _nextNetworkReadySendTime;
        private float _nextManualNotificationTime;
        private static int NetworkCommanderCount;
        private static bool NetworkCountsInitialized;

        private const int NetworkReasonStateOnly = 0;
        private const int NetworkReasonCounts = 1;
        private const int NetworkReasonWithdrawal = 2;
        private const int NetworkReasonCommanderAlert = 3;
        private const int NetworkReasonCommanderKia = 4;

        private static readonly Dictionary<int, string> ISBNames = new Dictionary<int, string>
        {
            { 13702, "ISB Firefly (Cipher One)" },
            { 13703, "ISB Vipper (Snake One)" },
            { 13704, "ISB Loghan (Apex One)" },
            { 13705, "Shielder One (The Chosen One)" },
            { 13706, "Shielder Two (The Dark Lord)" },
            { 13709, "ISB Team Leader" },
            { 13710, "ISB Specialist" },
            { 13701, "ISB Operator" },
            { 13707, "\"The Chosen One\"" },
            { 13708, "\"The Dark Lord\"" },
        };

        private static readonly Dictionary<int, string> ISBEmojis = new Dictionary<int, string>
        {
            { 13702, "\u2666" },
            { 13703, "\u2666" },
            { 13704, "\u2666" },
            { 13705, "\u25A0" },
            { 13706, "\u25A0" },
            { 13709, "\u2666" },
            { 13710, "\u25A0" },
            { 13701, "\u25CF" },
            { 13707, "\u2716" },
            { 13708, "\u2716" },
        };

        private const string DefaultEmoji = "\u25CF";

        private const string TotalBarSymbol = "\u2620";

        private static readonly int[] DisplayOrder =
        {
            13702,
            13703,
            13704,
            13705,
            13706,
            13709,
            13710,
            13701,
        };

        private void Awake()
        {
            Instance = this;

            EnablePlugin = Config.Bind(
                "Settings for the ISB SOF Notifier plugin",
                "Enable in-raid Notifier",
                true,
                "When enabled, if you are in a raid where ISB operations are taking place, you will receive a notification indicating which types of operators are active and their respective numbers."
            );

            OnlyInitialAlert = Config.Bind(
                "Settings for the ISB SOF Notifier plugin",
                "Only Show Initial Alert",
                false,
                "When enabled, you will only receive the single initial notification at the start of the raid, and not the recurring updates that refresh as operators are eliminated. You can still press the O key at any time to manually check the current status."
            );

            EnableCommanderAlert = Config.Bind(
                "Settings for the ISB SOF Notifier plugin",
                "Enable Commander Alert",
                true,
                "When enabled, a special warning is shown when an ISB Commander and his guard are deployed to hunt you. Disable this to remove that specific alert without affecting the regular ISB notifications."
            );

            EnableCommanderKIA = Config.Bind(
                "Settings for the ISB SOF Notifier plugin",
                "Enable Commander KIA notification",
                true,
                "When enabled, a dedicated notification is shown when an ISB Commander or his guard is eliminated, separate from the regular ISB withdrawal message."
            );

            new ISBSpawnPatch().Enable();
            new RaidStartPatch().Enable();
            new CommanderDeathPatch().Enable();

            TryInitializeOptionalFikaBridge();

            Logger.LogInfo(
                $"ISB SOF Notifier {PluginVersion} initialized. Local mode is ready; " +
                "the optional Fika addon can attach independently.");
        }

        private void TryInitializeOptionalFikaBridge()
        {
            if (!Chainloader.PluginInfos.ContainsKey(FikaPluginGuid))
                return;

            string pluginDirectory = Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(pluginDirectory))
            {
                Logger.LogWarning(
                    "ISB SOF Notifier: Fika is installed, but the plugin directory could not be resolved.");
                return;
            }

            string bridgePath = Path.Combine(
                pluginDirectory,
                FikaBridgeAssemblyFileName);

            if (!File.Exists(bridgePath))
            {
                Logger.LogWarning(
                    "ISB SOF Notifier: Fika is installed, but " +
                    FikaBridgeAssemblyFileName +
                    " was not found next to ISBNotify.dll. Synchronization is disabled.");
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
                {
                    throw new MissingMethodException(
                        FikaBridgeBootstrapTypeName,
                        "Initialize/Shutdown");
                }

                initializeMethod.Invoke(null, new object[] { Logger });
                _fikaBridgeInitialized =
                    _networkBridge != null && _networkBridge.Active;

                if (!_fikaBridgeInitialized)
                {
                    Logger.LogWarning(
                        "ISB SOF Notifier: the optional Fika bridge loaded, but did not become active.");
                }
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Logger.LogError(
                    "ISB SOF Notifier: optional Fika bridge initialization failed: " +
                    cause.Message);
                _fikaBridgeShutdownMethod = null;
                _fikaBridgeInitialized = false;
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    "ISB SOF Notifier: optional Fika bridge initialization failed: " +
                    ex.Message);
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
                    if (string.Equals(
                        assembly.GetName().Name,
                        assemblyName,
                        StringComparison.OrdinalIgnoreCase))
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
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                Logger.LogWarning(
                    "ISB SOF Notifier: optional Fika bridge shutdown failed: " +
                    cause.Message);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(
                    "ISB SOF Notifier: optional Fika bridge shutdown failed: " +
                    ex.Message);
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

            if (instance._networkBridge != null &&
                !ReferenceEquals(instance._networkBridge, NoNetworkBridge.Instance) &&
                !ReferenceEquals(instance._networkBridge, bridge))
            {
                instance.Logger.LogWarning(
                    "ISB SOF Notifier: another network bridge is already installed.");
                return false;
            }

            instance._networkBridge = bridge;
            instance.ResetNetworkHandshakeState();

            instance.Logger.LogInfo(
                "ISB SOF Notifier: optional network bridge attached. ready=" +
                bridge.Ready + ", server=" + bridge.IsServer +
                ", headless=" + bridge.IsHeadless + ".");

            if (bridge.IsHeadless)
            {
                instance.Logger.LogInfo(
                    "ISB SOF Notifier: Fika headless mode detected. " +
                    "This process will synchronize state without presenting local UI.");
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
            instance.Logger.LogInfo(
                "ISB SOF Notifier: optional network bridge detached.");
        }

        private void ResetNetworkHandshakeState()
        {
            _networkClientReadyAcknowledged = false;
            _networkPresentationClientReady = false;
            _networkReadyRequestLogged = false;
            _nextNetworkReadySendTime = 0f;
            NetworkCommanderCount = 0;
            NetworkCountsInitialized = false;
        }

        private void Update()
        {
            if (_networkBridge != null && _networkBridge.Active)
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

                _nextManualNotificationTime =
                    Time.realtimeSinceStartup + MANUAL_NOTIFICATION_COOLDOWN;

                if (IsEventAuthority())
                    RebuildAliveCounts();

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

                CopyCurrentCountsToLastShown();
            }
        }

        private static bool IsNetworkActive()
        {
            return Instance != null &&
                   Instance._networkBridge != null &&
                   Instance._networkBridge.Active;
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
            return !IsHeadless();
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

        private static bool IsLocalNetworkClientRaidReady()
        {
            return Singleton<GameWorld>.Instantiated &&
                   Singleton<GameWorld>.Instance != null &&
                   Singleton<GameWorld>.Instance.MainPlayer != null;
        }

        private void TrySendNetworkClientReady()
        {
            if (!IsNetworkSession() || IsEventAuthority() || IsHeadless() ||
                _networkClientReadyAcknowledged || !_networkBridge.Ready)
            {
                return;
            }

            if (!IsLocalNetworkClientRaidReady())
                return;

            if (Time.realtimeSinceStartup < _nextNetworkReadySendTime)
                return;

            _nextNetworkReadySendTime = Time.realtimeSinceStartup + 2f;

            if (_networkBridge.SendToServer("Q|READY") && !_networkReadyRequestLogged)
            {
                _networkReadyRequestLogged = true;
                Logger.LogInfo("ISB SOF Notifier: Player client reported raid-ready; waiting for authority acknowledgement.");
            }
        }

        private void HandleNetworkClientReady(string[] parts)
        {
            if (parts.Length < 2 || parts[1] != "READY" || !_networkRaidStateInitialized)
                return;

            bool firstReadyClient = !_networkPresentationClientReady;
            _networkPresentationClientReady = true;

            RebuildAliveCounts();
            _networkBridge.Broadcast("K|READY");
            _networkBridge.Broadcast(BuildNetworkSnapshot(NetworkReasonStateOnly, 0));

            Logger.LogInfo(firstReadyClient
                ? "ISB SOF Notifier: A Fika player client is raid-ready. Headless notifier timeline may now start."
                : "ISB SOF Notifier: A late/reconnected Fika client is raid-ready; current notifier state was resent.");
        }

        private static IEnumerator WaitForHeadlessPresentationClient()
        {
            if (!IsNetworkActive() || !IsHeadless() || !IsEventAuthority())
                yield break;

            float nextLogTime = 0f;
            while (Instance != null &&
                   IsNetworkActive() &&
                   !Instance._networkPresentationClientReady)
            {
                if (Time.realtimeSinceStartup >= nextLogTime)
                {
                    nextLogTime = Time.realtimeSinceStartup + 10f;
                    Instance.Logger.LogInfo(
                        IsNetworkSession()
                            ? "ISB SOF Notifier: Headless authority is waiting for a player client READY handshake."
                            : "ISB SOF Notifier: Headless authority is waiting for the Fika network manager.");
                }

                yield return new WaitForSeconds(0.25f);
            }
        }

        private static string BuildNetworkSnapshot(int reason, int commanderRole)
        {
            int commanderCount = GetAliveCommanderCountLocal();
            string[] parts = new string[7 + DisplayOrder.Length];
            parts[0] = "N";
            parts[1] = reason.ToString();
            parts[2] = ISBSpawnedThisRaid ? "1" : "0";
            parts[3] = NoAliveMessageShown ? "1" : "0";
            parts[4] = commanderCount.ToString();
            parts[5] = commanderRole.ToString();
            parts[6] = InitialMessageShown ? "1" : "0";

            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                int count;
                ISBAliveCounts.TryGetValue(DisplayOrder[i], out count);
                parts[7 + i] = count.ToString();
            }

            return string.Join("|", parts);
        }

        private static void ApplyNetworkSnapshot(string payload)
        {
            if (string.IsNullOrEmpty(payload) || IsEventAuthority())
                return;

            string[] parts = payload.Split('|');
            if (parts.Length < 7 + DisplayOrder.Length || parts[0] != "N")
                return;

            if (!int.TryParse(parts[1], out int reason) ||
                !int.TryParse(parts[4], out int commanderCount) ||
                !int.TryParse(parts[5], out int commanderRole))
            {
                return;
            }

            bool hadCountSnapshot = NetworkCountsInitialized;

            ISBSpawnedThisRaid = parts[2] == "1";
            NoAliveMessageShown = parts[3] == "1";
            InitialMessageShown = parts[6] == "1" || reason != NetworkReasonStateOnly;
            NetworkCommanderCount = commanderCount;

            if ((reason == NetworkReasonCounts || reason == NetworkReasonWithdrawal) ||
                (reason == NetworkReasonStateOnly && InitialMessageShown))
            {
                NetworkCountsInitialized = true;
            }

            ISBAliveCounts.Clear();
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if (int.TryParse(parts[7 + i], out int count) && count > 0)
                    ISBAliveCounts[DisplayOrder[i]] = count;
            }

            CopyCurrentCountsToLastShown();

            if (!IsPluginEnabled())
                return;

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
            }
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
            ISBSpawnedThisRaid = false;
            InitialMessageShown = false;
            NoAliveMessageShown = false;
            CommanderAlertShown = false;
            CommanderKIAShown.Clear();
            NetworkCommanderCount = 0;
            NetworkCountsInitialized = false;

            if (Instance != null)
            {
                Instance.ResetNetworkHandshakeState();
                Instance._networkRaidStateInitialized = false;
                Instance._nextManualNotificationTime = 0f;
            }

            ISBAliveCounts.Clear();
            LastShownCounts.Clear();
        }

        private static bool IsCommanderRole(WildSpawnType role)
        {
            return role == (WildSpawnType)ISBCommanderEnum ||
                   role == (WildSpawnType)ISBFollowerCommanderEnum;
        }

        private static bool IsISBRole(WildSpawnType role)
        {
            return role == (WildSpawnType)13701 ||
                   role == (WildSpawnType)13702 ||
                   role == (WildSpawnType)13703 ||
                   role == (WildSpawnType)13704 ||
                   role == (WildSpawnType)13705 ||
                   role == (WildSpawnType)13706 ||
                   role == (WildSpawnType)13709 ||
                   role == (WildSpawnType)13710;
        }

        private static bool IsAliveISBPlayer(Player player)
        {
            if (player == null)
                return false;

            if (!player.IsAI)
                return false;

            if (player.HealthController == null)
                return false;

            if (!player.HealthController.IsAlive)
                return false;

            if (player.Profile == null ||
                player.Profile.Info == null ||
                player.Profile.Info.Settings == null)
                return false;

            return IsISBRole(player.Profile.Info.Settings.Role);
        }

        private static void RebuildAliveCounts()
        {
            if (!IsEventAuthority())
                return;

            ISBAliveCounts.Clear();

            if (!ShouldTrackState())
                return;

            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return;

            GameWorld gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld.AllAlivePlayersList == null)
                return;

            foreach (Player player in gameWorld.AllAlivePlayersList)
            {
                if (!IsAliveISBPlayer(player))
                    continue;

                int enumValue = (int)player.Profile.Info.Settings.Role;

                if (!ISBAliveCounts.ContainsKey(enumValue))
                    ISBAliveCounts[enumValue] = 0;

                ISBAliveCounts[enumValue]++;

                ISBSpawnedThisRaid = true;
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

            if (!Singleton<GameWorld>.Instantiated || Singleton<GameWorld>.Instance == null)
                return 0;

            GameWorld gameWorld = Singleton<GameWorld>.Instance;

            if (gameWorld.AllAlivePlayersList == null)
                return 0;

            foreach (Player player in gameWorld.AllAlivePlayersList)
            {
                if (player == null || !player.IsAI)
                    continue;

                if (player.HealthController == null || !player.HealthController.IsAlive)
                    continue;

                if (player.Profile == null || player.Profile.Info == null || player.Profile.Info.Settings == null)
                    continue;

                if (IsCommanderRole(player.Profile.Info.Settings.Role))
                    total++;
            }

            return total;
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
            return ISBEmojis.TryGetValue(enumValue, out string emoji)
                ? emoji
                : DefaultEmoji;
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

        private static void ShowCurrentAliveMessage()
        {
            if (!CanPresentLocally())
                return;

            if (!IsPluginEnabled())
                return;

            int total = GetTotalAliveISBCount();

            if (total <= 0)
                return;

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("=== ISB SOF ACTIVITY DETECTED ===");
            sb.AppendLine("ISB SOF Operators have been seen in your current raid.");
            sb.AppendLine("Extreme caution is advised!");
            sb.AppendLine();
            sb.AppendLine($"Total Alive ({total}):  {BuildTotalEmojiBar()}");
            sb.AppendLine("---------------------------------");
            sb.AppendLine();

            foreach (int enumValue in GetOrderedAliveKeys())
            {
                int count = ISBAliveCounts[enumValue];
                string emoji = GetEmojiFor(enumValue);

                string name = ISBNames.TryGetValue(enumValue, out string n)
                    ? n
                    : $"ISB Unknown ({enumValue})";

                sb.AppendLine($"{emoji}  {name}  x{count}");
            }

            NotificationManagerClass.DisplayMessageNotification(
                sb.ToString(),
                ENotificationDurationType.Long
            );
        }

        private static void ShowNoAliveISBMessage()
        {
            if (!CanPresentLocally())
                return;

            if (!IsPluginEnabled())
                return;

            NotificationManagerClass.DisplayMessageNotification(
                "No ISB Operators remaining in the Current Raid. ISB HQ has ordered a complete withdrawal. You may rest, but remain on alert.",
                ENotificationDurationType.Long
            );
        }

        private static void ShowCommanderStatus()
        {
            if (!CanPresentLocally())
                return;

            if (!IsPluginEnabled())
                return;

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("\u2716 === ISB HIGH-VALUE TARGET DETECTED === \u2716");
            sb.AppendLine("ISB's Operation still active in this raid.");
            sb.AppendLine("Stay alert.");

            NotificationManagerClass.DisplayMessageNotification(
                sb.ToString(),
                ENotificationDurationType.Long
            );
        }

        private static void ShowCommanderAlert()
        {
            if (!CanPresentLocally())
                return;

            if (!IsPluginEnabled())
                return;

            if (EnableCommanderAlert != null && !EnableCommanderAlert.Value)
                return;

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("\u2716 === ISB HIGH-VALUE TARGET DETECTED === \u2716");
            sb.AppendLine("Rumors suggest that a high-ranking ISB Commander was");
            sb.AppendLine("deployed to your location. EXTREME CAUTION ADVISED.");
            sb.AppendLine();
            sb.AppendLine("Survive, or make them regret.");

            NotificationManagerClass.DisplayMessageNotification(
                sb.ToString(),
                ENotificationDurationType.Long
            );
        }

        private static bool CountsChanged()
        {
            if (ISBAliveCounts.Count != LastShownCounts.Count)
                return true;

            foreach (KeyValuePair<int, int> entry in ISBAliveCounts)
            {
                if (!LastShownCounts.TryGetValue(entry.Key, out int oldCount))
                    return true;

                if (oldCount != entry.Value)
                    return true;
            }

            return false;
        }

        private static void CopyCurrentCountsToLastShown()
        {
            LastShownCounts.Clear();

            foreach (KeyValuePair<int, int> entry in ISBAliveCounts)
            {
                LastShownCounts[entry.Key] = entry.Value;
            }
        }

        private static void ShowCommanderKIA(WildSpawnType role)
        {
            if (!CanPresentLocally())
                return;

            if (!IsPluginEnabled())
                return;

            if (EnableCommanderKIA != null && !EnableCommanderKIA.Value)
                return;

            string who = role == (WildSpawnType)ISBCommanderEnum
                ? "\"The Chosen One\""
                : "\"The Dark Lord\"";

            StringBuilder sb = new StringBuilder();

            sb.AppendLine("\u2716 === ISB HIGH-VALUE TARGET DOWN === \u2716");
            sb.AppendLine($"{who} has been eliminated.");
            sb.AppendLine("You'll definitely make it to ISB HQ if you remember that.");

            NotificationManagerClass.DisplayMessageNotification(
                sb.ToString(),
                ENotificationDurationType.Long
            );
        }

        private void OnDestroy()
        {
            ShutdownOptionalFikaBridge();

            if (_networkBridge != null &&
                !ReferenceEquals(_networkBridge, NoNetworkBridge.Instance))
            {
                _networkBridge.Dispose();
            }

            _networkBridge = NoNetworkBridge.Instance;

            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        private class CommanderDeathPatch : ModulePatch
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

                if (__instance == null ||
                    __instance.Profile == null ||
                    __instance.Profile.Info == null ||
                    __instance.Profile.Info.Settings == null)
                {
                    return;
                }

                WildSpawnType role = __instance.Profile.Info.Settings.Role;

                if (!IsCommanderRole(role))
                    return;

                int enumValue = (int)role;

                if (CommanderKIAShown.Contains(enumValue))
                    return;

                CommanderKIAShown.Add(enumValue);
                ShowCommanderKIA(role);
                RebuildAliveCounts();
                BroadcastNetworkSnapshot(NetworkReasonCommanderKia, enumValue);
            }
        }

        private class ISBSpawnPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
            {
                return typeof(BotBoss).GetConstructors()[0];
            }

            [PatchPostfix]
            private static void PatchPostfix(BotBoss __instance)
            {
                if (!IsEventAuthority() || !ShouldTrackState())
                    return;

                if (__instance == null ||
                    __instance.Owner == null ||
                    __instance.Owner.Profile == null ||
                    __instance.Owner.Profile.Info == null ||
                    __instance.Owner.Profile.Info.Settings == null)
                {
                    return;
                }

                WildSpawnType role = __instance.Owner.Profile.Info.Settings.Role;

                if (IsCommanderRole(role))
                {
                    if (!CommanderAlertShown)
                    {
                        CommanderAlertShown = true;
                        ShowCommanderAlert();
                        BroadcastNetworkSnapshot(NetworkReasonCommanderAlert, (int)role);
                    }

                    return;
                }

                if (IsISBRole(role))
                {
                    ISBSpawnedThisRaid = true;
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

                InitialMessageShown = true;

                if (ShouldTrackState())
                {
                    RebuildAliveCounts();

                    int totalAlive = GetTotalAliveISBCount();

                    if (totalAlive > 0)
                    {
                        ShowCurrentAliveMessage();
                        CopyCurrentCountsToLastShown();
                        NoAliveMessageShown = false;
                        BroadcastNetworkSnapshot(NetworkReasonCounts);
                    }
                    else if (ISBSpawnedThisRaid && GetAliveCommanderCount() == 0)
                    {
                        ShowNoAliveISBMessage();
                        CopyCurrentCountsToLastShown();
                        NoAliveMessageShown = true;
                        BroadcastNetworkSnapshot(NetworkReasonWithdrawal);
                    }
                    else
                    {
                        CopyCurrentCountsToLastShown();
                        BroadcastNetworkSnapshot(NetworkReasonStateOnly);
                    }
                }

                while (true)
                {
                    yield return new WaitForSeconds(BOTS_SCAN);

                    if (!ShouldTrackState())
                        continue;

                    if (IsOnlyInitialAlert())
                    {
                        RebuildAliveCounts();
                        if (CountsChanged())
                            BroadcastNetworkSnapshot(NetworkReasonCounts);
                        CopyCurrentCountsToLastShown();
                        continue;
                    }

                    RebuildAliveCounts();

                    int totalAlive = GetTotalAliveISBCount();

                    if (totalAlive > 0)
                    {
                        NoAliveMessageShown = false;

                        if (!CountsChanged())
                            continue;

                        ShowCurrentAliveMessage();
                        CopyCurrentCountsToLastShown();
                        BroadcastNetworkSnapshot(NetworkReasonCounts);

                        continue;
                    }

                    if (totalAlive == 0 && GetAliveCommanderCount() == 0 && ISBSpawnedThisRaid && !NoAliveMessageShown)
                    {
                        ShowNoAliveISBMessage();
                        CopyCurrentCountsToLastShown();
                        NoAliveMessageShown = true;
                        BroadcastNetworkSnapshot(NetworkReasonWithdrawal);
                    }
                }
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