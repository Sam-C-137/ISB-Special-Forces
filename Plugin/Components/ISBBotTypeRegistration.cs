using System;
using System.Collections.Generic;
using EFT;
using HarmonyLib;
using MoreBotsAPI;

namespace ISBSpecialForces.Components
{
    internal static class ISBBotTypeRegistration
    {
        private static bool _registered;
        public static void Initialize()
        {
            if (_registered)
                return;
            if (!Enum.TryParse<EFT.Counters.EFenceStandingSource>("ISBKill", out var standingSource) || (int)standingSource != 1000)
                throw new InvalidOperationException("ISB reputation enum is missing. Restart the server and game with the ISB enum extensions installed.");
            bool registerGroup = !CustomWildSpawnTypeManager.GetCustomWildSpawnTypeDict().ContainsKey(13709);
            var ISBBrains = new List<string>()
            {
                "PMC",
                "ExUsec"
            };
            var ISBSpecialForcesLayers = new List<string>()
            {
                "Request",
                "PmcBear",
                "PmcUsec",
                "ExURequest"
            };
            int ISBBrainInt = 9;
            var ISBSpecialForces = new CustomWildSpawnType(13709, "ISBTeamLeader", "ISBBoss", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(false, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings2 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Team Leader",
                Description = "A senior specialist operator who was handpicked by Aishi to lead the teams during ISB operations.",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 1.50f
            };
            ISBSpecialForces.SetSAINSettings(settings2);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13710, "ISBSecondLeader", "ISBGuard", ISBBrainInt, false, true, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(false, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings44 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Specialist",
                Description = "A senior operator with extensive knowledge and experience. Assigned to provide on-site support to field leaders.",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 1.25f
            };
            ISBSpecialForces.SetSAINSettings(settings44);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13711, "ISBScout", "ISBScout", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings10 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Scout",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 2.5f
            };
            ISBSpecialForces.SetSAINSettings(settings10);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13712, "ISBHeavy", "ISBHeavy", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings11 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Heavy",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 1.5f
            };
            ISBSpecialForces.SetSAINSettings(settings11);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13713, "ISBAssassin", "ISBAssassin", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings13 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Assassin",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 1.8f
            };
            ISBSpecialForces.SetSAINSettings(settings13);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13714, "ISBMarksman", "ISBSniper", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings12 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Marksman",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 2.0f
            };
            ISBSpecialForces.SetSAINSettings(settings12);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13701, "ISBSpecialForces", "ISB", ISBBrainInt, false, true, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Operative",
                Description = "An special operative from the Military Coalition established by the ISB",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 1.0f
            };
            ISBSpecialForces.SetSAINSettings(settings);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13702, "ISBBossFirefly", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings6 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March One",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 5.0f
            };
            ISBSpecialForces.SetSAINSettings(settings6);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13703, "ISBFollowerVipper", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings7 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Two",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 4.5f
            };
            ISBSpecialForces.SetSAINSettings(settings7);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13704, "ISBFollowerLoghan", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings8 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Three",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 4.0f
            };
            ISBSpecialForces.SetSAINSettings(settings8);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13705, "ISBFollowerSpearthrower", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings9 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Four",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 3.8f
            };
            ISBSpecialForces.SetSAINSettings(settings9);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13706, "ISBFollowerZofia", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings16 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Zofia",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 1.5f
            };
            ISBSpecialForces.SetSAINSettings(settings16);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13715, "ISBFollowerEvernight", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings14 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Six",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 3.8f
            };
            ISBSpecialForces.SetSAINSettings(settings14);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13716, "ISBFollowerNemesis", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings15 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Seven",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 3.8f
            };
            ISBSpecialForces.SetSAINSettings(settings15);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13707, "ISBBossCommander", "ISBCommander", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings3 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Vengeful Chosen One",
                Description = "An elite operator in the ISB Special Forces. The limited information available about him describes him as the pride of Aishi. Internally, he is sometimes called “Evernight,” but in Tarkov he is known as “The Chosen One.”",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 2.5f
            };
            ISBSpecialForces.SetSAINSettings(settings3);
            RegisterBot(ISBSpecialForces);
            ISBSpecialForces = new CustomWildSpawnType(13708, "ISBFollowerCommander", "ISBBossFollower", ISBBrainInt, false, true, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings4 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Vengeful Dark Lord",
                Description = "An elite operator in the ISB’s special forces. The limited information available about him describes him as extremely highly skilled and a member of the White Tusks Special Task Force. The report that was discovered leaves room for interpretation that he may be a former Black Division operator who defected after discovering confidential information about TerraGroup’s experiments. Today, he fights alongside “The Chosen One,” bearing the title “The Dark Lord.”",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 2.15f
            };
            ISBSpecialForces.SetSAINSettings(settings4);
            RegisterBot(ISBSpecialForces);
            _registered = true;
            if (registerGroup)
                CustomWildSpawnTypeManager.AddSuitableGroup(new List<int> { 13701, 13702, 13703, 13704, 13705, 13706, 13707, 13708, 13709, 13710, 13711, 13712, 13713, 13714, 13715, 13716 });
        }

        private static void RegisterBot(CustomWildSpawnType botType)
        {
            if (!Enum.TryParse<WildSpawnType>(botType.WildSpawnTypeName, out var role) || (int)role != botType.WildSpawnTypeValue)
                throw new InvalidOperationException("ISB native enum is missing or mismatched: " + botType.WildSpawnTypeName);
            var registered = CustomWildSpawnTypeManager.GetCustomWildSpawnTypeDict();
            bool exists = registered.TryGetValue(botType.WildSpawnTypeValue, out var existing);
            if (exists && existing.WildSpawnTypeName != botType.WildSpawnTypeName)
                throw new InvalidOperationException("ISB bot ID conflicts with another registered role: " + botType.WildSpawnTypeValue);
            var excluded = AccessTools.Field(typeof(BotInternalSettingsController), "ExcludedDifficulties")?.GetValue(null) as Dictionary<WildSpawnType, List<BotDifficulty>> ?? throw new InvalidOperationException("ISB could not initialize bot difficulty settings.");
            excluded[role] = botType.ExcludedDifficulties.ConvertAll(value => (BotDifficulty)value);
            var settings = new WildSpawnTypeSettings(botType.IsBoss, botType.IsFollower, botType.IsHostileToEverybody, "ScavRole/" + botType.ScavRole, (ETagStatus)0);
            settings.CountAsBossForStatistics = botType.CountAsBossForStatistics;
            WildSpawnTypeExtension._spawnTypeSettings[role] = settings;
            if (exists)
                return;
            CustomWildSpawnTypeManager.GetCustomWildSpawnTypes().Add(botType);
            CustomWildSpawnTypeManager.GetCustomWildSpawnTypeDict().Add(botType.WildSpawnTypeValue, botType);
            if (botType.SAINSettings != null)
                CustomWildSpawnTypeManager.GetSAINSettings().Add(botType.SAINSettings);
        }
    }
}
