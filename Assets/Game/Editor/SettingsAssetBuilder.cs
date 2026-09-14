using System.IO;
using UnityEditor;
using UnityEngine;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;
using Grotto.Procedural;

namespace Grotto.Editor
{
    /// <summary>
    /// Generates the project's ScriptableObject assets.
    ///
    /// The repository stores no .asset files. They are YAML with embedded GUIDs that
    /// nobody can review in a pull request and that merge badly, and every value in
    /// them is a design decision that deserves to be written down next to its reason.
    /// So the campaign, the cast and the tuning live here as code, and this tool bakes
    /// them into assets a designer can then tweak in the inspector.
    ///
    /// Re-running it updates the existing assets in place rather than replacing them,
    /// so references from scenes survive, but any inspector edits are overwritten —
    /// which is the correct trade for a regeneration tool and is warned about first.
    /// </summary>
    public static class SettingsAssetBuilder
    {
        public const string ResourcesPath = "Assets/Game/Resources";
        public const string SettingsPath = "Assets/Game/Settings";
        public const string NightsPath = SettingsPath + "/Nights";

        /// <summary>
        /// The cast lives under Resources because it is loaded at runtime now: the
        /// scene no longer carries character references, since which characters stand
        /// where depends on the site the player picked at the menu.
        /// </summary>
        public const string CastPath = ResourcesPath + "/" + CastSpawner.ResourceFolder;

        [MenuItem("Tools/Grotto/Rebuild Settings Assets", priority = 20)]
        public static void RebuildWithPrompt()
        {
            bool proceed = EditorUtility.DisplayDialog(
                "Rebuild settings assets",
                "This regenerates GameConfig, the facility layout and tuning, the six nights " +
                "and the five animatronics from code.\n\n" +
                "Existing assets are updated in place, so scene references survive — but any " +
                "changes you made in the inspector will be overwritten.\n\nContinue?",
                "Rebuild", "Cancel");

            if (proceed) Rebuild();
        }

        public static void Rebuild()
        {
            EnsureFolders();

            var tuning = GetOrCreate<FacilityTuning>($"{ResourcesPath}/FacilityTuning.asset");

            // Every shipping site, baked from its code layout. The catalog is the
            // source of truth for which sites exist and what each asset is called.
            int nodeTotal = 0;
            for (int i = 0; i < SiteCatalog.Count; i++)
            {
                var entry = SiteCatalog.EntryAt(i);
                var site = GetOrCreate<FacilityLayout>($"{ResourcesPath}/{entry.ResourceName}.asset");
                entry.Populate(site);
                EditorUtility.SetDirty(site);
                nodeTotal += site.nodes.Count;
            }

            var cast = BuildCast();
            var nights = BuildNights();

            var config = GetOrCreate<GameConfig>($"{ResourcesPath}/GameConfig.asset");
            config.nights = nights;
            config.customNightTemplate = nights.Length > 6 ? nights[6] : null;
            config.editorAutoStartNight = 1;
            config.targetFrameRate = 60;
            EditorUtility.SetDirty(config);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GLog.Info(LogChannel.Core,
                $"Settings rebuilt: {nights.Length} nights, {cast.Length} characters, " +
                $"{SiteCatalog.Count} sites, {nodeTotal} nodes.");
        }

        private static void EnsureFolders()
        {
            foreach (var path in new[] { ResourcesPath, SettingsPath, NightsPath, CastPath })
            {
                if (Directory.Exists(path)) continue;
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }

        private static T GetOrCreate<T>(string path) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;

            var created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

        // =====================================================================
        // The cast
        // =====================================================================

        public static AnimatronicDefinition[] BuildCast()
        {
            return new[]
            {
                BuildBarty(),
                BuildVesper(),
                BuildMarlow(),
                BuildEcho(),
                BuildChorus()
            };
        }

        private static AnimatronicDefinition BuildBarty()
        {
            var def = GetOrCreate<AnimatronicDefinition>($"{CastPath}/Animatronic_Barty.asset");

            def.id = "barty";
            def.displayName = "Bartholomew Bellows";
            def.dossier =
                "PROSPECTOR BEAR. Show host, 1979-1993. Animatronic #1, the only one of the " +
                "four built on site. Twelve-channel pneumatic, rebuilt twice. His pick still " +
                "swings on the quarter hour whether or not anything is driving it.";

            def.behaviour = BehaviourKind.Host;
            def.traversal = TraversalMask.Walk;
            def.homeNode = "STAGE";
            def.retreatNode = "GRAND";

            def.movementIntervalSeconds = 8f;
            def.intervalJitter = 0.25f;
            def.moveSpeed = 2.1f;

            // The one who plays by the rules: doors stop him, and he comes to noise.
            def.respectsBarriers = true;
            def.lightAverse = false;
            def.noiseAffinity = 0.7f;
            def.monitorBoldness = 0.3f;

            def.attackNodes.Clear();
            def.attackNodes.Add("ADIT_N");
            def.attackNodes.Add("ADIT_S");

            def.attackWindowSeconds = 2.6f;
            def.patienceSeconds = 14f;
            def.retreatCooldownSeconds = 20f;

            def.mapColor = new Color(0.85f, 0.52f, 0.18f);
            def.model = new AnimatronicModelSpec
            {
                species = Species.Bear,
                height = 2.05f,
                bulk = 1.35f,
                headScale = 1.2f,
                shellPrimary = new Color(0.40f, 0.24f, 0.13f),
                shellSecondary = new Color(0.72f, 0.55f, 0.32f),
                fabric = new Color(0.42f, 0.13f, 0.15f),
                eyeGlow = new Color(1f, 0.76f, 0.32f),
                wear = 0.6f,
                exposedEndoskeleton = true,
                seed = 1
            };

            EditorUtility.SetDirty(def);
            return def;
        }

        private static AnimatronicDefinition BuildVesper()
        {
            var def = GetOrCreate<AnimatronicDefinition>($"{CastPath}/Animatronic_Vesper.asset");

            def.id = "vesper";
            def.displayName = "Vesper";
            def.dossier =
                "CAVE BAT. Added 1984 for the Bell Chimney flight effect — she ran on a wire " +
                "from the chimney to the Midway, four times an hour. The wire was cut in 1991. " +
                "She has not needed it since.";

            def.behaviour = BehaviourKind.Ceiling;
            def.traversal = TraversalMask.Crawl | TraversalMask.Climb;
            def.homeNode = "CHIMNEY";
            def.retreatNode = "MIDWAY";

            // The fastest of the cast, and the route with no door on it.
            def.movementIntervalSeconds = 5.5f;
            def.intervalJitter = 0.3f;
            def.moveSpeed = 3.4f;

            def.respectsBarriers = true;
            def.lightAverse = true;
            def.noiseAffinity = -0.65f;     // noise confuses her; a quiet facility is her friend
            def.monitorBoldness = 0.35f;

            def.attackNodes.Clear();
            def.attackNodes.Add("CHASE");

            def.attackWindowSeconds = 1.8f;  // very little time to react
            def.patienceSeconds = 9f;
            def.retreatCooldownSeconds = 14f;

            def.mapColor = new Color(0.62f, 0.45f, 0.85f);
            def.model = new AnimatronicModelSpec
            {
                species = Species.Bat,
                height = 1.55f,
                bulk = 0.72f,
                headScale = 1.3f,
                shellPrimary = new Color(0.22f, 0.18f, 0.26f),
                shellSecondary = new Color(0.38f, 0.30f, 0.40f),
                fabric = new Color(0.30f, 0.16f, 0.24f),
                eyeGlow = new Color(0.85f, 0.55f, 1f),
                wear = 0.75f,
                exposedEndoskeleton = true,
                seed = 2
            };

            EditorUtility.SetDirty(def);
            return def;
        }

        private static AnimatronicDefinition BuildMarlow()
        {
            var def = GetOrCreate<AnimatronicDefinition>($"{CastPath}/Animatronic_Marlow.asset");

            def.id = "marlow";
            def.displayName = "Marlow";
            def.dossier =
                "MOLE. \"Down Below\" segment, 1981. Built with working digging arms for a gag " +
                "where he tunnelled up through a trapdoor in the Grand Gallery floor. The " +
                "trapdoor was sealed after the flood. The arms were not removed.";

            def.behaviour = BehaviourKind.Burrower;
            def.traversal = TraversalMask.Walk | TraversalMask.Burrow;
            def.homeNode = "WORKSHOP";
            def.retreatNode = "GEN";

            def.movementIntervalSeconds = 9f;
            def.intervalJitter = 0.2f;
            def.moveSpeed = 1.8f;

            def.respectsBarriers = true;
            def.lightAverse = false;
            def.noiseAffinity = 0.3f;
            def.monitorBoldness = 0.2f;

            def.attackNodes.Clear();
            def.attackNodes.Add("SUMP");

            def.attackWindowSeconds = 3f;   // slow to get through the floor
            def.patienceSeconds = 16f;
            def.retreatCooldownSeconds = 22f;

            def.mapColor = new Color(0.55f, 0.42f, 0.26f);
            def.model = new AnimatronicModelSpec
            {
                species = Species.Mole,
                height = 1.45f,
                bulk = 1.25f,
                headScale = 1.15f,
                shellPrimary = new Color(0.26f, 0.20f, 0.17f),
                shellSecondary = new Color(0.46f, 0.35f, 0.28f),
                fabric = new Color(0.30f, 0.26f, 0.18f),
                eyeGlow = new Color(1f, 0.62f, 0.25f),
                wear = 0.8f,
                exposedEndoskeleton = true,
                seed = 3
            };

            EditorUtility.SetDirty(def);
            return def;
        }

        private static AnimatronicDefinition BuildEcho()
        {
            var def = GetOrCreate<AnimatronicDefinition>($"{CastPath}/Animatronic_Echo.asset");

            def.id = "echo";
            def.displayName = "Echo";
            def.dossier =
                "SALAMANDER. Closing number, 1986-1993. Sealed shell, rated to two metres — " +
                "she performed from the spring terrace pools. She was in the lower gallery " +
                "when it flooded. The recovery diver's report is three lines long.";

            def.behaviour = BehaviourKind.Swimmer;
            def.traversal = TraversalMask.Walk | TraversalMask.Swim;
            def.homeNode = "DEEP";
            def.retreatNode = "RIVER";

            def.movementIntervalSeconds = 7.5f;
            def.intervalJitter = 0.25f;
            def.moveSpeed = 2.4f;

            def.respectsBarriers = true;
            def.lightAverse = false;
            def.noiseAffinity = 0.45f;
            def.monitorBoldness = 0.25f;

            def.attackNodes.Clear();
            def.attackNodes.Add("SUMP");

            def.attackWindowSeconds = 2.2f;
            def.patienceSeconds = 20f;      // she will wait
            def.retreatCooldownSeconds = 18f;

            def.mapColor = new Color(0.25f, 0.72f, 0.68f);
            def.model = new AnimatronicModelSpec
            {
                species = Species.Salamander,
                height = 1.6f,
                bulk = 0.95f,
                headScale = 1.1f,
                shellPrimary = new Color(0.14f, 0.26f, 0.25f),
                shellSecondary = new Color(0.28f, 0.44f, 0.40f),
                fabric = new Color(0.55f, 0.20f, 0.26f),
                eyeGlow = new Color(0.45f, 1f, 0.85f),
                wear = 0.9f,
                exposedEndoskeleton = false,   // sealed shell: nothing showing through
                seed = 4
            };

            EditorUtility.SetDirty(def);
            return def;
        }

        private static AnimatronicDefinition BuildChorus()
        {
            var def = GetOrCreate<AnimatronicDefinition>($"{CastPath}/Animatronic_Chorus.asset");

            def.id = "chorus";
            def.displayName = "The Chorus";
            def.dossier =
                "NO SERVICE RECORD. The 1994 inventory lists four animatronics recovered and " +
                "four unaccounted for. Both figures are correct. Whatever has been assembling " +
                "itself in the Deep Gallery since the water came is not on either list.";

            def.behaviour = BehaviourKind.Composite;
            def.traversal = TraversalMask.Walk | TraversalMask.Crawl | TraversalMask.Swim | TraversalMask.Climb;
            def.homeNode = "DEEP";
            def.retreatNode = "DEEP";

            def.movementIntervalSeconds = 11f;   // slow, but nothing stops it
            def.intervalJitter = 0.15f;
            def.moveSpeed = 1.6f;

            // It respects a door in the sense that a door is a thing to lean on.
            def.respectsBarriers = true;
            def.lightAverse = false;
            def.noiseAffinity = 0.9f;
            def.monitorBoldness = 0.4f;

            def.attackNodes.Clear();
            def.attackNodes.Add("ADIT_N");
            def.attackNodes.Add("ADIT_S");
            def.attackNodes.Add("SUMP");

            def.attackWindowSeconds = 2f;
            def.patienceSeconds = 45f;      // long enough to buckle a door
            def.retreatCooldownSeconds = 30f;

            def.mapColor = new Color(0.88f, 0.16f, 0.28f);
            def.model = new AnimatronicModelSpec
            {
                species = Species.Composite,
                height = 2.25f,
                bulk = 1.1f,
                headScale = 1f,
                shellPrimary = new Color(0.13f, 0.12f, 0.14f),
                shellSecondary = new Color(0.30f, 0.20f, 0.18f),
                fabric = new Color(0.24f, 0.10f, 0.12f),
                eyeGlow = new Color(1f, 0.22f, 0.18f),
                wear = 1f,
                exposedEndoskeleton = true,
                seed = 5
            };

            EditorUtility.SetDirty(def);
            return def;
        }

        // =====================================================================
        // The campaign
        // =====================================================================

        public static NightDefinition[] BuildNights()
        {
            var nights = new NightDefinition[7];

            // Night 1 teaches one thing: doors cost power, and power is finite.
            nights[0] = Night(1, "Night One", 60f,
                "Reclamation log, night one. You are monitoring, not repairing. " +
                "The genset runs the doors, the fan and the pump — watch the load, it is " +
                "an eight kilowatt set and it does not care how much you need it. " +
                "Anything moves, shut the door on that side. That is the whole job.",
                new[] { ("barty", 3) }, fuel: 145f, cans: 2, water: 0.85f, air: 0.85f);

            // Night 2 adds the route with no door on it.
            nights[1] = Night(2, "Night Two", 60f,
                "You will have noticed the cable chase above the ceiling. There is no door " +
                "on it. There was never meant to be anything in it. The floodlight in the " +
                "chase is on breaker three — use it, and remember the fan is the loudest " +
                "thing in the building.",
                new[] { ("barty", 5), ("vesper", 4) }, fuel: 135f, cans: 2, water: 0.95f, air: 1f);

            // Night 3 introduces the water dial, with both ends live at once.
            nights[2] = Night(3, "Night Three", 62f,
                "Survey wants the lower gallery drained by the weekend. I would not run the " +
                "pump flat out if I were you. There is a reason the sump grate has bolts on " +
                "it, and there is a reason they are on the inside.",
                new[] { ("barty", 7), ("vesper", 6), ("marlow", 5), ("echo", 4) },
                fuel: 125f, cans: 2, water: 1.1f, air: 1.1f,
                ramps: new[] { ("marlow", 3, 2), ("echo", 4, 2) });

            nights[3] = Night(4, "Night Four", 64f,
                "Spring's up. Inflow doubles by dawn and the day tank is down to a hundred " +
                "and ten. You have one can. I would rather you finished the night on an " +
                "empty tank than started the last hour cranking.",
                new[] { ("barty", 10), ("vesper", 9), ("marlow", 8), ("echo", 8) },
                fuel: 110f, cans: 1, water: 1.3f, air: 1.2f,
                ramps: new[] { ("barty", 3, 2), ("vesper", 4, 2), ("echo", 5, 3) });

            // Night 5: the Chorus arrives, and with it the reason to go dark.
            nights[4] = Night(5, "Night Five", 66f,
                "Something came up out of the Deep Gallery on the seismographs at four " +
                "minutes past two. It is not on the inventory. Doors will not hold it — I " +
                "watched it take the north one off its track. If it reaches you, kill " +
                "everything. Fan, pump, monitor, lights, the set itself. It tracks running " +
                "machinery. Go quiet and it loses you.",
                new[] { ("barty", 13), ("vesper", 12), ("marlow", 11), ("echo", 11), ("chorus", 4) },
                fuel: 105f, cans: 1, water: 1.45f, air: 1.3f,
                ramps: new[] { ("chorus", 3, 3), ("chorus", 5, 3), ("barty", 4, 2) });

            nights[5] = Night(6, "Night Six", 68f,
                "There is no shift report for tonight. Nobody filed one.",
                new[] { ("barty", 16), ("vesper", 15), ("marlow", 14), ("echo", 14), ("chorus", 10) },
                fuel: 95f, cans: 1, water: 1.6f, air: 1.45f,
                ramps: new[] { ("chorus", 3, 3), ("chorus", 5, 4) });

            // Custom night: everything at zero for the player to set.
            nights[6] = Night(7, "Custom Night", 60f,
                "Set your own. Nobody is coming to check.",
                new[] { ("barty", 0), ("vesper", 0), ("marlow", 0), ("echo", 0), ("chorus", 0) },
                fuel: 120f, cans: 2, water: 1f, air: 1f);
            nights[6].briefingSeconds = 0f;

            return nights;
        }

        private static NightDefinition Night(int number, string displayName, float secondsPerHour,
            string briefing, (string id, int level)[] levels, float fuel, int cans,
            float water, float air, (string id, int hour, int bonus)[] ramps = null)
        {
            var def = GetOrCreate<NightDefinition>($"{NightsPath}/Night_{number:00}.asset");

            def.night = number;
            def.displayName = displayName;
            def.briefing = briefing;
            def.secondsPerHour = secondsPerHour;
            def.briefingSeconds = number == 1 ? 12f : 8f;

            def.aiLevels.Clear();
            foreach (var (id, level) in levels)
                def.aiLevels.Add(new AiLevelEntry(id, level));

            def.hourlyRamps.Clear();
            if (ramps != null)
            {
                foreach (var (id, hour, bonus) in ramps)
                    def.hourlyRamps.Add(new AiLevelRamp { animatronicId = id, fromHour = hour, levelBonus = bonus });
            }

            def.startingFuelLitres = fuel;
            def.spareFuelCans = cans;
            def.waterInflowScale = water;
            def.airDecayScale = air;
            def.fuelBurnScale = 1f + (number - 1) * 0.06f;

            EditorUtility.SetDirty(def);
            return def;
        }
    }
}
