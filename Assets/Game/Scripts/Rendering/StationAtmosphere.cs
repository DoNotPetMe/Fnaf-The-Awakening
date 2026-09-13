using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Rendering
{
    /// <summary>
    /// Fog and ambient light for the cave.
    ///
    /// Ninety feet down there is no sky, so the ambient term is a flat, almost-black
    /// colour and the fog is doing nearly all the work of establishing depth. Fog
    /// density rises as the air goes bad — which is both literally true of a cave
    /// with failed ventilation and the cheapest possible way to make the player feel
    /// the air quality number without reading it.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class StationAtmosphere : MonoBehaviour
    {
        [Header("Fog")]
        [SerializeField] private Color cleanFog = new Color(0.045f, 0.05f, 0.058f);
        [SerializeField] private Color fouledFog = new Color(0.10f, 0.105f, 0.075f);

        [SerializeField] private float cleanDensity = 0.022f;
        [SerializeField] private float fouledDensity = 0.075f;

        [Header("Ambient")]
        [SerializeField] private Color ambientLit = new Color(0.048f, 0.052f, 0.062f);
        [SerializeField] private Color ambientDark = new Color(0.012f, 0.014f, 0.020f);

        [Tooltip("How quickly the atmosphere follows the simulation. Slow, so it is felt rather than noticed.")]
        [SerializeField] private float followLambda = 0.7f;

        private FacilityRuntime _facility;
        private float _foulness;
        private float _darkness;

        private void Start()
        {
            if (!ServiceLocator.TryGet(out _facility))
            {
                enabled = false;
                return;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            Apply(instant: true);
        }

        private void Update() => Apply(instant: false);

        private void Apply(bool instant)
        {
            float dt = Time.deltaTime;

            float targetFoulness = 1f - _facility.Ventilation.AirQuality01;
            float targetDarkness = _facility.Power.State switch
            {
                PowerState.Blackout => 1f,
                PowerState.Tripped => 0.6f,
                _ => 0f
            };

            if (instant)
            {
                _foulness = targetFoulness;
                _darkness = targetDarkness;
            }
            else
            {
                _foulness = MathUtil.ExpDecay(_foulness, targetFoulness, followLambda, dt);
                _darkness = MathUtil.ExpDecay(_darkness, targetDarkness, followLambda * 2.5f, dt);
            }

            RenderSettings.fogColor = Color.Lerp(cleanFog, fouledFog, _foulness);
            RenderSettings.fogDensity = Mathf.Lerp(cleanDensity, fouledDensity, _foulness);
            RenderSettings.ambientLight = Color.Lerp(ambientLit, ambientDark, _darkness);
        }
    }
}
