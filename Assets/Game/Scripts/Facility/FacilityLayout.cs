using System;
using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// Authored description of the cavern: what rooms exist, where they sit in world
    /// space, and how they join up.
    ///
    /// One asset drives three things that would otherwise drift apart — the AI's
    /// navigation graph, the procedurally generated geometry, and the map widget on
    /// the player's monitor. Move a room here and all three follow.
    /// </summary>
    [CreateAssetMenu(menuName = "Grotto/Facility Layout", fileName = "FacilityLayout_GrottoSprings")]
    public sealed class FacilityLayout : ScriptableObject
    {
        [Serializable]
        public sealed class NodeDef
        {
            public string id = "NODE";
            public string displayName = "Unnamed";
            public NodeKind kind = NodeKind.Room;
            public FacilityZone zone = FacilityZone.Show;

            [Tooltip("Centre of the space, world units. X east, Y up, Z north.")]
            public Vector3 position;

            [Tooltip("Interior extents used by the geometry builder and reverb.")]
            public Vector3 size = new Vector3(8f, 4f, 8f);

            public bool hasCamera = true;
            public bool hasAmbientLight;

            [Tooltip("How much sound made here is felt at the station through solid rock.")]
            [Range(0f, 1f)] public float stationCoupling = 0.2f;

            [TextArea(2, 5)]
            [Tooltip("Flavour shown on the monitor when this camera is selected.")]
            public string cameraCaption = "";
        }

        [Serializable]
        public sealed class LinkDef
        {
            public string a;
            public string b;
            public TraversalMask allowed = TraversalMask.Walk;
            [Min(0.25f)] public float traverseSeconds = 4f;
            [Range(0f, 1f)] public float noiseTransmission = 0.55f;
            [Range(0f, 1f)] public float minWater;
            [Range(0f, 1f)] public float maxWater = 1f;
            public string barrierId = "";
            public bool lightDeters;
            public bool oneWay;
        }

        [Header("Identity")]
        [Tooltip("Stable id. Used by the save file and the site picker.")]
        public string siteId = "grotto";

        public string siteName = "Grotto Springs Family Fun Caverns";

        [Tooltip("One line for the site picker.")]
        public string siteTagline = "A show cave. The water cuts both ways.";

        [Tooltip("Which axis this site leans on, for the picker.")]
        public string siteEmphasis = "Balanced";

        [Tooltip("Nights survived at any site before this one unlocks. 0 is always available.")]
        [Min(0)] public int unlockAfterNights;

        [TextArea(3, 8)]
        public string siteBlurb =
            "Opened 1979 in the Marrow Hollow limestone system. Closed 1993 after the " +
            "spring flooded the lower gallery. Reclamation survey ongoing.";

        /// <summary>
        /// Where one character lives at this site.
        ///
        /// An AnimatronicDefinition describes the *character* — how it moves, what
        /// stops it, what it looks like. Where it starts, retreats to and strikes from
        /// is a property of the building, so it belongs here. Without this split a
        /// second site is impossible: every definition would be pinned to the first
        /// map's room names.
        /// </summary>
        [Serializable]
        public sealed class CastPlacement
        {
            public string animatronicId = "";
            public string homeNode = "";
            public string retreatNode = "";
            public List<string> attackNodes = new List<string>();
        }

        [Header("Topology")]
        public List<NodeDef> nodes = new List<NodeDef>();
        public List<LinkDef> links = new List<LinkDef>();

        [Header("Cast placement")]
        [Tooltip("Where each character lives at this site. Falls back to the definition when absent.")]
        public List<CastPlacement> cast = new List<CastPlacement>();

        [Header("Camera captions")]
        [Tooltip("Node id whose camera the monitor opens on.")]
        public string defaultCameraNode = "GRAND";

        [Header("Site wiring")]
        [Tooltip("Which node plays which structural role. Replaces hard-coded ids.")]
        public SiteWiring wiring = new SiteWiring
        {
            northApproach = "ADIT_N",
            southApproach = "ADIT_S",
            sump = "SUMP",
            chase = "CHASE",
            generatorBay = "GEN",
            deepGallery = "DEEP"
        };

        [Tooltip("Where this site's routes open and close on the water axis.")]
        public WaterGates gates = WaterGates.Default;

        [Header("Site character")]
        [Tooltip("Water level at midnight.")]
        [Range(0f, 1f)] public float startingWaterLevel = 0.45f;

        [Tooltip("Multiplies the night's own water inflow scale.")]
        [Range(0.2f, 3f)] public float waterScale = 1f;

        [Tooltip("Multiplies the night's own air decay scale.")]
        [Range(0.2f, 3f)] public float airScale = 1f;

        [Tooltip("Multiplies the night's own fuel burn scale.")]
        [Range(0.2f, 3f)] public float fuelScale = 1f;

        [Header("Look")]
        [Tooltip("Surface palette. Natural rock, or built concrete and steel.")]
        public SitePalette palette = SitePalette.Limestone;

        /// <summary>Builds a runtime graph. The layout asset itself stays immutable.</summary>
        public FacilityGraph BuildGraph()
        {
            var graph = new FacilityGraph();

            for (int i = 0; i < nodes.Count; i++)
            {
                var def = nodes[i];
                graph.AddNode(new FacilityNode
                {
                    Id = new NodeId(def.id),
                    DisplayName = def.displayName,
                    Kind = def.kind,
                    Zone = def.zone,
                    Position = def.position,
                    Size = def.size,
                    HasCamera = def.hasCamera,
                    HasAmbientLight = def.hasAmbientLight,
                    StationCoupling = def.stationCoupling
                });
            }

            for (int i = 0; i < links.Count; i++)
            {
                var def = links[i];
                graph.AddLink(new FacilityLink
                {
                    A = new NodeId(def.a),
                    B = new NodeId(def.b),
                    Allowed = def.allowed,
                    TraverseSeconds = def.traverseSeconds,
                    NoiseTransmission = def.noiseTransmission,
                    MinWater = def.minWater,
                    MaxWater = def.maxWater,
                    BarrierId = string.IsNullOrWhiteSpace(def.barrierId) ? null : def.barrierId,
                    LightDeters = def.lightDeters,
                    OneWay = def.oneWay
                });
            }

            graph.Bake();
            return graph;
        }

        /// <summary>This site's placement for a character, or null if it uses the default.</summary>
        public CastPlacement FindPlacement(string animatronicId)
        {
            if (string.IsNullOrWhiteSpace(animatronicId)) return null;

            for (int i = 0; i < cast.Count; i++)
                if (string.Equals(cast[i].animatronicId, animatronicId, StringComparison.OrdinalIgnoreCase))
                    return cast[i];

            return null;
        }

        public NodeDef FindNode(string id)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (string.Equals(nodes[i].id, id, StringComparison.OrdinalIgnoreCase)) return nodes[i];
            return null;
        }

        /// <summary>Replaces the contents of this asset with the shipping Grotto Springs map.</summary>
        [ContextMenu("Populate with Grotto Springs")]
        public void PopulateWithDefault() => GrottoSpringsLayout.Populate(this);

        /// <summary>
        /// The site the profile has selected, or the grotto when there is no profile.
        ///
        /// Every caller that just wants "the map" uses this; <see cref="SiteCatalog"/>
        /// is the one that knows which map that is, and whether the player has
        /// actually unlocked it.
        /// </summary>
        public static FacilityLayout LoadDefault()
        {
            SaveData save = null;
            if (ServiceLocator.TryGet(out SaveSystem saves)) save = saves.Data;

            return SiteCatalog.Load(SiteCatalog.SelectedSiteId(save));
        }

        /// <summary>Loads one named site, ignoring what the profile has selected.</summary>
        public static FacilityLayout LoadSite(string siteId) => SiteCatalog.Load(siteId);
    }
}
