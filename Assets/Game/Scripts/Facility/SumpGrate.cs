using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// The steel grate over the pump intake, directly under the control room floor.
    ///
    /// Cheap to hold shut compared with a blast door — but it only answers *one* of
    /// the two things that use the sump. Marlow digs through the silt when the basin
    /// is dry and the bolts stop him. Echo comes up through the water when it is not,
    /// and a grate does very little against something that can fold itself flat.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SumpGrate : MonoBehaviour, IPowerConsumer, IFacilityBarrier
    {
        [SerializeField] private string barrierId = GrottoSpringsLayout.SumpGrate;
        [SerializeField] private string noiseNodeId = "SUMP";
        [SerializeField] private string displayName = "Sump grate bolts";

        [Tooltip("Moving geometry — the bolts, not the grate itself.")]
        [SerializeField] private Transform boltsTransform;
        [SerializeField] private Vector3 lockedLocalOffset = new Vector3(0f, 0.08f, 0f);

        [Tooltip("How much of Echo's mass the bolts actually resist. 1 would make her harmless.")]
        [Range(0f, 1f)] [SerializeField] private float swimmerResistance = 0.35f;

        private FacilityTuning _tuning;
        private NoiseField _noise;
        private WaterSystem _water;
        private NodeId _noiseNode;
        private Vector3 _unlockedLocalPosition;
        private bool _powered = true;
        private bool _locked;

        public bool IsLocked => _locked;

        /// <summary>Bolts are only actually shot when they have power.</summary>
        public bool IsEnergised => _locked && _powered;

        public event Action<bool> LockChanged;

        public string BarrierId => barrierId;

        /// <summary>
        /// Blocks a burrower outright. Against a swimmer it is a delay, not a wall —
        /// the AI reads <see cref="SwimmerPassChance"/> rather than this flag.
        /// </summary>
        public bool IsBlocking => IsEnergised;

        /// <summary>
        /// Chance per movement attempt that something swimming gets past the bolts.
        /// Rises as the basin fills, because a full basin floats the grate off its seat.
        /// </summary>
        public float SwimmerPassChance
        {
            get
            {
                if (!IsEnergised) return 1f;
                float buoyancy = _water != null ? Mathf.Clamp01(_water.Level01) : 0.5f;
                return Mathf.Clamp01((1f - swimmerResistance) + buoyancy * swimmerResistance * 0.8f);
            }
        }

        public void ApplyPressure(float amount)
        {
            // The grate does not buckle. It is bolted into bedrock; it just rattles.
            if (amount > 0f) _noise?.Emit(_noiseNode, Mathf.Clamp01(amount * 0.01f), NoiseKind.Impact);
        }

        private void Awake()
        {
            _unlockedLocalPosition = boltsTransform != null ? boltsTransform.localPosition : Vector3.zero;
            _noiseNode = new NodeId(noiseNodeId);
        }

        private void Start()
        {
            var runtime = FacilityRuntime.Instance;
            if (runtime == null)
            {
                enabled = false;
                return;
            }

            _tuning = runtime.Tuning;
            _noise = runtime.Noise;
            _water = runtime.Water;

            runtime.RegisterBarrier(this);
            runtime.Power.Register(this);
            ApplyBoltTransform();
        }

        private void OnDestroy()
        {
            var runtime = FacilityRuntime.Instance;
            if (runtime == null) return;
            runtime.UnregisterBarrier(this);
            runtime.Power.Unregister(this);
        }

        public void SetLocked(bool locked)
        {
            if (_locked == locked) return;
            _locked = locked;

            _noise?.Emit(_noiseNode, 0.5f, NoiseKind.Door);
            ApplyBoltTransform();
            LockChanged?.Invoke(locked);
            GLog.Info(LogChannel.Facility, $"{displayName} {(locked ? "shot" : "withdrawn")}.");
        }

        public void Toggle() => SetLocked(!_locked);

        private void ApplyBoltTransform()
        {
            if (boltsTransform == null) return;
            boltsTransform.localPosition = _unlockedLocalPosition + lockedLocalOffset * (IsEnergised ? 1f : 0f);
        }

        public string PowerLabel => displayName;
        public float LoadKilowatts => _locked && _tuning != null ? _tuning.grateLockKilowatts : 0f;
        public bool IsEssential => false;

        public void SetPowered(bool powered)
        {
            if (_powered == powered) return;
            _powered = powered;
            ApplyBoltTransform();
            if (!powered && _locked)
                EventBus.Publish(new AlertSignal("Grate bolts lost power.", AlertSeverity.Warning));
        }
    }
}
