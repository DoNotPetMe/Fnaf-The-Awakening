using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Editor
{
    /// <summary>
    /// Checks the things that go wrong silently.
    ///
    /// Most of this project is self-contained, but a handful of settings live outside
    /// the code and fail quietly when they are wrong: a missing layer makes every
    /// generated object land on Default, an unassigned render pipeline asset makes
    /// every custom shader fall back, and a layout with an unreachable node makes an
    /// animatronic stand still all night for no visible reason.
    ///
    /// Run from the menu, and again from <see cref="BuildScript"/> before any build,
    /// so a broken configuration fails the build rather than shipping.
    /// </summary>
    public static class ProjectValidator
    {
        private static readonly string[] RequiredLayers =
        {
            "Facility", "Animatronic", "Interactable", "Station",
            "CameraOnly", "PlayerOnly", "WaterSurface", "NoiseOccluder", "Prop", "VolumetricFog"
        };

        private static readonly string[] RequiredTags =
        {
            // Custom tags only. Unity's own seven — Untagged, Respawn, Finish,
            // EditorOnly, MainCamera, Player and GameController — are built in, and
            // re-declaring one in TagManager.asset logs "already registered" on every
            // single import until somebody takes it out again.
            "Animatronic", "CameraNode", "BlastDoor", "StationPanel", "DevOnly"
        };

        [MenuItem("Tools/Grotto/Validate Project", priority = 60)]
        public static void ValidateWithReport()
        {
            var problems = new List<string>();
            var warnings = new List<string>();

            Validate(problems, warnings);

            var builder = new StringBuilder();

            if (problems.Count == 0 && warnings.Count == 0)
            {
                builder.AppendLine("Everything checks out.");
            }
            else
            {
                if (problems.Count > 0)
                {
                    builder.AppendLine($"{problems.Count} problem(s):");
                    foreach (var problem in problems) builder.AppendLine("  • " + problem);
                    builder.AppendLine();
                }

                if (warnings.Count > 0)
                {
                    builder.AppendLine($"{warnings.Count} warning(s):");
                    foreach (var warning in warnings) builder.AppendLine("  • " + warning);
                }
            }

            string report = builder.ToString();
            Debug.Log("[Grotto] Project validation\n" + report);

            EditorUtility.DisplayDialog(
                problems.Count > 0 ? "Validation failed" : "Validation passed",
                report, "OK");
        }

        /// <summary>Returns true when nothing blocking was found.</summary>
        public static bool Validate(List<string> problems, List<string> warnings)
        {
            ValidateLayers(problems);
            ValidateTags(warnings);
            ValidateRenderPipeline(problems, warnings);
            ValidateSites(problems, warnings);
            ValidateSettingsAssets(problems, warnings);
            ValidateLayout(problems, warnings);
            ValidateCast(problems, warnings);
            ValidateScene(warnings);

            return problems.Count == 0;
        }

        private static void ValidateLayers(List<string> problems)
        {
            foreach (var layer in RequiredLayers)
            {
                if (LayerMask.NameToLayer(layer) >= 0) continue;
                problems.Add($"Layer '{layer}' is missing. " +
                             "ProjectSettings/TagManager.asset defines it — did it fail to import?");
            }
        }

        private static void ValidateTags(List<string> warnings)
        {
            var tags = new HashSet<string>(UnityEditorInternal.InternalEditorUtility.tags);

            foreach (var tag in RequiredTags)
                if (!tags.Contains(tag)) warnings.Add($"Tag '{tag}' is missing.");
        }

        private static void ValidateRenderPipeline(List<string> problems, List<string> warnings)
        {
            var pipeline = GraphicsSettings.defaultRenderPipeline;

            if (pipeline == null)
            {
                problems.Add(
                    "No render pipeline asset is assigned in Project Settings > Graphics. " +
                    "The project's shaders target URP and will render magenta without one. " +
                    "Run Tools > Grotto > Rebuild Render Pipeline.");
                return;
            }

            if (!pipeline.GetType().FullName.Contains("Universal"))
                warnings.Add($"The assigned pipeline is '{pipeline.GetType().Name}', not URP. " +
                             "The Grotto shaders target URP.");

            foreach (var shaderName in new[]
                     {
                         "Grotto/CaveTriplanar", "Grotto/SurfaceLit", "Grotto/WaterSurface",
                         "Grotto/MonitorFeed", "Grotto/StationOverlay"
                     })
            {
                if (Shader.Find(shaderName) == null)
                    warnings.Add($"Shader '{shaderName}' did not compile or was not found. " +
                                 "The material library falls back, but the effect is lost.");
            }
        }

        private static void ValidateSettingsAssets(List<string> problems, List<string> warnings)
        {
            string resources = SettingsAssetBuilder.ResourcesPath;

            foreach (var (path, label) in new[]
                     {
                         ($"{resources}/GameConfig.asset", "GameConfig"),
                         ($"{resources}/FacilityTuning.asset", "FacilityTuning")
                     })
            {
                if (File.Exists(path)) continue;
                problems.Add($"{label} is missing at {path}. " +
                             "Run Tools > Grotto > Rebuild Settings Assets.");
            }

            // Every site the catalog knows about. A missing layout asset is not fatal —
            // SiteCatalog builds from code when Resources has nothing — so this is a
            // warning rather than a problem, and it names the site.
            for (int i = 0; i < SiteCatalog.Count; i++)
            {
                var entry = SiteCatalog.EntryAt(i);
                if (File.Exists($"{resources}/{entry.ResourceName}.asset")) continue;

                warnings.Add($"Site '{entry.Id}' has no baked layout asset. It will build " +
                             "from code, which is fine, but a designer cannot tweak it in the " +
                             "inspector until Rebuild Settings Assets has run.");
            }

            // The cast has to be under Resources now: CastSpawner loads it at runtime.
            string castFolder = SettingsAssetBuilder.CastPath;
            if (!Directory.Exists(castFolder))
            {
                problems.Add($"No character assets at {castFolder}. The cast is loaded from " +
                             "Resources at runtime, so without them the site spawns empty. " +
                             "Run Tools > Grotto > Rebuild Settings Assets.");
            }
            else if (Directory.GetFiles(castFolder, "*.asset").Length == 0)
            {
                problems.Add($"{castFolder} exists but is empty. Run Tools > Grotto > " +
                             "Rebuild Settings Assets.");
            }

            var config = AssetDatabase.LoadAssetAtPath<GameConfig>($"{resources}/GameConfig.asset");
            if (config == null) return;

            for (int night = 1; night <= 6; night++)
                if (config.GetNight(night) == null)
                    warnings.Add($"GameConfig has no definition for night {night}.");
        }

        /// <summary>
        /// Builds every site from code and runs the graph validator over it.
        ///
        /// The same checks <c>python3 Tools/check_layouts.py</c> runs outside Unity, so
        /// a map broken by an edit shows up here too rather than only in CI.
        /// </summary>
        private static void ValidateSites(List<string> problems, List<string> warnings)
        {
            for (int i = 0; i < SiteCatalog.Count; i++)
            {
                var entry = SiteCatalog.EntryAt(i);
                var layout = SiteCatalog.Build(entry.Id);

                try
                {
                    if (layout.nodes.Count == 0)
                    {
                        problems.Add($"Site '{entry.Id}' has no nodes.");
                        continue;
                    }

                    foreach (var problem in layout.BuildGraph().Validate())
                        problems.Add($"Site '{entry.Id}': {problem}");

                    foreach (var (role, id) in new[]
                             {
                                 ("northApproach", layout.wiring.northApproach),
                                 ("southApproach", layout.wiring.southApproach),
                                 ("sump", layout.wiring.sump),
                                 ("chase", layout.wiring.chase),
                                 ("generatorBay", layout.wiring.generatorBay),
                                 ("deepGallery", layout.wiring.deepGallery)
                             })
                    {
                        if (string.IsNullOrWhiteSpace(id) || layout.FindNode(id) == null)
                            problems.Add($"Site '{entry.Id}': wiring.{role} names '{id}', " +
                                         "which is not a node there.");
                    }

                    foreach (var placement in layout.cast)
                    {
                        if (layout.FindNode(placement.homeNode) == null)
                            problems.Add($"Site '{entry.Id}': {placement.animatronicId}'s home " +
                                         $"'{placement.homeNode}' is not a node there.");

                        foreach (var attack in placement.attackNodes)
                            if (layout.FindNode(attack) == null)
                                problems.Add($"Site '{entry.Id}': {placement.animatronicId} attacks " +
                                             $"from '{attack}', which is not a node there.");
                    }

                    if (layout.gates.SafeBand < 0f)
                        warnings.Add($"Site '{entry.Id}': the dry and wet routes overlap by " +
                                     $"{-layout.gates.SafeBand:0.00}. Deliberate at a relentless " +
                                     "site; a mistake anywhere else.");
                }
                finally
                {
                    Object.DestroyImmediate(layout);
                }
            }
        }

        private static void ValidateLayout(List<string> problems, List<string> warnings)
        {
            var layout = AssetDatabase.LoadAssetAtPath<FacilityLayout>(
                $"{SettingsAssetBuilder.ResourcesPath}/FacilityLayout_GrottoSprings.asset");

            if (layout == null) return;

            var graph = layout.BuildGraph();
            foreach (var problem in graph.Validate()) problems.Add("Layout: " + problem);

            // Every barrier a link names has to exist somewhere, or the link is
            // permanently open and nobody notices until playtest.
            var barrierIds = new HashSet<string>
            {
                FacilityBarriers.DoorNorth,
                FacilityBarriers.DoorSouth,
                FacilityBarriers.SumpGrate
            };

            foreach (var link in layout.links)
            {
                if (string.IsNullOrEmpty(link.barrierId)) continue;
                if (!barrierIds.Contains(link.barrierId))
                    warnings.Add($"Link {link.a}<->{link.b} names barrier '{link.barrierId}', " +
                                 "which nothing in the scene builder creates.");
            }
        }

        private static void ValidateCast(List<string> problems, List<string> warnings)
        {
            var layout = AssetDatabase.LoadAssetAtPath<FacilityLayout>(
                $"{SettingsAssetBuilder.ResourcesPath}/FacilityLayout_GrottoSprings.asset");
            if (layout == null) return;

            var graph = layout.BuildGraph();
            var guids = AssetDatabase.FindAssets("t:AnimatronicDefinition");

            if (guids.Length == 0)
            {
                warnings.Add("No AnimatronicDefinition assets found. Run Rebuild Settings Assets.");
                return;
            }

            var seenIds = new HashSet<string>();

            foreach (var guid in guids)
            {
                var definition = AssetDatabase.LoadAssetAtPath<AnimatronicDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (definition == null) continue;

                if (!seenIds.Add(definition.id))
                    problems.Add($"Two animatronics share the id '{definition.id}'.");

                if (!graph.Contains(new NodeId(definition.homeNode)))
                    problems.Add($"{definition.displayName}: home node '{definition.homeNode}' is not in the layout.");

                if (definition.attackNodes.Count == 0)
                {
                    warnings.Add($"{definition.displayName} has no attack nodes and can never kill the player.");
                    continue;
                }

                foreach (var attackNode in definition.attackNodes)
                {
                    var node = new NodeId(attackNode);

                    if (!graph.Contains(node))
                    {
                        problems.Add($"{definition.displayName}: attack node '{attackNode}' is not in the layout.");
                        continue;
                    }

                    // An attack node that does not touch the station is a dead end the
                    // character will walk to and then give up on, forever.
                    if (graph.FindLink(node, graph.StationNode) == null)
                        problems.Add($"{definition.displayName}: attack node '{attackNode}' has no link " +
                                     "to the station, so the approach can never resolve.");
                }

                // Can this body actually reach its own attack nodes?
                bool reachable = false;
                var scratch = new List<NodeId>();
                var filter = MakeCapabilityFilter(definition.traversal);

                foreach (var attackNode in definition.attackNodes)
                {
                    if (graph.TryFindPath(new NodeId(definition.homeNode), new NodeId(attackNode), filter, scratch))
                    {
                        reachable = true;
                        break;
                    }
                }

                if (!reachable)
                    warnings.Add($"{definition.displayName} cannot route from '{definition.homeNode}' to any " +
                                 $"attack node with traversal {definition.traversal}, at least at a dry water level.");
            }
        }

        /// <summary>
        /// A water-agnostic capability filter for validation. Deliberately ignores
        /// doors and water so the check reports genuine topology errors rather than
        /// the current state of the simulation.
        /// </summary>
        private static FacilityGraph.LinkFilter MakeCapabilityFilter(TraversalMask capability)
            => (link, from, to) => (link.Allowed & capability) != 0;

        private static void ValidateScene(List<string> warnings)
        {
            if (!File.Exists(FacilitySceneBuilder.ScenePath))
            {
                warnings.Add("Assets/Game/Scenes/Facility.unity does not exist. " +
                             "Run Tools > Grotto > Build Facility Scene.");
                return;
            }

            bool inBuild = false;
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.path == FacilitySceneBuilder.ScenePath && scene.enabled) inBuild = true;

            if (!inBuild)
                warnings.Add("Facility.unity is not enabled in Build Settings, so a player build would be empty.");
        }
    }
}
