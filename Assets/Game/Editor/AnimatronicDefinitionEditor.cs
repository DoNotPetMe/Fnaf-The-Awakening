using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Editor
{
    /// <summary>
    /// Adds a live analysis panel to the animatronic inspector.
    ///
    /// The questions a designer actually has when tuning a character — can it reach
    /// the station at all, under what water levels, and what stops it — are answers
    /// that require running the traversal rules against the layout. Making the
    /// inspector answer them turns balancing from playtest-and-guess into reading.
    /// </summary>
    [CustomEditor(typeof(AnimatronicDefinition))]
    public sealed class AnimatronicDefinitionEditor : UnityEditor.Editor
    {
        private static readonly float[] ProbeWaterLevels = { 0f, 0.2f, 0.35f, 0.5f, 0.55f, 0.75f, 0.95f };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var definition = (AnimatronicDefinition)target;

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("Route analysis", EditorStyles.boldLabel);

            var layout = AssetDatabase.LoadAssetAtPath<FacilityLayout>(
                $"{SettingsAssetBuilder.ResourcesPath}/FacilityLayout_GrottoSprings.asset");

            if (layout == null)
            {
                EditorGUILayout.HelpBox(
                    "No layout asset. Run Tools > Grotto > Rebuild Settings Assets to enable this panel.",
                    MessageType.Info);
                return;
            }

            var graph = layout.BuildGraph();
            EditorGUILayout.HelpBox(Analyse(definition, graph, layout), MessageType.None);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Difficulty reference", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(DescribeCadence(definition), EditorStyles.wordWrappedMiniLabel);
        }

        private static string Analyse(AnimatronicDefinition definition, FacilityGraph graph, FacilityLayout layout)
        {
            var builder = new StringBuilder(1024);
            var scratch = new List<NodeId>();

            var home = new NodeId(definition.homeNode);
            if (!graph.Contains(home))
                return $"Home node '{definition.homeNode}' is not in the layout.";

            builder.AppendLine($"Traversal: {definition.traversal}");
            builder.AppendLine();

            // Walk the water range and report where each attack route opens and closes.
            // This is the table that makes Marlow and Echo's opposing gates visible.
            foreach (var attack in definition.attackNodes)
            {
                var attackNode = new NodeId(attack);
                if (!graph.Contains(attackNode))
                {
                    builder.AppendLine($"{attack}: NOT IN LAYOUT");
                    continue;
                }

                builder.Append(attack.PadRight(10));

                bool anyOpen = false;
                foreach (float water in ProbeWaterLevels)
                {
                    var filter = MakeFilter(definition.traversal, water);
                    bool reachable = graph.TryFindPath(home, attackNode, filter, scratch);
                    anyOpen |= reachable;

                    builder.Append(reachable ? $"{water:0.00}:ok  " : $"{water:0.00}:--  ");
                }

                builder.AppendLine();

                if (!anyOpen)
                    builder.AppendLine("           unreachable at every water level — check the traversal mask");
            }

            builder.AppendLine();

            // What actually stands in the way on the last step.
            foreach (var attack in definition.attackNodes)
            {
                var attackNode = new NodeId(attack);
                var link = graph.FindLink(attackNode, graph.StationNode);

                if (link == null)
                {
                    builder.AppendLine($"{attack} -> station: NO LINK (the approach can never resolve)");
                    continue;
                }

                var stoppedBy = new List<string>();
                if (!string.IsNullOrEmpty(link.BarrierId) && definition.respectsBarriers)
                    stoppedBy.Add(link.BarrierId);
                if (link.LightDeters && definition.lightAverse) stoppedBy.Add("light");
                if (link.MinWater > 0f) stoppedBy.Add($"needs water >= {link.MinWater:0.00}");
                if (link.MaxWater < 1f) stoppedBy.Add($"needs water <= {link.MaxWater:0.00}");

                builder.AppendLine($"{attack} -> station: " +
                                   (stoppedBy.Count == 0
                                       ? "NOTHING STOPS IT"
                                       : string.Join(", ", stoppedBy)));
            }

            return builder.ToString();
        }

        /// <summary>
        /// Traversal filter at a hypothetical water level, ignoring doors — doors are a
        /// runtime state, and this panel is about what the layout permits.
        /// </summary>
        private static FacilityGraph.LinkFilter MakeFilter(TraversalMask capability, float water)
        {
            return (link, from, to) =>
                (link.Allowed & capability) != 0 &&
                water >= link.MinWater &&
                water <= link.MaxWater;
        }

        private static string DescribeCadence(AnimatronicDefinition definition)
        {
            var builder = new StringBuilder(256);

            foreach (int level in new[] { 5, 10, 15, 20 })
            {
                float interval = MovementRoll.NextInterval(null, definition.movementIntervalSeconds, level, 0f);
                float chance = MovementRoll.SuccessChance(level);

                // Expected seconds per successful move at neutral pressure.
                float perMove = chance <= 0f ? float.PositiveInfinity : interval / chance;

                builder.Append($"L{level}: rolls every {interval:0.0}s at {chance * 100f:0}% " +
                               $"(~{perMove:0.0}s per move)    ");
            }

            return builder.ToString();
        }
    }
}
