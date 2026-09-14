using UnityEngine;

namespace Grotto.Facility
{
    /// <summary>
    /// The shipping map: Grotto Springs Family Fun Caverns, Marrow Hollow.
    ///
    /// The cave is arranged around one idea — the player has four ways in and each is
    /// answered by a *different* resource:
    ///
    ///   North Adit  -> the north blast door        (electrical load)
    ///   South Adit  -> the south blast door        (electrical load)
    ///   Sump Basin  -> the grate lock, or flooding (water level, low side)
    ///   Cable Chase -> the chase floodlight        (battery, and noise)
    ///
    /// The water level is the spine of the design. Drain the lower gallery and the
    /// Styx Channel becomes impassable, sealing Echo out — but the sump goes dry and
    /// Marlow can dig. Let it rise and Marlow is drowned out while Echo swims the
    /// channel and the control room edges toward flooding. There is no safe setting,
    /// only a setting you are currently paying for.
    ///
    /// Written as code rather than hand-authored YAML so the map is diffable in
    /// review, and so every position, size and gate threshold sits next to the note
    /// explaining why it has that value. <c>Tools > Grotto > Rebuild Settings Assets</c>
    /// bakes it into a ScriptableObject for designers to tweak in the inspector.
    /// </summary>
    public static class GrottoSpringsLayout
    {
        // Water thresholds, named once so the layout and the systems agree.
        /// <summary>Above this the Styx Channel is deep enough to swim.</summary>
        public const float ChannelSwimmable = 0.55f;
        /// <summary>Below this the sump basin is dry enough to dig through.</summary>
        public const float SumpDiggable = 0.35f;
        /// <summary>
        /// Below this the basin can still be waded in and out of, but not tunnelled
        /// through. Between the two marks Marlow can sit under the station on CAM 03
        /// and do nothing — which is how the player learns the water dial has a shape.
        /// </summary>
        public const float SumpWadeable = 0.5f;
        /// <summary>Above this the crossing can no longer be walked.</summary>
        public const float CrossingDrowned = 0.75f;

        // Aliases. The ids themselves belong to the station, not to this site.
        public const string DoorNorth = FacilityBarriers.DoorNorth;
        public const string DoorSouth = FacilityBarriers.DoorSouth;
        public const string SumpGrate = FacilityBarriers.SumpGrate;

        public static void Populate(FacilityLayout layout)
        {
            layout.siteId = "grotto";
            layout.siteName = "Grotto Springs Family Fun Caverns";
            layout.siteTagline = "A show cave, ninety feet down. The water cuts both ways.";
            layout.siteEmphasis = "Balanced";
            layout.unlockAfterNights = 0;
            layout.palette = SitePalette.Limestone;

            layout.wiring = new SiteWiring
            {
                northApproach = "ADIT_N",
                southApproach = "ADIT_S",
                sump = "SUMP",
                chase = "CHASE",
                generatorBay = "GEN",
                deepGallery = "DEEP"
            };

            layout.gates = new WaterGates
            {
                diggable = SumpDiggable,
                wadeable = SumpWadeable,
                swimmable = ChannelSwimmable,
                drowned = CrossingDrowned
            };

            layout.startingWaterLevel = 0.45f;
            layout.waterScale = 1f;
            layout.airScale = 1f;
            layout.fuelScale = 1f;

            layout.siteBlurb =
                "Opened July 1979 in the Marrow Hollow limestone system: a show cavern, " +
                "a mineral spring terrace and an arcade, ninety feet under a hillside. " +
                "Closed in 1993 when the spring took the lower gallery back. The cast " +
                "was never recovered — the insurers called it cheaper to leave them.";
            layout.defaultCameraNode = "GRAND";

            layout.nodes.Clear();
            layout.links.Clear();

            // =================================================================
            // THE STATION AND ITS FOUR APPROACHES
            // =================================================================

            Node(layout, "STATION", "Pump House Control Room", NodeKind.Station, FacilityZone.Station,
                pos: new Vector3(0f, 0f, 0f), size: new Vector3(7f, 3.2f, 8f),
                hasCamera: false, ambientLight: true, coupling: 1f,
                caption: "");

            Node(layout, "ADIT_N", "North Adit", NodeKind.Adit, FacilityZone.Station,
                pos: new Vector3(0f, 0f, 11f), size: new Vector3(3.4f, 3f, 14f),
                ambientLight: true, coupling: 0.85f,
                caption: "CAM 01 — NORTH ADIT. Shotcrete lining, 1981. Sight line to the lobby.");

            Node(layout, "ADIT_S", "South Adit", NodeKind.Adit, FacilityZone.Station,
                pos: new Vector3(0f, 0f, -11f), size: new Vector3(3.4f, 3f, 14f),
                ambientLight: true, coupling: 0.85f,
                caption: "CAM 02 — SOUTH ADIT. Service run to the generator bay.");

            Node(layout, "SUMP", "Sump Basin", NodeKind.Sump, FacilityZone.Station,
                pos: new Vector3(0f, -5.5f, -1.5f), size: new Vector3(6f, 3f, 6f),
                ambientLight: false, coupling: 0.95f,
                caption: "CAM 03 — SUMP BASIN. Intake screen. Silt line reads ninety-one.");

            Node(layout, "CHASE", "Cable Chase", NodeKind.Crawlway, FacilityZone.Station,
                pos: new Vector3(0f, 3.4f, 3f), size: new Vector3(1.5f, 1.2f, 9f),
                hasCamera: false, ambientLight: false, coupling: 0.9f,
                caption: "");

            // =================================================================
            // SERVICE SIDE — east, built rooms, where the machinery lives
            // =================================================================

            Node(layout, "GEN", "Generator Bay", NodeKind.Room, FacilityZone.Service,
                pos: new Vector3(9f, 0f, -9f), size: new Vector3(9f, 3.6f, 8f),
                ambientLight: true, coupling: 0.7f,
                caption: "CAM 04 — GENERATOR BAY. 8kW Lister. Day tank on the west wall.");

            Node(layout, "WORKSHOP", "Maintenance Workshop", NodeKind.Room, FacilityZone.Service,
                pos: new Vector3(13f, 0f, 5f), size: new Vector3(9f, 3.4f, 9f),
                ambientLight: false, coupling: 0.5f,
                caption: "CAM 05 — WORKSHOP. Costume racks. Three endoskeleton cradles, two empty.");

            Node(layout, "LOCKER", "Staff Lockers", NodeKind.Room, FacilityZone.Service,
                pos: new Vector3(7f, 0f, 15f), size: new Vector3(6f, 3f, 6f),
                ambientLight: false, coupling: 0.4f,
                caption: "CAM 06 — STAFF LOCKERS. Rota still pinned for the week of the flood.");

            // =================================================================
            // UPPER — the way the public came in
            // =================================================================

            Node(layout, "LOBBY", "Ticket Grotto", NodeKind.Cavern, FacilityZone.Upper,
                pos: new Vector3(0f, 0.5f, 26f), size: new Vector3(14f, 5f, 12f),
                ambientLight: true, coupling: 0.3f,
                caption: "CAM 07 — TICKET GROTTO. Turnstiles chained. Rope lights still on the tie.");

            Node(layout, "INCLINE", "Incline Railway", NodeKind.Shaft, FacilityZone.Upper,
                pos: new Vector3(0f, 5f, 37f), size: new Vector3(4f, 5f, 12f),
                ambientLight: false, coupling: 0.15f,
                caption: "CAM 08 — INCLINE RAILWAY. Car parked at the head. Surface door welded.");

            Node(layout, "GIFT", "Gift Grotto", NodeKind.Room, FacilityZone.Upper,
                pos: new Vector3(-8f, 0f, 22f), size: new Vector3(8f, 3.2f, 8f),
                ambientLight: false, coupling: 0.25f,
                caption: "CAM 09 — GIFT GROTTO. Shelves of polished agate. Nothing has been taken.");

            // =================================================================
            // SHOW SIDE — west, the big caverns
            // =================================================================

            Node(layout, "MIDWAY", "The Midway", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-15f, -0.5f, 13f), size: new Vector3(18f, 6f, 12f),
                ambientLight: false, coupling: 0.22f,
                caption: "CAM 10 — THE MIDWAY. Eleven cabinets. Two draw attract power from somewhere.");

            Node(layout, "DINE", "Mineral Springs Terrace", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-27f, -2f, 17f), size: new Vector3(16f, 7f, 14f),
                ambientLight: false, coupling: 0.18f,
                caption: "CAM 11 — SPRING TERRACE. Travertine pools. Water is ninety-four degrees, still.");

            Node(layout, "GRAND", "The Grand Gallery", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-19f, -1f, -4f), size: new Vector3(22f, 9f, 18f),
                ambientLight: true, coupling: 0.2f,
                caption: "CAM 12 — GRAND GALLERY. House lights on the emergency circuit. Seats for four hundred.");

            Node(layout, "STAGE", "Songbird Stage", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-32f, -1f, -7f), size: new Vector3(12f, 7f, 10f),
                ambientLight: true, coupling: 0.16f,
                caption: "CAM 13 — SONGBIRD STAGE. Four marks on the boards. Count them every hour.");

            Node(layout, "CHIMNEY", "Bell Chimney", NodeKind.Shaft, FacilityZone.Show,
                pos: new Vector3(-21f, 3f, 2f), size: new Vector3(3.5f, 10f, 3.5f),
                hasCamera: false, ambientLight: false, coupling: 0.3f,
                caption: "");

            // =================================================================
            // CRAWLWAYS — the ceiling route. No cameras up here, ever.
            // =================================================================

            Node(layout, "CRAWL_A", "Karst Crawlway A", NodeKind.Crawlway, FacilityZone.Deep,
                pos: new Vector3(-9f, 3.5f, 8f), size: new Vector3(1.6f, 1.3f, 12f),
                hasCamera: false, ambientLight: false, coupling: 0.45f,
                caption: "");

            Node(layout, "CRAWL_B", "Karst Crawlway B", NodeKind.Crawlway, FacilityZone.Deep,
                pos: new Vector3(6f, 3.5f, 1f), size: new Vector3(1.6f, 1.3f, 12f),
                hasCamera: false, ambientLight: false, coupling: 0.6f,
                caption: "");

            // =================================================================
            // DEEP — flooded, mostly unlit, where the ones you cannot see live
            // =================================================================

            Node(layout, "XING", "Crossing Bridge", NodeKind.Adit, FacilityZone.Deep,
                pos: new Vector3(15f, -3f, -8f), size: new Vector3(4f, 4f, 12f),
                ambientLight: false, coupling: 0.35f,
                caption: "CAM 14 — CROSSING. Timber deck over the channel. Rated for two tons in 1979.");

            Node(layout, "RIVER", "The Styx Channel", NodeKind.Watercourse, FacilityZone.Deep,
                pos: new Vector3(23f, -6.5f, -16f), size: new Vector3(9f, 4f, 20f),
                ambientLight: false, coupling: 0.5f,
                caption: "CAM 15 — STYX CHANNEL. Flow gauge reads whatever the pump leaves it.");

            Node(layout, "DEEP", "The Deep Gallery", NodeKind.Cavern, FacilityZone.Deep,
                pos: new Vector3(35f, -9f, -24f), size: new Vector3(20f, 12f, 20f),
                hasCamera: false,          // <- deliberately blind. This is the seismograph's job.
                ambientLight: false, coupling: 0.55f,
                caption: "");

            // =================================================================
            // LINKS
            // =================================================================

            // --- Station approaches -------------------------------------------------
            Link(layout, "STATION", "ADIT_N", TraversalMask.Walk | TraversalMask.Crawl,
                seconds: 3f, noise: 0.9f, barrier: DoorNorth);

            Link(layout, "STATION", "ADIT_S", TraversalMask.Walk | TraversalMask.Crawl,
                seconds: 3f, noise: 0.9f, barrier: DoorSouth);

            // The grate. Marlow digs in when the basin is dry; Echo swims up when it is not.
            Link(layout, "STATION", "SUMP", TraversalMask.Burrow | TraversalMask.Swim,
                seconds: 5f, noise: 0.95f, barrier: SumpGrate);

            // The chase has no door at all — light is the only answer.
            Link(layout, "STATION", "CHASE", TraversalMask.Crawl | TraversalMask.Climb,
                seconds: 4f, noise: 0.85f, lightDeters: true);

            // --- Service loop -------------------------------------------------------
            Link(layout, "ADIT_S", "GEN", TraversalMask.Walk, seconds: 4f, noise: 0.8f);
            Link(layout, "GEN", "WORKSHOP", TraversalMask.Walk, seconds: 5f, noise: 0.6f);
            Link(layout, "WORKSHOP", "LOCKER", TraversalMask.Walk, seconds: 4f, noise: 0.55f);
            Link(layout, "LOCKER", "LOBBY", TraversalMask.Walk, seconds: 5f, noise: 0.5f);
            Link(layout, "WORKSHOP", "CRAWL_B", TraversalMask.Crawl | TraversalMask.Climb,
                seconds: 6f, noise: 0.5f);

            // --- Upper --------------------------------------------------------------
            Link(layout, "ADIT_N", "LOBBY", TraversalMask.Walk, seconds: 5f, noise: 0.7f);
            Link(layout, "LOBBY", "INCLINE", TraversalMask.Walk | TraversalMask.Climb,
                seconds: 6f, noise: 0.35f);
            Link(layout, "LOBBY", "GIFT", TraversalMask.Walk, seconds: 3f, noise: 0.6f);
            Link(layout, "LOBBY", "MIDWAY", TraversalMask.Walk, seconds: 6f, noise: 0.5f);
            Link(layout, "GIFT", "MIDWAY", TraversalMask.Walk, seconds: 4f, noise: 0.5f);

            // --- Show ---------------------------------------------------------------
            Link(layout, "MIDWAY", "DINE", TraversalMask.Walk, seconds: 6f, noise: 0.45f);
            Link(layout, "MIDWAY", "GRAND", TraversalMask.Walk, seconds: 7f, noise: 0.45f);
            Link(layout, "DINE", "GRAND", TraversalMask.Walk, seconds: 7f, noise: 0.4f);
            Link(layout, "GRAND", "STAGE", TraversalMask.Walk, seconds: 4f, noise: 0.6f);
            Link(layout, "GRAND", "CHIMNEY", TraversalMask.Climb, seconds: 6f, noise: 0.4f);

            // --- Ceiling route ------------------------------------------------------
            Link(layout, "CHIMNEY", "CRAWL_A", TraversalMask.Crawl | TraversalMask.Climb,
                seconds: 5f, noise: 0.45f);
            Link(layout, "CRAWL_A", "CRAWL_B", TraversalMask.Crawl, seconds: 6f, noise: 0.6f);
            Link(layout, "CRAWL_B", "CHASE", TraversalMask.Crawl, seconds: 5f, noise: 0.8f);
            // One-way drop into the Midway: you can fall out of the karst, not climb back.
            Link(layout, "CRAWL_A", "MIDWAY", TraversalMask.Crawl | TraversalMask.Climb,
                seconds: 3f, noise: 0.55f, oneWay: true);

            // --- Deep and the water gates -------------------------------------------
            Link(layout, "ADIT_S", "XING", TraversalMask.Walk, seconds: 5f, noise: 0.6f);

            // The crossing walks until the channel comes up over the deck.
            Link(layout, "XING", "RIVER", TraversalMask.Walk, seconds: 4f, noise: 0.6f,
                maxWater: CrossingDrowned);
            // Once it is deep, the same gap is a swim instead.
            Link(layout, "XING", "RIVER", TraversalMask.Swim, seconds: 5f, noise: 0.4f,
                minWater: ChannelSwimmable);

            Link(layout, "RIVER", "DEEP", TraversalMask.Swim, seconds: 7f, noise: 0.45f,
                minWater: ChannelSwimmable);
            Link(layout, "RIVER", "SUMP", TraversalMask.Swim, seconds: 6f, noise: 0.7f,
                minWater: 0.5f);

            // Marlow's private tunnel. He can come and go while the basin is merely
            // damp; breaking through the station floor needs it properly dry, which
            // MarlowBehaviour.CanBreach enforces at SumpDiggable.
            Link(layout, "SUMP", "GEN", TraversalMask.Burrow, seconds: 8f, noise: 0.5f,
                maxWater: SumpWadeable);

            // The long way round the back of the show cavern, dry-only.
            Link(layout, "DEEP", "STAGE", TraversalMask.Walk, seconds: 10f, noise: 0.3f,
                maxWater: CrossingDrowned);

            // =================================================================
            // CAST
            // =================================================================
            //
            // Where the five live *here*. The character definitions describe how each
            // one moves; this says which rooms it moves between, so the same cast can
            // be dropped into another building without editing five assets.

            layout.cast.Clear();

            // Barty walks the show route and knocks on both doors. The obvious threat,
            // and the one the player learns the camera rhythm from.
            Place(layout, "barty", home: "STAGE", retreat: "GRAND", "ADIT_N", "ADIT_S");

            // Vesper drops out of the bell chimney into the karst and comes down the chase.
            Place(layout, "vesper", home: "CHIMNEY", retreat: "MIDWAY", "CHASE");

            // Marlow lives in the workshop and digs up through the basin when it is dry.
            Place(layout, "marlow", home: "WORKSHOP", retreat: "GEN", "SUMP");

            // Echo only exists when the water is high enough to swim the channel.
            Place(layout, "echo", home: "DEEP", retreat: "RIVER", "SUMP");

            // Chorus has no body of its own. It borrows every approach at once.
            Place(layout, "chorus", home: "DEEP", retreat: "DEEP", "ADIT_N", "ADIT_S", "SUMP");
        }

        // ---------------------------------------------------------------------
        // Authoring helpers
        // ---------------------------------------------------------------------

        // Forwarders, so this file reads as authoring rather than as plumbing.

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
    }
}
