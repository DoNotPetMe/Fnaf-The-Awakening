using System;
using UnityEngine;

namespace Grotto.Core
{
    /// <summary>
    /// The night clock: 12 AM through 6 AM.
    ///
    /// Owns *game* time only. It is ticked explicitly by <see cref="NightController"/>
    /// rather than from its own Update, so that pausing, the dev console's time scale
    /// and deterministic fixed-step replay all go through a single path.
    /// </summary>
    public sealed class GameClock
    {
        /// <summary>Hour the night starts at. 0 reads as "12 AM".</summary>
        public const int StartHour = 0;

        /// <summary>Hour the night is survived at.</summary>
        public const int EndHour = 6;

        private float _secondsPerHour;
        private float _elapsed;
        private int _hour;

        /// <summary>Raised with the new hour each time the clock rolls over.</summary>
        public event Action<int> HourChanged;

        /// <summary>Raised once when the clock reaches <see cref="EndHour"/>.</summary>
        public event Action NightComplete;

        public bool IsRunning { get; private set; }

        /// <summary>Current hour, 0 (12 AM) through 6.</summary>
        public int Hour => _hour;

        /// <summary>Progress through the current hour, 0..1.</summary>
        public float HourProgress01 => _secondsPerHour <= 0f ? 0f : Mathf.Repeat(_elapsed, _secondsPerHour) / _secondsPerHour;

        /// <summary>Progress through the whole night, 0..1.</summary>
        public float NightProgress01 => Mathf.Clamp01(_elapsed / TotalNightSeconds);

        public float ElapsedSeconds => _elapsed;
        public float TotalNightSeconds => _secondsPerHour * (EndHour - StartHour);
        public float RemainingSeconds => Mathf.Max(0f, TotalNightSeconds - _elapsed);

        /// <summary>Real seconds per in-game hour.</summary>
        public float SecondsPerHour
        {
            get => _secondsPerHour;
            set => _secondsPerHour = Mathf.Max(1f, value);
        }

        public GameClock(float secondsPerHour)
        {
            SecondsPerHour = secondsPerHour;
            Reset();
        }

        public void Reset()
        {
            _elapsed = 0f;
            _hour = StartHour;
            IsRunning = false;
        }

        public void Start() => IsRunning = true;
        public void Pause() => IsRunning = false;
        public void Resume() => IsRunning = true;

        /// <summary>
        /// Advances the clock. <paramref name="deltaTime"/> is unscaled game time;
        /// <see cref="DebugFlags.ClockScale"/> is applied here so that every consumer
        /// of the clock stays consistent when a developer speeds the night up.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsRunning || deltaTime <= 0f) return;

            float scale = DebugFlags.IsDevBuild ? Mathf.Max(0f, DebugFlags.ClockScale) : 1f;
            _elapsed += deltaTime * scale;

            int newHour = StartHour + Mathf.FloorToInt(_elapsed / _secondsPerHour);
            newHour = Mathf.Clamp(newHour, StartHour, EndHour);

            while (_hour < newHour)
            {
                _hour++;
                GLog.Info(LogChannel.Core, $"Clock rolled over to {MathUtil.FormatHour(_hour)}");
                HourChanged?.Invoke(_hour);
                EventBus.Publish(new NightHourChangedSignal(_hour));

                if (_hour >= EndHour)
                {
                    IsRunning = false;
                    NightComplete?.Invoke();
                    break;
                }
            }
        }

        /// <summary>Developer jump. Fires every intervening HourChanged so systems stay in sync.</summary>
        public void DebugSetHour(int hour)
        {
            hour = Mathf.Clamp(hour, StartHour, EndHour);
            if (hour <= _hour)
            {
                // Jumping backwards: rewind silently, nothing downstream expects it.
                _hour = hour;
                _elapsed = hour * _secondsPerHour;
                return;
            }

            _elapsed = hour * _secondsPerHour - 0.001f;
            Tick(0.002f);
        }

        public string DisplayHour => MathUtil.FormatHour(_hour);
    }
}
