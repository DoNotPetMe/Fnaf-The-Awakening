using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.UI
{
    /// <summary>
    /// Geophone readout: what the rock under the control room is picking up.
    ///
    /// The map only shows what a camera can see, and two of the routes into the
    /// station have no camera on them at all. This is the instrument that covers
    /// them — it reports a *magnitude per node*, coupled through the rock, with no
    /// identification and no direction beyond the node name. Something is moving in
    /// the Deep Gallery. It does not say what, and it never will.
    ///
    /// Bars use a fast attack and a slow decay with a peak-hold tick, the way a real
    /// VU meter behaves, so a single footfall registers visibly instead of flashing
    /// for one frame and vanishing.
    /// </summary>
    public sealed class SeismographWidget : MonoBehaviour
    {
        private sealed class Channel
        {
            public NodeId Node;
            public Image Bar;
            public Image Peak;
            public Text Label;
            public float Level;
            public float PeakLevel;
            public float PeakHold;
        }

        [SerializeField] private float attackLambda = 22f;
        [SerializeField] private float decayLambda = 1.8f;
        [SerializeField] private float peakHoldSeconds = 1.1f;

        private readonly List<Channel> _channels = new List<Channel>(8);
        private FacilityRuntime _facility;

        /// <summary>Builds one channel per node that couples well enough to be worth a row.</summary>
        public void Build(RectTransform parent, FacilityRuntime facility, float minimumCoupling = 0.3f)
        {
            _facility = facility;

            var candidates = new List<FacilityNode>();
            foreach (var node in facility.Graph.Nodes)
            {
                if (node.Kind == NodeKind.Station) continue;
                if (node.StationCoupling < minimumCoupling) continue;
                candidates.Add(node);
            }

            // Loudest-coupled first: the channels that matter sit at the top.
            candidates.Sort((a, b) => b.StationCoupling.CompareTo(a.StationCoupling));

            const float rowHeight = 22f;
            for (int i = 0; i < candidates.Count; i++)
            {
                var node = candidates[i];

                var row = UIFactory.Group(parent, "Channel_" + node.Id);
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0f, 1f);
                row.offsetMin = new Vector2(0f, 0f);
                row.offsetMax = new Vector2(0f, 0f);
                row.anchoredPosition = new Vector2(0f, -i * rowHeight);
                row.sizeDelta = new Vector2(0f, rowHeight - 3f);

                var label = UIFactory.Label(row, "Label", ShortLabel(node), 12,
                    TextAnchor.MiddleLeft, UIFactory.InkDim);
                var labelRect = UIFactory.Stretch(label.rectTransform);
                labelRect.anchorMax = new Vector2(0.34f, 1f);
                labelRect.offsetMin = new Vector2(4f, 0f);
                labelRect.offsetMax = Vector2.zero;

                var track = UIFactory.Panel(row, "Track", new Color(0f, 0f, 0f, 0.5f));
                var trackRect = UIFactory.Stretch(track.rectTransform);
                trackRect.anchorMin = new Vector2(0.35f, 0f);
                trackRect.offsetMin = Vector2.zero;
                trackRect.offsetMax = new Vector2(-4f, 0f);

                var bar = UIFactory.Panel(track.transform, "Bar", UIFactory.InkCold);
                var barRect = UIFactory.Stretch(bar.rectTransform, 2f);
                barRect.pivot = new Vector2(0f, 0.5f);
                bar.sprite = UIFactory.BuiltinSprite;
                bar.type = Image.Type.Filled;
                bar.fillMethod = Image.FillMethod.Horizontal;
                bar.fillAmount = 0f;

                var peak = UIFactory.Panel(track.transform, "Peak", UIFactory.InkWarn);
                var peakRect = peak.rectTransform;
                peakRect.anchorMin = new Vector2(0f, 0f);
                peakRect.anchorMax = new Vector2(0f, 1f);
                peakRect.pivot = new Vector2(0.5f, 0.5f);
                peakRect.sizeDelta = new Vector2(2f, -4f);

                _channels.Add(new Channel { Node = node.Id, Bar = bar, Peak = peak, Label = label });
            }

            GLog.Info(LogChannel.UI, $"Seismograph built with {_channels.Count} channels.");
        }

        private static string ShortLabel(FacilityNode node)
        {
            string name = node.DisplayName;
            return name.Length <= 18 ? name : name.Substring(0, 17) + "…";
        }

        private void Update()
        {
            if (_facility == null) return;

            float dt = Time.deltaTime;

            for (int i = 0; i < _channels.Count; i++)
            {
                var channel = _channels[i];

                // Coupled magnitude, gained up: raw coupling values are small, and the
                // instrument is supposed to be sensitive.
                float felt = Mathf.Clamp01(_facility.Noise.FeltAtStation(channel.Node) * 2.6f);

                // Fast attack, slow release — a transient must be visible.
                float lambda = felt > channel.Level ? attackLambda : decayLambda;
                channel.Level = MathUtil.ExpDecay(channel.Level, felt, lambda, dt);

                if (channel.Level >= channel.PeakLevel)
                {
                    channel.PeakLevel = channel.Level;
                    channel.PeakHold = peakHoldSeconds;
                }
                else
                {
                    channel.PeakHold -= dt;
                    if (channel.PeakHold <= 0f)
                        channel.PeakLevel = Mathf.Max(channel.Level, channel.PeakLevel - dt * 0.45f);
                }

                channel.Bar.fillAmount = channel.Level;

                // Warm the bar as it climbs: a loud channel should draw the eye.
                channel.Bar.color = Color.Lerp(UIFactory.InkCold, UIFactory.InkAlarm,
                    Mathf.InverseLerp(0.45f, 1f, channel.Level));

                var trackRect = (RectTransform)channel.Peak.transform.parent;
                float width = trackRect.rect.width;
                channel.Peak.rectTransform.anchoredPosition =
                    new Vector2(Mathf.Clamp(channel.PeakLevel * width, 2f, width - 2f), 0f);
                channel.Peak.enabled = channel.PeakLevel > 0.02f;

                channel.Label.color = channel.Level > 0.35f ? UIFactory.Ink : UIFactory.InkDim;
            }
        }
    }
}
