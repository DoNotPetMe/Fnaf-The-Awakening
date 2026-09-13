using System;
using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// Distribution: who is drawing, whether the supply can carry it, and what
    /// survives when it cannot.
    ///
    /// The breaker is the interesting part. Exceed the continuous rating and a timer
    /// starts; let it run out and the breaker opens, which drops **the blast doors**
    /// along with everything else non-essential. Resetting it means holding a lever
    /// for four and a half seconds with the monitor down. So "just close both doors
    /// and run the pump" is a plan that works right up until it catastrophically
    /// does not, which is the whole point.
    /// </summary>
    public sealed class PowerGrid
    {
        private struct Entry
        {
            public IPowerConsumer Consumer;
            public bool Powered;
        }

        private readonly FacilityTuning _tuning;
        private readonly List<Entry> _entries = new List<Entry>(16);

        private float _overloadTimer;
        private float _resetTimer;
        private bool _breakerOpen;

        public Generator Generator { get; }

        public PowerState State { get; private set; } = PowerState.Online;

        /// <summary>Sum of every consumer's draw, whether or not the supply can meet it.</summary>
        public float TotalLoadKilowatts { get; private set; }

        /// <summary>Portion of the load that would survive on battery.</summary>
        public float EssentialLoadKilowatts { get; private set; }

        /// <summary>Load as a fraction of the generator's continuous rating. Can exceed 1.</summary>
        public float LoadFraction => _tuning.generatorRatedKilowatts <= 0f
            ? 0f
            : TotalLoadKilowatts / _tuning.generatorRatedKilowatts;

        public float BatteryCharge01 { get; private set; } = 1f;

        public bool BreakerOpen => _breakerOpen;

        /// <summary>0..1 toward the trip. Shown as an amber bar before anything goes wrong.</summary>
        public float OverloadProgress01 => _tuning.overloadGraceSeconds <= 0f
            ? 0f
            : Mathf.Clamp01(_overloadTimer / _tuning.overloadGraceSeconds);

        public bool IsResettingBreaker { get; private set; }

        public float ResetProgress01 => _tuning.breakerResetSeconds <= 0f
            ? 0f
            : Mathf.Clamp01(_resetTimer / _tuning.breakerResetSeconds);

        /// <summary>True while the player is occupied at the breaker or the genset.</summary>
        public bool IsPlayerOccupied => IsResettingBreaker || Generator.IsBusy;

        public event Action<PowerState, PowerState> StateChanged;

        /// <summary>Raised with the reason when the breaker opens.</summary>
        public event Action<string> BreakerTripped;

        /// <summary>Raised with a 0..1 loudness for one-off electrical noises.</summary>
        public event Action<float> NoiseBurst;

        public PowerGrid(FacilityTuning tuning)
        {
            _tuning = tuning != null ? tuning : ScriptableObject.CreateInstance<FacilityTuning>();
            Generator = new Generator(_tuning);
            Generator.NoiseBurst += n => NoiseBurst?.Invoke(n);
        }

        // ---------------------------------------------------------------------
        // Registration
        // ---------------------------------------------------------------------

        public void Register(IPowerConsumer consumer)
        {
            if (consumer == null) return;
            for (int i = 0; i < _entries.Count; i++)
                if (ReferenceEquals(_entries[i].Consumer, consumer)) return;

            _entries.Add(new Entry { Consumer = consumer, Powered = State != PowerState.Blackout });
            consumer.SetPowered(_entries[_entries.Count - 1].Powered);
        }

        public void Unregister(IPowerConsumer consumer)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_entries[i].Consumer, consumer)) _entries.RemoveAt(i);
        }

        /// <summary>Per-consumer draw, for the station's load readout and the debug overlay.</summary>
        public void SnapshotLoads(List<(string label, float kilowatts, bool powered)> results)
        {
            results.Clear();
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                results.Add((e.Consumer.PowerLabel, e.Consumer.LoadKilowatts, e.Powered));
            }
        }

        public void ResetForNight(float startingFuelLitres, int spareCans)
        {
            Generator.ResetForNight(startingFuelLitres, spareCans);
            BatteryCharge01 = 1f;
            _breakerOpen = false;
            _overloadTimer = 0f;
            _resetTimer = 0f;
            IsResettingBreaker = false;
            SetState(PowerState.Online);
        }

        // ---------------------------------------------------------------------
        // Simulation
        // ---------------------------------------------------------------------

        public void Tick(float hourDelta, float realDelta, float fuelBurnScale)
        {
            AccumulateLoads();

            Generator.Tick(hourDelta, realDelta, _breakerOpen ? 0f : TotalLoadKilowatts, fuelBurnScale);

            TickBreakerReset(realDelta);
            TickOverload(realDelta);
            TickBattery(hourDelta);

            var next = ResolveState();
            if (next != State) SetState(next);

            PushPowerToConsumers();
        }

        private void AccumulateLoads()
        {
            TotalLoadKilowatts = 0f;
            EssentialLoadKilowatts = 0f;

            for (int i = 0; i < _entries.Count; i++)
            {
                var consumer = _entries[i].Consumer;
                float kw = Mathf.Max(0f, consumer.LoadKilowatts);
                TotalLoadKilowatts += kw;
                if (consumer.IsEssential) EssentialLoadKilowatts += kw;
            }
        }

        private void TickOverload(float realDelta)
        {
            if (_breakerOpen || !Generator.IsSupplying)
            {
                _overloadTimer = 0f;
                return;
            }

            float ceiling = _tuning.generatorRatedKilowatts * _tuning.overloadFactor;
            if (TotalLoadKilowatts > ceiling)
            {
                _overloadTimer += realDelta;
                if (_overloadTimer >= _tuning.overloadGraceSeconds)
                    TripBreaker($"Overload: {TotalLoadKilowatts:0.0} kW on an {_tuning.generatorRatedKilowatts:0.0} kW set");
            }
            else
            {
                // Bleed the timer back down rather than snapping it to zero, so riding
                // the limit repeatedly still eventually costs you.
                _overloadTimer = Mathf.Max(0f, _overloadTimer - realDelta * 0.65f);
            }
        }

        private void TickBreakerReset(float realDelta)
        {
            if (!IsResettingBreaker) return;

            _resetTimer += realDelta;
            if (_resetTimer < _tuning.breakerResetSeconds) return;

            IsResettingBreaker = false;
            _resetTimer = 0f;
            _breakerOpen = false;
            _overloadTimer = 0f;

            NoiseBurst?.Invoke(0.7f);
            GLog.Info(LogChannel.Facility, "Breaker reset.");
            EventBus.Publish(new AlertSignal("Main breaker reset.", AlertSeverity.Info));
        }

        private void TickBattery(float hourDelta)
        {
            bool onBattery = _breakerOpen || !Generator.IsSupplying;

            if (DebugFlags.IsDevBuild && DebugFlags.InfinitePower)
            {
                BatteryCharge01 = 1f;
                return;
            }

            if (onBattery)
            {
                if (_tuning.batteryHours > 0f)
                    BatteryCharge01 = Mathf.Max(0f, BatteryCharge01 - hourDelta / _tuning.batteryHours);
            }
            else if (_tuning.batteryRechargeHours > 0f)
            {
                BatteryCharge01 = Mathf.Min(1f, BatteryCharge01 + hourDelta / _tuning.batteryRechargeHours);
            }
        }

        private PowerState ResolveState()
        {
            if (Generator.IsSupplying && !_breakerOpen)
            {
                float ceiling = _tuning.generatorRatedKilowatts * _tuning.overloadFactor;
                return TotalLoadKilowatts > ceiling ? PowerState.Overloaded : PowerState.Online;
            }

            return BatteryCharge01 > 0f ? PowerState.Tripped : PowerState.Blackout;
        }

        private void PushPowerToConsumers()
        {
            bool mainsLive = State == PowerState.Online || State == PowerState.Overloaded;
            bool batteryLive = State == PowerState.Tripped;

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                bool shouldBePowered = mainsLive || (batteryLive && entry.Consumer.IsEssential);

                if (shouldBePowered == entry.Powered) continue;

                entry.Powered = shouldBePowered;
                _entries[i] = entry;
                entry.Consumer.SetPowered(shouldBePowered);
            }
        }

        private void SetState(PowerState next)
        {
            var previous = State;
            State = next;

            GLog.Info(LogChannel.Facility, $"Power state {previous} -> {next} ({TotalLoadKilowatts:0.0} kW).");
            StateChanged?.Invoke(previous, next);

            switch (next)
            {
                case PowerState.Tripped:
                    EventBus.Publish(new AlertSignal("MAIN BREAKER OPEN — doors released.", AlertSeverity.Critical));
                    break;
                case PowerState.Blackout:
                    EventBus.Publish(new AlertSignal("TOTAL LOSS OF SUPPLY.", AlertSeverity.Critical));
                    break;
                case PowerState.Overloaded:
                    EventBus.Publish(new AlertSignal("Load above continuous rating.", AlertSeverity.Warning));
                    break;
            }
        }

        // ---------------------------------------------------------------------
        // Player actions
        // ---------------------------------------------------------------------

        public void TripBreaker(string reason)
        {
            if (_breakerOpen) return;

            _breakerOpen = true;
            _overloadTimer = 0f;
            IsResettingBreaker = false;
            _resetTimer = 0f;

            NoiseBurst?.Invoke(0.85f);
            GLog.Warn(LogChannel.Facility, $"Breaker tripped — {reason}");
            BreakerTripped?.Invoke(reason);
        }

        /// <summary>Starts the reset hold. Returns false when there is nothing to reset.</summary>
        public bool BeginBreakerReset()
        {
            if (!_breakerOpen || IsResettingBreaker) return false;
            IsResettingBreaker = true;
            _resetTimer = 0f;
            return true;
        }

        /// <summary>Letting go of the lever loses all progress. That is intentional.</summary>
        public void CancelBreakerReset()
        {
            if (!IsResettingBreaker) return;
            IsResettingBreaker = false;
            _resetTimer = 0f;
        }
    }
}
