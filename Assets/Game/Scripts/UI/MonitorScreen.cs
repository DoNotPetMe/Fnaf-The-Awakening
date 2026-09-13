using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.UI
{
    /// <summary>
    /// The surveillance monitor: live feed, camera selector, plan and geophones.
    ///
    /// Everything the player can learn about the cave is on this panel, and raising it
    /// costs them the room they are sitting in. That trade is the game's core loop, so
    /// the panel is built to make the trade feel worth taking — the feed is large, the
    /// plan is legible at a glance, and the geophone rows cover exactly the approaches
    /// the cameras cannot.
    /// </summary>
    public sealed class MonitorScreen : MonoBehaviour
    {
        private FacilityRuntime _facility;
        private AIDirector _ai;
        private PhantomCotton _phantom;

        private RawImage _feed;
        private Material _feedMaterial;
        private Text _caption;
        private Text _status;
        private Image _phantomFigure;

        private readonly Dictionary<NodeId, Button> _cameraButtons = new Dictionary<NodeId, Button>(16);
        private readonly Dictionary<NodeId, Image> _cameraButtonBacks = new Dictionary<NodeId, Image>(16);

        private MapWidget _map;
        private SeismographWidget _seismograph;

        private static readonly int SignalProperty = Shader.PropertyToID("_Signal");
        private static readonly int StaticProperty = Shader.PropertyToID("_StaticAmount");

        public void Build(RectTransform parent, FacilityRuntime facility, AIDirector ai)
        {
            _facility = facility;
            _ai = ai;
            ServiceLocator.TryGet(out _phantom);

            var backdrop = UIFactory.Panel(parent, "MonitorBackdrop", new Color(0.02f, 0.022f, 0.025f, 0.97f));
            UIFactory.Stretch(backdrop.rectTransform);

            BuildFeed(backdrop.rectTransform);
            BuildCameraSelector(backdrop.rectTransform);
            BuildSidebar(backdrop.rectTransform);
        }

        // ---------------------------------------------------------------------

        private void BuildFeed(RectTransform parent)
        {
            var frame = UIFactory.Panel(parent, "FeedFrame", new Color(0.05f, 0.05f, 0.055f, 1f));
            var frameRect = frame.rectTransform;
            frameRect.anchorMin = new Vector2(0.02f, 0.18f);
            frameRect.anchorMax = new Vector2(0.63f, 0.96f);
            frameRect.offsetMin = frameRect.offsetMax = Vector2.zero;

            var shader = Shader.Find("Grotto/MonitorFeed");
            if (shader != null)
            {
                _feedMaterial = new Material(shader) { name = "MonitorFeed (runtime)" };
            }
            else
            {
                GLog.Warn(LogChannel.UI,
                    "Grotto/MonitorFeed did not compile; the feed will render without CRT treatment.");
            }

            _feed = UIFactory.Feed(frameRect, "Feed", _feedMaterial);
            UIFactory.Stretch(_feed.rectTransform, 6f);

            // The phantom is drawn over the feed rather than in the 3D scene, because
            // it is not in the 3D scene. It is a picture the monitor is producing.
            _phantomFigure = UIFactory.Panel(_feed.rectTransform, "Phantom", new Color(0f, 0f, 0f, 0.85f));
            var phantomRect = _phantomFigure.rectTransform;
            phantomRect.anchorMin = new Vector2(0.36f, 0.05f);
            phantomRect.anchorMax = new Vector2(0.64f, 0.85f);
            phantomRect.offsetMin = phantomRect.offsetMax = Vector2.zero;
            _phantomFigure.gameObject.SetActive(false);

            _caption = UIFactory.Label(frameRect, "Caption", "", 17, TextAnchor.UpperLeft, UIFactory.InkDim);
            var captionRect = _caption.rectTransform;
            captionRect.anchorMin = new Vector2(0f, 0f);
            captionRect.anchorMax = new Vector2(1f, 0f);
            captionRect.pivot = new Vector2(0.5f, 1f);
            captionRect.anchoredPosition = new Vector2(0f, -4f);
            captionRect.sizeDelta = new Vector2(-16f, 48f);

            _status = UIFactory.Label(frameRect, "Status", "", 20, TextAnchor.UpperRight, UIFactory.InkWarn);
            UIFactory.Anchor(_status.rectTransform, UIFactory.TopRight, new Vector2(-14f, -10f), new Vector2(320f, 30f));
        }

        private void BuildCameraSelector(RectTransform parent)
        {
            var strip = UIFactory.Group(parent, "CameraStrip");
            strip.anchorMin = new Vector2(0.02f, 0.03f);
            strip.anchorMax = new Vector2(0.63f, 0.16f);
            strip.offsetMin = strip.offsetMax = Vector2.zero;

            var order = _facility.Surveillance.CameraOrder;
            const int columns = 8;
            float cellWidth = 1f / columns;

            for (int i = 0; i < order.Count; i++)
            {
                var node = order[i];
                int row = i / columns;
                int column = i % columns;

                var captured = node;
                var button = UIFactory.TextButton(strip, "Cam_" + node,
                    $"{_facility.Surveillance.CameraNumber(node):00}",
                    Vector2.zero, () => _facility.Surveillance.SelectNode(captured), 18);

                var rect = (RectTransform)button.transform;
                rect.anchorMin = new Vector2(column * cellWidth, 1f - (row + 1) * 0.5f);
                rect.anchorMax = new Vector2((column + 1) * cellWidth, 1f - row * 0.5f);
                rect.offsetMin = new Vector2(3f, 3f);
                rect.offsetMax = new Vector2(-3f, -3f);
                rect.sizeDelta = Vector2.zero;

                _cameraButtons[node] = button;
                _cameraButtonBacks[node] = button.GetComponent<Image>();
            }
        }

        private void BuildSidebar(RectTransform parent)
        {
            // Plan.
            var planFrame = UIFactory.Panel(parent, "PlanFrame", new Color(0.04f, 0.045f, 0.05f, 1f));
            var planRect = planFrame.rectTransform;
            planRect.anchorMin = new Vector2(0.65f, 0.46f);
            planRect.anchorMax = new Vector2(0.98f, 0.96f);
            planRect.offsetMin = planRect.offsetMax = Vector2.zero;

            var planTitle = UIFactory.Label(planRect, "Title", "GROTTO SPRINGS — PLAN", 15,
                TextAnchor.UpperLeft, UIFactory.InkDim);
            UIFactory.Anchor(planTitle.rectTransform, UIFactory.TopLeft, new Vector2(8f, -6f), new Vector2(320f, 22f));

            var plot = UIFactory.Group(planRect, "Plot");
            plot.anchorMin = new Vector2(0f, 0f);
            plot.anchorMax = new Vector2(1f, 1f);
            plot.offsetMin = new Vector2(10f, 10f);
            plot.offsetMax = new Vector2(-10f, -30f);

            _map = plot.gameObject.AddComponent<MapWidget>();

            // Geophones.
            var seismoFrame = UIFactory.Panel(parent, "SeismoFrame", new Color(0.04f, 0.045f, 0.05f, 1f));
            var seismoRect = seismoFrame.rectTransform;
            seismoRect.anchorMin = new Vector2(0.65f, 0.03f);
            seismoRect.anchorMax = new Vector2(0.98f, 0.44f);
            seismoRect.offsetMin = seismoRect.offsetMax = Vector2.zero;

            var seismoTitle = UIFactory.Label(seismoRect, "Title", "GEOPHONE ARRAY", 15,
                TextAnchor.UpperLeft, UIFactory.InkDim);
            UIFactory.Anchor(seismoTitle.rectTransform, UIFactory.TopLeft, new Vector2(8f, -6f), new Vector2(320f, 22f));

            var channels = UIFactory.Group(seismoRect, "Channels");
            channels.anchorMin = new Vector2(0f, 0f);
            channels.anchorMax = new Vector2(1f, 1f);
            channels.offsetMin = new Vector2(8f, 8f);
            channels.offsetMax = new Vector2(-8f, -28f);

            _seismograph = channels.gameObject.AddComponent<SeismographWidget>();
        }

        /// <summary>
        /// Widgets are built a frame after construction so their RectTransforms have
        /// been through a layout pass — the plan projects into its own rect, and a
        /// zero-sized rect would collapse every node onto the origin.
        /// </summary>
        private void Start()
        {
            if (_map != null) _map.Build((RectTransform)_map.transform, _facility, _ai);
            if (_seismograph != null) _seismograph.Build((RectTransform)_seismograph.transform, _facility);
        }

        private void Update()
        {
            if (_facility == null) return;

            var surveillance = _facility.Surveillance;
            var active = surveillance.ActiveNode;

            UpdateFeed(surveillance, active);
            UpdateCaption(surveillance, active);
            UpdateButtons(surveillance, active);
            UpdatePhantom(active);
        }

        private void UpdateFeed(SurveillanceSystem surveillance, NodeId active)
        {
            var texture = surveillance.TextureOf(active);
            if (texture != null) _feed.texture = texture;

            if (_feedMaterial == null) return;

            float signal;
            if (surveillance.IsRebooting(active)) signal = 0f;
            else if (!surveillance.IsOnline(active)) signal = 0.05f;
            else signal = Mathf.Clamp01(surveillance.ConditionOf(active));

            // Bad air degrades the picture as well as the player, so the instrument
            // itself becomes less trustworthy exactly when it matters.
            float haze = _facility.Ventilation.HallucinationPressure01;
            signal *= Mathf.Lerp(1f, 0.55f, haze);

            _feedMaterial.SetFloat(SignalProperty, signal);
            _feedMaterial.SetFloat(StaticProperty, Mathf.Lerp(0.05f, 0.3f, haze));
        }

        private void UpdateCaption(SurveillanceSystem surveillance, NodeId active)
        {
            var node = _facility.Graph.Node(active);
            if (node == null) return;

            string caption = "";
            var layout = _facility.Layout;
            if (layout != null)
            {
                var def = layout.FindNode(active.Key);
                if (def != null) caption = def.cameraCaption;
            }

            _caption.text = string.IsNullOrEmpty(caption)
                ? $"CAM {surveillance.CameraNumber(active):00} — {node.DisplayName.ToUpperInvariant()}"
                : caption;

            if (surveillance.IsRebooting(active))
            {
                _status.text = $"REBOOTING  {surveillance.RebootProgress01(active) * 100f:0}%";
                _status.color = UIFactory.InkCold;
            }
            else if (!surveillance.IsOnline(active))
            {
                _status.text = "SIGNAL LOST";
                _status.color = UIFactory.InkAlarm;
            }
            else
            {
                float condition = surveillance.ConditionOf(active);
                _status.text = condition < 0.45f ? $"DEGRADED  {condition * 100f:0}%" : "";
                _status.color = UIFactory.InkWarn;
            }
        }

        private void UpdateButtons(SurveillanceSystem surveillance, NodeId active)
        {
            foreach (var pair in _cameraButtonBacks)
            {
                bool isActive = pair.Key == active;
                bool online = surveillance.IsOnline(pair.Key);

                pair.Value.color = isActive
                    ? new Color(0.28f, 0.24f, 0.10f, 0.95f)
                    : online ? UIFactory.PanelBack : new Color(0.16f, 0.06f, 0.06f, 0.9f);
            }
        }

        private void UpdatePhantom(NodeId active)
        {
            if (_phantomFigure == null) return;

            bool showing = _phantom != null
                           && _phantom.PhantomRemaining > 0f
                           && _phantom.ActivePhantomNode == active;

            _phantomFigure.gameObject.SetActive(showing);
            if (!showing) return;

            // It does not move. It is not doing anything. That is the unpleasant part.
            float fade = Mathf.Clamp01(_phantom.PhantomRemaining);
            _phantomFigure.color = new Color(0f, 0f, 0f, 0.65f + 0.25f * fade);
        }

        /// <summary>Attempts a reboot of the live camera. Bound to the interact key.</summary>
        public void RebootActiveCamera()
        {
            var surveillance = _facility.Surveillance;
            if (surveillance.BeginReboot(surveillance.ActiveNode))
                EventBus.Publish(new AlertSignal("Rebooting camera...", AlertSeverity.Info));
        }
    }
}
