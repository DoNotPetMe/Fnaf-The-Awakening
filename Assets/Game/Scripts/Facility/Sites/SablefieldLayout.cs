using UnityEngine;

namespace Grotto.Facility
{
    /// <summary>
    /// Site three: Sablefield Grain Terminal, a 1954 concrete elevator with a
    /// twenty-bin silo block, and a walk-through attraction called Harvest Hollow
    /// wedged into the annex some time in 1977.
    ///
    /// This is the site where the water dial is *inverted*.
    ///
    /// The building sits high and dry. It starts the night at 0.18, which at every
    /// other site would be an emergency and here is simply Tuesday. That means every
    /// dry route is open by default — Marlow's burrow, the pit walk, the whole
    /// south loop — and the only way to close them is to let the sump fill, which
    /// takes the pump running *backwards* from the habit the player has built over
    /// two maps.
    ///
    /// And letting it fill costs you twice:
    ///
    ///   * Above 0.55 the sump well joins the dust pit and the pit, which is Echo's
    ///     entire road in. You closed Marlow's door by opening Echo's.
    ///   * Air decays at 1.85. A grain elevator full of settled dust has to be
    ///     ventilated hard and continuously, and the fan is the single biggest draw
    ///     on the board. Time spent thinking about water is time the fan was off.
    ///
    /// So Sablefield is about the air, and the water is a lever you can pull exactly
    /// when you have the power budget for the consequences. Fuel is slightly cheap
    /// (0.95) because there is no pump load worth speaking of — that is the one
    /// concession the site makes.
    ///
    /// The silos are the vertical spine: everything climbs. Two of the three
    /// approaches are at ground level, the dust trunking is in the ceiling, and the
    /// cast spends the night working down from the headhouse.
    /// </summary>
    public static class SablefieldLayout
    {
        public const string SiteId = "sablefield";

        // --- Water gates -----------------------------------------------------
        // Shifted *up* relative to Grotto Springs, and wider. The night starts below
        // all four marks and the player has to work to get above any of them.

        /// <summary>Below this the dust pit floor is dry enough to tunnel through.</summary>
        public const float SumpDiggable = 0.30f;

        /// <summary>Below this the pit can be waded, not dug.</summary>
        public const float SumpWadeable = 0.45f;

        /// <summary>Above this the sump well is deep enough to swim. Echo's threshold.</summary>
        public const float ChannelSwimmable = 0.58f;

        /// <summary>Above this the low south loop is under water and nobody walks it.</summary>
        public const float CrossingDrowned = 0.88f;

        public static void Populate(FacilityLayout layout)
        {
            layout.siteId = SiteId;
            layout.siteName = "Sablefield Grain Terminal";
            layout.siteTagline = "Twenty bins of dust. Run the fan or stop seeing.";
            layout.siteEmphasis = "Air. Inverted water.";
            layout.unlockAfterNights = 4;
            layout.palette = SitePalette.Steel;

            layout.wiring = new SiteWiring
            {
                northApproach = "TUNNEL",
                southApproach = "BOOTPIT",
                sump = "DUSTPIT",
                chase = "TRUNKING",
                generatorBay = "PLANT",
                deepGallery = "PIT"
            };

            layout.gates = new WaterGates
            {
                diggable = SumpDiggable,
                wadeable = SumpWadeable,
                swimmable = ChannelSwimmable,
                drowned = CrossingDrowned
            };

            layout.startingWaterLevel = 0.18f;
            layout.waterScale = 0.75f;     // it fills slowly; getting wet is a project
            layout.airScale = 1.85f;       // and this is the real clock
            layout.fuelScale = 0.95f;

            layout.siteBlurb =
                "Built 1954 beside the Sablefield branch line: a hundred-and-ten-foot " +
                "slipformed elevator, twenty bins, and a leg that could lift four " +
                "thousand bushels an hour. The annex became a walk-through attraction " +
                "in 1977 to keep the co-op solvent. A dust explosion in 1986 took the " +
                "headhouse roof off. The cast was inside it.";

            layout.defaultCameraNode = "HEADHOUSE";

            layout.nodes.Clear();
            layout.links.Clear();

            // =================================================================
            // THE STATION AND ITS FOUR APPROACHES
            // =================================================================

            Node(layout, "STATION", "Weighbridge Office", NodeKind.Station, FacilityZone.Station,
                pos: new Vector3(0f, 0f, 0f), size: new Vector3(7f, 3f, 8f),
                hasCamera: false, ambientLight: true, coupling: 1f);

            Node(layout, "TUNNEL", "Conveyor Tunnel", NodeKind.Adit, FacilityZone.Station,
                pos: new Vector3(0f, 0f, 12f), size: new Vector3(3.6f, 3f, 14f),
                ambientLight: true, coupling: 0.85f,
                caption: "CAM 01 — CONVEYOR TUNNEL. Belt still tensioned. Something has been on it.");

            Node(layout, "BOOTPIT", "Elevator Boot Pit", NodeKind.Adit, FacilityZone.Station,
                pos: new Vector3(0f, 0f, -12f), size: new Vector3(4f, 3.4f, 14f),
                ambientLight: true, coupling: 0.85f,
                caption: "CAM 02 — BOOT PIT. Foot of the leg. Cups hang where the belt stopped.");

            Node(layout, "DUSTPIT", "Dust Collection Pit", NodeKind.Sump, FacilityZone.Station,
                pos: new Vector3(0f, -5f, -2f), size: new Vector3(6f, 3f, 6f),
                ambientLight: false, coupling: 0.95f,
                caption: "CAM 03 — DUST PIT. Cyclone drop. Fines are eighteen inches deep.");

            Node(layout, "TRUNKING", "Dust Trunking", NodeKind.Crawlway, FacilityZone.Station,
                pos: new Vector3(0f, 3.3f, 3f), size: new Vector3(1.5f, 1.2f, 10f),
                hasCamera: false, ambientLight: false, coupling: 0.9f);

            // =================================================================
            // THE VERTICAL SPINE — silos, headhouse, belt gallery
            // =================================================================

            Node(layout, "SILO_A", "Silo A", NodeKind.Shaft, FacilityZone.Show,
                pos: new Vector3(-12f, 8f, 2f), size: new Vector3(7f, 26f, 7f),
                ambientLight: false, coupling: 0.3f,
                caption: "CAM 04 — SILO A. Empty to the cone. Ladder rungs start at eleven feet.");

            Node(layout, "SILO_B", "Silo B", NodeKind.Shaft, FacilityZone.Deep,
                pos: new Vector3(-12f, 8f, -8f), size: new Vector3(7f, 26f, 7f),
                hasCamera: false, ambientLight: false, coupling: 0.35f);

            Node(layout, "HEADHOUSE", "Headhouse", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-4f, 16f, 4f), size: new Vector3(12f, 8f, 12f),
                ambientLight: true, coupling: 0.25f,
                caption: "CAM 05 — HEADHOUSE. Roof open to the sky since eighty-six. Birds do not come in.");

            Node(layout, "GALLERY", "Belt Gallery", NodeKind.Adit, FacilityZone.Show,
                pos: new Vector3(-6f, 15f, 14f), size: new Vector3(4f, 3f, 18f),
                ambientLight: false, coupling: 0.2f,
                caption: "CAM 06 — BELT GALLERY. Eighty feet up, glazed both sides. Wind moves it.");

            // =================================================================
            // GROUND — service and plant
            // =================================================================

            Node(layout, "DRIER", "Grain Drier", NodeKind.Room, FacilityZone.Service,
                pos: new Vector3(12f, 0f, 6f), size: new Vector3(9f, 5f, 9f),
                ambientLight: false, coupling: 0.45f,
                caption: "CAM 07 — GRAIN DRIER. Burner locked out. Column still warm on the gauge.");

            Node(layout, "PLANT", "Plant Room", NodeKind.Room, FacilityZone.Service,
                pos: new Vector3(11f, 0f, -9f), size: new Vector3(9f, 3.6f, 8f),
                ambientLight: true, coupling: 0.7f,
                caption: "CAM 08 — PLANT ROOM. Standby set, aspiration fan, and the sump control panel.");

            // =================================================================
            // HARVEST HOLLOW — the attraction, west, and the reason for the cast
            // =================================================================

            Node(layout, "ANNEX", "Harvest Hollow Annex", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-20f, 0f, 14f), size: new Vector3(18f, 6f, 14f),
                ambientLight: true, coupling: 0.18f,
                caption: "CAM 09 — HARVEST HOLLOW. Painted corn to the ceiling. Track still in the floor.");

            Node(layout, "STAGE", "Harvest Stage", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-30f, 0f, 10f), size: new Vector3(12f, 6f, 10f),
                ambientLight: true, coupling: 0.15f,
                caption: "CAM 10 — HARVEST STAGE. Four marks on the boards. Count them every hour.");

            // =================================================================
            // BELOW — the wet end, mostly shut on a normal night
            // =================================================================

            Node(layout, "SUMPWELL", "Sump Well", NodeKind.Watercourse, FacilityZone.Deep,
                pos: new Vector3(6f, -7f, -18f), size: new Vector3(7f, 4f, 14f),
                ambientLight: false, coupling: 0.5f,
                caption: "CAM 11 — SUMP WELL. Site drainage. Float switch wired out in seventy-nine.");

            Node(layout, "PIT", "The Pit", NodeKind.Cavern, FacilityZone.Deep,
                pos: new Vector3(-14f, -8f, -16f), size: new Vector3(16f, 9f, 16f),
                hasCamera: false,          // blind on purpose — the seismograph covers this
                ambientLight: false, coupling: 0.6f);

            // =================================================================
            // LINKS
            // =================================================================

            // --- The four approaches -------------------------------------------------
            // Short. This is a small building at ground level and the walls are steel.
            StationApproaches(layout,
                north: "TUNNEL", south: "BOOTPIT", sump: "DUSTPIT", chase: "TRUNKING",
                doorSeconds: 2.8f, sumpSeconds: 4.5f, chaseSeconds: 3.5f);

            // --- The attraction ------------------------------------------------------
            Link(layout, "TUNNEL", "ANNEX", TraversalMask.Walk, seconds: 6f, noise: 0.5f);
            Link(layout, "ANNEX", "STAGE", TraversalMask.Walk, seconds: 5f, noise: 0.55f);

            // --- The climb -----------------------------------------------------------
            // Annex -> Silo A -> headhouse -> belt gallery -> trunking -> you. Long,
            // loud, and the route the player learns to read on the seismograph.
            Link(layout, "ANNEX", "SILO_A", TraversalMask.Climb, seconds: 7f, noise: 0.4f);
            Link(layout, "SILO_A", "HEADHOUSE", TraversalMask.Climb, seconds: 8f, noise: 0.35f);
            Link(layout, "SILO_B", "HEADHOUSE", TraversalMask.Climb, seconds: 8f, noise: 0.35f);
            Link(layout, "SILO_A", "SILO_B", TraversalMask.Crawl, seconds: 6f, noise: 0.5f);
            Link(layout, "HEADHOUSE", "GALLERY", TraversalMask.Walk | TraversalMask.Crawl,
                seconds: 5f, noise: 0.45f);
            Link(layout, "GALLERY", "TRUNKING", TraversalMask.Crawl, seconds: 7f, noise: 0.75f);

            // The inter-bin drop: silo B has no camera and bottoms out in the pit.
            // Anything that takes this route disappears from the monitor entirely.
            Link(layout, "SILO_B", "PIT", TraversalMask.Climb, seconds: 9f, noise: 0.4f);

            // --- Ground loop ---------------------------------------------------------
            Link(layout, "BOOTPIT", "PLANT", TraversalMask.Walk, seconds: 4f, noise: 0.7f);
            Link(layout, "PLANT", "DRIER", TraversalMask.Walk, seconds: 5f, noise: 0.55f);
            Link(layout, "DRIER", "TUNNEL", TraversalMask.Walk, seconds: 6f, noise: 0.5f);

            // --- Dry gates: open by default, and closing them costs you ---------------
            Link(layout, "PLANT", "DUSTPIT", TraversalMask.Burrow, seconds: 8f, noise: 0.5f,
                maxWater: SumpWadeable);
            Link(layout, "PLANT", "SUMPWELL", TraversalMask.Walk, seconds: 5f, noise: 0.5f,
                maxWater: CrossingDrowned);
            Link(layout, "PIT", "BOOTPIT", TraversalMask.Walk, seconds: 9f, noise: 0.45f,
                maxWater: CrossingDrowned);
            Link(layout, "STAGE", "PIT", TraversalMask.Walk, seconds: 11f, noise: 0.3f,
                maxWater: CrossingDrowned);

            // --- Wet gates: shut on a normal night, and you are the one who opens them -
            Link(layout, "SUMPWELL", "DUSTPIT", TraversalMask.Swim, seconds: 6f, noise: 0.7f,
                minWater: 0.55f);
            Link(layout, "SUMPWELL", "PIT", TraversalMask.Swim, seconds: 8f, noise: 0.45f,
                minWater: ChannelSwimmable);

            // =================================================================
            // CAST
            // =================================================================

            layout.cast.Clear();

            // Barty works the attraction and comes up the ground loop. Same job as
            // always, but here he has a genuinely long walk and the fan noise covers him.
            Place(layout, "barty", home: "STAGE", retreat: "ANNEX", "TUNNEL", "BOOTPIT");

            // Vesper climbs. Silo A to the headhouse to the gallery to the trunking —
            // six links of warning if you are watching, none at all if you are not.
            Place(layout, "vesper", home: "SILO_A", retreat: "HEADHOUSE", "TRUNKING");

            // Marlow starts the night with his burrow *open*, which is new. At every
            // other site he has to wait for the pump. Here he has to be flooded out.
            Place(layout, "marlow", home: "DRIER", retreat: "PLANT", "DUSTPIT");

            // Echo needs 0.58 and the night starts at 0.18, so he is a threat the
            // player creates. That is the whole joke of the site.
            Place(layout, "echo", home: "PIT", retreat: "SUMPWELL", "DUSTPIT");

            Place(layout, "chorus", home: "PIT", retreat: "PIT",
                "TUNNEL", "BOOTPIT", "DUSTPIT");
        }

        // ---------------------------------------------------------------------
        // Authoring forwarders — see LayoutAuthoring.
        // ---------------------------------------------------------------------

        private static void Node(FacilityLayout layout, string id, string name, NodeKind kind,
            FacilityZone zone, Vector3 pos, Vector3 size, bool hasCamera = true,
            bool ambientLight = false, float coupling = 0.2f, string caption = "")
            => LayoutAuthoring.Node(layout, id, name, kind, zone, pos, size,
                hasCamera, ambientLight, coupling, caption);

        private static void Link(FacilityLayout layout, string a, string b, TraversalMask allowed,
            float seconds, float noise, float minWater = 0f, float maxWater = 1f,
            string barrier = "", bool lightDeters = false, bool oneWay = false)
            => LayoutAuthoring.Link(layout, a, b, allowed, seconds, noise,
                minWater, maxWater, barrier, lightDeters, oneWay);

        private static void Place(FacilityLayout layout, string animatronicId,
            string home, string retreat, params string[] attackNodes)
            => LayoutAuthoring.Place(layout, animatronicId, home, retreat, attackNodes);

        private static void StationApproaches(FacilityLayout layout,
            string north, string south, string sump, string chase,
            float doorSeconds, float sumpSeconds, float chaseSeconds)
            => LayoutAuthoring.StationApproaches(layout, north, south, sump, chase,
                doorSeconds, sumpSeconds, chaseSeconds);
    }
}
