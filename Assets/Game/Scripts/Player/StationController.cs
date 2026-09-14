using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Player
{
    /// <summary>
    /// Where the player looks in the control room.
    ///
    /// Named zones rather than free angles, because everything else in the game keys
    /// off them: which light the player can reach, what a scare is aimed at, and where
    /// the camera settles when the monitor comes down.
    /// </summary>
    public enum StationFocus { Desk, NorthDoor, SouthDoor, SumpGrate, CableChase }

    /// <summary>
    /// The player.
    ///
    /// They do not walk. Every verb in the game is a switch on the desk, which is what
    /// makes the resource layer the entire gameplay rather than a system wrapped
    /// around exploration. This component is the whole input surface: it reads the
    /// action map, applies the clamped head turn, and routes each key to the facility
    /// system that owns the consequence.
    ///
    /// It contains no rules of its own. Whether a door *can* close is the door's
    /// business; whether closing it was wise is the player's.
    /// </summary>
    [DefaultExecutionOrder(-600)]
    [DisallowMultipleComponent]
    public sealed class StationController : MonoBehaviour
    {
        [Header("Rig")]
        [Tooltip("Pivot the look rotation is applied to. Usually the camera's parent.")]
        [SerializeField] private Transform headPivot;

        [SerializeField] private Headlamp headlamp;

        [Header("Seated pose")]
        [Tooltip("Place the head at a seated height relative to the control room floor on start.")]
        [SerializeField] private bool applySeatedPose = true;

        [Tooltip("Eye height above the control room floor, in metres.")]
        [SerializeField] private float eyeHeight = 1.18f;

        [Tooltip("How far back from the room centre the chair sits. Positive is south.")]
        [SerializeField] private float seatOffset = 2.1f;

        [Header("Look")]
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float maxYaw = 118f;
        [SerializeField] private float maxPitch = 48f;

        [Tooltip("How quickly the head settles toward the requested angle.")]
        [SerializeField] private float lookLambda = 18f;

        [Header("Monitor pose")]
        [Tooltip("Head angles held while the monitor is up.")]
        [SerializeField] private Vector2 monitorPose = new Vector2(0f, 12f);

        [SerializeField] private float monitorRaiseLambda = 9f;

        [Header("Fixtures (found by node id when left empty)")]
        [SerializeField] private Floodlight northLight;
        [SerializeField] private Floodlight southLight;
        [SerializeField] private Floodlight chaseLight;

        private GrottoInput _input;
        private FacilityRuntime _facility;
        private NightController _night;

        // Resolved on demand rather than cached in Start.
        //
        // BlastDoor and SumpGrate register themselves with the facility in their own
        // Start, and this component runs at -600 — long before them. Caching here
        // captured nulls every time, which is why pressing a door key reported that
        // no door was wired to the station.
        private BlastDoor DoorNorth => _facility.GetBarrier(FacilityBarriers.DoorNorth) as BlastDoor;
        private BlastDoor DoorSouth => _facility.GetBarrier(FacilityBarriers.DoorSouth) as BlastDoor;
        private SumpGrate Grate => _facility.GetBarrier(FacilityBarriers.SumpGrate) as SumpGrate;

        private float _targetYaw;
        private float _targetPitch;
        private float _currentYaw;
        private float _currentPitch;
        private float _monitorBlend;

        /// <summary>Where the player is looking right now.</summary>
        public StationFocus Focus { get; private set; } = StationFocus.Desk;

        /// <summary>0 when the monitor is down, 1 when it is fully up.</summary>
        public float MonitorBlend01 => _monitorBlend;

        /// <summary>True while a multi-second action has taken the player's hands.</summary>
        public bool IsOccupied => _facility != null && _facility.Power.IsPlayerOccupied;

        public GrottoInput Input => _input;

        private void Awake()
        {
            _input = new GrottoInput();
            if (headPivot == null) headPivot = transform;
        }

        private void OnEnable() => _input.Enable();
        private void OnDisable() => _input.Disable();
        private void OnDestroy()
        {
            _input?.Dispose();
            ServiceLocator.Unregister(this);
        }

        private void Start()
        {
            if (!ServiceLocator.TryGet(out _facility))
            {
                GLog.Error(LogChannel.Player, "StationController found no FacilityRuntime.");
                enabled = false;
                return;
            }

            ServiceLocator.TryGet(out _night);
            ServiceLocator.Register(this);

            ResolveFloodlights();
            ApplySeatedPose();

            if (ServiceLocator.TryGet(out SaveSystem save))
                lookSensitivity *= Mathf.Max(0.1f, save.Data.settings.lookSensitivity);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>
        /// Puts the head at a seated height measured from the control room's actual
        /// floor, rather than trusting whatever offset the scene was built with.
        ///
        /// The floor sits below the node's centre, so a pivot placed at the node
        /// origin ends up near the ceiling of a 3.2 metre room — which is most of why
        /// the opening view was unreadable.
        /// </summary>
        private void ApplySeatedPose()
        {
            if (!applySeatedPose || headPivot == null) return;

            var station = _facility.Graph.Node(_facility.StationNode);
            if (station == null) return;

            // CaveShaper puts a node's floor at 35% of its height below the centre.
            float floorY = station.Position.y - station.Size.y * 0.35f;

            headPivot.position = new Vector3(
                station.Position.x,
                floorY + eyeHeight,
                station.Position.z - seatOffset);

            headPivot.localRotation = Quaternion.identity;

            GLog.Info(LogChannel.Player,
                $"Seated at {headPivot.position.y:0.00}m ({eyeHeight:0.00}m above a floor at {floorY:0.00}m).");
        }

        private void ResolveFloodlights()
        {
            if (northLight != null && southLight != null && chaseLight != null) return;

            var all = FindObjectsByType<Floodlight>(FindObjectsSortMode.None);
            foreach (var light in all)
            {
                string label = light.name.ToUpperInvariant();
                if (northLight == null && label.Contains("ADIT_N")) northLight = light;
                else if (southLight == null && label.Contains("ADIT_S")) southLight = light;
                else if (chaseLight == null && label.Contains("CHASE")) chaseLight = light;
            }
        }

        /// <summary>
        /// While true the player's hands and head are the menu's, not theirs.
        ///
        /// This exists rather than simply disabling the component because disabling it
        /// runs OnDisable, which tears down the input map — and the input map is what
        /// the menu reads Escape from. A paused game that cannot be unpaused with the
        /// key that paused it is a bad enough bug to be worth one bool.
        /// </summary>
        public bool ControlSuspended { get; private set; }

        public void SetControlSuspended(bool suspended) => ControlSuspended = suspended;

        private void Update()
        {
            if (ControlSuspended) return;

            float dt = Time.deltaTime;

            TickLook(dt);
            TickActions();
            TickMonitorBlend(dt);
        }

        // ---------------------------------------------------------------------
        // Look
        // ---------------------------------------------------------------------

        private void TickLook(float dt)
        {
            bool monitorUp = _facility.Surveillance.MonitorUp;

            if (!monitorUp && !IsOccupied)
            {
                var delta = _input.Look.ReadValue<Vector2>();
                _targetYaw = Mathf.Clamp(_targetYaw + delta.x * lookSensitivity, -maxYaw, maxYaw);
                _targetPitch = Mathf.Clamp(_targetPitch - delta.y * lookSensitivity, -maxPitch, maxPitch);
            }

            // With the monitor up, or while both hands are busy, the head is parked.
            float yawGoal = monitorUp || IsOccupied ? monitorPose.x : _targetYaw;
            float pitchGoal = monitorUp || IsOccupied ? monitorPose.y : _targetPitch;

            float lambda = monitorUp || IsOccupied ? monitorRaiseLambda : lookLambda;
            _currentYaw = MathUtil.ExpDecay(_currentYaw, yawGoal, lambda, dt);
            _currentPitch = MathUtil.ExpDecay(_currentPitch, pitchGoal, lambda, dt);

            headPivot.localRotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);

            Focus = ResolveFocus(_currentYaw, _currentPitch);
        }

        private StationFocus ResolveFocus(float yaw, float pitch)
        {
            if (pitch > 30f) return StationFocus.SumpGrate;
            if (pitch < -28f) return StationFocus.CableChase;
            if (yaw < -55f) return StationFocus.NorthDoor;
            if (yaw > 55f) return StationFocus.SouthDoor;
            return StationFocus.Desk;
        }

        private void TickMonitorBlend(float dt)
        {
            float target = _facility.Surveillance.MonitorUp ? 1f : 0f;
            _monitorBlend = MathUtil.ExpDecay(_monitorBlend, target, monitorRaiseLambda, dt);
        }

        // ---------------------------------------------------------------------
        // Actions
        // ---------------------------------------------------------------------

        private void TickActions()
        {
            // While refuelling or cranking, the player has a jerry can in both hands.
            // Only the breaker release is honoured.
            if (IsOccupied)
            {
                if (!_input.HoldBreakerReset.IsPressed()) _facility.Power.CancelBreakerReset();
                return;
            }

            if (_night != null && _night.CurrentPhase == NightController.Phase.Briefing
                && _input.Interact.WasPressedThisFrame())
            {
                _night.SkipBriefing();
                return;
            }

            if (_input.ToggleMonitor.WasPressedThisFrame()) _facility.Surveillance.ToggleMonitor();
            if (_input.NextCamera.WasPressedThisFrame()) _facility.Surveillance.SelectNext(1);
            if (_input.PreviousCamera.WasPressedThisFrame()) _facility.Surveillance.SelectNext(-1);

            if (_input.DoorNorth.WasPressedThisFrame()) ToggleDoor(DoorNorth, "north");
            if (_input.DoorSouth.WasPressedThisFrame()) ToggleDoor(DoorSouth, "south");

            if (_input.LightNorth.WasPressedThisFrame()) northLight?.Toggle();
            if (_input.LightSouth.WasPressedThisFrame()) southLight?.Toggle();
            if (_input.LightChase.WasPressedThisFrame()) chaseLight?.Toggle();

            if (_input.CycleFan.WasPressedThisFrame()) _facility.Ventilation.Cycle();

            if (_input.TogglePump.WasPressedThisFrame())
                _facility.Water.PumpCommanded = !_facility.Water.PumpCommanded;

            if (_input.ToggleGrate.WasPressedThisFrame()) Grate?.Toggle();

            if (_input.Headlamp.WasPressedThisFrame()) headlamp?.Toggle();

            TickBreaker();

            if (_input.Refuel.WasPressedThisFrame()) BeginRefuel();
            if (_input.Crank.WasPressedThisFrame()) BeginCrank();
        }

        private void ToggleDoor(BlastDoor door, string side)
        {
            if (door == null)
            {
                GLog.Warn(LogChannel.Player, $"No {side} blast door is wired to the station.");
                return;
            }

            // Raising the monitor to see a door you are about to close is a choice the
            // player makes; closing one blind is also a choice. Neither is blocked.
            door.Toggle();
        }

        private void TickBreaker()
        {
            var power = _facility.Power;

            if (_input.HoldBreakerReset.WasPressedThisFrame())
            {
                if (!power.BreakerOpen)
                {
                    EventBus.Publish(new AlertSignal("Breaker is already closed.", AlertSeverity.Info));
                }
                else if (power.BeginBreakerReset())
                {
                    // The monitor drops: you cannot hold the lever and watch the cameras.
                    _facility.Surveillance.SetMonitorUp(false);
                    EventBus.Publish(new AlertSignal("Holding the breaker lever...", AlertSeverity.Warning));
                }
            }

            if (_input.HoldBreakerReset.WasReleasedThisFrame() && power.IsResettingBreaker)
            {
                power.CancelBreakerReset();
                EventBus.Publish(new AlertSignal("Lever released. Reset lost.", AlertSeverity.Warning));
            }
        }

        private void BeginRefuel()
        {
            var generator = _facility.Power.Generator;

            if (generator.SpareCans <= 0)
            {
                EventBus.Publish(new AlertSignal("No spare cans.", AlertSeverity.Warning));
                return;
            }

            if (generator.BeginRefuel())
            {
                _facility.Surveillance.SetMonitorUp(false);
                EventBus.Publish(new AlertSignal(
                    $"Pouring a can. {generator.SpareCans} left.", AlertSeverity.Warning));
            }
        }

        private void BeginCrank()
        {
            var generator = _facility.Power.Generator;

            if (generator.CurrentState == Generator.State.Running)
            {
                EventBus.Publish(new AlertSignal("The set is already running.", AlertSeverity.Info));
                return;
            }

            if (generator.BeginCrank())
            {
                _facility.Surveillance.SetMonitorUp(false);
                EventBus.Publish(new AlertSignal("Cranking. Everything can hear this.", AlertSeverity.Critical));
            }
            else
            {
                EventBus.Publish(new AlertSignal("Nothing in the day tank.", AlertSeverity.Critical));
            }
        }

        /// <summary>Snaps the head to a zone. Used by scares and by the dev console.</summary>
        public void ForceLook(StationFocus focus)
        {
            switch (focus)
            {
                case StationFocus.NorthDoor: _targetYaw = -maxYaw; _targetPitch = 0f; break;
                case StationFocus.SouthDoor: _targetYaw = maxYaw; _targetPitch = 0f; break;
                case StationFocus.SumpGrate: _targetYaw = 0f; _targetPitch = maxPitch; break;
                case StationFocus.CableChase: _targetYaw = 0f; _targetPitch = -maxPitch; break;
                default: _targetYaw = 0f; _targetPitch = 0f; break;
            }
        }
    }
}
