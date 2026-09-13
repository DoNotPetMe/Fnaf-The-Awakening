using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// Every simulation constant in the facility, in one asset.
    ///
    /// Rates are expressed **per in-game hour**, not per real second. A night is six
    /// in-game hours however long that takes in wall-clock time, so making the night
    /// longer or shorter in <see cref="NightDefinition.secondsPerHour"/> changes only
    /// pacing, never balance. <see cref="FacilityRuntime"/> does the conversion once
    /// and hands every system the same hour-delta.
    /// </summary>
    [CreateAssetMenu(menuName = "Grotto/Facility Tuning", fileName = "FacilityTuning")]
    public sealed class FacilityTuning : ScriptableObject
    {
        // =====================================================================
        [Header("Generator")]
        // =====================================================================

        [Tooltip("Continuous rating. Draw more than this by the overload factor and the breaker starts counting.")]
        public float generatorRatedKilowatts = 8f;

        [Tooltip("Multiple of the rating tolerated before the overload timer runs.")]
        [Range(1f, 2f)] public float overloadFactor = 1.15f;

        [Tooltip("Seconds of sustained overload before the breaker trips.")]
        public float overloadGraceSeconds = 4f;

        [Tooltip("Day tank capacity in litres.")]
        public float fuelCapacityLitres = 160f;

        [Tooltip("Litres per in-game hour with no load at all.")]
        public float idleBurnPerHour = 1.2f;

        [Tooltip("Additional litres per in-game hour at the full continuous rating.")]
        public float fullLoadBurnPerHour = 4.4f;

        [Tooltip("Litres restored by one jerry can.")]
        public float jerryCanLitres = 45f;

        [Tooltip("Seconds spent pouring a can in. You are not watching the cameras during this.")]
        public float refuelSeconds = 5.5f;

        [Tooltip("Seconds of cranking to restart a stalled genset.")]
        public float crankSeconds = 3.5f;

        [Tooltip("Noise the running generator puts into the generator bay, 0-1.")]
        [Range(0f, 1f)] public float generatorNoise = 0.55f;

        [Tooltip("Noise of the starter motor. Loud, and it carries.")]
        [Range(0f, 1f)] public float crankNoise = 0.95f;

        // =====================================================================
        [Header("Breaker and battery")]
        // =====================================================================

        [Tooltip("Seconds the reset lever must be held. The monitor is down the whole time.")]
        public float breakerResetSeconds = 4.5f;

        [Tooltip("In-game hours the emergency battery lasts under essential load.")]
        public float batteryHours = 0.35f;

        [Tooltip("In-game hours to recharge the battery from empty once the grid is back.")]
        public float batteryRechargeHours = 1.4f;

        // =====================================================================
        [Header("Ventilation")]
        // =====================================================================

        [Tooltip("Air quality lost per in-game hour with the fan off.")]
        public float airDecayPerHour = 0.62f;

        [Tooltip("Air quality gained per in-game hour on the low setting.")]
        public float airRecoveryLowPerHour = 2.1f;

        [Tooltip("Air quality gained per in-game hour on purge.")]
        public float airRecoveryPurgePerHour = 4.8f;

        public float fanLowKilowatts = 1.2f;
        public float fanPurgeKilowatts = 2.7f;

        [Range(0f, 1f)] public float fanLowNoise = 0.45f;
        [Range(0f, 1f)] public float fanPurgeNoise = 0.85f;

        [Tooltip("Below this the air starts producing hallucinations.")]
        [Range(0f, 1f)] public float hallucinationThreshold = 0.42f;

        [Tooltip("Below this you are actively suffocating.")]
        [Range(0f, 0.5f)] public float criticalAirThreshold = 0.08f;

        [Tooltip("Seconds below the critical threshold before the night ends.")]
        public float suffocationSeconds = 26f;

        // =====================================================================
        [Header("Water")]
        // =====================================================================

        [Tooltip("Level gained per in-game hour from the spring at midnight.")]
        public float inflowPerHourAtMidnight = 0.34f;

        [Tooltip("Multiplier applied to inflow by 6 AM. The spring wakes up with everything else.")]
        public float inflowRampByDawn = 2.4f;

        [Tooltip("Level removed per in-game hour by a healthy pump.")]
        public float pumpOutflowPerHour = 1.35f;

        public float pumpKilowatts = 2.4f;

        [Range(0f, 1f)] public float pumpNoise = 0.8f;

        [Tooltip("Run the pump with the basin below this and it cavitates.")]
        [Range(0f, 0.3f)] public float cavitationBelow = 0.07f;

        [Tooltip("Impeller condition lost per in-game hour of cavitation.")]
        public float cavitationDamagePerHour = 1.6f;

        [Tooltip("Worst efficiency a damaged pump falls to.")]
        [Range(0.1f, 1f)] public float pumpEfficiencyFloor = 0.4f;

        [Tooltip("Level the night starts at.")]
        [Range(0f, 1f)] public float startingWaterLevel = 0.45f;

        // =====================================================================
        [Header("Blast doors and the grate")]
        // =====================================================================

        [Tooltip("Seconds for a door to travel. It cannot be reversed mid-cycle.")]
        public float doorCycleSeconds = 1.1f;

        [Tooltip("Kilowatts per closed door — the hold magnets, not the motor.")]
        public float doorHoldKilowatts = 1.5f;

        [Tooltip("Extra kilowatts drawn during the travel itself.")]
        public float doorMotorKilowatts = 2.2f;

        [Range(0f, 1f)] public float doorNoise = 0.9f;

        [Tooltip("Pressure a door absorbs before it buckles. Only Chorus pushes this hard.")]
        public float doorPressureCapacity = 100f;

        [Tooltip("Pressure bled off per in-game hour when nothing is pushing.")]
        public float doorPressureReliefPerHour = 140f;

        public float grateLockKilowatts = 0.7f;

        // =====================================================================
        [Header("Surveillance")]
        // =====================================================================

        [Tooltip("Kilowatts drawn while the monitor is up, regardless of which camera.")]
        public float monitorKilowatts = 0.9f;

        [Tooltip("Camera condition lost per in-game hour while that camera is the live feed.")]
        public float cameraWearPerHour = 0.16f;

        [Tooltip("Condition below which a camera drops to snow.")]
        [Range(0f, 1f)] public float cameraFailureThreshold = 0.18f;

        [Tooltip("Seconds to reboot a failed camera from the station.")]
        public float cameraRebootSeconds = 6f;

        // =====================================================================
        [Header("Lighting")]
        // =====================================================================

        public float floodlightKilowatts = 0.55f;

        [Tooltip("In-game hours the headlamp cell lasts under continuous use.")]
        public float headlampHours = 0.5f;

        [Tooltip("In-game hours for the headlamp to recharge in its cradle.")]
        public float headlampRechargeHours = 1.1f;

        // =====================================================================
        [Header("Acoustics")]
        // =====================================================================

        [Tooltip("Exponential decay constant for node noise. Higher forgets faster.")]
        public float noiseDecayLambda = 0.9f;

        [Tooltip("Fraction of a node's noise that bleeds into neighbours each second.")]
        [Range(0f, 1f)] public float noiseBleedPerSecond = 0.35f;

        [Tooltip("Below this a node reads as silent on the seismograph.")]
        [Range(0f, 0.2f)] public float noiseFloor = 0.02f;

        private static FacilityTuning _cached;

        /// <summary>Loads the shared tuning asset, or an in-memory default if none exists.</summary>
        public static FacilityTuning LoadDefault()
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<FacilityTuning>("FacilityTuning");
            if (_cached == null)
            {
                GLog.Info(LogChannel.Facility, "No FacilityTuning asset; using built-in defaults.");
                _cached = CreateInstance<FacilityTuning>();
            }
            return _cached;
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _cached = null;
#endif
    }
}
