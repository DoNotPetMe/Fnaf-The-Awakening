using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// The navigable topology of Grotto Springs.
    ///
    /// Animatronics never use Unity's NavMesh — they hop between named nodes on this
    /// graph. That is a deliberate design choice, not a shortcut: the whole game is
    /// about a player reading positions off a map, so positions must be discrete,
    /// nameable, loggable and forceable from a console. Mesh-level movement is then
    /// just presentation layered on top.
    /// </summary>
    public sealed class FacilityGraph
    {
        /// <summary>Returns true if the traversal is currently permitted.</summary>
        public delegate bool LinkFilter(FacilityLink link, NodeId from, NodeId to);

        private readonly Dictionary<NodeId, FacilityNode> _nodes = new Dictionary<NodeId, FacilityNode>(32);
        private readonly Dictionary<NodeId, List<FacilityLink>> _adjacency = new Dictionary<NodeId, List<FacilityLink>>(32);
        private readonly List<FacilityLink> _links = new List<FacilityLink>(64);
        private readonly Dictionary<NodeId, int> _hopsToStation = new Dictionary<NodeId, int>(32);

        // Scratch buffers — pathfinding runs several times a second across the cast,
        // and none of it should generate garbage.
        private readonly Queue<NodeId> _frontier = new Queue<NodeId>(32);
        private readonly Dictionary<NodeId, NodeId> _cameFrom = new Dictionary<NodeId, NodeId>(32);
        private readonly HashSet<NodeId> _visited = new HashSet<NodeId>();

        public IReadOnlyCollection<FacilityNode> Nodes => _nodes.Values;
        public IReadOnlyList<FacilityLink> Links => _links;
        public int NodeCount => _nodes.Count;

        /// <summary>The player's station. Resolved when the graph is built.</summary>
        public NodeId StationNode { get; private set; } = NodeId.None;

        // ---------------------------------------------------------------------
        // Construction
        // ---------------------------------------------------------------------

        public void Clear()
        {
            _nodes.Clear();
            _adjacency.Clear();
            _links.Clear();
            _hopsToStation.Clear();
            StationNode = NodeId.None;
        }

        public void AddNode(FacilityNode node)
        {
            if (node == null || !node.Id.IsValid)
            {
                GLog.Error(LogChannel.Facility, "Tried to add a node with no id.");
                return;
            }

            if (_nodes.ContainsKey(node.Id))
            {
                GLog.Warn(LogChannel.Facility, $"Duplicate node '{node.Id}' ignored.");
                return;
            }

            _nodes[node.Id] = node;
            _adjacency[node.Id] = new List<FacilityLink>(4);

            if (node.Kind == NodeKind.Station)
            {
                if (StationNode.IsValid)
                    GLog.Warn(LogChannel.Facility, $"Second Station node '{node.Id}'; '{StationNode}' stays authoritative.");
                else
                    StationNode = node.Id;
            }
        }

        public void AddLink(FacilityLink link)
        {
            if (link == null) return;

            if (!_nodes.ContainsKey(link.A) || !_nodes.ContainsKey(link.B))
            {
                GLog.Error(LogChannel.Facility,
                    $"Link {link} references a node that does not exist; it will be dropped.");
                return;
            }

            _links.Add(link);
            _adjacency[link.A].Add(link);
            if (!link.OneWay) _adjacency[link.B].Add(link);
        }

        /// <summary>Recomputes cached derived data. Call once after all nodes and links are added.</summary>
        public void Bake()
        {
            _hopsToStation.Clear();
            if (!StationNode.IsValid)
            {
                GLog.Error(LogChannel.Facility, "Facility graph has no Station node — threat scoring will not work.");
                return;
            }

            // Unfiltered BFS out from the station gives every node a "how close is this
            // to me" number that is independent of the current door and water state.
            _frontier.Clear();
            _visited.Clear();
            _frontier.Enqueue(StationNode);
            _visited.Add(StationNode);
            _hopsToStation[StationNode] = 0;

            while (_frontier.Count > 0)
            {
                var current = _frontier.Dequeue();
                int depth = _hopsToStation[current];

                var links = _adjacency[current];
                for (int i = 0; i < links.Count; i++)
                {
                    var next = links[i].Other(current);
                    if (!_visited.Add(next)) continue;
                    _hopsToStation[next] = depth + 1;
                    _frontier.Enqueue(next);
                }
            }

            foreach (var node in _nodes.Values)
            {
                if (!_hopsToStation.ContainsKey(node.Id))
                    GLog.Warn(LogChannel.Facility,
                        $"Node '{node.Id}' is unreachable from the station — check the layout's links.");
            }

            GLog.Info(LogChannel.Facility,
                $"Facility graph baked: {_nodes.Count} nodes, {_links.Count} links, station '{StationNode}'.");
        }

        // ---------------------------------------------------------------------
        // Queries
        // ---------------------------------------------------------------------

        public bool TryGetNode(NodeId id, out FacilityNode node) => _nodes.TryGetValue(id, out node);

        public FacilityNode Node(NodeId id) => _nodes.TryGetValue(id, out var n) ? n : null;

        public bool Contains(NodeId id) => _nodes.ContainsKey(id);

        public IReadOnlyList<FacilityLink> LinksFrom(NodeId id)
            => _adjacency.TryGetValue(id, out var list) ? list : System.Array.Empty<FacilityLink>();

        /// <summary>Hops from the station ignoring doors and water. Lower is more dangerous.</summary>
        public int HopsToStation(NodeId id) => _hopsToStation.TryGetValue(id, out int d) ? d : int.MaxValue;

        public Vector3 PositionOf(NodeId id) => _nodes.TryGetValue(id, out var n) ? n.Position : Vector3.zero;

        /// <summary>Collects the neighbours reachable right now under <paramref name="filter"/>.</summary>
        public void GetNeighbours(NodeId from, LinkFilter filter, List<NodeId> results)
        {
            results.Clear();
            if (!_adjacency.TryGetValue(from, out var links)) return;

            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (link.OneWay && link.A != from) continue;

                var to = link.Other(from);
                if (filter != null && !filter(link, from, to)) continue;
                results.Add(to);
            }
        }

        /// <summary>Finds the link joining two nodes, or null.</summary>
        public FacilityLink FindLink(NodeId from, NodeId to)
        {
            if (!_adjacency.TryGetValue(from, out var links)) return null;
            for (int i = 0; i < links.Count; i++)
                if (links[i].Connects(from, to)) return links[i];
            return null;
        }

        /// <summary>
        /// Breadth-first path search. Writes node ids into <paramref name="path"/>
        /// excluding <paramref name="from"/> and including <paramref name="to"/>.
        /// Breadth-first rather than A* on purpose — the graph is ~20 nodes, hop count
        /// is the metric that matters, and BFS has no heuristic to get subtly wrong.
        /// </summary>
        public bool TryFindPath(NodeId from, NodeId to, LinkFilter filter, List<NodeId> path)
        {
            path.Clear();
            if (from == to) return true;
            if (!_nodes.ContainsKey(from) || !_nodes.ContainsKey(to)) return false;

            _frontier.Clear();
            _cameFrom.Clear();
            _visited.Clear();

            _frontier.Enqueue(from);
            _visited.Add(from);

            bool found = false;
            while (_frontier.Count > 0)
            {
                var current = _frontier.Dequeue();
                if (current == to) { found = true; break; }

                var links = _adjacency[current];
                for (int i = 0; i < links.Count; i++)
                {
                    var link = links[i];
                    if (link.OneWay && link.A != current) continue;

                    var next = link.Other(current);
                    if (_visited.Contains(next)) continue;
                    if (filter != null && !filter(link, current, next)) continue;

                    _visited.Add(next);
                    _cameFrom[next] = current;
                    _frontier.Enqueue(next);
                }
            }

            if (!found) return false;

            // Walk the parent chain back and reverse in place.
            var step = to;
            while (step != from)
            {
                path.Add(step);
                if (!_cameFrom.TryGetValue(step, out step)) return false;
            }
            path.Reverse();
            return true;
        }

        /// <summary>First step of a path, or <see cref="NodeId.None"/> when there is no route.</summary>
        public NodeId NextStepToward(NodeId from, NodeId to, LinkFilter filter, List<NodeId> scratch)
        {
            if (!TryFindPath(from, to, filter, scratch) || scratch.Count == 0) return NodeId.None;
            return scratch[0];
        }

        /// <summary>Layout sanity check, surfaced by the editor validator.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();

            if (!StationNode.IsValid) problems.Add("No node of kind Station exists.");

            foreach (var node in _nodes.Values)
            {
                if (_adjacency[node.Id].Count == 0)
                    problems.Add($"Node '{node.Id}' has no links.");
                if (HopsToStation(node.Id) == int.MaxValue)
                    problems.Add($"Node '{node.Id}' is unreachable from the station.");
            }

            foreach (var link in _links)
            {
                if (link.Allowed == TraversalMask.None)
                    problems.Add($"Link {link} allows no traversal type and can never be used.");
                if (link.MinWater > link.MaxWater)
                    problems.Add($"Link {link} has MinWater {link.MinWater} above MaxWater {link.MaxWater}; it is never passable.");
                if (link.TraverseSeconds <= 0f)
                    problems.Add($"Link {link} has a non-positive traverse time.");
            }

            return problems;
        }
    }
}
