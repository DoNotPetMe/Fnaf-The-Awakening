using System;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;
using Grotto.Procedural;

namespace Grotto.AI
{
    public enum AnimatronicState
    {
        /// <summary>AI level 0, or the night has not started.</summary>
        Dormant,
        /// <summary>Wandering the far side of the map.</summary>
        Roam,
        /// <summary>Closing in — within a couple of hops of the station.</summary>
        Stalk,
        /// <summary>At a door, a grate or the chase mouth, waiting for a way in.</summary>
        Threshold,
        /// <summary>Committed. The attack window is running.</summary>
        Attack,
        /// <summary>Gave up on an approach; heading back and on cooldown.</summary>
        Retreat,
        /// <summary>Its route does not currently exist — Echo in a drained channel.</summary>
        Stranded
    }

    /// <summary>
    /// Moves one animatronic through the facility graph.
    ///
    /// Position is discrete: a character *is* at a node, and transit between nodes is
    /// presentation with a duration. Everything the player reasons about — the map,
    /// the camera feeds, the seismograph, the dev console — therefore agrees on one
    /// unambiguous answer to "where is it", which is exactly the property a game
    /// about reading positions off a screen needs.
    ///
    /// Ticked by <see cref="AIDirector"/> rather than by Unity, so the cast updates in
    /// a fixed order and a seeded night replays identically.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AnimatronicController : MonoBehaviour
    {
        [SerializeField] private AnimatronicDefinition definition;

        [Tooltip("Vertical offset applied when placing the model at a node.")]
        [SerializeField] private float groundOffset;

        [Tooltip("Procedural animation rig. Found in children when left empty.")]
        [SerializeField] private ServoAnimator servos;

        [Tooltip("What the head tracks. Defaults to the main camera.")]
        [SerializeField] private Transform lookTarget;

        private AnimatronicBehaviour _behaviour;
        private FacilityRuntime _facility;
        private AIDirector _director;
        private RandomSource _rng;
        private StateMachine<AnimatronicState> _machine;

        private float _rollTimer;
        private float _nextRollIn = 4f;
        private float _patienceTimer;
        private float _attackTimer;
        private float _retreatTimer;
        private bool _thresholdTellPlayed;

        // Transit
        private NodeId _transitFrom;
        private NodeId _transitTo;
        private float _transitTimer;
        private float _transitDuration;
        private FacilityLink _transitLink;

        public AnimatronicDefinition Definition => definition;
        public string Id => definition != null ? definition.id : name;
        public string DisplayName => definition != null ? definition.displayName : name;

        public int AiLevel { get; private set; }
        public NodeId CurrentNode { get; private set; }
        public AnimatronicState State => _machine != null ? _machine.Current : AnimatronicState.Dormant;
        public float TimeInState => _machine != null ? _machine.TimeInState : 0f;

        public bool IsInTransit { get; private set; }
        public NodeId TransitTarget => _transitTo;

        /// <summary>0..1 through the current transit. 0 when stationary.</summary>
        public float TransitProgress01 => _transitDuration <= 0f ? 0f : Mathf.Clamp01(_transitTimer / _transitDuration);

        /// <summary>Seconds until the next movement opportunity.</summary>
        public float NextRollIn => Mathf.Max(0f, _nextRollIn - _rollTimer);

        /// <summary>0..1 through the attack window. Drives the audio tell and the overlay.</summary>
        public float AttackProgress01 => definition == null || definition.attackWindowSeconds <= 0f
            ? 0f
            : Mathf.Clamp01(_attackTimer / definition.attackWindowSeconds);

        /// <summary>Normalised movement speed for the servo animator.</summary>
        public float MotionSpeed01 { get; private set; }

        public event Action<AnimatronicState, AnimatronicState> StateChanged;

        /// <summary>Raised when the character arrives at a node, with the noise it made.</summary>
        public event Action<NodeId, float> Arrived;

        // ---------------------------------------------------------------------
        // Setup
        // ---------------------------------------------------------------------

        public void Initialise(AIDirector director, FacilityRuntime facility, RandomSource rng, int aiLevel)
        {
            _director = director;
            _facility = facility;
            _rng = rng;
            AiLevel = Mathf.Clamp(aiLevel, 0, MovementRoll.MaxAiLevel);

            if (definition == null)
            {
                GLog.Error(LogChannel.AI, $"{name} has no AnimatronicDefinition; it will not run.", this);
                enabled = false;
                return;
            }

            _behaviour = AnimatronicBehaviour.Create(definition.behaviour);
            _behaviour.Initialise(this, facility, rng);

            CurrentNode = new NodeId(definition.homeNode);
            if (!facility.Graph.Contains(CurrentNode))
            {
                GLog.Error(LogChannel.AI,
                    $"{Id}: home node '{definition.homeNode}' is not in the layout. Falling back to the station's neighbour.");
                CurrentNode = facility.Graph.StationNode;
            }

            if (servos == null) servos = GetComponentInChildren<ServoAnimator>();
            if (lookTarget == null && Camera.main != null) lookTarget = Camera.main.transform;
            if (servos != null) servos.LookTarget = lookTarget;

            BuildStateMachine();
            SnapToNode(CurrentNode);
            ScheduleNextRoll();

            _machine.Begin(AiLevel > 0 ? AnimatronicState.Roam : AnimatronicState.Dormant);
            PublishState();
        }

        private void BuildStateMachine()
        {
            _machine = new StateMachine<AnimatronicState>(Id);
            _machine
                .Configure(AnimatronicState.Dormant, tick: TickDormant)
                .Configure(AnimatronicState.Roam, tick: TickRoam)
                .Configure(AnimatronicState.Stalk, tick: TickRoam)
                .Configure(AnimatronicState.Threshold, enter: EnterThreshold, tick: TickThreshold, exit: ExitThreshold)
                .Configure(AnimatronicState.Attack, tick: TickAttack)
                .Configure(AnimatronicState.Retreat, enter: EnterRetreat, tick: TickRetreat)
                .Configure(AnimatronicState.Stranded, tick: TickStranded);

            _machine.Changed += (from, to) =>
            {
                StateChanged?.Invoke(from, to);
                PublishState();
            };
        }

        private void PublishState()
            => EventBus.Publish(new AiStateChangedSignal(Id, State.ToString(), CurrentNode));

        // ---------------------------------------------------------------------
        // Tick — driven by the director
        // ---------------------------------------------------------------------

        public void Tick(float deltaTime)
        {
            if (_machine == null || definition == null) return;

            if (DebugFlags.IsDevBuild && DebugFlags.FreezeAI)
            {
                MotionSpeed01 = 0f;
                return;
            }

            if (IsInTransit)
            {
                TickTransit(deltaTime);
                _machine.Tick(0f);   // keep TimeInState honest without running logic mid-move
                DriveServos();
                return;
            }

            MotionSpeed01 = MathUtil.ExpDecay(MotionSpeed01, 0f, 6f, deltaTime);
            _machine.Tick(deltaTime);
            DriveServos();
        }

        /// <summary>
        /// Hands the presentation layer what it needs and nothing more. The animator
        /// never learns about nodes or states — only speed, agitation and whether the
        /// lamps are lit — which is what keeps Procedural free of any AI dependency.
        /// </summary>
        private void DriveServos()
        {
            if (servos == null) return;

            servos.Speed01 = MotionSpeed01;
            servos.EyesLit = AiLevel > 0 && State != AnimatronicState.Dormant;

            servos.Agitation01 = State switch
            {
                AnimatronicState.Threshold => Mathf.Lerp(0.45f, 0.95f, AttackProgress01),
                AnimatronicState.Attack => 1f,
                AnimatronicState.Stalk => 0.3f,
                AnimatronicState.Retreat => 0.15f,
                _ => 0.05f
            };
        }

        // ---------------------------------------------------------------------
        // States
        // ---------------------------------------------------------------------

        private void TickDormant(float dt)
        {
            if (AiLevel > 0) _machine.Transition(AnimatronicState.Roam);
        }

        private void TickRoam(float dt)
        {
            if (_behaviour.IsStranded())
            {
                _machine.Transition(AnimatronicState.Stranded);
                return;
            }

            if (_behaviour.IsAttackNode(CurrentNode))
            {
                _machine.Transition(AnimatronicState.Threshold);
                return;
            }

            _rollTimer += dt;
            if (_rollTimer < _nextRollIn) return;

            _rollTimer = 0f;
            ScheduleNextRoll();

            float pressure = _behaviour.MovementPressure() * _director.GlobalPressure(this);
            if (!MovementRoll.Attempt(_rng, AiLevel, pressure)) return;

            AttemptMove();
        }

        private void EnterThreshold()
        {
            _patienceTimer = 0f;
            _attackTimer = 0f;
            _thresholdTellPlayed = false;
            GLog.Info(LogChannel.AI, $"{Id} is at {CurrentNode}.");
        }

        private void ExitThreshold()
        {
            _attackTimer = 0f;
            _patienceTimer = 0f;
        }

        private void TickThreshold(float dt)
        {
            var station = _facility.StationNode;
            var link = _facility.Graph.FindLink(CurrentNode, station);

            if (link == null)
            {
                // Listed as an attack node but not actually adjacent — a layout error.
                GLog.Warn(LogChannel.AI,
                    $"{Id}: '{CurrentNode}' is an attack node with no link to the station. Retreating.");
                _machine.Transition(AnimatronicState.Retreat);
                return;
            }

            var barrier = _facility.GetBarrier(link.BarrierId);
            _behaviour.OnThresholdTick(dt, link, barrier);

            bool open = _facility.CanTraverse(link, CurrentNode, station,
                definition.traversal, definition.respectsBarriers, definition.lightAverse);
            open = _behaviour.CanBreach(open, link, barrier);

            if (open)
            {
                if (!_thresholdTellPlayed)
                {
                    _thresholdTellPlayed = true;
                    EventBus.Publish(new ScareSignal(0.45f, Id + "-threshold"));
                    _facility.Noise.Emit(CurrentNode, 0.5f, NoiseKind.Footstep);
                }

                _attackTimer += dt;
                if (_attackTimer >= definition.attackWindowSeconds)
                    _machine.Transition(AnimatronicState.Attack);
                return;
            }

            // Shut out. Bleed the window back down so slamming a door late still saves you.
            _attackTimer = Mathf.Max(0f, _attackTimer - dt * 1.5f);
            _thresholdTellPlayed = false;

            _patienceTimer += dt;
            if (_patienceTimer >= definition.patienceSeconds)
            {
                GLog.Info(LogChannel.AI, $"{Id} gave up at {CurrentNode}.");
                _machine.Transition(AnimatronicState.Retreat);
            }
        }

        private void TickAttack(float dt)
        {
            if (!_director.TryClaimAttack(this))
            {
                // Someone else has the kill. Hold at the threshold rather than double up.
                _machine.Transition(AnimatronicState.Threshold);
                return;
            }

            GLog.Info(LogChannel.AI, $"{Id} attacking out of {CurrentNode}.");
            EventBus.Publish(new AttackSignal(Id, CurrentNode));

            // God mode absorbs the attack upstream, so back off and let the night continue.
            _machine.Transition(AnimatronicState.Retreat);
        }

        private void EnterRetreat()
        {
            _retreatTimer = 0f;
            _director.ReleaseAttack(this);
        }

        private void TickRetreat(float dt)
        {
            _retreatTimer += dt;

            var home = new NodeId(definition.EffectiveRetreatNode);
            if (CurrentNode != home)
            {
                _rollTimer += dt;
                if (_rollTimer >= _nextRollIn)
                {
                    _rollTimer = 0f;
                    ScheduleNextRoll();

                    // Retreating always routes home, whatever the behaviour would prefer.
                    var step = StepTowardRetreat(home);
                    if (step.IsValid) TryStep(step);
                }
            }

            if (_retreatTimer >= definition.retreatCooldownSeconds)
                _machine.Transition(AnimatronicState.Roam);
        }

        private void TickStranded(float dt)
        {
            // Re-check every couple of seconds; conditions change when the pump does.
            _rollTimer += dt;
            if (_rollTimer < 2f) return;
            _rollTimer = 0f;

            if (!_behaviour.IsStranded())
                _machine.Transition(AnimatronicState.Roam);
        }

        private NodeId StepTowardRetreat(NodeId home)
        {
            var filter = _facility.MakeFilter(definition.traversal, respectsBarriers: false);
            var scratch = _director.PathScratch;
            return _facility.Graph.NextStepToward(CurrentNode, home, filter, scratch);
        }

        // ---------------------------------------------------------------------
        // Movement
        // ---------------------------------------------------------------------

        private void AttemptMove()
        {
            var desired = _behaviour.ChooseNextNode(CurrentNode);
            if (!desired.IsValid || desired == CurrentNode) return;

            // The final step into the station is an attack, handled by the threshold state.
            if (desired == _facility.StationNode) return;

            TryStep(desired);
        }

        private void TryStep(NodeId desired)
        {
            var link = _facility.Graph.FindLink(CurrentNode, desired);
            if (link == null) return;

            bool allowed = _facility.CanTraverse(link, CurrentNode, desired,
                definition.traversal, definition.respectsBarriers, definition.lightAverse);

            if (!allowed)
            {
                _behaviour.OnStepBlocked(desired, link);
                return;
            }

            BeginTransit(desired, link);
        }

        private void BeginTransit(NodeId target, FacilityLink link)
        {
            _transitFrom = CurrentNode;
            _transitTo = target;
            _transitLink = link;
            _transitTimer = 0f;
            _transitDuration = Mathf.Max(0.2f, link.TraverseSeconds);
            IsInTransit = true;

            GLog.Verbose(LogChannel.AI, $"{Id}: {_transitFrom} -> {_transitTo} ({_transitDuration:0.0}s)");
        }

        private void TickTransit(float dt)
        {
            _transitTimer += dt;
            float t = Mathf.Clamp01(_transitTimer / _transitDuration);

            var from = _facility.Graph.PositionOf(_transitFrom);
            var to = _facility.Graph.PositionOf(_transitTo);
            var position = Vector3.Lerp(from, to, MathUtil.SmoothStep01(t));
            position.y += groundOffset;
            transform.position = position;

            var forward = to - from;
            if (forward.sqrMagnitude > 0.001f)
            {
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f)
                {
                    var target = Quaternion.LookRotation(forward.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, target, 1f - Mathf.Exp(-6f * dt));
                }
            }

            MotionSpeed01 = Mathf.Clamp01(Vector3.Distance(from, to) / Mathf.Max(0.01f, _transitDuration) / 3f);

            if (t < 1f) return;

            IsInTransit = false;
            CurrentNode = _transitTo;
            SnapToNode(CurrentNode);

            // Arriving makes a noise, scaled by how well the link carries it.
            float loudness = Mathf.Clamp01(0.35f * (_transitLink != null ? _transitLink.NoiseTransmission : 0.5f) + 0.1f);
            _facility.Noise.Emit(CurrentNode, loudness, NoiseKind.Footstep);
            Arrived?.Invoke(CurrentNode, loudness);
            _behaviour.OnArrived(CurrentNode);

            PublishState();

            if (_behaviour.IsAttackNode(CurrentNode) && State != AnimatronicState.Retreat)
                _machine.Transition(AnimatronicState.Threshold);
            else if (State == AnimatronicState.Roam && _facility.Graph.HopsToStation(CurrentNode) <= 2)
                _machine.Transition(AnimatronicState.Stalk);
            else if (State == AnimatronicState.Stalk && _facility.Graph.HopsToStation(CurrentNode) > 2)
                _machine.Transition(AnimatronicState.Roam);
        }

        private void SnapToNode(NodeId node)
        {
            var position = _facility.Graph.PositionOf(node);
            position.y += groundOffset;
            transform.position = position;
        }

        private void ScheduleNextRoll()
            => _nextRollIn = MovementRoll.NextInterval(_rng, definition.movementIntervalSeconds, AiLevel, definition.intervalJitter);

        // ---------------------------------------------------------------------
        // Developer affordances
        // ---------------------------------------------------------------------

        public void SetLevel(int level)
        {
            AiLevel = Mathf.Clamp(level, 0, MovementRoll.MaxAiLevel);
            if (AiLevel == 0 && _machine != null) _machine.Transition(AnimatronicState.Dormant);
            else if (_machine != null && State == AnimatronicState.Dormant) _machine.Transition(AnimatronicState.Roam);
        }

        /// <summary>Teleports the character. Used by <c>ai.move</c>.</summary>
        public void DebugTeleport(NodeId node)
        {
            if (!_facility.Graph.Contains(node))
            {
                GLog.Warn(LogChannel.AI, $"No node '{node}' to move {Id} to.");
                return;
            }

            IsInTransit = false;
            CurrentNode = node;
            SnapToNode(node);
            _machine.Transition(_behaviour.IsAttackNode(node) ? AnimatronicState.Threshold : AnimatronicState.Roam, force: true);
            GLog.Info(LogChannel.AI, $"{Id} moved to {node}.");
        }

        public void DebugForceState(AnimatronicState state) => _machine.Transition(state, force: true);

        /// <summary>Current odds of the next roll succeeding, for the debug overlay.</summary>
        public float CurrentRollChance01 => _behaviour == null || _director == null
            ? 0f
            : MovementRoll.SuccessChance(AiLevel, _behaviour.MovementPressure() * _director.GlobalPressure(this));

        public string BehaviourSummary => _behaviour != null ? _behaviour.DebugSummary() : string.Empty;
    }
}
