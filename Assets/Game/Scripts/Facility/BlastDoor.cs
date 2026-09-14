using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// A pneumatic blast door on one of the station's adits.
    ///
    /// Three costs, all paid at once: the motor draws hard while it travels, the hold
    /// magnets draw continuously while it is shut, and the whole cavern hears it.
    /// Closing a door to hide from Barty is also ringing a dinner bell for him.
    ///
    /// A door in motion does not block — the cycle takes about a second, and beating
    /// that second is the skill the game is actually testing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlastDoor : MonoBehaviour, IPowerConsumer, IFacilityBarrier
    {
        public enum DoorState { Open, Closing, Closed, Opening, Buckled }

        [Header("Identity")]
        [Tooltip("Must match the barrierId on the corresponding link in the layout.")]
        [SerializeField] private string barrierId = FacilityBarriers.DoorNorth;

        [Tooltip("Node the door's noise is made in — the corridor side, not the station.")]
        [SerializeField] private string noiseNodeId = "ADIT_N";

        [SerializeField] private string displayName = "North blast door";

        [Header("Geometry")]
        [Tooltip("The moving panel. Slides between its authored position and that plus the closed offset.")]
        [SerializeField] private Transform panel;

        [SerializeField] private Vector3 closedLocalOffset = new Vector3(0f, -3.1f, 0f);

        [Header("Behaviour")]
        [Tooltip("Doors start the night open.")]
        [SerializeField] private bool startClosed;

        private FacilityTuning _tuning;
        private NoiseField _noise;
        private NodeId _noiseNode;
        private Vector3 _openLocalPosition;
        private float _cycleTimer;
        private float _pressure;
        private bool _powered = true;

        public DoorState State { get; private set; } = DoorState.Open;

        /// <summary>0 fully open, 1 fully closed. Drives the panel and the audio.</summary>
        public float Closed01 { get; private set; }

        /// <summary>Structural load on the door, 0..1 of capacity. Only Chorus generates this.</summary>
        public float Pressure01 => _tuning == null || _tuning.doorPressureCapacity <= 0f
            ? 0f
            : Mathf.Clamp01(_pressure / _tuning.doorPressureCapacity);

        public bool IsMoving => State == DoorState.Closing || State == DoorState.Opening;

        /// <summary>The door is commanded shut (or getting there). Used by the UI, not by the AI.</summary>
        public bool IsCommandedClosed => State == DoorState.Closed || State == DoorState.Closing;

        public event Action<DoorState> StateChanged;

        // ---- IFacilityBarrier -----------------------------------------------

        public string BarrierId => barrierId;

        /// <summary>Only a fully seated door stops a body. Buckled doors never will again.</summary>
        public bool IsBlocking => State == DoorState.Closed;

        public void ApplyPressure(float amount)
        {
            if (State == DoorState.Buckled || amount <= 0f) return;

            _pressure += amount;
            if (_pressure < _tuning.doorPressureCapacity) return;

            State = DoorState.Buckled;
            Closed01 = 0f;
            GLog.Warn(LogChannel.Facility, $"{displayName} buckled under load.");
            EventBus.Publish(new AlertSignal($"{displayName.ToUpperInvariant()} HAS FAILED.", AlertSeverity.Critical));
            EventBus.Publish(new ScareSignal(0.8f, "door-buckle"));
            EmitNoise(1f, NoiseKind.Impact);
            StateChanged?.Invoke(State);
        }

        // ---- Unity ----------------------------------------------------------

        /// <summary>
        /// Wires this door from code. Used by the runtime fixture spawner, which builds
        /// the control room for whichever site is being played — the scene cannot bake
        /// a door whose position depends on a map chosen at the menu.
        /// </summary>
        public void Configure(string barrier, string noiseNode, string label,
            Transform panelTransform, Vector3 closedOffset, bool startClosed)
        {
            barrierId = barrier;
            noiseNodeId = noiseNode;
            displayName = label;
            panel = panelTransform;
            closedLocalOffset = closedOffset;
            this.startClosed = startClosed;
        }

        private void Awake()
        {
            _openLocalPosition = panel != null ? panel.localPosition : Vector3.zero;
            _noiseNode = new NodeId(noiseNodeId);
        }

        private void Start()
        {
            var runtime = FacilityRuntime.Instance;
            if (runtime == null)
            {
                GLog.Error(LogChannel.Facility, $"{name}: no FacilityRuntime in the scene; the door will be inert.");
                enabled = false;
                return;
            }

            _tuning = runtime.Tuning;
            _noise = runtime.Noise;

            runtime.RegisterBarrier(this);
            runtime.Power.Register(this);

            if (startClosed)
            {
                State = DoorState.Closed;
                Closed01 = 1f;
            }
            ApplyPanelTransform();
        }

        private void OnDestroy()
        {
            var runtime = FacilityRuntime.Instance;
            if (runtime == null) return;
            runtime.UnregisterBarrier(this);
            runtime.Power.Unregister(this);
        }

        private void Update()
        {
            float dt = Time.deltaTime;

            TickCycle(dt);
            BleedPressure(dt);
            ApplyPanelTransform();
        }

        private void TickCycle(float dt)
        {
            if (!IsMoving) return;

            float duration = Mathf.Max(0.05f, _tuning.doorCycleSeconds);
            _cycleTimer += dt;
            float t = Mathf.Clamp01(_cycleTimer / duration);

            Closed01 = State == DoorState.Closing
                ? MathUtil.SmoothStep01(t)
                : 1f - MathUtil.SmoothStep01(t);

            if (t < 1f) return;

            State = State == DoorState.Closing ? DoorState.Closed : DoorState.Open;
            Closed01 = State == DoorState.Closed ? 1f : 0f;
            _cycleTimer = 0f;

            EmitNoise(_tuning.doorNoise, NoiseKind.Door);
            StateChanged?.Invoke(State);
        }

        private void BleedPressure(float dt)
        {
            if (_pressure <= 0f || _tuning == null) return;
            float hourDelta = FacilityRuntime.Instance != null ? FacilityRuntime.Instance.LastHourDelta : 0f;
            _pressure = Mathf.Max(0f, _pressure - _tuning.doorPressureReliefPerHour * hourDelta);
            // Real-time relief too, so a door recovers even with the clock paused.
            _pressure = Mathf.Max(0f, _pressure - dt * 2f);
        }

        private void ApplyPanelTransform()
        {
            if (panel == null) return;
            panel.localPosition = _openLocalPosition + closedLocalOffset * Closed01;
        }

        private void EmitNoise(float loudness, NoiseKind kind)
        {
            _noise?.Emit(_noiseNode, loudness, kind);
        }

        // ---- Commands --------------------------------------------------------

        /// <summary>Starts closing. Returns false if the door cannot move right now.</summary>
        public bool Close()
        {
            if (State == DoorState.Buckled || State == DoorState.Closed || State == DoorState.Closing) return false;
            if (!_powered)
            {
                EventBus.Publish(new AlertSignal($"{displayName}: no power to the motor.", AlertSeverity.Warning));
                return false;
            }

            State = DoorState.Closing;
            _cycleTimer = 0f;
            EmitNoise(_tuning.doorNoise * 0.8f, NoiseKind.Door);
            StateChanged?.Invoke(State);
            return true;
        }

        /// <summary>Starts opening.</summary>
        public bool Open()
        {
            if (State == DoorState.Buckled || State == DoorState.Open || State == DoorState.Opening) return false;

            State = DoorState.Opening;
            _cycleTimer = 0f;
            StateChanged?.Invoke(State);
            return true;
        }

        public void Toggle()
        {
            if (IsCommandedClosed) Open();
            else Close();
        }

        /// <summary>Puts the door back in service. Dev tooling only — the game never does this.</summary>
        public void DebugRepair()
        {
            _pressure = 0f;
            State = DoorState.Open;
            Closed01 = 0f;
            StateChanged?.Invoke(State);
        }

        // ---- IPowerConsumer --------------------------------------------------

        public string PowerLabel => displayName;

        public float LoadKilowatts
        {
            get
            {
                if (_tuning == null) return 0f;
                float load = 0f;
                if (State == DoorState.Closed) load += _tuning.doorHoldKilowatts;
                if (IsMoving) load += _tuning.doorMotorKilowatts;
                return load;
            }
        }

        /// <summary>
        /// Deliberately non-essential: when the breaker opens, the hold magnets let go
        /// and both doors release. That is the moment the night turns.
        /// </summary>
        public bool IsEssential => false;

        public void SetPowered(bool powered)
        {
            if (_powered == powered) return;
            _powered = powered;

            if (powered || State == DoorState.Buckled) return;

            // Losing the magnets drops a shut door open.
            if (State == DoorState.Closed || State == DoorState.Closing)
            {
                State = DoorState.Opening;
                _cycleTimer = 0f;
                GLog.Info(LogChannel.Facility, $"{displayName} released on loss of supply.");
                StateChanged?.Invoke(State);
            }
        }
    }
}
