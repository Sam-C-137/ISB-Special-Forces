using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ISBSpecialForces.Components
{
    public sealed class ISBSAINPresetMigration : MonoBehaviour
    {
        private const string SainGuid = "me.sol.sain";
        private const string ConfigRoute = "/ISB/sainDefaults";
        private const string HarmonyId = "com.isb.specialforces.sain-preset-migration";
        private const string StateDirectoryName = "ISBSpecialForces";
        private const string StateFileName = "sain_preset_migration_state.json";
        private static readonly RoleDefinition[] Roles =
        {
            new RoleDefinition("ISBSpecialForces", 13701, "ISB Operative"),
            new RoleDefinition("ISBBossFirefly", 13702, "ISB March One"),
            new RoleDefinition("ISBFollowerVipper", 13703, "ISB March Two"),
            new RoleDefinition("ISBFollowerLoghan", 13704, "ISB March Three"),
            new RoleDefinition("ISBFollowerSpearthrower", 13705, "ISB March Four"),
            new RoleDefinition("ISBFollowerZofia", 13706, "ISB Zofia"),
            new RoleDefinition("ISBBossCommander", 13707, "ISB Vengeful The Chosen One"),
            new RoleDefinition("ISBFollowerCommander", 13708, "ISB Vengeful The Dark Lord"),
            new RoleDefinition("ISBTeamLeader", 13709, "ISB Team Leader"),
            new RoleDefinition("ISBSecondLeader", 13710, "ISB Specialist"),
            new RoleDefinition("ISBScout", 13711, "ISB Scout"),
            new RoleDefinition("ISBHeavy", 13712, "ISB Heavy"),
            new RoleDefinition("ISBAssassin", 13713, "ISB Assassin"),
            new RoleDefinition("ISBMarksman", 13714, "ISB Marksman"),
            new RoleDefinition("ISBFollowerEvernight", 13715, "ISB March Six"),
            new RoleDefinition("ISBFollowerNemesis", 13716, "ISB March Seven")
        };
        private static readonly JsonSerializerSettings SainJsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters =
            {
                new StringEnumConverter()
            }
        };
        private static ISBSAINPresetMigration _instance;
        private ISBSAINDefaultsConfig _config;
        private ISBSAINMigrationState _state;
        private Harmony _harmony;
        private Assembly _sainAssembly;
        private Type _presetHandlerType;
        private FieldInfo _loadedPresetField;
        private MethodInfo _initPresetFromDefinitionMethod;
        private MethodInfo _updateExistingBotsMethod;
        private string _sainPluginFolder;
        private string _sainPresetsFolder;
        private string _statePath;
        private bool _runtimeReady;
        private bool _runtimePatchBusy;
        private static PropertyInfo _forgetEnemyTime;
        private void Start()
        {
            if (!Chainloader.PluginInfos.ContainsKey(SainGuid))
            {
                Destroy(this);
                return;
            }

            _instance = this;
            StartCoroutine(Initialize());
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }

        private IEnumerator Initialize()
        {
            float configTimeout = Time.realtimeSinceStartup + 15f;
            while (Time.realtimeSinceStartup < configTimeout)
            {
                if (TryLoadConfig())
                    break;
                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (_config == null)
            {
                LogError("SAIN is installed, but ISB SAIN defaults could not be loaded. No SAIN presets were changed.");
                Destroy(this);
                yield break;
            }

            if (!_config.enabled || _config.migrationVersion <= 0 || !HasConfiguredOverrides())
            {
                Destroy(this);
                yield break;
            }

            if (!TryResolveSainRuntime())
            {
                LogError("SAIN was detected, but the SAIN 4.4.x preset runtime could not be resolved. No SAIN presets were changed.");
                Destroy(this);
                yield break;
            }

            float sainTimeout = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < sainTimeout)
            {
                if (RuntimeContainsConfiguredRoles())
                {
                    _runtimeReady = true;
                    break;
                }

                yield return new WaitForSecondsRealtime(0.5f);
            }

            if (!_runtimeReady)
            {
                LogError("SAIN was detected, but its loaded preset did not contain all configured ISB bot types. No SAIN presets were changed.");
                Destroy(this);
                yield break;
            }

            LoadLocalState();
            InstallRuntimeHook();
            bool migrationRequired = _state.migrationVersion < _config.migrationVersion;
            if (migrationRequired)
            {
                if (!MigrateAllExistingCustomPresets())
                {
                    LogError("One or more SAIN presets could not be migrated. The migration marker was not advanced, so ISB will retry next launch.");
                    yield break;
                }

                _state.migrationVersion = _config.migrationVersion;
                SaveLocalState();
            }

            HandleLoadedPreset(isCopy: false, forceCurrentCustom: migrationRequired);
        }

        private bool TryLoadConfig()
        {
            try
            {
                string json = RequestHandler.GetJson(ConfigRoute);
                if (string.IsNullOrWhiteSpace(json))
                    return false;
                _config = JsonConvert.DeserializeObject<ISBSAINDefaultsConfig>(json);
                return _config != null;
            }
            catch
            {
                return false;
            }
        }

        private bool TryResolveSainRuntime()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(SainGuid, out var pluginInfo) || pluginInfo?.Instance == null)
                    return false;
                _sainAssembly = pluginInfo.Instance.GetType().Assembly;
                _sainPluginFolder = Path.GetDirectoryName(_sainAssembly.Location);
                if (string.IsNullOrWhiteSpace(_sainPluginFolder))
                    return false;
                _sainPresetsFolder = Path.Combine(_sainPluginFolder, "Presets");
                _statePath = Path.Combine(BepInEx.Paths.ConfigPath, StateDirectoryName, StateFileName);
                _presetHandlerType = _sainAssembly.GetType("SAIN.Plugin.PresetHandler", false);
                if (_presetHandlerType == null)
                    return false;
                _loadedPresetField = _presetHandlerType.GetField("LoadedPreset", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                _initPresetFromDefinitionMethod = _presetHandlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).FirstOrDefault(method => method.Name == "InitPresetFromDefinition" && method.GetParameters().Length >= 1);
                _updateExistingBotsMethod = _presetHandlerType.GetMethod("UpdateExistingBots", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return _loadedPresetField != null && _initPresetFromDefinitionMethod != null;
            }
            catch
            {
                return false;
            }
        }

        private bool RuntimeContainsConfiguredRoles()
        {
            try
            {
                object loadedPreset = _loadedPresetField?.GetValue(null);
                if (loadedPreset == null)
                    return false;
                IDictionary dictionary = GetRuntimeSettingsDictionary(loadedPreset);
                if (dictionary == null)
                    return false;
                foreach (RoleDefinition role in Roles)
                {
                    if (!IsRoleConfigured(role))
                        continue;
                    if (FindRuntimeGroup(dictionary, role.EnumValue) == null)
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool MigrateAllExistingCustomPresets()
        {
            if (!Directory.Exists(_sainPresetsFolder))
            {
                return true;
            }

            int checkedCount = 0;
            int changedCount = 0;
            foreach (string presetDirectory in Directory.GetDirectories(_sainPresetsFolder))
            {
                string infoPath = Path.Combine(presetDirectory, "Info.json");
                if (!File.Exists(infoPath))
                    continue;
                try
                {
                    JObject info = JObject.Parse(File.ReadAllText(infoPath));
                    if (!GetBoolToken(info, "IsCustom"))
                        continue;
                    string presetName = GetToken(info, "Name")?.ToString();
                    if (string.IsNullOrWhiteSpace(presetName))
                        presetName = Path.GetFileName(presetDirectory);
                    checkedCount++;
                    if (!PatchCustomPresetDirectory(presetDirectory, forceConfiguredValues: true, out bool presetChanged))
                    {
                        return false;
                    }

                    if (presetChanged)
                        changedCount++;
                    MarkPresetSeeded(presetName, saveImmediately: false);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            return true;
        }

        private bool PatchCustomPresetDirectory(string presetDirectory, bool forceConfiguredValues, out bool changed)
        {
            changed = false;
            string botSettingsDirectory = Path.Combine(presetDirectory, "BotSettings");
            Directory.CreateDirectory(botSettingsDirectory);
            foreach (RoleDefinition role in Roles)
            {
                if (!TryGetRoleConfig(role, out ISBSAINRoleDefaults roleConfig) || roleConfig == null || !roleConfig.enabled)
                    continue;
                JObject rolePatch = BuildRolePatch(roleConfig);
                if (!rolePatch.HasValues)
                    continue;
                string botFile = Path.Combine(botSettingsDirectory, role.SainName + ".json");
                JObject group;
                if (File.Exists(botFile))
                {
                    group = JObject.Parse(File.ReadAllText(botFile));
                }
                else
                {
                    group = CreateFreshGroupTemplate(role);
                    if (group == null)
                    {
                        return false;
                    }

                    changed = true;
                }

                if (!forceConfiguredValues && File.Exists(botFile))
                    continue;
                JObject before = (JObject)group.DeepClone();
                ApplySafePatch(group, rolePatch);
                SetPropertyIgnoreCase(group, "Name", role.SainName);
                EnsureWildSpawnType(group, role);
                if (!File.Exists(botFile) || !JToken.DeepEquals(before, group))
                {
                    WriteSainJson(botFile, group);
                    changed = true;
                }
            }

            return true;
        }

        private JObject CreateFreshGroupTemplate(RoleDefinition role)
        {
            try
            {
                object loadedPreset = _loadedPresetField?.GetValue(null);
                if (loadedPreset == null)
                    return null;
                object botSettings = GetMemberValue(loadedPreset, "BotSettings");
                IDictionary dictionary = GetRuntimeSettingsDictionary(loadedPreset);
                object existingRuntimeGroup = FindRuntimeGroup(dictionary, role.EnumValue);
                if (botSettings == null || existingRuntimeGroup == null)
                    return null;
                Type groupType = existingRuntimeGroup.GetType();
                ConstructorInfo constructor = groupType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(ctor =>
                {
                    ParameterInfo[] parameters = ctor.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsArray && parameters[0].ParameterType.GetElementType()?.IsEnum == true;
                });
                if (constructor == null)
                    return null;
                Type difficultyEnumType = constructor.GetParameters()[0].ParameterType.GetElementType();
                string[] difficultyNames =
                {
                    "easy",
                    "normal",
                    "hard",
                    "impossible"
                };
                Array difficulties = Array.CreateInstance(difficultyEnumType, difficultyNames.Length);
                for (int i = 0; i < difficultyNames.Length; i++)
                {
                    object difficulty = Enum.Parse(difficultyEnumType, difficultyNames[i], true);
                    difficulties.SetValue(difficulty, i);
                }

                object freshGroup = constructor.Invoke(new object[] { difficulties });
                SetMemberValue(freshGroup, "Name", role.SainName);
                SetEnumMemberValue(freshGroup, "WildSpawnType", role.EnumValue);
                if (TryGetSainDefaultDifficultyModifier(botSettings, role.EnumValue, out float defaultModifier))
                    SetMemberValue(freshGroup, "DifficultyModifier", defaultModifier);
                else
                {
                    object runtimeModifier = GetMemberValue(existingRuntimeGroup, "DifficultyModifier");
                    if (runtimeModifier != null)
                        SetMemberValue(freshGroup, "DifficultyModifier", runtimeModifier);
                }

                MethodInfo updateToEftDefault = botSettings.GetType().GetMethod("UpdateSAINSettingsToEFTDefault", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (updateToEftDefault == null)
                    return null;
                ParameterInfo[] updateParameters = updateToEftDefault.GetParameters();
                if (updateParameters.Length != 2 || !updateParameters[0].ParameterType.IsEnum)
                    return null;
                object wildSpawnType = Enum.ToObject(updateParameters[0].ParameterType, role.EnumValue);
                updateToEftDefault.Invoke(botSettings, new[] { wildSpawnType, freshGroup });
                MethodInfo init = groupType.GetMethod("Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                init?.Invoke(freshGroup, null);
                JsonSerializer serializer = JsonSerializer.Create(SainJsonSettings);
                return JObject.FromObject(freshGroup, serializer);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool TryGetSainDefaultDifficultyModifier(object botSettings, int enumValue, out float value)
        {
            value = 0f;
            if (botSettings == null)
                return false;
            FieldInfo field = botSettings.GetType().GetField("DefaultDifficultyModifier", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (!(field?.GetValue(null)is IDictionary dictionary))
                return false;
            foreach (DictionaryEntry entry in dictionary)
            {
                try
                {
                    if (Convert.ToInt32(entry.Key) != enumValue)
                        continue;
                    value = Convert.ToSingle(entry.Value);
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private static IDictionary GetRuntimeSettingsDictionary(object preset)
        {
            if (preset == null)
                return null;
            object botSettings = GetMemberValue(preset, "BotSettings");
            object dictionaryObject = GetMemberValue(botSettings, "SAINSettings");
            return dictionaryObject as IDictionary;
        }

        private void InstallRuntimeHook()
        {
            if (_initPresetFromDefinitionMethod == null)
                return;
            try
            {
                MethodInfo postfix = typeof(ISBSAINPresetMigration).GetMethod(nameof(SainPresetLoadedPostfix), BindingFlags.NonPublic | BindingFlags.Static);
                if (postfix == null)
                    return;
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(_initPresetFromDefinitionMethod, postfix: new HarmonyMethod(postfix));
                var info = _sainAssembly.GetType("SAIN.SAINComponent.Classes.Info.SAINBotInfoClass", true);
                _forgetEnemyTime = AccessTools.Property(info, "ForgetEnemyTime");
                var search = AccessTools.Method(info, "CalcTimeBeforeSearch");
                if (search == null || _forgetEnemyTime?.GetSetMethod(true) == null)
                    throw new MissingMemberException("SAIN enemy memory settings");
                _harmony.Patch(search, postfix: new HarmonyMethod(typeof(ISBSAINPresetMigration), nameof(SainMemoryPostfix)));
            }
            catch (Exception ex)
            {
                LogError($"Could not install SAIN preset-load hook: {UnwrapExceptionMessage(ex)}");
            }
        }

        private static void SainPresetLoadedPostfix(object[] __args)
        {
            ISBSAINPresetMigration instance = _instance;
            if (instance == null || !instance._runtimeReady || instance._runtimePatchBusy)
                return;
            bool isCopy = __args != null && __args.Length > 1 && __args[1] is bool copy && copy;
            instance.HandleLoadedPreset(isCopy, forceCurrentCustom: false);
        }

        private static void SainMemoryPostfix(object __instance)
        {
            var bot = GetMemberValue(__instance, "BotOwner") as EFT.BotOwner;
            if (bot?.Profile?.Info?.Settings == null || !WildSpawnTypeExtensions.IsISB(bot.Profile.Info.Settings.Role))
                return;
            bot.Settings.FileSettings.Mind.TIME_TO_FORGOR_ABOUT_ENEMY_SEC = 952f;
            _forgetEnemyTime.SetValue(__instance, 952f, null);
        }

        private void HandleLoadedPreset(bool isCopy, bool forceCurrentCustom)
        {
            if (!_runtimeReady || _runtimePatchBusy || _loadedPresetField == null)
                return;
            object loadedPreset = _loadedPresetField.GetValue(null);
            if (loadedPreset == null)
                return;
            object infoObject = GetMemberValue(loadedPreset, "Info");
            bool isCustom = GetBoolMemberValue(infoObject, "IsCustom");
            string presetName = GetMemberValue(infoObject, "Name")?.ToString();
            if (!isCustom)
            {
                PatchLoadedPresetInMemory(persistCustomFiles: false);
                return;
            }

            if (string.IsNullOrWhiteSpace(presetName))
                return;
            if (isCopy)
            {
                PatchLoadedPresetInMemory(persistCustomFiles: false, requestedOnly: true);
                MarkPresetSeeded(presetName, saveImmediately: true);
                return;
            }

            bool needsSeed = forceCurrentCustom || !IsPresetSeeded(presetName);
            if (!needsSeed)
            {
                PatchLoadedPresetInMemory(persistCustomFiles: false, requestedOnly: true);
                return;
            }
            if (PatchLoadedPresetInMemory(persistCustomFiles: true))
                MarkPresetSeeded(presetName, saveImmediately: true);
        }

        private bool PatchLoadedPresetInMemory(bool persistCustomFiles, bool requestedOnly = false)
        {
            if (_runtimePatchBusy || _loadedPresetField == null)
                return false;
            _runtimePatchBusy = true;
            try
            {
                object loadedPreset = _loadedPresetField.GetValue(null);
                if (loadedPreset == null)
                    return false;
                object infoObject = GetMemberValue(loadedPreset, "Info");
                string presetName = GetMemberValue(infoObject, "Name")?.ToString();
                IDictionary dictionary = GetRuntimeSettingsDictionary(loadedPreset);
                if (dictionary == null)
                    return false;
                string customBotSettingsDirectory = null;
                if (persistCustomFiles && !string.IsNullOrWhiteSpace(presetName))
                {
                    customBotSettingsDirectory = Path.Combine(_sainPresetsFolder, presetName, "BotSettings");
                    Directory.CreateDirectory(customBotSettingsDirectory);
                }

                int patched = 0;
                foreach (RoleDefinition role in Roles)
                {
                    if (!TryGetRoleConfig(role, out ISBSAINRoleDefaults roleConfig) || roleConfig == null || !roleConfig.enabled)
                        continue;
                    JObject rolePatch = BuildRolePatch(roleConfig);
                    if (requestedOnly && rolePatch["Settings"] is JObject difficulties)
                    {
                        var limited = new JObject();
                        foreach (var difficulty in difficulties.Properties())
                        {
                            var settings = new JObject();
                            foreach (var path in new[] { "Aiming.AIMING_TYPE", "Aiming.ANY_PART_SHOOT_TIME", "Look.GOAL_TO_FULL_DISSAPEAR", "Look.GOAL_TO_FULL_DISSAPEAR_GREEN", "Grenade.GrenadePrecision" })
                            {
                                var value = difficulty.Value.SelectToken(path);
                                if (value == null) continue;
                                var parts = path.Split('.');
                                var category = settings[parts[0]] as JObject;
                                if (category == null) settings[parts[0]] = category = new JObject();
                                category[parts[1]] = value.DeepClone();
                            }
                            limited[difficulty.Name] = settings;
                        }
                        rolePatch = new JObject { ["Settings"] = limited };
                    }
                    if (!rolePatch.HasValues)
                        continue;
                    object group = FindRuntimeGroup(dictionary, role.EnumValue);
                    if (group == null)
                        continue;
                    JsonSerializer serializer = JsonSerializer.Create(SainJsonSettings);
                    JObject serialized = JObject.FromObject(group, serializer);
                    JObject before = (JObject)serialized.DeepClone();
                    ApplySafePatch(serialized, rolePatch);
                    SetPropertyIgnoreCase(serialized, "Name", role.SainName);
                    EnsureWildSpawnType(serialized, role);
                    if (!JToken.DeepEquals(before, serialized))
                    {
                        JsonConvert.PopulateObject(serialized.ToString(Formatting.None), group);
                        MethodInfo init = group.GetType().GetMethod("Init", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        init?.Invoke(group, null);
                        patched++;
                    }

                    if (GetMemberValue(group, "Settings") is IDictionary runtimeDifficulties && roleConfig.difficulties != null)
                        foreach (DictionaryEntry difficulty in runtimeDifficulties)
                        {
                            var configured = roleConfig.difficulties.FirstOrDefault(d => string.Equals(d.Key, difficulty.Key.ToString(), StringComparison.OrdinalIgnoreCase)).Value;
                            if (configured == null) continue;
                            foreach (var path in new[] { "Aiming.AIMING_TYPE", "Aiming.ANY_PART_SHOOT_TIME", "Look.GOAL_TO_FULL_DISSAPEAR", "Look.GOAL_TO_FULL_DISSAPEAR_GREEN", "Grenade.GrenadePrecision" })
                            {
                                var value = configured.SelectToken(path);
                                if (value == null) continue;
                                var parts = path.Split('.');
                                var category = GetMemberValue(difficulty.Value, parts[0]);
                                var field = category?.GetType().GetField(parts[1], BindingFlags.Public | BindingFlags.Instance);
                                if (field == null) throw new MissingFieldException("SAIN native setting " + path);
                                field.SetValue(category, value.ToObject(field.FieldType));
                            }
                        }

                    if (customBotSettingsDirectory != null)
                    {
                        string path = Path.Combine(customBotSettingsDirectory, role.SainName + ".json");
                        WriteSainJson(path, serialized);
                    }
                }

                if (patched > 0)
                    _updateExistingBotsMethod?.Invoke(null, null);
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Could not apply ISB defaults to the loaded SAIN preset: {UnwrapExceptionMessage(ex)}");
                return false;
            }
            finally
            {
                _runtimePatchBusy = false;
            }
        }

        private static object FindRuntimeGroup(IDictionary dictionary, int enumValue)
        {
            if (dictionary == null)
                return null;
            foreach (DictionaryEntry entry in dictionary)
            {
                try
                {
                    if (Convert.ToInt32(entry.Key) == enumValue)
                        return entry.Value;
                }
                catch
                {
                }
            }

            return null;
        }

        private void LoadLocalState()
        {
            _state = new ISBSAINMigrationState();
            try
            {
                if (!string.IsNullOrWhiteSpace(_statePath) && File.Exists(_statePath))
                {
                    ISBSAINMigrationState loaded = JsonConvert.DeserializeObject<ISBSAINMigrationState>(File.ReadAllText(_statePath));
                    if (loaded != null)
                        _state = loaded;
                }
            }
            catch (Exception)
            {
                _state = new ISBSAINMigrationState();
            }

            _state.seededPresets = new Dictionary<string, int>(_state.seededPresets ?? new Dictionary<string, int>(), StringComparer.OrdinalIgnoreCase);
        }

        private void SaveLocalState()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_statePath))
                    return;
                string directory = Path.GetDirectoryName(_statePath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);
                File.WriteAllText(_statePath, JsonConvert.SerializeObject(_state, Formatting.Indented) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                LogError($"Could not save local SAIN migration state: {ex.Message}");
            }
        }

        private bool IsPresetSeeded(string presetName)
        {
            if (_state?.seededPresets == null || string.IsNullOrWhiteSpace(presetName))
                return false;
            return _state.seededPresets.TryGetValue(presetName, out int version) && version >= _config.migrationVersion;
        }

        private void MarkPresetSeeded(string presetName, bool saveImmediately)
        {
            if (_state == null || string.IsNullOrWhiteSpace(presetName))
                return;
            if (_state.seededPresets == null)
                _state.seededPresets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _state.seededPresets[presetName] = _config.migrationVersion;
            if (saveImmediately)
                SaveLocalState();
        }

        private static JObject BuildRolePatch(ISBSAINRoleDefaults roleConfig)
        {
            var patch = new JObject();
            if (roleConfig == null)
                return patch;
            if (roleConfig.patch != null && roleConfig.patch.HasValues)
            {
                patch.Merge(roleConfig.patch.DeepClone(), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Ignore });
            }

            if (roleConfig.difficultyModifier.HasValue)
                patch["DifficultyModifier"] = roleConfig.difficultyModifier.Value;
            if (roleConfig.difficulties != null && roleConfig.difficulties.Count > 0)
            {
                var settingsPatch = new JObject();
                foreach (KeyValuePair<string, JObject> difficulty in roleConfig.difficulties)
                {
                    if (string.IsNullOrWhiteSpace(difficulty.Key) || difficulty.Value == null)
                        continue;
                    JObject difficultyPatch = (JObject)difficulty.Value.DeepClone();
                    PruneUnsetValues(difficultyPatch);
                    if (difficultyPatch.HasValues)
                        settingsPatch[difficulty.Key] = difficultyPatch;
                }

                if (settingsPatch.HasValues)
                    patch["Settings"] = settingsPatch;
            }

            PruneUnsetValues(patch);
            return patch;
        }

        private static void PruneUnsetValues(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties().ToList())
                {
                    if (property.Value == null || property.Value.Type == JTokenType.Null || property.Value.Type == JTokenType.Undefined)
                    {
                        property.Remove();
                        continue;
                    }

                    PruneUnsetValues(property.Value);
                    if ((property.Value is JObject childObject && !childObject.HasValues) || (property.Value is JArray childArray && childArray.Count == 0))
                    {
                        property.Remove();
                    }
                }
            }
            else if (token is JArray array)
            {
                for (int i = array.Count - 1; i >= 0; i--)
                {
                    JToken item = array[i];
                    if (item == null || item.Type == JTokenType.Null || item.Type == JTokenType.Undefined)
                    {
                        array.RemoveAt(i);
                        continue;
                    }

                    PruneUnsetValues(item);
                }
            }
        }

        private static void ApplySafePatch(JObject target, JObject patch)
        {
            if (target == null || patch == null)
                return;
            JObject safePatch = (JObject)patch.DeepClone();
            RemovePropertyIgnoreCase(safePatch, "Name");
            RemovePropertyIgnoreCase(safePatch, "WildSpawnType");
            AdaptAimingPatch(target, safePatch);
            target.Merge(safePatch, new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace, MergeNullValueHandling = MergeNullValueHandling.Ignore });
        }

        private static void AdaptAimingPatch(JObject target, JObject patch)
        {
            if (!(patch["Settings"] is JObject settings) || !(target["Settings"] is JObject current))
                return;
            foreach (JProperty difficulty in settings.Properties())
            {
                var aiming = difficulty.Value["Aiming"] as JObject;
                var native = current[difficulty.Name]?["Aiming"] as JObject;
                if (aiming?.Property("AimCenterMass") == null || native?.Property("OverrideAimTargetWeights") == null)
                    continue;
                bool centerMass = aiming.Value<bool>("AimCenterMass");
                aiming.Remove("AimCenterMass");
                native.Remove("AimCenterMass");
                aiming["OverrideAimTargetWeights"] = centerMass;
                if (centerMass)
                    aiming["AimTargetWeights"] = new JObject
                    {
                        ["Head"] = .3f,
                        ["Chest"] = 4f,
                        ["Stomach"] = 2.5f,
                        ["Arms"] = 0f,
                        ["Legs"] = 0f
                    };
            }
        }

        private static void EnsureWildSpawnType(JObject group, RoleDefinition role)
        {
            JProperty property = group.Properties().FirstOrDefault(p => string.Equals(p.Name, "WildSpawnType", StringComparison.OrdinalIgnoreCase));
            if (property == null)
            {
                group["WildSpawnType"] = role.EnumValue;
                return;
            }

            property.Value = role.EnumValue;
        }

        private static void SetPropertyIgnoreCase(JObject obj, string name, JToken value)
        {
            JProperty property = obj?.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (property != null)
                property.Value = value;
            else if (obj != null)
                obj[name] = value;
        }

        private static void WriteSainJson(string path, JObject value)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, value.ToString(Formatting.Indented) + Environment.NewLine);
        }

        private bool TryGetRoleConfig(RoleDefinition role, out ISBSAINRoleDefaults config)
        {
            config = null;
            if (_config?.wildtypes == null)
                return false;
            foreach (KeyValuePair<string, ISBSAINRoleDefaults> entry in _config.wildtypes)
            {
                if (string.Equals(entry.Key, role.ConfigKey, StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Key, role.EnumValue.ToString(), StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Key, role.SainName, StringComparison.OrdinalIgnoreCase))
                {
                    config = entry.Value;
                    return true;
                }
            }

            return false;
        }

        private bool IsRoleConfigured(RoleDefinition role)
        {
            return TryGetRoleConfig(role, out ISBSAINRoleDefaults roleConfig) && roleConfig != null && roleConfig.enabled && BuildRolePatch(roleConfig).HasValues;
        }

        private bool HasConfiguredOverrides()
        {
            return Roles.Any(IsRoleConfigured);
        }

        private static bool GetBoolToken(JObject parent, string name)
        {
            JToken token = GetToken(parent, name);
            if (token == null)
                return false;
            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();
            return bool.TryParse(token.ToString(), out bool value) && value;
        }

        private static JToken GetToken(JObject parent, string name)
        {
            if (parent == null)
                return null;
            foreach (JProperty property in parent.Properties())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            }

            return null;
        }

        private static void RemovePropertyIgnoreCase(JObject obj, string name)
        {
            if (obj == null)
                return;
            JProperty property = obj.Properties().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            property?.Remove();
        }

        private static void SetMemberValue(object instance, string name, object value)
        {
            if (instance == null)
                return;
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value, null);
                return;
            }

            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(instance, value);
        }

        private static void SetEnumMemberValue(object instance, string name, int enumValue)
        {
            if (instance == null)
                return;
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null && property.CanWrite && property.PropertyType.IsEnum)
            {
                property.SetValue(instance, Enum.ToObject(property.PropertyType, enumValue), null);
                return;
            }

            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.FieldType.IsEnum)
                field.SetValue(instance, Enum.ToObject(field.FieldType, enumValue));
        }

        private static object GetMemberValue(object instance, string name)
        {
            if (instance == null)
                return null;
            Type type = instance.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property != null)
                return property.GetValue(instance, null);
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(instance);
        }

        private static bool GetBoolMemberValue(object instance, string name)
        {
            object value = GetMemberValue(instance, name);
            return value is bool boolean && boolean;
        }

        private static string UnwrapExceptionMessage(Exception ex)
        {
            while (ex is TargetInvocationException invocation && invocation.InnerException != null)
                ex = invocation.InnerException;
            return ex?.Message ?? "Unknown error";
        }

        private void LogError(string message)
        {
            Plugin.LogSource?.LogError($"[ISB SAIN] {message}");
        }

        private sealed class RoleDefinition
        {
            public RoleDefinition(string configKey, int enumValue, string sainName)
            {
                ConfigKey = configKey;
                EnumValue = enumValue;
                SainName = sainName;
            }

            public string ConfigKey { get; }
            public int EnumValue { get; }
            public string SainName { get; }
        }
    }

    public sealed class ISBSAINDefaultsConfig
    {
        public bool enabled { get; set; } = true;
        public bool logs { get; set; } = false;
        public int migrationVersion { get; set; } = 1;
        public Dictionary<string, ISBSAINRoleDefaults> wildtypes { get; set; } = new Dictionary<string, ISBSAINRoleDefaults>();
    }

    public sealed class ISBSAINRoleDefaults
    {
        public bool enabled { get; set; } = true;
        public float? difficultyModifier { get; set; }
        public Dictionary<string, JObject> difficulties { get; set; } = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        public JObject patch { get; set; } = new JObject();
    }

    public sealed class ISBSAINMigrationState
    {
        public int migrationVersion { get; set; } = 0;
        public Dictionary<string, int> seededPresets { get; set; } = new Dictionary<string, int>();
    }
}
