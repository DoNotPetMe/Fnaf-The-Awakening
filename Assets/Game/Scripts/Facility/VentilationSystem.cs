using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    public enum FanMode
    {
        Off,
        /// <summary>Enough to hold the air steady. Audible in the adits.</summary>
        Low,
        /// <summary>Clears bad air fast. Audible everywhere, and expensive.</summary>
        Purge
    }

    /// <summary>
    /// Air handling for a cave ninety feet down.
    ///
    /// This is the game's second axis of tension. Bad air does not kill quickly — it
    /// makes you *see things*, which means the cameras stop being trustworthy exactly
    /// when you most need them. The fix is a fan that is loud enough to bring Barty
    /// across the Grand Gallery. Every option is the wrong one; you pick which wrong.
    /// </summary>
    public sealed class VentilationSystem : IPowerConsumer
    {
        private readonly FacilityTuning _tuning;
        private FanMode _mode = FanMode.Low;
        private bool _powered = true;
        private float _suffocationTimer;

        public FanMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                var previous = _mode;
                _mode = value;
                GLog.Info(LogChannel.Facility, $"Fan {previous} -> {value}.");
                ModeChanged?.Invoke(value);
            }
        }

        /// <summary>1 is clean, 0 is unbreathable.</summary>
        public float AirQuality01 { get; private set; } = 1f;

        /// <summary>True when the fan is both commanded on and actually receiving power.</summary>
        public bool IsRunning => _powered && _mode != FanMode.Off;

        /// <summary>
        /// 0 while the air is fine, ramping to 1 as it approaches the critical
        /// threshold. Drives hallucination frequency and the screen distortion.
        /// </summary>
        public float HallucinationPressure01
        {
            get
            {
                if (AirQuality01 >= _tuning.hallucinationThreshold) return 0f;
                return MathUtil.Remap01(AirQuality01, _tuning.hallucinationThreshold, _tuning.criticalAirThreshold);
            }
        }

        public bool IsCritical => AirQuality01 <= _tuning.criticalAirThreshold;

        /// <summary>Seconds spent below the critical threshold, out of the fatal total.</summary>
        public float SuffocationProgress01 => _tuning.suffocationSeconds <= 0f
            ? 0f
            : Mathf.Clamp01(_suffocationTimer / _tuning.suffocationSeconds);

        public event Action<FanMode> ModeChanged;

        /// <summary>Raised once when the player has been in critical air too long.</summary>
        public event Action Suffocated;

        public VentilationSystem(FacilityTuning tuning)
        {
            _tuning = tuning != null ? tuning : ScriptableObject.CreateInstance<FacilityTuning>();
        }

        public void ResetForNight()
        {
            AirQuality01 = 1f;
            _suffocationTimer = 0f;
            Mode = FanMode.Low;
        }

        public void Tick(float hourDelta, float realDelta, float decayScale)
        {
            if (DebugFlags.IsDevBuild && DebugFlags.FreezeEnvironment)
                return;

            float delta;
            if (IsRunning)
            {
                float recovery = _mode == FanMode.Purge
                    ? _tuning.airRecoveryPurgePerHour
                    : _tuning.airRecoveryLowPerHour;

                // The fan still has to fight the incoming bad air, so recovery is net.
                delta = (recovery - _tuning.airDecayPerHour * decayScale) * hourDelta;
            }
            else
            {
                delta = -_tuning.airDecayPerHour * decayScale * hourDelta;
            }

            AirQuality01 = Mathf.Clamp01(AirQuality01 + delta);

            if (IsCritical)
            {
                _suffocationTimer += realDelta;
                if (_suffocationTimer >= _tuning.suffocationSeconds)
                {
                    _suffocationTimer = 0f;
                    GLog.Warn(LogChannel.Facility, "Air quality fatal.");
                    Suffocated?.Invoke();
                }
            }
            else
            {
                // Recovering from a near miss takes a moment, so repeated dips compound.
                _suffocationTimer = Mathf.Max(0f, _suffocationTimer - realDelta * 0.5f);
            }
        }

        /// <summary>Continuous noise the fan puts into the control room and the adits.</summary>
        public float Noise => !IsRunning ? 0f
            : _mode == FanMode.Purge ? _tuning.fanPurgeNoise : _tuning.fanLowNoise;

        public void Cycle()
        {
            Mode = _mode switch
            {
                FanMode.Off => FanMode.Low,
                FanMode.Low => FanMode.Purge,
                _ => FanMode.Off
            };
        }

        // ---- IPowerConsumer --------------------------------------------------

        public string PowerLabel => "Ventilation fan";

        public float LoadKilowatts => !IsRunning ? 0f
            : _mode == FanMode.Purge ? _tuning.fanPurgeKilowatts : _tuning.fanLowKilowatts;

        /// <summary>Not essential. A breaker trip stops the fan, and the air starts going.</summary>
        public bool IsEssential => false;

        public void SetPowered(bool powered)
        {
            if (_powered == powered) return;
            _powered = powered;
            if (!powered && _mode != FanMode.Off)
                EventBus.Publish(new AlertSignal("Ventilation lost power.", AlertSeverity.Warning));
        }

        public void DebugSetAir(float quality01)
        {
            AirQuality01 = Mathf.Clamp01(quality01);
            _suffocationTimer = 0f;
        }
    }
}
