using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Rendering
{
    /// <summary>
    /// Drives the post-processing volume from the facility's state.
    ///
    /// The profile is built in code rather than authored as an asset, for the same
    /// reason the map is: the values live next to the note explaining them, and a
    /// merge conflict in a volume profile is not something anyone should have to
    /// resolve. It is created as a hidden runtime instance, so nothing is written to
    /// the project and entering play mode twice cannot leave two of them behind.
    ///
    /// Respects <see cref="SettingsData.photosensitiveMode"/>: every flashing,
    /// strobing and high-contrast response is clamped hard when it is on. That is an
    /// accessibility setting, not a difficulty setting, and it does not touch the
    /// simulation.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class PostFxController : MonoBehaviour
    {
        [Header("Baseline")]
        [SerializeField] private float baseVignette = 0.32f;
        [SerializeField] private float baseGrain = 0.22f;
        [SerializeField] private float baseBloom = 0.55f;

        [Header("Response")]
        [Tooltip("Extra vignette at full hallucination pressure.")]
        [SerializeField] private float hallucinationVignette = 0.3f;

        [Tooltip("Lens distortion at full hallucination pressure. Subtle; it should be felt, not seen.")]
        [SerializeField] private float hallucinationDistortion = -0.22f;

        [Tooltip("Chromatic aberration at full pressure.")]
        [SerializeField] private float hallucinationAberration = 0.55f;

        [Tooltip("How fast a scare decays back to nothing, in units per second.")]
        [SerializeField] private float scareDecay = 0.8f;

        private Volume _volume;
        private VolumeProfile _profile;

        private Vignette _vignette;
        private FilmGrain _grain;
        private ChromaticAberration _aberration;
        private LensDistortion _distortion;
        private Bloom _bloom;
        private ColorAdjustments _colour;

        private FacilityRuntime _facility;
        private float _scare;
        private bool _photosensitive;

        /// <summary>Current scare level, 0..1. Read by the overlay so both agree.</summary>
        public float ScareLevel => _scare;

        /// <summary>Current hallucination pressure, mirrored for the overlay.</summary>
        public float HallucinationLevel { get; private set; }

        /// <summary>Blackout amount, driven by the power state.</summary>
        public float BlackoutLevel { get; private set; }

        private void Awake()
        {
            BuildProfile();
            EventBus.Subscribe<ScareSignal>(OnScare);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<ScareSignal>(OnScare);

            if (_profile != null) Destroy(_profile);
            ServiceLocator.Unregister(this);
        }

        private void Start()
        {
            ServiceLocator.TryGet(out _facility);
            ServiceLocator.Register(this);

            if (ServiceLocator.TryGet(out SaveSystem save))
                _photosensitive = save.Data.settings.photosensitiveMode;
        }

        private void BuildProfile()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "Grotto Runtime Profile";
            _profile.hideFlags = HideFlags.HideAndDontSave;

            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.Override(baseVignette);
            _vignette.smoothness.Override(0.6f);
            _vignette.color.Override(Color.black);

            _grain = _profile.Add<FilmGrain>(true);
            _grain.type.Override(FilmGrainLookup.Medium2);
            _grain.intensity.Override(baseGrain);
            _grain.response.Override(0.8f);

            _aberration = _profile.Add<ChromaticAberration>(true);
            _aberration.intensity.Override(0.06f);

            _distortion = _profile.Add<LensDistortion>(true);
            _distortion.intensity.Override(0f);

            _bloom = _profile.Add<Bloom>(true);
            _bloom.intensity.Override(baseBloom);
            _bloom.threshold.Override(0.85f);
            _bloom.scatter.Override(0.72f);

            _colour = _profile.Add<ColorAdjustments>(true);
            _colour.postExposure.Override(0f);
            _colour.contrast.Override(8f);
            _colour.saturation.Override(-14f);

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.weight = 1f;
            _volume.profile = _profile;

            GLog.Info(LogChannel.Rendering, "Runtime post-processing profile built.");
        }

        private void OnScare(ScareSignal signal)
        {
            float intensity = Mathf.Clamp01(signal.Intensity);
            if (_photosensitive) intensity *= 0.35f;
            _scare = Mathf.Max(_scare, intensity);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _scare = Mathf.Max(0f, _scare - scareDecay * dt);

            if (_facility == null) return;

            float pressure = _facility.Ventilation.HallucinationPressure01;
            if (_photosensitive) pressure *= 0.4f;
            HallucinationLevel = pressure;

            BlackoutLevel = _facility.Power.State switch
            {
                PowerState.Blackout => 1f,
                PowerState.Tripped => 0.45f,
                _ => 0f
            };

            // Vignette: closes in with bad air, a blackout, and momentarily on a scare.
            _vignette.intensity.value = Mathf.Clamp01(
                baseVignette
                + pressure * hallucinationVignette
                + BlackoutLevel * 0.25f
                + _scare * 0.2f);

            // Grain rises as the air goes — the eye straining, not the camera.
            _grain.intensity.value = Mathf.Clamp01(baseGrain + pressure * 0.45f + _scare * 0.2f);

            _aberration.intensity.value = Mathf.Clamp01(0.06f + pressure * hallucinationAberration + _scare * 0.3f);

            // Barrel distortion that breathes, so the room feels like it is moving
            // without anything in it actually moving.
            float breath = Mathf.Sin(Time.time * 0.9f) * 0.5f + 0.5f;
            _distortion.intensity.value = pressure * hallucinationDistortion * Mathf.Lerp(0.6f, 1f, breath);

            // Colour drains as the supply fails; bloom lifts so the few live lamps
            // smear the way a dark-adapted eye sees them.
            _colour.saturation.value = -14f - BlackoutLevel * 40f - pressure * 22f;
            _colour.postExposure.value = -BlackoutLevel * 0.5f + _scare * 0.35f;
            _bloom.intensity.value = baseBloom + BlackoutLevel * 0.5f + _scare * 0.4f;
        }

        /// <summary>Re-reads the accessibility settings, after the options menu changes them.</summary>
        public void ApplySettings(SettingsData settings)
        {
            if (settings == null) return;
            _photosensitive = settings.photosensitiveMode;
            GLog.Info(LogChannel.Rendering, $"Photosensitive mode {(_photosensitive ? "on" : "off")}.");
        }

        /// <summary>Forces a scare of a given strength. Used by <c>fx.scare</c>.</summary>
        public void DebugScare(float intensity) => _scare = Mathf.Clamp01(intensity);
    }
}
