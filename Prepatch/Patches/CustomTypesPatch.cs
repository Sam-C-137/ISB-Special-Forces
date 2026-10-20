using BepInEx.Logging;
using Mono.Cecil;
using MoreBotsAPI;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace ISBSpecialForces.Prepatch
{
    public static class WildSpawnTypePatch
    {
        public static IEnumerable<string> TargetDLLs { get; } = new[]
        {
            "Assembly-CSharp.dll"
        };

        private static bool _registered;
        public static void Patch(ref AssemblyDefinition assembly)
        {
            if (_registered)
                return;
            ISBZofiaSainMigration.Run();
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
            int ISBBrainInt = 137;
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
            ISBSpecialForces = new CustomWildSpawnType(13706, "ISBFollowerZofia", "ISBWhiteTusk", ISBBrainInt, true, false, false);
            ISBSpecialForces.SetCountAsBossForStatistics(true);
            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);
            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });
            SAINSettings settings16 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Five",
                Description = "",
                Section = "ISB Special Forces",
                BaseBrain = "PMC",
                BrainsToApply = ISBBrains,
                LayersToRemove = ISBSpecialForcesLayers,
                DifficultyModifier = 1.5f
            };
            ISBSpecialForces.SetSAINSettings(settings16);
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
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
            RegisterBot(ISBSpecialForces, assembly);
            RegisterStandingSource(assembly);
            _registered = true;
            CustomWildSpawnTypeManager.AddSuitableGroup(new List<int> { 13701, 13702, 13703, 13704, 13705, 13706, 13707, 13708, 13709, 13710, 13711, 13712, 13713, 13714, 13715, 13716 });
        }

        private static void RegisterBot(CustomWildSpawnType botType, AssemblyDefinition assembly)
        {
            if (CustomWildSpawnTypeManager.GetCustomWildSpawnTypeDict().ContainsKey(botType.WildSpawnTypeValue))
                throw new System.InvalidOperationException("ISB bot ID already registered: " + botType.WildSpawnTypeValue);
            var enumType = assembly.MainModule.GetType("EFT.WildSpawnType");
            foreach (var field in enumType.Fields)
                if (field.Name == botType.WildSpawnTypeName || (field.HasConstant && System.Convert.ToInt32(field.Constant) == botType.WildSpawnTypeValue))
                    throw new System.InvalidOperationException("ISB client enum collision: " + botType.WildSpawnTypeName);
            CustomWildSpawnTypeManager.RegisterWildSpawnType(botType, assembly);
        }

        private static void RegisterStandingSource(AssemblyDefinition assembly)
        {
            var enumType = assembly.MainModule.GetType("EFT.Counters.EFenceStandingSource") ?? throw new System.InvalidOperationException("EFT reputation enum was not found.");
            foreach (var field in enumType.Fields)
            {
                if (field.Name != "ISBKill" && !(field.HasConstant && System.Convert.ToInt32(field.Constant) == 1000))
                    continue;
                if (field.Name == "ISBKill" && field.HasConstant && System.Convert.ToInt32(field.Constant) == 1000)
                    return;
                throw new System.InvalidOperationException("ISB reputation enum collision.");
            }

            MoreBotsAPI.Prepatch.Utils.AddEnumValue(ref enumType, "ISBKill", 1000);
        }
    }
}
