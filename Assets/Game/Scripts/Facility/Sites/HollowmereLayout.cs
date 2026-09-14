using UnityEngine;

namespace Grotto.Facility
{
    /// <summary>
    /// Site two: Hollowmere Hydro, a 1931 generating station buried in the toe of a
    /// dam, with a visitor gallery bolted on in 1968.
    ///
    /// Grotto Springs asks you to hold the water somewhere in the middle. Hollowmere
    /// takes that dial away. You are inside the dam, so the water level is not a
    /// choice you make once an hour — it is a thing that rises on its own and that
    /// your pump only slows. Every night here starts two thirds flooded and gets
    /// worse.
    ///
    /// What that does to the map:
    ///
    ///   * The dry routes are the ones that *close*. Marlow's burrow under the
    ///     generator floor and the walk out along the draft tube both shut the moment
    ///     the level passes the wading mark, and they never reopen unless you spend
    ///     serious pump power getting back under it.
    ///   * The wet routes are the ones that *open*. The tailrace, the penstock and
    ///     the wheel pit stitch the far end of the building to the sump the instant
    ///     the level clears the swim mark, and Echo lives down there.
    ///   * The forebay and the dam crest drown out entirely at 0.80, which takes the
    ///     long way round off the table for everybody — including you, if you were
    ///     counting on Barty taking eleven seconds to come back.
    ///
    /// So the night is a slide from one threat model to another, and the interesting
    /// decision is when to stop fighting it. Pump hard early and you buy a dry hour
    /// you have to survive with Marlow in the floor; let it go and you trade him for
    /// Echo, who is worse, but who at least only uses two doors.
    ///
    /// Air is thin (0.85) because a machine hall this size vents through one shaft.
    /// Fuel burns fast (1.15) because the pump is doing real work all night.
    /// </summary>
    public static class HollowmereLayout
    {
        public const string SiteId = "hollowmere";

        // --- Water gates -----------------------------------------------------
        // Tighter than Grotto Springs, and shifted down: the whole band lives in the
        // bottom half of the dial, so the level spends most of the night above all
        // four marks. Getting under any of them is an achievement, not a default.

        /// <summary>Below this the draft tube floor is dry enough to tunnel through.</summary>
        public const float SumpDiggable = 0.22f;

        /// <summary>Below this the draft tube can still be walked, not dug.</summary>
        public const float SumpWadeable = 0.34f;

        /// <summary>Above this the tailrace and penstock are deep enough to swim.</summary>
        public const float ChannelSwimmable = 0.42f;

        /// <summary>Above this the forebay and crest are gone. Nobody walks the top.</summary>
        public const float CrossingDrowned = 0.80f;

        public static void Populate(FacilityLayout layout)
        {
            layout.siteId = SiteId;
            layout.siteName = "Hollowmere Hydro Station";
            layout.siteTagline = "Inside the dam. The water only goes one way.";
            layout.siteEmphasis = "Water. Relentless.";
            layout.unlockAfterNights = 2;
            layout.palette = SitePalette.Concrete;

            layout.wiring = new SiteWiring
            {
                northApproach = "GALLERY",
                southApproach = "CABLEWAY",
                sump = "DRAFTTUBE",
                chase = "AIRSHAFT",
                generatorBay = "GENFLOOR",
                deepGallery = "WHEELPIT"
            };

            layout.gates = new WaterGates
            {
                diggable = SumpDiggable,
                wadeable = SumpWadeable,
                swimmable = ChannelSwimmable,
                drowned = CrossingDrowned
            };

            // Two thirds full at midnight, filling half again as fast as the grotto,
            // with less air and a hungrier pump.
            layout.startingWaterLevel = 0.66f;
            layout.waterScale = 1.55f;
            layout.airScale = 0.85f;
            layout.fuelScale = 1.15f;

            layout.siteBlurb =
                "Commissioned 1931 on the Hollowmere reservoir: two 900kW Francis sets " +
                "in a hall cut into the dam's toe. A visitor gallery and an animatronic " +
                "show were added in 1968 to sell the place as a day out. Generation " +
                "stopped in 1989. The dam did not.";

            layout.defaultCameraNode = "TURBINE";

            layout.nodes.Clear();
            layout.links.Clear();

            // =================================================================
            // THE STATION AND ITS FOUR APPROACHES
            // =================================================================
            //
            // Station first, always: LayoutAuthoring.StationApproaches wires nodes[0].

            Node(layout, "STATION", "Switch Room", NodeKind.Station, FacilityZone.Station,
                pos: new Vector3(0f, 0f, 0f), size: new Vector3(8f, 3.4f, 9f),
                hasCamera: false, ambientLight: true, coupling: 1f);

            Node(layout, "GALLERY", "Visitor Gallery", NodeKind.Adit, FacilityZone.Station,
                pos: new Vector3(0f, 0f, 13f), size: new Vector3(4.5f, 3.2f, 16f),
                ambientLight: true, coupling: 0.85f,
                caption: "CAM 01 — VISITOR GALLERY. Interpretive boards, 1968. Glass to the hall.");

            Node(layout, "CABLEWAY", "Cable Gallery", NodeKind.Adit, FacilityZone.Station,
                pos: new Vector3(0f, 0f, -13f), size: new Vector3(4.5f, 3.2f, 16f),
                ambientLight: true, coupling: 0.85f,
                caption: "CAM 02 — CABLE GALLERY. Six trays. Two still warm to the hand.");

            Node(layout, "DRAFTTUBE", "Draft Tube", NodeKind.Sump, FacilityZone.Station,
                pos: new Vector3(0f, -6f, -2.5f), size: new Vector3(7f, 3.5f, 7f),
                ambientLight: false, coupling: 0.95f,
                caption: "CAM 03 — DRAFT TUBE. Runner removed 1991. The hole is still there.");

            Node(layout, "AIRSHAFT", "Ventilation Shaft", NodeKind.Crawlway, FacilityZone.Station,
                pos: new Vector3(0f, 3.7f, 3f), size: new Vector3(1.6f, 1.3f, 10f),
                hasCamera: false, ambientLight: false, coupling: 0.9f);

            // =================================================================
            // THE MACHINE HALL — west, and enormous
            // =================================================================

            Node(layout, "TURBINE", "Turbine Hall", NodeKind.Cavern, FacilityZone.Show,
                pos: new Vector3(-20f, -1f, -2f), size: new Vector3(24f, 11f, 16f),
                ambientLight: true, coupling: 0.2f,
                caption: "CAM 04 — TURBINE HALL. Both sets under dust sheets. One sheet has moved.");

            Node(layout, "PENSTOCK", "Penstock Shaft", NodeKind.Shaft, FacilityZone.Deep,
                pos: new Vector3(-26f, 6f, 10f), size: new Vector3(5f, 16f, 5f),
                hasCamera: false, ambientLight: false, coupling: 0.35f);

            Node(layout, "INTAKE", "Intake Tower", NodeKind.Shaft, FacilityZone.Upper,
                pos: new Vector3(-28f, 12f, 22f), size: new Vector3(6f, 10f, 6f),
                ambientLight: false, coupling: 0.15f,
                caption: "CAM 05 — INTAKE TOWER. Trash rack clear. Gate hoist seized open.");

            Node(layout, "FOREBAY", "Forebay", NodeKind.Watercourse, FacilityZone.Upper,
                pos: new Vector3(-20f, 10f, 30f), size: new Vector3(18f, 6f, 14f),
                ambientLight: false, coupling: 0.12f,
                caption: "CAM 06 — FOREBAY. Surface is flat tonight. It should not be.");

            // =================================================================
            // THE PUBLIC SIDE — north, above the water for now
            // =================================================================

            Node(layout, "CREST", "Dam Crest", NodeKind.Adit, FacilityZone.Upper,
                pos: new Vector3(0f, 8f, 32f), size: new Vector3(28f, 4f, 6f),
                ambientLight: true, coupling: 0.1f,
                caption: "CAM 07 — DAM CREST. Walkway lamps on the standby circuit. Rail is low.");

            Node(layout, "MESS", "Mess Room", NodeKind.Room, FacilityZone.Upper,
                pos: new Vector3(8f, 0f, 15f), size: new Vector3(6f, 3f, 6f),
                ambientLight: false, coupling: 0.4f,
                caption: "CAM 08 — MESS ROOM. Four chairs out. The fifth is against the wall.");

            // =================================================================
            // SERVICE SIDE — east, the built rooms
            // =================================================================

            Node(layout, "FITTING", "Fitting Shop", NodeKind.Room, FacilityZone.Service,
                pos: new Vector3(15f, 0f, 4f), size: new Vector3(9f, 3.6f, 9f),
                ambientLight: false, coupling: 0.5f,
                caption: "CAM 09 — FITTING SHOP. Lathe, bench, and a crate the size of a man.");

            Node(layout, "GENFLOOR", "Generator Floor", NodeKind.Room, FacilityZone.Service,
                pos: new Vector3(11f, 0f, -10f), size: new Vector3(10f, 4f, 9f),
                ambientLight: true, coupling: 0.7f,
                caption: "CAM 10 — GENERATOR FLOOR. Standby diesel. Day tank reads what it reads.");

            Node(layout, "DUCTA", "Cable Duct", NodeKind.Crawlway, FacilityZone.Deep,
                pos: new Vector3(8f, 3.7f, -4f), size: new Vector3(1.6f, 1.3f, 14f),
                hasCamera: false, ambientLight: false, coupling: 0.6f);

            // =================================================================
            // THE WET END — south-east, and the reason you are here
            // =================================================================

            Node(layout, "TAILRACE", "Tailrace", NodeKind.Watercourse, FacilityZone.Deep,
                pos: new Vector3(17f, -7f, -22f), size: new Vector3(10f, 4f, 20f),
                ambientLight: false, coupling: 0.5f,
                caption: "CAM 11 — TAILRACE. Discharge channel. Gauge board legible to the six mark.");

            Node(layout, "WHEELPIT", "Wheel Pit", NodeKind.Cavern, FacilityZone.Deep,
                pos: new Vector3(29f, -10f, -30f), size: new Vector3(18f, 10f, 18f),
                hasCamera: false,          // blind on purpose — the seismograph covers this
                ambientLight: false, coupling: 0.6f);

            // =================================================================
            // LINKS
            // =================================================================

            // --- The four approaches -------------------------------------------------
            // Longer than the grotto's: this building has real corridors, and the extra
            // second on each is the whole reason a Hollowmere night is survivable at all.
            StationApproaches(layout,
                north: "GALLERY", south: "CABLEWAY", sump: "DRAFTTUBE", chase: "AIRSHAFT",
                doorSeconds: 3.5f, sumpSeconds: 5.5f, chaseSeconds: 4.5f);

            // --- Public side ---------------------------------------------------------
            Link(layout, "GALLERY", "CREST", TraversalMask.Walk | TraversalMask.Climb,
                seconds: 7f, noise: 0.4f);
            Link(layout, "GALLERY", "MESS", TraversalMask.Walk, seconds: 5f, noise: 0.55f);
            Link(layout, "MESS", "FITTING", TraversalMask.Walk, seconds: 4f, noise: 0.5f);

            // --- Service loop --------------------------------------------------------
            Link(layout, "FITTING", "GENFLOOR", TraversalMask.Walk, seconds: 5f, noise: 0.6f);
            Link(layout, "CABLEWAY", "GENFLOOR", TraversalMask.Walk, seconds: 4f, noise: 0.75f);
            Link(layout, "CABLEWAY", "TURBINE", TraversalMask.Walk, seconds: 7f, noise: 0.5f);

            // --- The ceiling route ---------------------------------------------------
            // Fitting shop -> duct -> air shaft -> you. Vesper's road in, and the only
            // approach that never drowns, which is why it is also the only one without
            // a door.
            Link(layout, "FITTING", "DUCTA", TraversalMask.Crawl | TraversalMask.Climb,
                seconds: 6f, noise: 0.45f);
            Link(layout, "DUCTA", "AIRSHAFT", TraversalMask.Crawl, seconds: 5f, noise: 0.8f);
            Link(layout, "TURBINE", "DUCTA", TraversalMask.Crawl | TraversalMask.Climb,
                seconds: 9f, noise: 0.4f);

            // --- The high water route ------------------------------------------------
            // Penstock, intake, forebay, crest. Dry-ish until 0.80, then gone.
            Link(layout, "TURBINE", "PENSTOCK", TraversalMask.Climb, seconds: 7f, noise: 0.45f);
            Link(layout, "PENSTOCK", "INTAKE", TraversalMask.Climb, seconds: 6f, noise: 0.35f);
            Link(layout, "INTAKE", "CREST", TraversalMask.Walk, seconds: 6f, noise: 0.3f);
            Link(layout, "INTAKE", "FOREBAY", TraversalMask.Walk, seconds: 4f, noise: 0.35f,
                maxWater: CrossingDrowned);
            Link(layout, "FOREBAY", "CREST", TraversalMask.Walk, seconds: 5f, noise: 0.3f,
                maxWater: CrossingDrowned);

            // The penstock floods from the top down, so above the swim mark the shaft
            // becomes a shortcut rather than a climb.
            Link(layout, "PENSTOCK", "FOREBAY", TraversalMask.Swim, seconds: 7f, noise: 0.3f,
                minWater: ChannelSwimmable);

            // --- The dry gates: these CLOSE as the night goes on ----------------------
            Link(layout, "TURBINE", "DRAFTTUBE", TraversalMask.Walk, seconds: 5f, noise: 0.65f,
                maxWater: SumpWadeable);

            // Marlow's tunnel. Same shape as the grotto's, but the window to use it is
            // the first forty minutes of the night and then never again.
            Link(layout, "GENFLOOR", "DRAFTTUBE", TraversalMask.Burrow, seconds: 9f, noise: 0.5f,
                maxWater: SumpWadeable);

            Link(layout, "GENFLOOR", "TAILRACE", TraversalMask.Walk, seconds: 6f, noise: 0.55f,
                maxWater: CrossingDrowned);

            // --- The wet gates: these OPEN as the night goes on -----------------------
            Link(layout, "TAILRACE", "DRAFTTUBE", TraversalMask.Swim, seconds: 6f, noise: 0.7f,
                minWater: 0.40f);
            Link(layout, "TAILRACE", "WHEELPIT", TraversalMask.Swim, seconds: 8f, noise: 0.45f,
                minWater: ChannelSwimmable);

            // The long walk round the back of the machine hall. Eleven seconds of warning,
            // until the hall floor goes under and it stops existing.
            Link(layout, "WHEELPIT", "TURBINE", TraversalMask.Walk, seconds: 11f, noise: 0.3f,
                maxWater: CrossingDrowned);

            // =================================================================
            // CAST
            // =================================================================

            layout.cast.Clear();

            // Barty has the whole machine hall to cross, which makes him slower to
            // arrive here than at the grotto — and he knocks on the two doors that
            // are also the two routes you need for everything else.
            Place(layout, "barty", home: "TURBINE", retreat: "TURBINE", "GALLERY", "CABLEWAY");

            // Vesper starts up the penstock and comes down the duct. The air shaft is
            // the only approach the flood never touches, so she is the constant here.
            Place(layout, "vesper", home: "PENSTOCK", retreat: "TURBINE", "AIRSHAFT");

            // Marlow's window is early and short. Miss it and he is out of the night;
            // pump too hard and you have handed it back to him.
            Place(layout, "marlow", home: "FITTING", retreat: "GENFLOOR", "DRAFTTUBE");

            // Echo owns the back half of the site from the moment the level clears 0.42,
            // which on a default night is about ninety minutes in.
            Place(layout, "echo", home: "WHEELPIT", retreat: "TAILRACE", "DRAFTTUBE");

            Place(layout, "chorus", home: "WHEELPIT", retreat: "WHEELPIT",
                "GALLERY", "CABLEWAY", "DRAFTTUBE");
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
