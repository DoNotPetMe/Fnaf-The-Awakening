using System;
using System.Collections;
using UnityEngine;

namespace Grotto.Core
{
    /// <summary>
    /// Owns the lifecycle of a single night: briefing, the running clock, and how it
    /// ends. It knows nothing about animatronics, power or water — those systems
    /// subscribe to its signals. That separation is what lets the whole night flow be
    /// unit tested and lets the dev console end a night without reaching into the AI.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class NightController : MonoBehaviour
    {
        public enum Phase { Idle, Briefing, Active, Resolving, Complete }

        [SerializeField] private GameConfig config;

        [Tooltip("Night to run when this scene is entered directly from the editor. 0 uses GameConfig.")]
        [SerializeField] private int overrideStartNight;

        private StateMachine<Phase> _phases;
        private float _briefingRemaining;
        private NightOutcome _pendingOutcome;
        private bool _outcomeQueued;

        public GameClock Clock { get; private set; }
        public RandomSource Rng { get; private set; }
        public SaveSystem Save { get; private set; }

        public int CurrentNight { get; private set; }
        public NightDefinition CurrentDefinition { get; private set; }
        public Phase CurrentPhase => _phases?.Current ?? Phase.Idle;
        public bool IsNightRunning => CurrentPhase == Phase.Active;

        /// <summary>Fires once the briefing ends and the clock starts.</summary>
        public event Action<NightDefinition> NightBegun;

        /// <summary>Fires when the night resolves, with the reason.</summary>
        public event Action<NightOutcome> NightResolved;

        /// <summary>Seconds remaining in the briefing phase; 0 outside it.</summary>
        public float BriefingRemaining => _briefingRemaining;

        /// <summary>
        /// While true the briefing countdown is paused.
        ///
        /// The briefing screen sets this so a player reading the shift orders for the
        /// first time is not thrown into the night halfway through a sentence. Cleared
        /// when they dismiss it.
        /// </summary>
        public bool HoldBriefing { get; set; }

        private void Awake()
        {
            if (config == null) config = GameConfig.LoadDefault();

            Clock = new GameClock(60f);
            Clock.NightComplete += OnClockReachedSix;

            if (!ServiceLocator.TryGet(out SaveSystem save))
            {
                save = new SaveSystem();
                save.Load();
                ServiceLocator.Register(save);
            }
            Save = save;

            _phases = new StateMachine<Phase>(nameof(NightController))
                .Configure(Phase.Briefing, enter: EnterBriefing, tick: TickBriefing)
                .Configure(Phase.Active, enter: EnterActive, tick: TickActive)
                .Configure(Phase.Resolving, enter: EnterResolving);
            _phases.Begin(Phase.Idle);

            ServiceLocator.Register(this);
            EventBus.Subscribe<AttackSignal>(OnAttack);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<AttackSignal>(OnAttack);
            if (Clock != null) Clock.NightComplete -= OnClockReachedSix;
            ServiceLocator.Unregister(this);
        }

        private void Start()
        {
            int autoStart = overrideStartNight > 0
                ? overrideStartNight
                : (config != null ? config.editorAutoStartNight : 0);

            if (autoStart > 0 && CurrentPhase == Phase.Idle)
                StartNight(autoStart);
        }

        private void Update()
        {
            _phases.Tick(Time.deltaTime);

            // Outcomes raised from a signal handler are deferred by one frame so that
            // listeners are never torn down midway through an event dispatch.
            if (_outcomeQueued)
            {
                _outcomeQueued = false;
                Resolve(_pendingOutcome);
            }
        }

        // ---------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------

        /// <summary>Begins a night. Safe to call while another night is running — it restarts.</summary>
        public void StartNight(int night, int? seedOverride = null)
        {
            CurrentNight = Mathf.Max(1, night);
            CurrentDefinition = config != null ? config.GetNight(CurrentNight) : null;

            if (CurrentDefinition == null)
            {
                GLog.Warn(LogChannel.Core,
                    $"No NightDefinition authored for night {CurrentNight}; falling back to a generated default.");
                CurrentDefinition = CreateFallbackNight(CurrentNight);
            }

            int seed = seedOverride
                       ?? DebugFlags.ForcedSeed
                       ?? unchecked(Environment.TickCount ^ (CurrentNight * 7919));
            Rng = new RandomSource(seed);

            Clock.SecondsPerHour = CurrentDefinition.secondsPerHour;
            Clock.Reset();

            Save?.RecordNightAttempt(CurrentNight);

            GLog.Info(LogChannel.Core, $"Starting {CurrentDefinition.displayName} (seed {seed}).");
            EventBus.Publish(new NightStartedSignal(CurrentNight, seed));

            _phases.Transition(CurrentDefinition.briefingSeconds > 0f ? Phase.Briefing : Phase.Active, force: true);
        }

        /// <summary>Ends the night for a non-attack reason (flood, suffocation, quit).</summary>
        public void RequestOutcome(NightOutcome outcome)
        {
            if (CurrentPhase != Phase.Active && CurrentPhase != Phase.Briefing) return;
            _pendingOutcome = outcome;
            _outcomeQueued = true;
        }

        /// <summary>Skips the remaining briefing. Used by "press any key" and by the dev console.</summary>
        public void SkipBriefing()
        {
            HoldBriefing = false;
            if (CurrentPhase == Phase.Briefing) _phases.Transition(Phase.Active);
        }

        // ---------------------------------------------------------------------
        // Phases
        // ---------------------------------------------------------------------

        private void EnterBriefing()
        {
            HoldBriefing = false;
            _briefingRemaining = CurrentDefinition.briefingSeconds;
            EventBus.Publish(new AlertSignal(CurrentDefinition.briefing));
        }

        private void TickBriefing(float dt)
        {
            if (HoldBriefing) return;

            _briefingRemaining -= dt;
            if (_briefingRemaining <= 0f) _phases.Transition(Phase.Active);
        }

        private void EnterActive()
        {
            _briefingRemaining = 0f;
            Clock.Start();
            NightBegun?.Invoke(CurrentDefinition);
            GLog.Info(LogChannel.Core, $"{CurrentDefinition.displayName}: clock running.");
        }

        private void TickActive(float dt)
        {
            Clock.Tick(dt);
        }

        private void EnterResolving()
        {
            Clock.Pause();
        }

        private void OnClockReachedSix()
        {
            if (CurrentPhase == Phase.Active) Resolve(NightOutcome.Survived);
        }

        private void OnAttack(AttackSignal signal)
        {
            if (CurrentPhase != Phase.Active) return;

            if (DebugFlags.IsDevBuild && DebugFlags.GodMode)
            {
                GLog.Info(LogChannel.AI,
                    $"God mode absorbed an attack from '{signal.AnimatronicId}' out of {signal.From}.");
                EventBus.Publish(new AlertSignal(
                    $"[GOD MODE] {signal.AnimatronicId} would have killed you.", AlertSeverity.Warning));
                return;
            }

            _pendingOutcome = NightOutcome.Killed;
            _outcomeQueued = true;
        }

        private void Resolve(NightOutcome outcome)
        {
            if (CurrentPhase == Phase.Complete || CurrentPhase == Phase.Resolving) return;

            _phases.Transition(Phase.Resolving);

            float survived = Clock.ElapsedSeconds;
            Save?.RecordNightResult(CurrentNight, outcome, survived);

            GLog.Info(LogChannel.Core,
                $"Night {CurrentNight} resolved: {outcome} after {survived:0.0}s " +
                $"({Clock.DisplayHour}).");

            NightResolved?.Invoke(outcome);
            EventBus.Publish(new NightEndedSignal(CurrentNight, outcome));

            StartCoroutine(CompleteAfterPresentation(outcome));
        }

        private IEnumerator CompleteAfterPresentation(NightOutcome outcome)
        {
            float hold = outcome == NightOutcome.Survived
                ? (config != null ? config.survivalHoldSeconds : 5f)
                : (config != null ? config.jumpscareSeconds : 2.2f);

            yield return new WaitForSecondsRealtime(hold);
            _phases.Transition(Phase.Complete);
        }

        /// <summary>
        /// Keeps the facility scene playable when a night asset has not been authored
        /// yet — a designer opening the scene should get a working night, not a null
        /// reference.
        /// </summary>
        private static NightDefinition CreateFallbackNight(int night)
        {
            var def = ScriptableObject.CreateInstance<NightDefinition>();
            def.night = night;
            def.displayName = "Night " + night + " (generated)";
            def.briefingSeconds = 0f;
            def.secondsPerHour = 60f;
            def.aiLevels.Add(new AiLevelEntry("barty", Mathf.Clamp(night, 1, 20)));
            def.aiLevels.Add(new AiLevelEntry("vesper", Mathf.Clamp(night - 1, 0, 20)));
            def.aiLevels.Add(new AiLevelEntry("marlow", Mathf.Clamp(night - 2, 0, 20)));
            def.aiLevels.Add(new AiLevelEntry("echo", Mathf.Clamp(night - 2, 0, 20)));
            def.aiLevels.Add(new AiLevelEntry("chorus", Mathf.Clamp(night - 4, 0, 20)));
            return def;
        }
    }
}
