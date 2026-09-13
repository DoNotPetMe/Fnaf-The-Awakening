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
        public string siteName = "Grotto Springs Family Fun Caverns";

        [TextArea(3, 8)]
        public string siteBlurb =
            "Opened 1979 in the Marrow Hollow limestone system. Closed 1993 after the " +
            "spring flooded the lower gallery. Reclamation survey ongoing.";

        [Header("Topology")]
        public List<NodeDef> nodes = new List<NodeDef>();
        public List<LinkDef> links = new List<LinkDef>();

        [Header("Camera captions")]
        [Tooltip("Node id whose camera the monitor opens on.")]
        public string defaultCameraNode = "GRAND";

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

        public NodeDef FindNode(string id)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (string.Equals(nodes[i].id, id, StringComparison.OrdinalIgnoreCase)) return nodes[i];
            return null;
        }

        /// <summary>Replaces the contents of this asset with the shipping Grotto Springs map.</summary>
        [ContextMenu("Populate with Grotto Springs")]
        public void PopulateWithDefault() => GrottoSpringsLayout.Populate(this);

        private static FacilityLayout _cached;

        /// <summary>
        /// Loads the layout asset from Resources, falling back to a code-built copy so
        /// that the graph, the AI and the tests all work in a project where nobody has
        /// created the asset yet.
        /// </summary>
        public static FacilityLayout LoadDefault()
        {
            if (_cached != null) return _cached;

            _cached = Resources.Load<FacilityLayout>("FacilityLayout_GrottoSprings");
            if (_cached == null)
            {
                GLog.Info(LogChannel.Facility, "No layout asset found; building Grotto Springs from code.");
                _cached = CreateInstance<FacilityLayout>();
                GrottoSpringsLayout.Populate(_cached);
            }
            return _cached;
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _cached = null;
#endif
    }
}
