using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// The 8kW Lister in the generator bay.
    ///
    /// Power in this game is not an abstract percentage bar. It is a diesel engine
    /// with a day tank, a load-dependent burn rate, a finite number of jerry cans and
    /// a starter motor that can be heard from the Grand Gallery. Every decision the
    /// player makes about power is therefore also a decision about noise, and about
    /// the seconds they spend not looking at the cameras.
    ///
    /// A plain class, not a MonoBehaviour: it is ticked by <see cref="FacilityRuntime"/>
    /// in a defined order and is exercised directly by the edit-mode tests.
    /// </summary>
    public sealed class Generator
    {
        public enum State
        {
            Running,
            /// <summary>Stopped, with fuel available. Crank to restart.</summary>
            Stalled,
            /// <summary>Starter motor engaged.</summary>
            Cranking,
            /// <summary>Dry. Refuel before cranking.</summary>
            Dry
        }

        private readonly FacilityTuning _tuning;
        private float _actionTimer;
        private float _actionDuration;

        public State CurrentState { get; private set; } = State.Running;

        public float FuelLitres { get; private set; }
        public int SpareCans { get; private set; }

        /// <summary>Kilowatts the grid asked for on the last tick. Drives burn and engine note.</summary>
        public float LoadKilowatts { get; private set; }

        /// <summary>Smoothed 0..1 engine speed. Presentation only — audio pitch and gauge needle.</summary>
        public float Rpm01 { get; private set; }

        /// <summary>True while refuelling or cranking. The station forces the monitor down.</summary>
        public bool IsBusy => CurrentState == State.Cranking || IsRefuelling;

        public bool IsRefuelling { get; private set; }

        /// <summary>Progress of the current multi-second action, 0..1.</summary>
        public float ActionProgress01 => _actionDuration <= 0f ? 0f : Mathf.Clamp01(_actionTimer / _actionDuration);

        public bool IsSupplying => CurrentState == State.Running;

        public float FuelFraction => _tuning.fuelCapacityLitres <= 0f
            ? 0f
            : Mathf.Clamp01(FuelLitres / _tuning.fuelCapacityLitres);

        /// <summary>Fraction of the continuous rating currently drawn. Can exceed 1.</summary>
        public float LoadFraction => _tuning.generatorRatedKilowatts <= 0f
            ? 0f
            : LoadKilowatts / _tuning.generatorRatedKilowatts;

        /// <summary>Raised when the engine stops for any reason, with a human-readable cause.</summary>
        public event Action<string> Stopped;

        /// <summary>Raised when the engine catches.</summary>
        public event Action Started;

        /// <summary>Raised with (noise 0..1) whenever the genset makes a one-off racket.</summary>
        public event Action<float> NoiseBurst;

        public Generator(FacilityTuning tuning)
        {
            _tuning = tuning != null ? tuning : ScriptableObject.CreateInstance<FacilityTuning>();
            FuelLitres = _tuning.fuelCapacityLitres;
        }

        /// <summary>Sets up the tank for a night.</summary>
        public void ResetForNight(float startingFuelLitres, int spareCans)
        {
            FuelLitres = Mathf.Clamp(startingFuelLitres, 0f, _tuning.fuelCapacityLitres);
            SpareCans = Mathf.Max(0, spareCans);
            CurrentState = FuelLitres > 0f ? State.Running : State.Dry;
            Rpm01 = CurrentState == State.Running ? 1f : 0f;
            LoadKilowatts = 0f;
            IsRefuelling = false;
            _actionTimer = _actionDuration = 0f;
        }

        /// <summary>
        /// Advances the engine.
        /// <paramref name="hourDelta"/> is elapsed in-game hours (drives fuel burn);
        /// <paramref name="realDelta"/> is wall-clock seconds (drives player actions).
        /// </summary>
        public void Tick(float hourDelta, float realDelta, float requestedKilowatts, float burnScale)
        {
            LoadKilowatts = Mathf.Max(0f, requestedKilowatts);

            TickAction(realDelta);

            if (CurrentState == State.Running)
            {
                if (!(DebugFlags.IsDevBuild && DebugFlags.InfinitePower))
                {
                    float burn = _tuning.idleBurnPerHour
                                 + _tuning.fullLoadBurnPerHour * Mathf.Clamp01(LoadFraction);
                    FuelLitres -= burn * hourDelta * Mathf.Max(0.01f, burnScale);
                }

                if (FuelLitres <= 0f)
                {
                    FuelLitres = 0f;
                    CurrentState = State.Dry;
                    Rpm01 = 0f;
                    GLog.Info(LogChannel.Facility, "Generator ran the day tank dry.");
                    Stopped?.Invoke("Day tank empty");
                }
            }

            // Engine note follows load with a little lag, so the player hears the
            // generator lug when they close a second door.
            float targetRpm = CurrentState switch
            {
                State.Running => Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(LoadFraction)),
                State.Cranking => 0.3f,
                _ => 0f
            };
            Rpm01 = MathUtil.ExpDecay(Rpm01, targetRpm, 2.5f, realDelta);
        }

        private void TickAction(float realDelta)
        {
            if (_actionDuration <= 0f) return;

            _actionTimer += realDelta;
            if (_actionTimer < _actionDuration) return;

            _actionTimer = _actionDuration = 0f;

            if (IsRefuelling)
            {
                IsRefuelling = false;
                FuelLitres = Mathf.Min(_tuning.fuelCapacityLitres, FuelLitres + _tuning.jerryCanLitres);
                if (CurrentState == State.Dry) CurrentState = State.Stalled;
                GLog.Info(LogChannel.Facility, $"Refuelled. Day tank at {FuelLitres:0} L, {SpareCans} can(s) left.");
            }
            else if (CurrentState == State.Cranking)
            {
                if (FuelLitres > 0f)
                {
                    CurrentState = State.Running;
                    GLog.Info(LogChannel.Facility, "Generator caught.");
                    Started?.Invoke();
                }
                else
                {
                    CurrentState = State.Dry;
                    Stopped?.Invoke("Cranked dry");
                }
            }
        }

        /// <summary>Engages the starter. Returns false if it cannot run right now.</summary>
        public bool BeginCrank()
        {
            if (IsBusy || CurrentState == State.Running) return false;
            if (FuelLitres <= 0f)
            {
                GLog.Info(LogChannel.Facility, "Crank refused: no fuel.");
                return false;
            }

            CurrentState = State.Cranking;
            _actionTimer = 0f;
            _actionDuration = Mathf.Max(0.1f, _tuning.crankSeconds);
            NoiseBurst?.Invoke(_tuning.crankNoise);
            return true;
        }

        /// <summary>Pours a spare can into the day tank. Returns false if there are none left.</summary>
        public bool BeginRefuel()
        {
            if (IsBusy) return false;
            if (SpareCans <= 0)
            {
                GLog.Info(LogChannel.Facility, "Refuel refused: no cans left.");
                return false;
            }
            if (FuelLitres >= _tuning.fuelCapacityLitres - 0.5f) return false;

            SpareCans--;
            IsRefuelling = true;
            _actionTimer = 0f;
            _actionDuration = Mathf.Max(0.1f, _tuning.refuelSeconds);
            NoiseBurst?.Invoke(0.5f);
            return true;
        }

        /// <summary>Cuts the engine deliberately — the only way to make the bay truly quiet.</summary>
        public void Shutdown(string reason = "Manual shutdown")
        {
            if (CurrentState != State.Running) return;
            CurrentState = State.Stalled;
            GLog.Info(LogChannel.Facility, $"Generator shut down: {reason}");
            Stopped?.Invoke(reason);
        }

        /// <summary>Continuous noise the running engine contributes to the generator bay.</summary>
        public float ContinuousNoise => CurrentState switch
        {
            State.Running => _tuning.generatorNoise * Mathf.Lerp(0.7f, 1.15f, Mathf.Clamp01(LoadFraction)),
            State.Cranking => _tuning.crankNoise,
            _ => 0f
        };

        // ---- Developer affordances ------------------------------------------

        public void DebugSetFuel(float litres)
        {
            FuelLitres = Mathf.Clamp(litres, 0f, _tuning.fuelCapacityLitres);
            if (FuelLitres > 0f && CurrentState == State.Dry) CurrentState = State.Stalled;
        }

        public void DebugSetCans(int cans) => SpareCans = Mathf.Max(0, cans);

        public void DebugForceRunning()
        {
            if (FuelLitres <= 0f) FuelLitres = _tuning.fuelCapacityLitres;
            IsRefuelling = false;
            _actionTimer = _actionDuration = 0f;
            CurrentState = State.Running;
        }
    }
}
