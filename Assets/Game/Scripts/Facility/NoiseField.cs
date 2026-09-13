using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>Flavour tag on a noise event. Drives which sound plays, not how loud it is.</summary>
    public enum NoiseKind { Machinery, Door, Footstep, Impact, Water, Voice }

    /// <summary>
    /// Sound as a gameplay quantity.
    ///
    /// Every node carries a loudness that decays over time and bleeds into its
    /// neighbours through the links, attenuated by each link's transmission. Two
    /// things read this field: the animatronics, who are drawn toward loud places,
    /// and the station's seismograph, which is the *only* way to know anything about
    /// the Deep Gallery — there is no camera down there, on purpose.
    ///
    /// So running the pump is not just a power cost. It is a beacon.
    /// </summary>
    public sealed class NoiseField
    {
        private readonly FacilityTuning _tuning;
        private readonly FacilityGraph _graph;

        /// <summary>Decaying one-off noise per node.</summary>
        private readonly Dictionary<NodeId, float> _transient = new Dictionary<NodeId, float>(32);

        /// <summary>Sustained sources, keyed so a source can update or clear its own contribution.</summary>
        private readonly Dictionary<string, ContinuousSource> _continuous = new Dictionary<string, ContinuousSource>(8);

        private readonly Dictionary<NodeId, float> _levels = new Dictionary<NodeId, float>(32);
        private readonly Dictionary<NodeId, float> _continuousByNode = new Dictionary<NodeId, float>(32);
        private readonly Dictionary<NodeId, float> _bleed = new Dictionary<NodeId, float>(32);
        private readonly Dictionary<NodeId, NoiseKind> _lastKind = new Dictionary<NodeId, NoiseKind>(32);

        private struct ContinuousSource
        {
            public NodeId Node;
            public float Level;
        }

        public NoiseField(FacilityGraph graph, FacilityTuning tuning)
        {
            _graph = graph;
            _tuning = tuning != null ? tuning : ScriptableObject.CreateInstance<FacilityTuning>();

            foreach (var node in _graph.Nodes)
            {
                _transient[node.Id] = 0f;
                _levels[node.Id] = 0f;
            }
        }

        /// <summary>A one-off sound. <paramref name="loudness"/> is 0..1.</summary>
        public void Emit(NodeId node, float loudness, NoiseKind kind = NoiseKind.Impact)
        {
            if (!_transient.ContainsKey(node)) return;
            _transient[node] = Mathf.Clamp01(_transient[node] + Mathf.Clamp01(loudness));
            _lastKind[node] = kind;
        }

        /// <summary>
        /// Registers or updates a sustained source. Call every tick with the current
        /// level; call <see cref="ClearContinuous"/> or pass 0 when it stops.
        /// </summary>
        public void SetContinuous(string key, NodeId node, float level)
        {
            if (level <= 0.001f)
            {
                _continuous.Remove(key);
                return;
            }
            _continuous[key] = new ContinuousSource { Node = node, Level = Mathf.Clamp01(level) };
        }

        public void ClearContinuous(string key) => _continuous.Remove(key);

        public void Clear()
        {
            _continuous.Clear();
            var keys = new List<NodeId>(_transient.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                _transient[keys[i]] = 0f;
                _levels[keys[i]] = 0f;
            }
        }

        public void Tick(float realDelta)
        {
            if (realDelta <= 0f) return;

            // 1. Transients fade.
            float decay = Mathf.Exp(-_tuning.noiseDecayLambda * realDelta);
            var nodes = _graph.Nodes;
            foreach (var node in nodes)
            {
                float t = _transient[node.Id] * decay;
                _transient[node.Id] = t < _tuning.noiseFloor ? 0f : t;
            }

            // 2. Fold in whatever is running right now.
            _continuousByNode.Clear();
            foreach (var pair in _continuous)
            {
                var src = pair.Value;
                _continuousByNode.TryGetValue(src.Node, out float existing);
                // Sources at the same node combine, but never past saturation.
                _continuousByNode[src.Node] = Mathf.Clamp01(existing + src.Level);
            }

            RecomputeLevels();

            // 3. Sound bleeds downhill through the links.
            _bleed.Clear();
            var links = _graph.Links;
            for (int i = 0; i < links.Count; i++)
            {
                var link = links[i];
                if (!_levels.TryGetValue(link.A, out float la)) continue;
                if (!_levels.TryGetValue(link.B, out float lb)) continue;

                float rate = _tuning.noiseBleedPerSecond * link.NoiseTransmission * realDelta;
                float difference = la - lb;
                if (Mathf.Abs(difference) < 0.001f) continue;

                var target = difference > 0f ? link.B : link.A;
                float amount = Mathf.Abs(difference) * Mathf.Clamp01(rate);

                _bleed.TryGetValue(target, out float acc);
                _bleed[target] = acc + amount;
            }

            foreach (var pair in _bleed)
                _transient[pair.Key] = Mathf.Clamp01(_transient[pair.Key] + pair.Value);

            RecomputeLevels();

            // 4. Publish onto the nodes so gizmos and the map can read it without a lookup.
            foreach (var node in nodes)
                node.NoiseLevel = _levels[node.Id];
        }

        private void RecomputeLevels()
        {
            foreach (var node in _graph.Nodes)
            {
                _continuousByNode.TryGetValue(node.Id, out float continuous);
                _levels[node.Id] = Mathf.Clamp01(_transient[node.Id] + continuous);
            }
        }

        /// <summary>Loudness at a node, 0..1.</summary>
        public float GetLevel(NodeId node) => _levels.TryGetValue(node, out float v) ? v : 0f;

        /// <summary>
        /// What the station's geophones actually register from a node — level scaled by
        /// how well that node couples to the rock under the control room. The Deep
        /// Gallery is far away but couples well; the Incline is close and couples badly.
        /// </summary>
        public float FeltAtStation(NodeId node)
        {
            var facilityNode = _graph.Node(node);
            if (facilityNode == null) return 0f;
            return GetLevel(node) * facilityNode.StationCoupling;
        }

        public NoiseKind LastKindAt(NodeId node) => _lastKind.TryGetValue(node, out var k) ? k : NoiseKind.Impact;

        /// <summary>The noisiest node right now, for AI attraction and for debugging.</summary>
        public NodeId Loudest(out float level)
        {
            level = 0f;
            var best = NodeId.None;
            foreach (var pair in _levels)
            {
                if (pair.Value <= level) continue;
                level = pair.Value;
                best = pair.Key;
            }
            return best;
        }

        /// <summary>Total energy in the field. A blunt "how loud is tonight" number for tooling.</summary>
        public float TotalEnergy
        {
            get
            {
                float sum = 0f;
                foreach (var pair in _levels) sum += pair.Value;
                return sum;
            }
        }
    }
}
