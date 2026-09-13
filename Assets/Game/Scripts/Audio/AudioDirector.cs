using System.Collections.Generic;
using UnityEngine;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Audio
{
    /// <summary>
    /// The game's mix.
    ///
    /// Two responsibilities. First, it keeps a small set of looping machinery sources
    /// pinned at the world positions of the nodes their machines live in, with their
    /// gain and pitch driven directly from the simulation — so the generator lugging
    /// under load, the pump starting to cavitate and the fan going to purge are all
    /// audible facts rather than UI readouts.
    ///
    /// Second, it fires one-shots for events, spatialised at the node where they
    /// happened. That is what makes the cave legible with the monitor down: a footfall
    /// from the north adit sounds like it came from the north adit, and the player can
    /// act on it without looking at anything.
    ///
    /// Every clip is synthesised by <see cref="ProceduralAudio"/> unless a same-named
    /// asset exists under <c>Resources/Audio/</c>, which is how a downloaded CC0 pack
    /// takes over without a code change.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    [DisallowMultipleComponent]
    public sealed class AudioDirector : MonoBehaviour
    {
        [Header("Mix")]
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float machineryVolume = 0.75f;
        [Range(0f, 1f)] [SerializeField] private float effectsVolume = 0.9f;
        [Range(0f, 1f)] [SerializeField] private float ambienceVolume = 0.5f;

        [Header("Spatialisation")]
        [SerializeField] private float minDistance = 3f;
        [SerializeField] private float maxDistance = 42f;

        [Tooltip("Simultaneous one-shot voices. Beyond this the oldest is stolen.")]
        [SerializeField] private int voiceCount = 16;

        private FacilityRuntime _facility;
        private AIDirector _ai;

        private AudioSource _generator;
        private AudioSource _fan;
        private AudioSource _pump;
        private AudioSource _cavitation;
        private AudioSource _monitorStatic;
        private AudioSource _ambience;

        private AudioSource[] _voices;
        private int _nextVoice;

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>(16);
        private readonly List<AnimatronicController> _hooked = new List<AnimatronicController>(8);

        private bool _reducedJumpscareAudio;

        private void Awake()
        {
            BuildClips();
            BuildSources();

            EventBus.Subscribe<AttackSignal>(OnAttack);
            EventBus.Subscribe<ScareSignal>(OnScare);
            EventBus.Subscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Subscribe<CameraSwitchedSignal>(OnCameraSwitched);
            EventBus.Subscribe<HallucinationSignal>(OnHallucination);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<AttackSignal>(OnAttack);
            EventBus.Unsubscribe<ScareSignal>(OnScare);
            EventBus.Unsubscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Unsubscribe<CameraSwitchedSignal>(OnCameraSwitched);
            EventBus.Unsubscribe<HallucinationSignal>(OnHallucination);

            for (int i = 0; i < _hooked.Count; i++)
                if (_hooked[i] != null) _hooked[i].Arrived -= OnAnimatronicArrived;

            ServiceLocator.Unregister(this);
        }

        private void Start()
        {
            if (!ServiceLocator.TryGet(out _facility))
            {
                enabled = false;
                return;
            }

            ServiceLocator.TryGet(out _ai);
            ServiceLocator.Register(this);

            if (ServiceLocator.TryGet(out SaveSystem save))
            {
                masterVolume = save.Data.settings.masterVolume;
                effectsVolume = save.Data.settings.sfxVolume;
                _reducedJumpscareAudio = save.Data.settings.reducedJumpscareAudio;
            }

            PlaceLoops();
            HookCast();
        }

        // ---------------------------------------------------------------------
        // Setup
        // ---------------------------------------------------------------------

        private void BuildClips()
        {
            // Resources override, synthesis fallback. Named so a downloaded pack can
            // drop straight in as Resources/Audio/<key>.
            Register("GeneratorHum", () => ProceduralAudio.GeneratorHum());
            Register("FanLoop", () => ProceduralAudio.FanLoop());
            Register("PumpLoop", () => ProceduralAudio.PumpLoop());
            Register("Cavitation", () => ProceduralAudio.CavitationLoop());
            Register("Static", () => ProceduralAudio.StaticHiss());
            Register("CaveAmbience", () => ProceduralAudio.CaveAmbience());
            Register("Drip", () => ProceduralAudio.Drip());
            Register("MetalImpact", () => ProceduralAudio.MetalImpact());
            Register("DoorCycle", () => ProceduralAudio.DoorCycle());
            Register("BreakerClack", () => ProceduralAudio.BreakerClack());
            Register("Footstep", () => ProceduralAudio.Footstep());
            Register("Jumpscare", () => ProceduralAudio.Jumpscare());
            Register("DawnChime", () => ProceduralAudio.DawnChime());
        }

        private void Register(string key, System.Func<AudioClip> synthesise)
        {
            var loaded = Resources.Load<AudioClip>("Audio/" + key);
            if (loaded != null)
            {
                _clips[key] = loaded;
                GLog.Info(LogChannel.Audio, $"Using downloaded clip for '{key}'.");
                return;
            }

            _clips[key] = synthesise();
        }

        private AudioClip Clip(string key) => _clips.TryGetValue(key, out var clip) ? clip : null;

        private void BuildSources()
        {
            _generator = CreateLoop("Generator", Clip("GeneratorHum"));
            _fan = CreateLoop("Fan", Clip("FanLoop"));
            _pump = CreateLoop("Pump", Clip("PumpLoop"));
            _cavitation = CreateLoop("Cavitation", Clip("Cavitation"));
            _monitorStatic = CreateLoop("MonitorStatic", Clip("Static"), spatial: false);
            _ambience = CreateLoop("Ambience", Clip("CaveAmbience"), spatial: false);

            _voices = new AudioSource[Mathf.Max(4, voiceCount)];
            for (int i = 0; i < _voices.Length; i++)
            {
                var go = new GameObject($"Voice {i:00}");
                go.transform.SetParent(transform, worldPositionStays: false);

                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.minDistance = minDistance;
                source.maxDistance = maxDistance;
                source.dopplerLevel = 0f;

                _voices[i] = source;
            }
        }

        private AudioSource CreateLoop(string sourceName, AudioClip clip, bool spatial = true)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, worldPositionStays: false);

            var source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.volume = 0f;
            source.spatialBlend = spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;

            if (clip != null) source.Play();
            return source;
        }

        private void PlaceLoops()
        {
            _generator.transform.position = _facility.Graph.PositionOf(new NodeId("GEN"));
            _pump.transform.position = _facility.Graph.PositionOf(new NodeId("SUMP"));
            _cavitation.transform.position = _pump.transform.position;
            _fan.transform.position = _facility.Graph.PositionOf(_facility.StationNode);
        }

        private void HookCast()
        {
            if (_ai == null) return;

            var cast = _ai.Cast;
            for (int i = 0; i < cast.Count; i++)
            {
                if (cast[i] == null) continue;
                cast[i].Arrived += OnAnimatronicArrived;
                _hooked.Add(cast[i]);
            }
        }

        // ---------------------------------------------------------------------
        // Per-frame mix
        // ---------------------------------------------------------------------

        private void Update()
        {
            if (_facility == null) return;

            float dt = Time.deltaTime;
            float machinery = masterVolume * machineryVolume;

            // Generator: gain and pitch both follow engine speed, so load is audible.
            var engine = _facility.Power.Generator;
            float engineGain = engine.IsSupplying || engine.CurrentState == Generator.State.Cranking
                ? Mathf.Lerp(0.15f, 0.9f, engine.Rpm01)
                : 0f;
            Fade(_generator, engineGain * machinery, dt, 4f);
            _generator.pitch = Mathf.Lerp(0.55f, 1.06f, engine.Rpm01);

            // Fan.
            var ventilation = _facility.Ventilation;
            float fanGain = !ventilation.IsRunning ? 0f
                : ventilation.Mode == FanMode.Purge ? 0.85f : 0.4f;
            Fade(_fan, fanGain * machinery, dt, 5f);
            _fan.pitch = ventilation.Mode == FanMode.Purge ? 1.18f : 0.92f;

            // Pump, and the separate cavitation bed when it starts eating air.
            var water = _facility.Water;
            Fade(_pump, (water.IsPumping ? 0.8f : 0f) * machinery, dt, 5f);
            _pump.pitch = water.IsCavitating ? 1.1f : Mathf.Lerp(0.9f, 1f, water.PumpCondition01);
            Fade(_cavitation, (water.IsCavitating ? 0.7f : 0f) * machinery, dt, 8f);

            // Monitor snow, loud in proportion to how bad the live feed is.
            var surveillance = _facility.Surveillance;
            float staticGain = 0f;
            if (surveillance.MonitorUp)
            {
                float condition = surveillance.ConditionOf(surveillance.ActiveNode);
                staticGain = Mathf.Lerp(0.45f, 0.05f, condition);
            }
            Fade(_monitorStatic, staticGain * masterVolume * effectsVolume, dt, 10f);

            // Room tone rises as the air goes — the cave's own sound getting closer.
            float ambienceGain = ambienceVolume * masterVolume *
                Mathf.Lerp(0.6f, 1.15f, ventilation.HallucinationPressure01);
            Fade(_ambience, ambienceGain, dt, 2f);

            TickIdleDrips(dt);
        }

        private static void Fade(AudioSource source, float target, float dt, float lambda)
        {
            if (source == null) return;
            source.volume = MathUtil.ExpDecay(source.volume, target, lambda, dt);
        }

        private static readonly NodeId[] DripNodes =
        {
            new NodeId("SUMP"), new NodeId("RIVER"), new NodeId("DINE"), new NodeId("STATION")
        };

        private float _dripTimer;

        private void TickIdleDrips(float dt)
        {
            _dripTimer -= dt;
            if (_dripTimer > 0f) return;

            _dripTimer = Random.Range(1.4f, 5.5f);

            // Drips come from wherever the water actually is.
            var node = DripNodes[Random.Range(0, DripNodes.Length)];
            if (!_facility.Graph.Contains(node)) node = _facility.StationNode;

            PlayAt("Drip", _facility.Graph.PositionOf(node),
                Random.Range(0.25f, 0.5f), Random.Range(0.85f, 1.25f));
        }

        // ---------------------------------------------------------------------
        // Events
        // ---------------------------------------------------------------------

        private void OnAnimatronicArrived(NodeId node, float loudness)
        {
            // Quiet arrivals in far rooms are not worth a voice.
            if (loudness < 0.12f) return;

            PlayAt("Footstep", _facility.Graph.PositionOf(node),
                Mathf.Clamp01(loudness) * 0.8f, Random.Range(0.8f, 1.15f));
        }

        private void OnAttack(AttackSignal signal)
        {
            if (DebugFlags.IsDevBuild && DebugFlags.DisableJumpscares) return;

            float volume = _reducedJumpscareAudio ? 0.35f : 1f;
            PlayAt("Jumpscare", transform.position, volume * masterVolume, 1f, spatial: false);
        }

        private void OnScare(ScareSignal signal)
        {
            float intensity = Mathf.Clamp01(signal.Intensity);
            if (intensity < 0.2f) return;

            PlayAt("MetalImpact", _facility.Graph.PositionOf(_facility.StationNode),
                intensity * 0.6f * masterVolume * effectsVolume, Random.Range(0.7f, 1.1f));
        }

        private void OnNightEnded(NightEndedSignal signal)
        {
            if (signal.Outcome != NightOutcome.Survived) return;
            PlayAt("DawnChime", transform.position, masterVolume, 1f, spatial: false);
        }

        private void OnCameraSwitched(CameraSwitchedSignal signal)
        {
            PlayAt("BreakerClack", transform.position, 0.18f * masterVolume, 1.8f, spatial: false);
        }

        private void OnHallucination(HallucinationSignal signal)
        {
            switch (signal.Kind)
            {
                case HallucinationKind.Presence:
                    PlayAt("MetalImpact", transform.position, 0.5f * masterVolume, 0.5f, spatial: false);
                    break;
                case HallucinationKind.FalseContact:
                    PlayAt("Footstep", _facility.Graph.PositionOf(signal.Node), 0.5f, 0.9f);
                    break;
                default:
                    PlayAt("Static", transform.position, 0.3f * masterVolume, 1f, spatial: false);
                    break;
            }
        }

        // ---------------------------------------------------------------------
        // Voice pool
        // ---------------------------------------------------------------------

        /// <summary>Plays a one-shot, stealing the oldest voice when they are all busy.</summary>
        public void PlayAt(string key, Vector3 position, float volume, float pitch = 1f, bool spatial = true)
        {
            var clip = Clip(key);
            if (clip == null || volume <= 0.001f) return;

            var source = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;

            source.transform.position = position;
            source.spatialBlend = spatial ? 1f : 0f;
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = Mathf.Clamp(pitch, 0.2f, 3f);
            source.Play();
        }

        /// <summary>Plays a door cycle at a node. Called by the scene's door wiring.</summary>
        public void PlayDoorCycle(NodeId node)
            => PlayAt("DoorCycle", _facility.Graph.PositionOf(node), 0.8f * masterVolume * effectsVolume);

        public void ApplySettings(SettingsData settings)
        {
            if (settings == null) return;
            masterVolume = settings.masterVolume;
            effectsVolume = settings.sfxVolume;
            _reducedJumpscareAudio = settings.reducedJumpscareAudio;
        }
    }
}
