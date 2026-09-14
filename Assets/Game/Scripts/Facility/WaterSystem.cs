using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// The spring, the sump and the pump that stands between them.
    ///
    /// Water level is the spine of the whole design, because it is the only resource
    /// where *both* directions are dangerous:
    ///
    ///   Low  — the sump basin dries out and Marlow can dig through the silt.
    ///   High — the Styx Channel becomes swimmable and Echo comes up the intake.
    ///   Full — the control room floods and the night is over.
    ///
    /// There is no correct level, only the threat you have decided to accept for the
    /// next few minutes. Inflow climbs toward dawn, so a setting that held at 1 AM
    /// will not hold at 5.
    /// </summary>
    public sealed class WaterSystem : IPowerConsumer
    {
        private readonly FacilityTuning _tuning;
        private WaterGates _gates = WaterGates.Default;
        private float _startingLevel;
        private bool _pumpCommanded;
        private bool _powered = true;
        private bool _floodReported;

        /// <summary>0 drained, 1 the control room is under.</summary>
        public float Level01 { get; private set; }

        /// <summary>1 healthy, falling toward the floor as the impeller cavitates.</summary>
        public float PumpCondition01 { get; private set; } = 1f;

        public bool PumpCommanded
        {
            get => _pumpCommanded;
            set
            {
                if (_pumpCommanded == value) return;
                _pumpCommanded = value;
                GLog.Info(LogChannel.Facility, $"Sump pump {(value ? "started" : "stopped")}.");
                PumpStateChanged?.Invoke(IsPumping);
            }
        }

        public bool IsPumping => _pumpCommanded && _powered;

        /// <summary>True when the pump is running dry and grinding itself down.</summary>
        public bool IsCavitating => IsPumping && Level01 <= _tuning.cavitationBelow;

        /// <summary>Current inflow in level units per in-game hour, after the dawn ramp.</summary>
        public float CurrentInflowPerHour { get; private set; }

        /// <summary>Net rate. Negative means the pump is winning.</summary>
        public float NetRatePerHour { get; private set; }

        /// <summary>Where this site's routes open and close. Set per site.</summary>
        public WaterGates Gates => _gates;

        public bool SumpIsDry => Level01 <= _gates.diggable;
        public bool ChannelIsSwimmable => Level01 >= _gates.swimmable;

        /// <summary>True in the band where neither the dig nor the swim route exists.</summary>
        public bool InSafeBand => Level01 > _gates.wadeable && Level01 < _gates.swimmable;

        public event Action<bool> PumpStateChanged;

        /// <summary>Raised once when the control room goes under.</summary>
        public event Action Flooded;

        public WaterSystem(FacilityTuning tuning)
        {
            _tuning = tuning != null ? tuning : ScriptableObject.CreateInstance<FacilityTuning>();
            _startingLevel = _tuning.startingWaterLevel;
            Level01 = _startingLevel;
        }

        /// <summary>Applies a site's water character. Call before the first night.</summary>
        public void ConfigureSite(WaterGates gates, float startingLevel)
        {
            _gates = gates.Sanitised();
            _startingLevel = Mathf.Clamp01(startingLevel);

            GLog.Info(LogChannel.Facility,
                $"Water configured: start {_startingLevel:0.00}, gates {_gates}.");
        }

        public void ResetForNight()
        {
            Level01 = Mathf.Clamp01(_startingLevel);
            PumpCondition01 = 1f;
            _floodReported = false;
            PumpCommanded = false;
        }

        /// <summary>
        /// <paramref name="nightProgress01"/> drives the dawn ramp — the spring runs
        /// harder the closer it gets to six.
        /// </summary>
        public void Tick(float hourDelta, float realDelta, float nightProgress01, float inflowScale)
        {
            if (DebugFlags.IsDevBuild && DebugFlags.FreezeEnvironment)
                return;

            float ramp = Mathf.Lerp(1f, _tuning.inflowRampByDawn, Mathf.Clamp01(nightProgress01));
            CurrentInflowPerHour = _tuning.inflowPerHourAtMidnight * ramp * Mathf.Max(0.01f, inflowScale);

            float outflow = 0f;
            if (IsPumping)
            {
                float efficiency = Mathf.Lerp(_tuning.pumpEfficiencyFloor, 1f, PumpCondition01);
                outflow = _tuning.pumpOutflowPerHour * efficiency;

                if (IsCavitating)
                {
                    PumpCondition01 = Mathf.Max(0f,
                        PumpCondition01 - _tuning.cavitationDamagePerHour * hourDelta);

                    // Sucking air moves almost nothing, and wrecks the impeller doing it.
                    outflow *= 0.15f;
                }
            }

            NetRatePerHour = CurrentInflowPerHour - outflow;
            Level01 = Mathf.Clamp01(Level01 + NetRatePerHour * hourDelta);

            if (Level01 >= 1f && !_floodReported)
            {
                _floodReported = true;
                GLog.Warn(LogChannel.Facility, "Control room flooded.");
                Flooded?.Invoke();
            }
        }

        /// <summary>Continuous noise the pump puts into the sump basin.</summary>
        public float Noise => !IsPumping ? 0f
            : _tuning.pumpNoise * (IsCavitating ? 1.2f : 1f);

        /// <summary>Level phrased the way the station gauge shows it, in feet above datum.</summary>
        public float GaugeFeet => Level01 * 14.5f;

        // ---- IPowerConsumer --------------------------------------------------

        public string PowerLabel => "Sump pump";
        public float LoadKilowatts => IsPumping ? _tuning.pumpKilowatts : 0f;

        /// <summary>Not essential — a breaker trip stops the pump and the level starts climbing.</summary>
        public bool IsEssential => false;

        public void SetPowered(bool powered)
        {
            if (_powered == powered) return;
            _powered = powered;
            PumpStateChanged?.Invoke(IsPumping);
            if (!powered && _pumpCommanded)
                EventBus.Publish(new AlertSignal("Sump pump lost power.", AlertSeverity.Critical));
        }

        public void DebugSetLevel(float level01)
        {
            Level01 = Mathf.Clamp01(level01);
            _floodReported = Level01 >= 1f;
        }

        public void DebugRepairPump() => PumpCondition01 = 1f;
    }
}
