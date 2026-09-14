using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Tests
{
    /// <summary>
    /// Properties that must hold for <em>every</em> site, not just the one that was
    /// written first.
    ///
    /// NavigationTests asserts the specific promises Grotto Springs makes. These
    /// assert the contract a site has to satisfy to be playable at all — every room
    /// reachable, every wired role present, every character placed somewhere that
    /// exists, and the water gates in an order that means something.
    ///
    /// Run as one test per site via a source, so a failure names the site rather than
    /// making you bisect a loop.
    /// </summary>
    public class SiteTests
    {
        private static IEnumerable<string> SiteIds
        {
            get
            {
                foreach (var id in SiteCatalog.Ids) yield return id;
            }
        }

        private readonly List<NodeId> _path = new List<NodeId>(32);

        private static FacilityLayout Build(string siteId) => SiteCatalog.Build(siteId);

        // =====================================================================
        // Structure
        // =====================================================================

        [Test]
        public void EverySite_HasAUniqueId([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                Assert.AreEqual(siteId, layout.siteId,
                    "A site's catalog id and the id its layout writes must agree, or the " +
                    "save file will name a site the catalog cannot find.");

                Assert.IsNotEmpty(layout.siteName);
                Assert.IsNotEmpty(layout.siteTagline);
                Assert.IsNotEmpty(layout.siteBlurb);
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void EverySite_GraphValidates([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                var problems = layout.BuildGraph().Validate();
                Assert.IsEmpty(problems, $"{siteId}: {string.Join("; ", problems)}");
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void EverySite_StartsWithTheStation([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                Assert.AreEqual(NodeKind.Station, layout.nodes[0].kind,
                    "LayoutAuthoring.StationApproaches wires nodes[0], so the station has to " +
                    "be declared first.");
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void EverySite_WiresEveryRole([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                var wiring = layout.wiring;

                foreach (var (role, id) in new[]
                         {
                             ("northApproach", wiring.northApproach),
                             ("southApproach", wiring.southApproach),
                             ("sump", wiring.sump),
                             ("chase", wiring.chase),
                             ("generatorBay", wiring.generatorBay),
                             ("deepGallery", wiring.deepGallery)
                         })
                {
                    Assert.IsNotEmpty(id, $"{siteId}: wiring.{role} is blank.");
                    Assert.IsNotNull(layout.FindNode(id),
                        $"{siteId}: wiring.{role} names '{id}', which is not a node here.");
                }
            }
            finally { Object.DestroyImmediate(layout); }
        }

        // =====================================================================
        // Reachability
        // =====================================================================

        [Test]
        public void EverySite_HasNoUnreachableRoom([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                var graph = layout.BuildGraph();

                const TraversalMask everything = TraversalMask.Walk | TraversalMask.Crawl |
                                                 TraversalMask.Climb | TraversalMask.Swim |
                                                 TraversalMask.Burrow;

                // Across the whole water dial: a room that only opens at one setting is
                // fine, a room that opens at none is a room nobody will ever see.
                foreach (var node in graph.Nodes)
                {
                    bool reachable = false;

                    foreach (float water in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                    {
                        if (!graph.TryFindPath(graph.StationNode, node.Id,
                                TraversalRules.Filter(everything, water), _path)) continue;

                        reachable = true;
                        break;
                    }

                    Assert.IsTrue(reachable,
                        $"{siteId}: '{node.Id}' ({node.DisplayName}) cannot be reached from the " +
                        "station at any water level.");
                }
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void EverySite_HasSomethingTheCamerasCannotSee([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                var deep = layout.FindNode(layout.wiring.deepGallery);

                Assert.IsNotNull(deep, $"{siteId}: no deep gallery.");
                Assert.IsFalse(deep.hasCamera,
                    $"{siteId}: the deep gallery has a camera on it. The seismograph exists " +
                    "because one room cannot be watched; giving it a camera removes the only " +
                    "reason that widget is on the desk.");
            }
            finally { Object.DestroyImmediate(layout); }
        }

        // =====================================================================
        // Cast placement
        // =====================================================================

        [Test]
        public void EverySite_PlacesTheWholeCast([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                foreach (var id in new[] { "barty", "vesper", "marlow", "echo", "chorus" })
                {
                    var placement = layout.FindPlacement(id);
                    Assert.IsNotNull(placement, $"{siteId}: '{id}' is not placed.");

                    Assert.IsNotNull(layout.FindNode(placement.homeNode),
                        $"{siteId}: {id}'s home '{placement.homeNode}' is not a node here.");
                    Assert.IsNotNull(layout.FindNode(placement.retreatNode),
                        $"{siteId}: {id}'s retreat '{placement.retreatNode}' is not a node here.");

                    Assert.IsNotEmpty(placement.attackNodes,
                        $"{siteId}: {id} has no attack node, so it can never threaten the station.");
                }
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void EverySite_OnlyAttacksFromAnApproach([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                var approaches = new HashSet<string>
                {
                    layout.wiring.northApproach,
                    layout.wiring.southApproach,
                    layout.wiring.sump,
                    layout.wiring.chase
                };

                foreach (var placement in layout.cast)
                {
                    foreach (var node in placement.attackNodes)
                    {
                        Assert.IsTrue(approaches.Contains(node),
                            $"{siteId}: {placement.animatronicId} attacks from '{node}', which is " +
                            "not one of the four approaches — it would reach a room next to the " +
                            "station and then loiter there all night.");
                    }
                }
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void EverySite_UsesAllFourApproaches([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                var used = new HashSet<string>();
                foreach (var placement in layout.cast)
                foreach (var node in placement.attackNodes)
                    used.Add(node);

                foreach (var (role, id) in new[]
                         {
                             ("north door", layout.wiring.northApproach),
                             ("south door", layout.wiring.southApproach),
                             ("sump grate", layout.wiring.sump),
                             ("cable chase", layout.wiring.chase)
                         })
                {
                    Assert.IsTrue(used.Contains(id),
                        $"{siteId}: nothing ever uses the {role}. Every approach costs the player " +
                        "a resource to defend; one nobody uses is a control that does nothing.");
                }
            }
            finally { Object.DestroyImmediate(layout); }
        }

        // =====================================================================
        // Water gates
        // =====================================================================

        [Test]
        public void EverySite_HasOrderedGates([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                var gates = layout.gates;

                Assert.LessOrEqual(gates.diggable, gates.wadeable,
                    $"{siteId}: a sump that can be tunnelled through but not waded in is not a thing.");
                Assert.LessOrEqual(gates.swimmable, gates.drowned,
                    $"{siteId}: the swim threshold must be at or below the drowning threshold.");

                Assert.Greater(gates.drowned, 0.5f,
                    $"{siteId}: routes drowning below half means the map mostly does not exist.");
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void EverySite_StartsInsideItsOwnRange([ValueSource(nameof(SiteIds))] string siteId)
        {
            var layout = Build(siteId);
            try
            {
                Assert.GreaterOrEqual(layout.startingWaterLevel, 0f);
                Assert.LessOrEqual(layout.startingWaterLevel, 1f);

                // A site that opens already drowned is not a difficulty choice, it is a
                // night the player cannot affect.
                Assert.Less(layout.startingWaterLevel, layout.gates.drowned,
                    $"{siteId}: midnight starts above the drowning mark.");
            }
            finally { Object.DestroyImmediate(layout); }
        }

        /// <summary>
        /// The three shipping sites are meant to disagree about what the water is for.
        /// If they converge on the same numbers, there is one map with three skins.
        /// </summary>
        [Test]
        public void TheSites_AreActuallyDifferent()
        {
            var grotto = Build("grotto");
            var hollowmere = Build(HollowmereLayout.SiteId);
            var sablefield = Build(SablefieldLayout.SiteId);

            try
            {
                Assert.Less(hollowmere.gates.wadeable, grotto.gates.wadeable,
                    "Hollowmere's dry band should be tighter than the grotto's.");

                Assert.Greater(sablefield.gates.swimmable, grotto.gates.swimmable,
                    "Sablefield's swim threshold should be higher than the grotto's.");

                Assert.Greater(hollowmere.startingWaterLevel, sablefield.startingWaterLevel + 0.3f,
                    "The hydro station and the grain terminal should start at opposite ends " +
                    "of the dial — that difference is the whole point of having both.");

                Assert.Greater(sablefield.airScale, 1.5f,
                    "Sablefield's clock is the air, not the water.");

                Assert.Greater(hollowmere.waterScale, 1.4f,
                    "Hollowmere's clock is the water.");

                // And the palettes, so they do not all read as the same building.
                Assert.AreNotEqual(grotto.palette, hollowmere.palette);
                Assert.AreNotEqual(hollowmere.palette, sablefield.palette);
            }
            finally
            {
                Object.DestroyImmediate(grotto);
                Object.DestroyImmediate(hollowmere);
                Object.DestroyImmediate(sablefield);
            }
        }

        // =====================================================================
        // Catalog
        // =====================================================================

        [Test]
        public void Catalog_DefaultSiteIsAlwaysUnlocked()
        {
            var layout = Build(SiteCatalog.DefaultSiteId);
            try
            {
                Assert.AreEqual(0, layout.unlockAfterNights,
                    "The default site must be available on a fresh profile, or a new player " +
                    "has nothing to play.");
            }
            finally { Object.DestroyImmediate(layout); }
        }

        [Test]
        public void Catalog_UnknownSiteFallsBackRatherThanThrowing()
        {
            var save = new SaveData { selectedSiteId = "a-site-from-a-newer-build" };
            Assert.AreEqual(SiteCatalog.DefaultSiteId, SiteCatalog.SelectedSiteId(save));
        }

        [Test]
        public void Catalog_LockedSiteRevertsToTheDefault()
        {
            var save = new SaveData { selectedSiteId = SablefieldLayout.SiteId };

            // Nothing cleared: Sablefield needs four nights.
            Assert.AreEqual(SiteCatalog.DefaultSiteId, SiteCatalog.SelectedSiteId(save));
        }

        [Test]
        public void Catalog_UnlocksOnNightsClearedAnywhere()
        {
            var save = new SaveData { selectedSiteId = HollowmereLayout.SiteId };

            for (int night = 1; night <= 2; night++)
                save.GetOrCreateRecord(night, "grotto").completed = true;

            Assert.AreEqual(2, save.TotalNightsCleared());
            Assert.AreEqual(HollowmereLayout.SiteId, SiteCatalog.SelectedSiteId(save),
                "Two nights cleared at the grotto should open Hollowmere — the sites are " +
                "alternative places to play a night, not parallel campaigns.");
        }
    }
}
