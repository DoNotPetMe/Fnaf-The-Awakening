using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.UI
{
    /// <summary>
    /// The schematic on the right of the monitor.
    ///
    /// A top-down plan of the cave built from the same layout the AI navigates, so it
    /// cannot drift out of step with the level. Nodes are plotted by their world X/Z,
    /// which means the map is a real projection of the place rather than a drawing of
    /// it — move a chamber and the map moves with it.
    ///
    /// It shows a marker for a character only where the player could plausibly know:
    /// a node with a working camera. The crawlways and the Deep Gallery stay blank,
    /// because reading those is the seismograph's job, and taking that away from the
    /// map is what gives the seismograph a reason to exist.
    /// </summary>
    public sealed class MapWidget : MonoBehaviour
    {
        private sealed class NodeMarker
        {
            public FacilityNode Node;
            public Image Box;
            public Text Label;
        }

        private readonly List<NodeMarker> _nodes = new List<NodeMarker>(24);
        private readonly List<Image> _occupants = new List<Image>(8);
        private readonly List<AnimatronicController> _scratch = new List<AnimatronicController>(4);

        private FacilityRuntime _facility;
        private AIDirector _ai;
        private RectTransform _plot;

        private Vector2 _worldMin;
        private Vector2 _worldSize;

        /// <summary>Builds the plan. Call once, after the facility exists.</summary>
        public void Build(RectTransform parent, FacilityRuntime facility, AIDirector ai)
        {
            _facility = facility;
            _ai = ai;
            _plot = parent;

            ComputeBounds();

            foreach (var node in facility.Graph.Nodes)
            {
                var box = UIFactory.Panel(_plot, "Node_" + node.Id, ColourFor(node));
                var rect = box.rectTransform;

                rect.anchorMin = rect.anchorMax = Vector2.zero;
                rect.pivot = UIFactory.Centre;
                rect.sizeDelta = SizeFor(node);
                rect.anchoredPosition = Project(node.Position);

                var outline = box.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
                outline.effectDistance = new Vector2(1f, -1f);

                var label = UIFactory.Label(rect, "Label", ShortName(node), 11,
                    TextAnchor.MiddleCenter, UIFactory.InkDim);
                UIFactory.Stretch(label.rectTransform);

                _nodes.Add(new NodeMarker { Node = node, Box = box, Label = label });
            }

            // A fixed pool of occupant markers: the cast never exceeds this.
            for (int i = 0; i < 8; i++)
            {
                var marker = UIFactory.Panel(_plot, $"Occupant{i}", Color.white);
                marker.rectTransform.sizeDelta = new Vector2(13f, 13f);
                marker.rectTransform.anchorMin = marker.rectTransform.anchorMax = Vector2.zero;
                marker.rectTransform.pivot = UIFactory.Centre;
                marker.sprite = UIFactory.BuiltinSprite;
                marker.gameObject.SetActive(false);
                _occupants.Add(marker);
            }

            GLog.Info(LogChannel.UI, $"Map built with {_nodes.Count} nodes.");
        }

        private void ComputeBounds()
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var node in _facility.Graph.Nodes)
            {
                minX = Mathf.Min(minX, node.Position.x - node.Size.x * 0.5f);
                maxX = Mathf.Max(maxX, node.Position.x + node.Size.x * 0.5f);
                minZ = Mathf.Min(minZ, node.Position.z - node.Size.z * 0.5f);
                maxZ = Mathf.Max(maxZ, node.Position.z + node.Size.z * 0.5f);
            }

            const float margin = 4f;
            _worldMin = new Vector2(minX - margin, minZ - margin);
            _worldSize = new Vector2(maxX - minX + margin * 2f, maxZ - minZ + margin * 2f);
        }

        private Vector2 Project(Vector3 world)
        {
            var size = _plot.rect.size;
            float u = Mathf.InverseLerp(_worldMin.x, _worldMin.x + _worldSize.x, world.x);
            float v = Mathf.InverseLerp(_worldMin.y, _worldMin.y + _worldSize.y, world.z);
            return new Vector2(u * size.x, v * size.y);
        }

        private Vector2 SizeFor(FacilityNode node)
        {
            var size = _plot.rect.size;
            float scaleX = size.x / Mathf.Max(0.01f, _worldSize.x);
            float scaleY = size.y / Mathf.Max(0.01f, _worldSize.y);

            return new Vector2(
                Mathf.Max(10f, node.Size.x * scaleX * 0.85f),
                Mathf.Max(10f, node.Size.z * scaleY * 0.85f));
        }

        private static string ShortName(FacilityNode node)
        {
            string key = node.Id.Key;
            return key.Length <= 7 ? key : key.Substring(0, 7);
        }

        private static Color ColourFor(FacilityNode node) => node.Kind switch
        {
            NodeKind.Station => new Color(0.22f, 0.30f, 0.20f, 0.95f),
            NodeKind.Crawlway => new Color(0.13f, 0.11f, 0.14f, 0.8f),
            NodeKind.Watercourse => new Color(0.10f, 0.20f, 0.26f, 0.9f),
            NodeKind.Sump => new Color(0.12f, 0.18f, 0.22f, 0.9f),
            NodeKind.Shaft => new Color(0.14f, 0.13f, 0.11f, 0.85f),
            _ => new Color(0.14f, 0.14f, 0.15f, 0.9f)
        };

        private void Update()
        {
            if (_facility == null) return;

            UpdateNodeTints();
            UpdateOccupants();
        }

        private void UpdateNodeTints()
        {
            var surveillance = _facility.Surveillance;

            for (int i = 0; i < _nodes.Count; i++)
            {
                var marker = _nodes[i];
                var baseColour = ColourFor(marker.Node);

                bool isActiveCamera = marker.Node.Id == surveillance.ActiveNode && surveillance.MonitorUp;
                bool cameraDown = marker.Node.HasCamera && !surveillance.IsOnline(marker.Node.Id);

                if (isActiveCamera) baseColour = Color.Lerp(baseColour, UIFactory.InkCold, 0.55f);
                else if (cameraDown) baseColour = Color.Lerp(baseColour, UIFactory.InkAlarm, 0.3f);

                // Lit nodes read brighter, so the player can see at a glance which
                // lights they have left burning.
                if (marker.Node.IsLit) baseColour = Color.Lerp(baseColour, UIFactory.InkWarn, 0.3f);

                marker.Box.color = baseColour;

                marker.Label.color = marker.Node.HasCamera
                    ? (surveillance.IsOnline(marker.Node.Id) ? UIFactory.InkDim : UIFactory.InkAlarm)
                    : new Color(0.35f, 0.33f, 0.30f);
            }
        }

        private void UpdateOccupants()
        {
            for (int i = 0; i < _occupants.Count; i++) _occupants[i].gameObject.SetActive(false);
            if (_ai == null) return;

            int used = 0;
            var surveillance = _facility.Surveillance;

            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i].Node;

                // Only report what a working camera could actually see. The blind
                // parts of the cave stay blind — that is the seismograph's territory.
                bool visible = node.HasCamera && surveillance.IsOnline(node.Id);
                if (!visible && !(DebugFlags.IsDevBuild && DebugFlags.ShowNodeGraph)) continue;

                _ai.CollectAt(node.Id, _scratch);

                for (int c = 0; c < _scratch.Count && used < _occupants.Count; c++)
                {
                    var controller = _scratch[c];
                    var marker = _occupants[used++];

                    marker.gameObject.SetActive(true);
                    marker.color = controller.Definition != null ? controller.Definition.mapColor : Color.white;

                    // Fan multiple occupants of one node so they do not stack.
                    var offset = new Vector2((c - (_scratch.Count - 1) * 0.5f) * 14f, 0f);
                    marker.rectTransform.anchoredPosition = Project(node.Position) + offset;
                }
            }
        }
    }
}
