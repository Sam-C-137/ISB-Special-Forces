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
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "Assembly-CSharp.dll" };

        public static void Patch(ref AssemblyDefinition assembly)
        {
            var ISBBrains = new List<string>() { "PMC", "ExUsec" };
            var ISBSpecialForcesLayers = new List<string>() {
                "Request",
                //"FightReqNull",
                //"PeacecReqNull",
                "KnightFight",
                //"PtrlBirdEye",
				"PmcBear",
                "PmcUsec",
                "ExURequest"
            };

            int ISBBrainInt = 9;
            int ISBBrainInt2 = 24;
            int ISBBrainInt3 = 24;

            var ISBSpecialForces = new CustomWildSpawnType(13709, "ISBTeamLeader", "ISBBoss", ISBBrainInt2, true, false, false);

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

            CustomWildSpawnTypeManager.RegisterWildSpawnType(ISBSpecialForces, assembly);

            ISBSpecialForces = new CustomWildSpawnType(13710, "ISBSecondLeader", "ISBGuard", ISBBrainInt3, false, true, false);

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

            CustomWildSpawnTypeManager.RegisterWildSpawnType(ISBSpecialForces, assembly);

            ISBSpecialForces = new CustomWildSpawnType(13701, "ISBSpecialForces", "ISB", ISBBrainInt, false, true, false);

            ISBSpecialForces.SetCountAsBossForStatistics(true);

            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);

            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });

            SAINSettings settings = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB Operator",

                Description = "An special operator from the Military Coalition established by the ISB",

                Section = "ISB Special Forces",

                BaseBrain = "PMC",

                BrainsToApply = ISBBrains,

                LayersToRemove = ISBSpecialForcesLayers,

                DifficultyModifier = 1.0f
            };

            ISBSpecialForces.SetSAINSettings(settings);

            CustomWildSpawnTypeManager.RegisterWildSpawnType(ISBSpecialForces, assembly);

            ISBSpecialForces = new CustomWildSpawnType(13707, "ISBBossCommander", "ISBCommander", ISBBrainInt2, true, false, false);

            ISBSpecialForces.SetCountAsBossForStatistics(true);

            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);

            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });

            SAINSettings settings3 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Five",

                Description = "An elite operator in the ISB Special Forces. The limited information available about him describes him as the pride of Aishi. Internally, he is sometimes called “Evernight,” but in Tarkov he is known as “The Chosen One.”",

                Section = "ISB Special Forces",

                BaseBrain = "PMC",

                BrainsToApply = ISBBrains,

                LayersToRemove = ISBSpecialForcesLayers,

                DifficultyModifier = 2.5f
            };

            ISBSpecialForces.SetSAINSettings(settings3);

            CustomWildSpawnTypeManager.RegisterWildSpawnType(ISBSpecialForces, assembly);

            ISBSpecialForces = new CustomWildSpawnType(13708, "ISBFollowerCommander", "ISBBossFollower", ISBBrainInt2, false, true, false);

            ISBSpecialForces.SetCountAsBossForStatistics(true);

            ISBSpecialForces.SetShouldUseFenceNoBossAttack(true, false);

            ISBSpecialForces.SetExcludedDifficulties(new List<int> { 0, 1, 2 });

            SAINSettings settings4 = new SAINSettings(ISBSpecialForces.WildSpawnTypeValue)
            {
                Name = "ISB March Six",

                Description = "An elite operator in the ISB’s special forces. The limited information available about him describes him as extremely highly skilled and a member of the White Tusks Special Task Force. The report that was discovered leaves room for interpretation that he may be a former Black Division operator who defected after discovering confidential information about TerraGroup’s experiments. Today, he fights alongside “The Chosen One,” bearing the title “The Dark Lord.”",

                Section = "ISB Special Forces",

                BaseBrain = "PMC",

                BrainsToApply = ISBBrains,

                LayersToRemove = ISBSpecialForcesLayers,

                DifficultyModifier = 2.15f
            };

            ISBSpecialForces.SetSAINSettings(settings4);

            CustomWildSpawnTypeManager.RegisterWildSpawnType(ISBSpecialForces, assembly);

            CustomWildSpawnTypeManager.AddSuitableGroup(new List<int> { 13709, 13710, 13701, 13707, 13708 });
        }

    }
}