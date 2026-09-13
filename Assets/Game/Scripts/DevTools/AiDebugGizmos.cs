using System.Collections.Generic;
using UnityEngine;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.DevTools
{
    /// <summary>
    /// Draws the navigation graph, the noise field and live AI routes in the Scene
    /// view.
    ///
    /// The most valuable thing here is that links are coloured by whether they are
    /// passable *right now*, for the capability being inspected. A link that looks
    /// fine in the layout but is water-gated shut, or blocked by a door, is the usual
    /// cause of "why is Echo just standing there" — and this makes that visible in a
    /// glance instead of a debugging session.
    ///
    /// Uses <see cref="Debug.DrawLine"/> during play so it appears in the Scene view
    /// while the game runs, and Gizmos when it is not.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AiDebugGizmos : MonoBehaviour
    {
        [Tooltip("Traversal capability the link colouring is evaluated for.")]
        [SerializeField] private TraversalMask inspectAs = TraversalMask.Walk;

        [SerializeField] private float nodeRadius = 0.8f;

        private static readonly Color PassableColour = new Color(0.35f, 0.85f, 0.45f, 0.85f);
        private static readonly Color BlockedColour = new Color(0.9f, 0.28f, 0.24f, 0.7f);
        private static readonly Color GatedColour = new Color(0.95f, 0.72f, 0.25f, 0.8f);
        private static readonly Color StationColour = new Color(0.4f, 0.8f, 1f, 1f);

        private FacilityRuntime _facility;
        private AIDirector _ai;
        private readonly List<NodeId> _pathScratch = new List<NodeId>(24);

        private void Start()
        {
            ServiceLocator.TryGet(out _facility);
            ServiceLocator.TryGet(out _ai);
        }

        private void Update()
        {
            if (_facility == null) return;

            if (DebugFlags.ShowNodeGraph) DrawGraphRuntime();
            if (DebugFlags.ShowNoiseField) DrawNoiseRuntime();
            if (DebugFlags.ShowAIPaths) DrawPathsRuntime();
        }

        // ---------------------------------------------------------------------
        // Runtime (Scene view during play)
        // ---------------------------------------------------------------------

        private void DrawGraphRuntime()
        {
            var graph = _facility.Graph;
            var links = graph.Links;

            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                var from = graph.PositionOf(link.A);
                var to = graph.PositionOf(link.B);

                Debug.DrawLine(from, to, ColourForLink(link), 0f, depthTest: false);
            }

            foreach (var node in graph.Nodes)
            {
                var colour = node.Kind == NodeKind.Station ? StationColour
                    : node.IsLit ? new Color(1f, 0.85f, 0.4f, 0.9f)
                    : new Color(0.55f, 0.58f, 0.62f, 0.6f);

                DrawCross(node.Position, nodeRadius, colour);
            }
        }

        private Color ColourForLink(FacilityLink link)
        {
            float water = _facility.Water.Level01;

            if ((link.Allowed & inspectAs) == 0)
                return new Color(0.3f, 0.3f, 0.34f, 0.35f);   // not this body's route at all

            if (water < link.MinWater || water > link.MaxWater) return GatedColour;

            if (!string.IsNullOrEmpty(link.BarrierId))
            {
                var barrier = _facility.GetBarrier(link.BarrierId);
                if (barrier != null && barrier.IsBlocking) return BlockedColour;
            }

            return PassableColour;
        }

        private void DrawNoiseRuntime()
        {
            foreach (var node in _facility.Graph.Nodes)
            {
                float level = _facility.Noise.GetLevel(node.Id);
                if (level < 0.02f) continue;

                // A vertical bar whose height is the level: readable from any angle.
                var basePoint = node.Position;
                var top = basePoint + Vector3.up * (level * 6f);

                var colour = Color.Lerp(new Color(0.3f, 0.7f, 0.9f), new Color(1f, 0.3f, 0.2f), level);
                Debug.DrawLine(basePoint, top, colour, 0f, depthTest: false);
                DrawCross(top, 0.3f, colour);
            }
        }

        private void DrawPathsRuntime()
        {
            if (_ai == null) return;

            var graph = _facility.Graph;
            var station = graph.StationNode;

            foreach (var controller in _ai.Cast)
            {
                if (controller == null || controller.AiLevel <= 0 || controller.Definition == null) continue;

                var filter = _facility.MakeFilter(controller.Definition.traversal, respectsBarriers: false);
                if (!graph.TryFindPath(controller.CurrentNode, station, filter, _pathScratch)) continue;

                var colour = controller.Definition.mapColor;
                var previous = graph.PositionOf(controller.CurrentNode) + Vector3.up * 0.4f;

                for (int i = 0; i < _pathScratch.Count; i++)
                {
                    var next = graph.PositionOf(_pathScratch[i]) + Vector3.up * 0.4f;
                    Debug.DrawLine(previous, next, colour, 0f, depthTest: false);
                    previous = next;
                }
            }
        }

        private static void DrawCross(Vector3 centre, float radius, Color colour)
        {
            Debug.DrawLine(centre - Vector3.right * radius, centre + Vector3.right * radius, colour, 0f, false);
            Debug.DrawLine(centre - Vector3.up * radius, centre + Vector3.up * radius, colour, 0f, false);
            Debug.DrawLine(centre - Vector3.forward * radius, centre + Vector3.forward * radius, colour, 0f, false);
        }

        // ---------------------------------------------------------------------
        // Edit mode
        // ---------------------------------------------------------------------

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            // In play mode the runtime drawing above is live and more accurate.
            if (Application.isPlaying) return;

            var layout = FacilityLayout.LoadDefault();
            if (layout == null) return;

            foreach (var link in layout.links)
            {
                var a = layout.FindNode(link.a);
                var b = layout.FindNode(link.b);
                if (a == null || b == null) continue;

                Gizmos.color = (link.allowed & inspectAs) != 0
                    ? new Color(0.4f, 0.8f, 0.5f, 0.6f)
                    : new Color(0.35f, 0.35f, 0.4f, 0.3f);

                Gizmos.DrawLine(a.position, b.position);
            }

            foreach (var node in layout.nodes)
            {
                Gizmos.color = node.kind == NodeKind.Station
                    ? StationColour
                    : new Color(0.6f, 0.62f, 0.66f, 0.5f);

                Gizmos.DrawWireSphere(node.position, nodeRadius);
                Gizmos.DrawWireCube(node.position, node.size);

                UnityEditor.Handles.color = new Color(0.85f, 0.85f, 0.9f, 0.9f);
                UnityEditor.Handles.Label(node.position + Vector3.up * (node.size.y * 0.5f + 0.6f), node.id);
            }
        }

        [DevCommand("show.as", Category = "global",
            Help = "Sets which traversal capability the graph gizmo colours for.",
            Usage = "show.as <walk|crawl|climb|swim|burrow|any>")]
        private static string ShowAs(CommandArgs args)
        {
            string requested = args.String(0);
            if (!System.Enum.TryParse(requested, ignoreCase: true, out TraversalMask mask))
                return $"Unknown traversal '{requested}'.";

            var gizmos = FindFirstObjectByType<AiDebugGizmos>();
            if (gizmos == null) return "No AiDebugGizmos in this scene.";

            gizmos.inspectAs = mask;
            return $"Graph gizmo now colouring for {mask}.";
        }
#endif
    }
}
