using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI
{
    /// <summary>
    /// Runs the cast: who is awake tonight, at what level, in what order, and how hard
    /// the night is allowed to lean on the player at any given moment.
    ///
    /// Two jobs that individual behaviours cannot do for themselves:
    ///
    /// <b>Pacing.</b> A night that opens at full aggression is not frightening, it is
    /// just short. The director holds the whole cast back for the first stretch and
    /// ramps toward dawn, so the shape of a night is authored rather than emergent.
    ///
    /// <b>Traffic control.</b> Five characters independently deciding to attack at once
    /// is not five times as scary; it is unreadable, and it feels unfair because it
    /// *is* unfair. Attacks are serialised through a single claim, and early in the
    /// night a character already at a threshold suppresses the others.
    ///
    /// It also ticks every controller itself rather than letting Unity do it, so the
    /// cast updates in a deterministic order and a seeded night replays exactly.
    /// </summary>
    [DefaultExecutionOrder(-700)]
    [DisallowMultipleComponent]
    public sealed class AIDirector : MonoBehaviour
    {
        [Header("Cast")]
        [Tooltip("Leave empty to collect every AnimatronicController under this object.")]
        [SerializeField] private List<AnimatronicController> cast = new List<AnimatronicController>();

        [Header("Pacing")]
        [Tooltip("Fraction of the night that is a grace period before pressure starts climbing.")]
        [Range(0f, 0.5f)] [SerializeField] private float gracePeriod01 = 0.12f;

        [Tooltip("Global pressure multiplier during the grace period.")]
        [Range(0.1f, 1f)] [SerializeField] private float gracePressure = 0.5f;

        [Tooltip("Global pressure multiplier at 6 AM.")]
        [Range(1f, 2.5f)] [SerializeField] private float dawnPressure = 1.35f;

        [Tooltip("Pressure applied to everyone else while one character is already at a threshold.")]
        [Range(0.1f, 1f)] [SerializeField] private float crowdingPenalty = 0.55f;

        [Tooltip("Past this point in the night the crowding penalty stops applying. Six AM should be chaos.")]
        [Range(0.5f, 1f)] [SerializeField] private float crowdingReliefAt01 = 0.8f;

        private readonly List<NodeId> _pathScratch = new List<NodeId>(24);
        private readonly Dictionary<string, AnimatronicController> _byId =
            new Dictionary<string, AnimatronicController>(8);

        private FacilityRuntime _facility;
        private NightController _night;
        private AnimatronicController _attackClaim;

        /// <summary>Shared scratch list so per-frame pathfinding does not allocate.</summary>
        public List<NodeId> PathScratch => _pathScratch;

        public IReadOnlyList<AnimatronicController> Cast => cast;

        /// <summary>The character currently allowed to kill, if any.</summary>
        public AnimatronicController AttackClaim => _attackClaim;

        /// <summary>Current global pacing multiplier, before per-character crowding.</summary>
        public float NightPressure { get; private set; } = 1f;

        private void Awake()
        {
            if (cast.Count == 0)
                GetComponentsInChildren(includeInactive: true, result: cast);

            ServiceLocator.Register(this);
        }

        private void OnDestroy()
        {
            if (_night != null)
            {
                _night.NightBegun -= OnNightBegun;
                _night.Clock.HourChanged -= OnHourChanged;
            }
            ServiceLocator.Unregister(this);
        }

        private void Start()
        {
            if (!ServiceLocator.TryGet(out FacilityRuntime facility))
            {
                GLog.Error(LogChannel.AI, "AIDirector found no FacilityRuntime; the cast cannot run.");
                enabled = false;
                return;
            }
            _facility = facility;

            if (ServiceLocator.TryGet(out NightController night))
            {
                _night = night;
                _night.NightBegun += OnNightBegun;
                _night.Clock.HourChanged += OnHourChanged;

                if (_night.CurrentDefinition != null) OnNightBegun(_night.CurrentDefinition);
            }
            else
            {
                GLog.Warn(LogChannel.AI, "No NightController; the cast will run at a flat level 5.");
                InitialiseCast(null, new RandomSource(1337));
            }
        }

        private void OnNightBegun(NightDefinition definition)
        {
            InitialiseCast(definition, _night != null ? _night.Rng : new RandomSource(1337));
        }

        private void InitialiseCast(NightDefinition definition, RandomSource nightRng)
        {
            _byId.Clear();
            _attackClaim = null;

            for (int i = 0; i < cast.Count; i++)
            {
                var controller = cast[i];
                if (controller == null || controller.Definition == null) continue;

                string id = controller.Definition.id;
                int level = definition != null ? definition.GetAiLevel(id, GameClock.StartHour) : 5;

                // Each character gets its own deterministic sub-stream, so changing one
                // character's level does not reshuffle everybody else's rolls.
                var rng = nightRng.Fork(i * 101 + 7);

                controller.gameObject.SetActive(true);
                controller.Initialise(this, _facility, rng, level);
                _byId[id] = controller;

                GLog.Info(LogChannel.AI, $"{controller.DisplayName} at AI level {level}.");
            }
        }

        private void OnHourChanged(int hour)
        {
            if (_night?.CurrentDefinition == null) return;

            foreach (var pair in _byId)
            {
                int level = _night.CurrentDefinition.GetAiLevel(pair.Key, hour);
                if (level == pair.Value.AiLevel) continue;

                GLog.Info(LogChannel.AI, $"{pair.Value.DisplayName} ramps to AI level {level} at {MathUtil.FormatHour(hour)}.");
                pair.Value.SetLevel(level);
            }
        }

        private void Update()
        {
            if (_facility == null) return;

            float dt = Time.deltaTime;
            NightPressure = ComputeNightPressure();

            // Fixed iteration order — the cast list order — keeps seeded nights reproducible.
            for (int i = 0; i < cast.Count; i++)
            {
                var controller = cast[i];
                if (controller == null || !controller.isActiveAndEnabled) continue;
                controller.Tick(dt);
            }

            // A claim only survives while its owner is still committed to the attack.
            if (_attackClaim != null &&
                _attackClaim.State != AnimatronicState.Attack &&
                _attackClaim.State != AnimatronicState.Threshold)
            {
                _attackClaim = null;
            }
        }

        private float ComputeNightPressure()
        {
            float progress = _night != null ? _night.Clock.NightProgress01 : 0.5f;

            if (progress <= gracePeriod01) return gracePressure;

            float t = MathUtil.Remap01(progress, gracePeriod01, 1f);
            return Mathf.Lerp(gracePressure, dawnPressure, MathUtil.SmoothStep01(t));
        }

        /// <summary>
        /// Pacing multiplier for one character's next roll. Combines the night ramp with
        /// the anti-crowding rule.
        /// </summary>
        public float GlobalPressure(AnimatronicController asking)
        {
            float pressure = NightPressure;

            float progress = _night != null ? _night.Clock.NightProgress01 : 0.5f;
            if (progress >= crowdingReliefAt01) return pressure;

            for (int i = 0; i < cast.Count; i++)
            {
                var other = cast[i];
                if (other == null || other == asking) continue;
                if (other.State != AnimatronicState.Threshold && other.State != AnimatronicState.Attack) continue;

                pressure *= crowdingPenalty;
                break;
            }

            return pressure;
        }

        /// <summary>
        /// Serialises kills. The first character to ask gets the claim and keeps it
        /// until it backs off; everyone else waits at their threshold.
        /// </summary>
        public bool TryClaimAttack(AnimatronicController controller)
        {
            if (_attackClaim == null || _attackClaim == controller)
            {
                _attackClaim = controller;
                return true;
            }
            return false;
        }

        public void ReleaseAttack(AnimatronicController controller)
        {
            if (_attackClaim == controller) _attackClaim = null;
        }

        // ---------------------------------------------------------------------
        // Lookups used by tooling and UI
        // ---------------------------------------------------------------------

        public AnimatronicController Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return _byId.TryGetValue(id.Trim().ToLowerInvariant(), out var controller) ? controller : null;
        }

        public IEnumerable<string> ActiveIds => _byId.Keys;

        /// <summary>Characters currently in a node, for the map widget.</summary>
        public void CollectAt(NodeId node, List<AnimatronicController> results)
        {
            results.Clear();
            for (int i = 0; i < cast.Count; i++)
            {
                var controller = cast[i];
                if (controller == null || controller.AiLevel <= 0) continue;
                if (controller.CurrentNode == node) results.Add(controller);
            }
        }

        /// <summary>True when anyone at all is at one of the station's thresholds.</summary>
        public bool AnyoneAtThreshold
        {
            get
            {
                for (int i = 0; i < cast.Count; i++)
                    if (cast[i] != null && cast[i].State == AnimatronicState.Threshold) return true;
                return false;
            }
        }
    }
}
