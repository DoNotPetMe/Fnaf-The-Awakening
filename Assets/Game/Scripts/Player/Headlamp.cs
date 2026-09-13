using System;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Player
{
    /// <summary>
    /// The cap lamp on the player's helmet.
    ///
    /// The one light source that does not run off the generator, which makes it the
    /// only thing the player still has during a blackout — and the reason a blackout
    /// is survivable rather than an instant loss. It runs on a cell measured in
    /// in-game hours and recharges in its cradle, so leaving it on "just in case"
    /// means not having it when the breaker finally goes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Headlamp : MonoBehaviour
    {
        [SerializeField] private Light lampLight;

        [Tooltip("Intensity at full charge. Falls off as the cell drains.")]
        [SerializeField] private float fullIntensity = 3.2f;

        [Tooltip("Below this charge the lamp browns out and starts to flicker.")]
        [Range(0f, 0.5f)] [SerializeField] private float brownoutBelow = 0.18f;

        private FacilityTuning _tuning;
        private FacilityRuntime _facility;
        private bool _on;
        private float _flickerSeed;

        /// <summary>Remaining charge, 0..1.</summary>
        public float Charge01 { get; private set; } = 1f;

        public bool IsOn => _on && Charge01 > 0.001f;

        public bool IsBrowningOut => IsOn && Charge01 < brownoutBelow;

        public event Action<bool> Toggled;

        private void Awake()
        {
            if (lampLight == null) lampLight = GetComponentInChildren<Light>();
            _flickerSeed = UnityEngine.Random.value * 100f;
            ApplyVisualState();
        }

        private void Start()
        {
            if (!ServiceLocator.TryGet(out _facility))
            {
                enabled = false;
                return;
            }
            _tuning = _facility.Tuning;
        }

        private void Update()
        {
            float hourDelta = _facility.LastHourDelta;

            if (IsOn)
            {
                if (!(DebugFlags.IsDevBuild && DebugFlags.InfinitePower) && _tuning.headlampHours > 0f)
                    Charge01 = Mathf.Max(0f, Charge01 - hourDelta / _tuning.headlampHours);

                if (Charge01 <= 0f)
                {
                    _on = false;
                    EventBus.Publish(new AlertSignal("Cap lamp is dead.", AlertSeverity.Warning));
                    Toggled?.Invoke(false);
                }
            }
            else if (_tuning.headlampRechargeHours > 0f)
            {
                Charge01 = Mathf.Min(1f, Charge01 + hourDelta / _tuning.headlampRechargeHours);
            }

            ApplyVisualState();
        }

        private void ApplyVisualState()
        {
            if (lampLight == null) return;

            lampLight.enabled = IsOn;
            if (!IsOn) return;

            // Falls off gently, then browns out hard at the end of the cell.
            float level = Charge01 >= brownoutBelow
                ? Mathf.Lerp(0.82f, 1f, Mathf.InverseLerp(brownoutBelow, 1f, Charge01))
                : Mathf.Lerp(0.15f, 0.82f, Charge01 / Mathf.Max(0.001f, brownoutBelow));

            if (IsBrowningOut)
            {
                float t = Time.time * 9f + _flickerSeed;
                level *= 0.6f + 0.4f * Mathf.PerlinNoise(t, _flickerSeed);
            }

            lampLight.intensity = fullIntensity * level;
        }

        public void Toggle() => SetOn(!_on);

        public void SetOn(bool on)
        {
            if (on && Charge01 <= 0.02f)
            {
                EventBus.Publish(new AlertSignal("Cap lamp cell is flat.", AlertSeverity.Warning));
                return;
            }

            if (_on == on) return;
            _on = on;
            ApplyVisualState();
            Toggled?.Invoke(on);
        }

        public void DebugSetCharge(float charge01) => Charge01 = Mathf.Clamp01(charge01);
    }
}
