using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// Owns and ticks every facility system, in one place, in a fixed order.
    ///
    /// The systems themselves are plain classes rather than MonoBehaviours. That is
    /// the single most useful structural decision in this layer: update order is
    /// explicit and readable instead of being an emergent property of script
    /// execution settings, the whole simulation can be stepped from a test with no
    /// scene at all, and there is exactly one place to look when asking "what happens
    /// each frame".
    /// </summary>
    [DefaultExecutionOrder(-850)]
    [DisallowMultipleComponent]
    public sealed class FacilityRuntime : MonoBehaviour
    {
        public static FacilityRuntime Instance { get; private set; }

        [Header("Data")]
        [SerializeField] private FacilityLayout layout;
        [SerializeField] private FacilityTuning tuning;

        [Header("Fallback pacing")]
        [Tooltip("Used when no NightController is present, so the facility scene still simulates on its own.")]
        [SerializeField] private float standaloneSecondsPerHour = 60f;

        private readonly Dictionary<string, IFacilityBarrier> _barriers = new Dictionary<string, IFacilityBarrier>(8);
        private readonly List<NodeId> _pathScratch = new List<NodeId>(16);

        private NightController _night;
        private float _lastClockElapsed;
        private float _airDecayScale = 1f;
        private float _waterInflowScale = 1f;
        private float _fuelBurnScale = 1f;

        public FacilityGraph Graph { get; private set; }
        public FacilityTuning Tuning => tuning;
        public FacilityLayout Layout => layout;

        public PowerGrid Power { get; private set; }
        public VentilationSystem Ventilation { get; private set; }
        public WaterSystem Water { get; private set; }
        public NoiseField Noise { get; private set; }
        public SurveillanceSystem Surveillance { get; private set; }

        /// <summary>In-game hours elapsed on the last tick. Systems that bill per hour use this.</summary>
        public float LastHourDelta { get; private set; }

        public NodeId StationNode => Graph.StationNode;

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                GLog.Warn(LogChannel.Facility, "A second FacilityRuntime was found; destroying the duplicate.");
                Destroy(this);
                return;
            }
            Instance = this;

            if (layout == null) layout = FacilityLayout.LoadDefault();
            if (tuning == null) tuning = FacilityTuning.LoadDefault();

            Graph = layout.BuildGraph();

            var problems = Graph.Validate();
            for (int i = 0; i < problems.Count; i++)
                GLog.Warn(LogChannel.Facility, $"Layout: {problems[i]}");

            Power = new PowerGrid(tuning);
            Ventilation = new VentilationSystem(tuning);
            Water = new WaterSystem(tuning);
            Noise = new NoiseField(Graph, tuning);
            Surveillance = new SurveillanceSystem(Graph, tuning);

            Power.Register(Ventilation);
            Power.Register(Water);
            Power.Register(Surveillance);

            Power.NoiseBurst += amount => Noise.Emit(NodeGenerator, amount, NoiseKind.Machinery);
            Surveillance.NoiseBurst += amount => Noise.Emit(Graph.StationNode, amount, NoiseKind.Machinery);

            Ventilation.Suffocated += () => _night?.RequestOutcome(NightOutcome.Suffocated);
            Water.Flooded += () => _night?.RequestOutcome(NightOutcome.Flooded);

            ServiceLocator.Register(this);
            GLog.Info(LogChannel.Facility, $"Facility runtime ready: {layout.siteName}.");
        }

        private void Start()
        {
            if (ServiceLocator.TryGet(out NightController night))
            {
                _night = night;
                _night.NightBegun += OnNightBegun;
                _lastClockElapsed = _night.Clock.ElapsedSeconds;

                // The scene may already be mid-night if this component was enabled late.
                if (_night.CurrentDefinition != null) OnNightBegun(_night.CurrentDefinition);
            }
            else
            {
                GLog.Info(LogChannel.Facility,
                    "No NightController found; the facility will simulate standalone at " +
                    $"{standaloneSecondsPerHour:0}s per hour.");
                ResetForNight(null);
            }
        }

        private void OnDestroy()
        {
            if (_night != null) _night.NightBegun -= OnNightBegun;
            ServiceLocator.Unregister(this);
            if (Instance == this) Instance = null;
        }

        private void OnNightBegun(NightDefinition definition) => ResetForNight(definition);

        private void ResetForNight(NightDefinition definition)
        {
            _airDecayScale = definition != null ? definition.airDecayScale : 1f;
            _waterInflowScale = definition != null ? definition.waterInflowScale : 1f;
            _fuelBurnScale = definition != null ? definition.fuelBurnScale : 1f;

            float fuel = definition != null ? definition.startingFuelLitres : tuning.fuelCapacityLitres * 0.75f;
            int cans = definition != null ? definition.spareFuelCans : 2;

            Power.ResetForNight(fuel, cans);
            Ventilation.ResetForNight();
            Water.ResetForNight();
            Surveillance.ResetForNight();
            Noise.Clear();

            _lastClockElapsed = _night != null ? _night.Clock.ElapsedSeconds : 0f;

            GLog.Info(LogChannel.Facility,
                $"Facility reset for {(definition != null ? definition.displayName : "standalone")}: " +
                $"{fuel:0} L, {cans} can(s).");
        }

        // ---------------------------------------------------------------------
        // Tick
        // ---------------------------------------------------------------------

        private void Update()
        {
            float realDelta = Time.deltaTime;
            LastHourDelta = ComputeHourDelta(realDelta);
            float nightProgress = _night != null ? _night.Clock.NightProgress01 : 0f;

            // Order matters and is deliberate:
            //  1. environment moves,
            //  2. the grid then sees the loads that movement implies,
            //  3. surveillance wears against the supply it just got,
            //  4. acoustics last, so every source this frame is already accounted for.
            Ventilation.Tick(LastHourDelta, realDelta, _airDecayScale);
            Water.Tick(LastHourDelta, realDelta, nightProgress, _waterInflowScale);
            Power.Tick(LastHourDelta, realDelta, _fuelBurnScale);
            Surveillance.Tick(LastHourDelta, realDelta);

            PublishContinuousNoise();
            Noise.Tick(realDelta);
        }

        private float ComputeHourDelta(float realDelta)
        {
            if (_night == null || !_night.IsNightRunning)
            {
                // Standalone: keep simulating so the scene is useful without a night.
                return _night == null ? realDelta / Mathf.Max(1f, standaloneSecondsPerHour) : 0f;
            }

            var clock = _night.Clock;
            float elapsed = clock.ElapsedSeconds;
            float delta = Mathf.Max(0f, elapsed - _lastClockElapsed);
            _lastClockElapsed = elapsed;
            return delta / Mathf.Max(1f, clock.SecondsPerHour);
        }

        private static readonly NodeId NodeGenerator = new NodeId("GEN");
        private static readonly NodeId NodeSump = new NodeId("SUMP");

        private void PublishContinuousNoise()
        {
            Noise.SetContinuous("generator", NodeGenerator, Power.Generator.ContinuousNoise);
            Noise.SetContinuous("fan", Graph.StationNode, Ventilation.Noise);
            Noise.SetContinuous("pump", NodeSump, Water.Noise);
        }

        // ---------------------------------------------------------------------
        // Barriers
        // ---------------------------------------------------------------------

        public void RegisterBarrier(IFacilityBarrier barrier)
        {
            if (barrier == null || string.IsNullOrEmpty(barrier.BarrierId)) return;

            if (_barriers.ContainsKey(barrier.BarrierId))
            {
                GLog.Warn(LogChannel.Facility,
                    $"Two barriers claim id '{barrier.BarrierId}'. The later one wins; check the scene.");
            }
            _barriers[barrier.BarrierId] = barrier;
        }

        public void UnregisterBarrier(IFacilityBarrier barrier)
        {
            if (barrier == null || string.IsNullOrEmpty(barrier.BarrierId)) return;
            if (_barriers.TryGetValue(barrier.BarrierId, out var existing) && ReferenceEquals(existing, barrier))
                _barriers.Remove(barrier.BarrierId);
        }

        public IFacilityBarrier GetBarrier(string barrierId)
        {
            if (string.IsNullOrEmpty(barrierId)) return null;
            return _barriers.TryGetValue(barrierId, out var barrier) ? barrier : null;
        }

        public IEnumerable<IFacilityBarrier> Barriers => _barriers.Values;

        // ---------------------------------------------------------------------
        // The traversal rule — one place, used by AI, tooling and tests alike
        // ---------------------------------------------------------------------

        /// <summary>
        /// Whether a body with <paramref name="capability"/> can use this link right now.
        ///
        /// Every "can it get here" question in the game routes through this method, so
        /// the AI, the debug graph overlay and the unit tests can never disagree about
        /// what the map currently permits.
        /// </summary>
        public bool CanTraverse(FacilityLink link, NodeId from, NodeId to,
            TraversalMask capability, bool respectsBarriers = true, bool lightAverse = false)
        {
            if (link == null) return false;
            if ((link.Allowed & capability) == 0) return false;

            float water = Water.Level01;
            if (water < link.MinWater || water > link.MaxWater) return false;

            if (lightAverse && link.LightDeters)
            {
                var target = Graph.Node(to);
                if (target != null && target.IsLit) return false;
            }

            if (respectsBarriers && !string.IsNullOrEmpty(link.BarrierId))
            {
                var barrier = GetBarrier(link.BarrierId);
                if (barrier != null && barrier.IsBlocking) return false;
            }

            return true;
        }

        /// <summary>Builds a reusable filter for one body's capabilities.</summary>
        public FacilityGraph.LinkFilter MakeFilter(TraversalMask capability,
            bool respectsBarriers = true, bool lightAverse = false)
        {
            return (link, from, to) => CanTraverse(link, from, to, capability, respectsBarriers, lightAverse);
        }

        /// <summary>Hops from a node to the station under a given capability, or -1 when no route exists.</summary>
        public int RouteLengthToStation(NodeId from, TraversalMask capability)
        {
            var filter = MakeFilter(capability);
            return Graph.TryFindPath(from, Graph.StationNode, filter, _pathScratch)
                ? _pathScratch.Count
                : -1;
        }
    }
}
