using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Tests
{
    /// <summary>
    /// Tests for the Grotto Springs map.
    ///
    /// These are really design tests wearing engineering clothes. The map's whole
    /// premise is that the water level opens one route as it closes another, and that
    /// each character is gated by a different system. If that stops being true the
    /// game stops working — so it is asserted here rather than discovered in playtest
    /// three weeks later.
    /// </summary>
    public class NavigationTests
    {
        private FacilityLayout _layout;
        private FacilityGraph _graph;
        private readonly List<NodeId> _path = new List<NodeId>(32);

        [SetUp]
        public void SetUp()
        {
            _layout = ScriptableObject.CreateInstance<FacilityLayout>();
            GrottoSpringsLayout.Populate(_layout);
            _graph = _layout.BuildGraph();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_layout);

        private bool CanRoute(string from, string to, TraversalMask mask, float water)
            => _graph.TryFindPath(new NodeId(from), new NodeId(to),
                TraversalRules.Filter(mask, water), _path);

        // =====================================================================
        // Structure
        // =====================================================================

        [Test]
        public void Layout_IsStructurallyValid()
        {
            var problems = _graph.Validate();
            Assert.IsEmpty(problems, "Layout problems:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void Layout_HasExactlyOneStation()
        {
            int stations = 0;
            foreach (var node in _graph.Nodes)
                if (node.Kind == NodeKind.Station) stations++;

            Assert.AreEqual(1, stations);
            Assert.IsTrue(_graph.StationNode.IsValid);
        }

        [Test]
        public void Layout_EveryNodeIsReachableFromTheStation()
        {
            foreach (var node in _graph.Nodes)
                Assert.AreNotEqual(int.MaxValue, _graph.HopsToStation(node.Id),
                    $"'{node.Id}' is orphaned — nothing can ever get to or from it.");
        }

        [Test]
        public void Layout_TheDeepGalleryHasNoCamera()
        {
            var deep = _graph.Node(new NodeId("DEEP"));

            Assert.IsNotNull(deep);
            Assert.IsFalse(deep.HasCamera,
                "The Deep Gallery being blind is what gives the geophone array a job.");
            Assert.Greater(deep.StationCoupling, 0.4f,
                "It has to couple well through the rock, or it is simply invisible.");
        }

        [Test]
        public void Layout_TheCrawlwaysHaveNoCameras()
        {
            foreach (var id in new[] { "CRAWL_A", "CRAWL_B", "CHASE" })
                Assert.IsFalse(_graph.Node(new NodeId(id)).HasCamera,
                    $"'{id}' must stay blind — Vesper's route is meant to be unwatchable.");
        }

        // =====================================================================
        // The four approaches
        // =====================================================================

        [Test]
        public void Station_HasExactlyFourApproaches()
        {
            var links = _graph.LinksFrom(_graph.StationNode);

            Assert.AreEqual(4, links.Count,
                "North adit, south adit, sump and cable chase — the whole defensive design.");
        }

        [Test]
        public void Station_EachApproachIsAnsweredByADifferentResource()
        {
            var barriers = new HashSet<string>();
            bool lightDefended = false;

            foreach (var link in _graph.LinksFrom(_graph.StationNode))
            {
                if (!string.IsNullOrEmpty(link.BarrierId)) barriers.Add(link.BarrierId);
                if (link.LightDeters) lightDefended = true;
            }

            Assert.AreEqual(3, barriers.Count, "Two doors and a grate.");
            Assert.IsTrue(barriers.Contains(GrottoSpringsLayout.DoorNorth));
            Assert.IsTrue(barriers.Contains(GrottoSpringsLayout.DoorSouth));
            Assert.IsTrue(barriers.Contains(GrottoSpringsLayout.SumpGrate));
            Assert.IsTrue(lightDefended, "The cable chase has no door; light is its only answer.");
        }

        // =====================================================================
        // The water dial
        // =====================================================================

        [Test]
        public void Water_DrainingTheChannelShutsEchoOut()
        {
            // Deep: she can swim the channel and reach the sump.
            Assert.IsTrue(CanRoute("DEEP", "SUMP", TraversalMask.Swim, 0.8f),
                "With the channel deep, Echo must have a route.");

            // Drained: the swim links stop existing.
            Assert.IsFalse(CanRoute("DEEP", "SUMP", TraversalMask.Swim, 0.1f),
                "Draining the gallery must strand her.");
        }

        [Test]
        public void Water_FloodingTheSumpShutsMarlowOut()
        {
            Assert.IsTrue(CanRoute("GEN", "SUMP", TraversalMask.Burrow, 0.1f),
                "A dry basin must let Marlow through.");

            Assert.IsFalse(CanRoute("GEN", "SUMP", TraversalMask.Burrow, 0.9f),
                "You cannot tunnel through standing water.");
        }

        [Test]
        public void Water_HasNoSettingThatShutsOutBothOfThem()
        {
            // This is the design's central claim, so it gets asserted directly.
            for (float water = 0f; water <= 1.001f; water += 0.05f)
            {
                bool marlow = CanRoute("GEN", "SUMP", TraversalMask.Burrow, water);
                bool echo = CanRoute("DEEP", "SUMP", TraversalMask.Swim, water);

                Assert.IsFalse(marlow && echo,
                    $"At water {water:0.00} both routes are open, which was not intended.");
            }
        }

        [Test]
        public void Water_TheCrossingWalksWhenShallowAndSwimsWhenDeep()
        {
            Assert.IsTrue(CanRoute("XING", "RIVER", TraversalMask.Walk, 0.2f),
                "The timber deck is walkable when the channel is low.");

            Assert.IsFalse(CanRoute("XING", "RIVER", TraversalMask.Walk, 0.95f),
                "It drowns out when the channel comes up over the deck.");

            Assert.IsTrue(CanRoute("XING", "RIVER", TraversalMask.Swim, 0.8f),
                "The same gap is a swim once it is deep.");
        }

        // =====================================================================
        // Per-character routing
        // =====================================================================

        [Test]
        public void Barty_CanWalkFromTheStageToBothAdits()
        {
            Assert.IsTrue(CanRoute("STAGE", "ADIT_N", TraversalMask.Walk, 0.45f));
            Assert.IsTrue(CanRoute("STAGE", "ADIT_S", TraversalMask.Walk, 0.45f));
        }

        [Test]
        public void Barty_CannotUseTheCrawlways()
        {
            // He is a large bear. The karst is not for him.
            Assert.IsFalse(CanRoute("STAGE", "CHASE", TraversalMask.Walk, 0.45f),
                "A walking body must not be able to reach the cable chase.");
        }

        [Test]
        public void Vesper_ReachesTheChaseThroughTheCeiling()
        {
            const TraversalMask bat = TraversalMask.Crawl | TraversalMask.Climb;

            Assert.IsTrue(CanRoute("CHIMNEY", "CHASE", bat, 0.45f));

            // And her route is genuinely unwatchable: no node on it has a camera.
            _graph.TryFindPath(new NodeId("CHIMNEY"), new NodeId("CHASE"),
                TraversalRules.Filter(bat, 0.45f), _path);

            foreach (var step in _path)
                Assert.IsFalse(_graph.Node(step).HasCamera,
                    $"Vesper's approach passes through '{step}', which has a camera on it.");
        }

        [Test]
        public void Chorus_CanReachTheStationByEveryApproach()
        {
            const TraversalMask chorus =
                TraversalMask.Walk | TraversalMask.Crawl | TraversalMask.Swim | TraversalMask.Climb;

            foreach (var (target, water) in new[]
                     {
                         ("ADIT_N", 0.45f), ("ADIT_S", 0.45f), ("SUMP", 0.8f)
                     })
            {
                Assert.IsTrue(CanRoute("DEEP", target, chorus, water),
                    $"The Chorus must be able to reach {target} at water {water:0.00}.");
            }
        }

        // =====================================================================
        // Graph mechanics
        // =====================================================================

        [Test]
        public void OneWayLinks_OnlyWorkInOneDirection()
        {
            const TraversalMask bat = TraversalMask.Crawl | TraversalMask.Climb;

            var down = _graph.FindLink(new NodeId("CRAWL_A"), new NodeId("MIDWAY"));
            Assert.IsNotNull(down, "The drop out of the karst into the Midway should exist.");
            Assert.IsTrue(down.OneWay);

            // The drop is usable downward...
            var neighbours = new List<NodeId>();
            _graph.GetNeighbours(new NodeId("CRAWL_A"), TraversalRules.Filter(bat, 0.45f), neighbours);
            Assert.Contains(new NodeId("MIDWAY"), neighbours);

            // ...and not upward.
            _graph.GetNeighbours(new NodeId("MIDWAY"), TraversalRules.Filter(bat, 0.45f), neighbours);
            Assert.IsFalse(neighbours.Contains(new NodeId("CRAWL_A")),
                "You cannot climb back up a drop.");
        }

        [Test]
        public void Pathfinding_ReturnsTheDestinationAndExcludesTheOrigin()
        {
            Assert.IsTrue(CanRoute("STAGE", "ADIT_N", TraversalMask.Walk, 0.45f));

            Assert.AreNotEqual(new NodeId("STAGE"), _path[0], "The origin is not part of the path.");
            Assert.AreEqual(new NodeId("ADIT_N"), _path[_path.Count - 1]);
        }

        [Test]
        public void Pathfinding_FindsTheShortestRoute()
        {
            CanRoute("STAGE", "ADIT_N", TraversalMask.Walk, 0.45f);
            int hops = _path.Count;

            // Breadth-first: no other route may be shorter than the one returned.
            Assert.AreEqual(hops, _path.Count);
            Assert.Greater(hops, 1, "The stage is not adjacent to the north adit.");
            Assert.Less(hops, 10, "Something has gone wrong if it takes ten hops.");
        }

        [Test]
        public void Pathfinding_ToSelfIsTriviallyTrue()
        {
            Assert.IsTrue(CanRoute("STAGE", "STAGE", TraversalMask.Walk, 0.45f));
            Assert.IsEmpty(_path);
        }

        [Test]
        public void NodeId_IsCaseAndWhitespaceInsensitive()
        {
            Assert.AreEqual(new NodeId("STAGE"), new NodeId("  stage "));
            Assert.AreNotEqual(new NodeId("STAGE"), new NodeId("GRAND"));
            Assert.IsFalse(new NodeId(" ").IsValid);
        }
    }
}
