using System.Collections.Generic;
using UnityEngine;

namespace Grotto.Facility
{
    /// <summary>
    /// Helpers for writing a site layout in code.
    ///
    /// Layouts are authored as C# rather than as .asset files so each node position,
    /// link time and water threshold sits next to the sentence explaining why it has
    /// that value — and so a map change is reviewable in a diff. These are the verbs
    /// that keeps the authoring readable.
    /// </summary>
    public static class LayoutAuthoring
    {
        public static void Node(FacilityLayout layout, string id, string name, NodeKind kind,
            FacilityZone zone, Vector3 pos, Vector3 size, bool hasCamera = true,
            bool ambientLight = false, float coupling = 0.2f, string caption = "")
        {
            layout.nodes.Add(new FacilityLayout.NodeDef
            {
                id = id,
                displayName = name,
                kind = kind,
                zone = zone,
                position = pos,
                size = size,
                hasCamera = hasCamera,
                hasAmbientLight = ambientLight,
                stationCoupling = coupling,
                cameraCaption = caption
            });
        }

        public static void Link(FacilityLayout layout, string a, string b, TraversalMask allowed,
            float seconds, float noise, float minWater = 0f, float maxWater = 1f,
            string barrier = "", bool lightDeters = false, bool oneWay = false)
        {
            layout.links.Add(new FacilityLayout.LinkDef
            {
                a = a,
                b = b,
                allowed = allowed,
                traverseSeconds = seconds,
                noiseTransmission = noise,
                minWater = minWater,
                maxWater = maxWater,
                barrierId = barrier,
                lightDeters = lightDeters,
                oneWay = oneWay
            });
        }

        /// <summary>Places one character at this site.</summary>
        public static void Place(FacilityLayout layout, string animatronicId,
            string home, string retreat, params string[] attackNodes)
        {
            layout.cast.Add(new FacilityLayout.CastPlacement
            {
                animatronicId = animatronicId,
                homeNode = home,
                retreatNode = retreat,
                attackNodes = new List<string>(attackNodes)
            });
        }

        /// <summary>The four approaches every station has, wired to this site's nodes.</summary>
        public static void StationApproaches(FacilityLayout layout,
            string north, string south, string sump, string chase,
            float doorSeconds = 3f, float sumpSeconds = 5f, float chaseSeconds = 4f)
        {
            Link(layout, layout.nodes[0].id, north, TraversalMask.Walk | TraversalMask.Crawl,
                doorSeconds, 0.9f, barrier: FacilityBarriers.DoorNorth);

            Link(layout, layout.nodes[0].id, south, TraversalMask.Walk | TraversalMask.Crawl,
                doorSeconds, 0.9f, barrier: FacilityBarriers.DoorSouth);

            Link(layout, layout.nodes[0].id, sump, TraversalMask.Burrow | TraversalMask.Swim,
                sumpSeconds, 0.95f, barrier: FacilityBarriers.SumpGrate);

            // No door on this one, ever. Light is the only answer.
            Link(layout, layout.nodes[0].id, chase, TraversalMask.Crawl | TraversalMask.Climb,
                chaseSeconds, 0.85f, lightDeters: true);
        }
    }
}
