using System;
using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// The reclamation survey: the one thing in the night that rewards looking at a
    /// camera rather than punishing it.
    ///
    /// The problem this solves is structural. Every other system makes the monitor a
    /// cost — it draws power, it makes noise, it parks your head so you cannot see the
    /// doors. A player who works that out optimally ends up staring at a blank wall
    /// with the monitor down, which is both correct play and the least interesting
    /// version of the game. There has to be a reason to look.
    ///
    /// So: the survey office wants condition readings, and releases your fuel
    /// allowance in stages as you file them. Hold a camera on the room it has asked
    /// for, for a few seconds, and a can's worth of diesel appears in the day tank.
    /// That is a real, legible payment into the resource the whole night is about.
    ///
    /// The tension is in *which* room it asks for. The target is drawn from the far
    /// side of the map — never an approach, never the station — so filing a reading
    /// always means several seconds looking somewhere that cannot hurt you, while
    /// something you cannot see closes on somewhere that can. And the dwell is long
    /// enough that you cannot do it between glances at the north door.
    ///
    /// Targets rotate on the hour whether or not you filed the last one, so a missed
    /// reading is a missed payment rather than a permanent block — the survey is
    /// optional pressure, not a checklist you fail.
    /// </summary>
    public sealed class SurveySystem
    {
        private readonly FacilityGraph _graph;
        private readonly List<NodeId> _candidates = new List<NodeId>(16);
        private readonly HashSet<NodeId> _filed = new HashSet<NodeId>();

        private RandomSource _rng = new RandomSource(1337);
        private NodeId _target = NodeId.None;
        private float _dwell;
        private int _lastHour = -1;

        /// <summary>Seconds a camera must hold on the target before the reading files.</summary>
        public const float DwellSeconds = 4.5f;

        /// <summary>Litres released per filed reading.</summary>
        public const float FuelPerReading = 14f;

        /// <summary>The room the survey currently wants. None when there is nothing to file.</summary>
        public NodeId Target => _target;

        /// <summary>Display name of the target room, or empty.</summary>
        public string TargetName
        {
            get
            {
                var node = _graph.Node(_target);
                return node != null ? node.DisplayName : "";
            }
        }

        /// <summary>0..1 progress on the current reading.</summary>
        public float Progress01 => Mathf.Clamp01(_dwell / DwellSeconds);

        /// <summary>True while the monitor is actually on the target and counting.</summary>
        public bool IsRecording { get; private set; }

        /// <summary>Readings filed tonight.</summary>
        public int Filed => _filed.Count;

        /// <summary>How many rooms the survey will ask about over a whole night.</summary>
        public int TargetCount { get; private set; }

        /// <summary>Litres the survey has released tonight.</summary>
        public float FuelReleased { get; private set; }

        /// <summary>Fires when a reading files, with the node and the litres released.</summary>
        public event Action<NodeId, float> ReadingFiled;

        /// <summary>Fires when the survey moves to a new room.</summary>
        public event Action<NodeId> TargetChanged;

        public SurveySystem(FacilityGraph graph)
        {
            _graph = graph;
        }

        /// <summary>
        /// Picks the pool of rooms the survey may ask about.
        ///
        /// Everything with a camera, except the station and its four approaches. A
        /// survey target you can file while already watching the door you need to watch
        /// is not a decision, and the approaches are the only rooms that qualify.
        /// </summary>
        public void ConfigureSite(SiteWiring wiring)
        {
            _candidates.Clear();

            var excluded = new HashSet<NodeId>
            {
                _graph.StationNode,
                new NodeId(wiring.northApproach),
                new NodeId(wiring.southApproach),
                new NodeId(wiring.sump),
                new NodeId(wiring.chase)
            };

            foreach (var node in _graph.Nodes)
            {
                if (!node.HasCamera) continue;
                if (excluded.Contains(node.Id)) continue;
                _candidates.Add(node.Id);
            }

            // Deterministic order, so a seeded night asks for the same rooms.
            _candidates.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        }

        public void ResetForNight(RandomSource nightRng, int targetCount)
        {
            _rng = nightRng ?? new RandomSource(1337);
            TargetCount = Mathf.Max(0, targetCount);

            _filed.Clear();
            _dwell = 0f;
            _lastHour = -1;
            FuelReleased = 0f;
            IsRecording = false;

            _target = NodeId.None;
        }

        /// <summary>
        /// Advances the survey.
        ///
        /// <paramref name="watching"/> is the node the monitor is currently showing, or
        /// None when the monitor is down. <paramref name="cameraCondition01"/> is that
        /// camera's health: a snowy feed files more slowly, which quietly makes camera
        /// maintenance worth doing rather than something you only notice when a feed
        /// dies completely.
        /// </summary>
        public void Tick(float realDelta, int hour, NodeId watching, float cameraCondition01,
            Generator generator)
        {
            if (TargetCount <= 0 || _candidates.Count == 0) return;

            // A new target on the hour, and one to open with.
            if (hour != _lastHour)
            {
                _lastHour = hour;
                if (_filed.Count < TargetCount) PickTarget();
            }

            if (!_target.IsValid)
            {
                IsRecording = false;
                return;
            }

            bool onTarget = watching.Equals(_target);
            IsRecording = onTarget;

            if (!onTarget)
            {
                // Decays rather than resets. A glance away to check a door should cost
                // you something, not everything — the alternative punishes exactly the
                // behaviour the rest of the game is trying to teach.
                _dwell = Mathf.Max(0f, _dwell - realDelta * 0.6f);
                return;
            }

            _dwell += realDelta * Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(cameraCondition01));
            if (_dwell < DwellSeconds) return;

            File(generator);
        }

        private void File(Generator generator)
        {
            _filed.Add(_target);
            _dwell = 0f;

            // What actually went in, not what was offered: a nearly full day tank
            // takes the overflow onto the floor, and the HUD should say so.
            float litres = generator != null ? generator.AddFuel(FuelPerReading) : FuelPerReading;
            FuelReleased += litres;

            GLog.Info(LogChannel.Facility,
                $"Survey reading filed for {TargetName}: {litres:0} L released " +
                $"({_filed.Count}/{TargetCount}).");

            ReadingFiled?.Invoke(_target, litres);

            // Straight on to the next one if the night has more to ask for. Waiting for
            // the hour would leave the player with nothing to do with the monitor for
            // up to a minute, which is the dead time this system exists to remove.
            if (_filed.Count < TargetCount) PickTarget();
            else
            {
                _target = NodeId.None;
                TargetChanged?.Invoke(_target);
                GLog.Info(LogChannel.Facility, "Survey complete for the night.");
            }
        }

        private void PickTarget()
        {
            // Prefer somewhere not yet filed; fall back to anywhere if the site is
            // small enough that the survey has already been everywhere.
            var pool = new List<NodeId>(_candidates.Count);
            for (int i = 0; i < _candidates.Count; i++)
                if (!_filed.Contains(_candidates[i])) pool.Add(_candidates[i]);

            if (pool.Count == 0) pool.AddRange(_candidates);

            var picked = pool[_rng.Range(0, pool.Count)];
            if (picked.Equals(_target) && pool.Count > 1)
                picked = pool[(pool.IndexOf(picked) + 1) % pool.Count];

            _target = picked;
            _dwell = 0f;

            TargetChanged?.Invoke(_target);
        }

        /// <summary>Files the current reading immediately. Used by <c>survey.file</c>.</summary>
        public void DebugFile(Generator generator)
        {
            if (_target.IsValid) File(generator);
        }

        /// <summary>Forces the survey onto a room. Used by <c>survey.target</c>.</summary>
        public void DebugSetTarget(NodeId node)
        {
            _target = node;
            _dwell = 0f;
            TargetChanged?.Invoke(_target);
        }
    }
}
