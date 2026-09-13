using System;
using System.Collections.Generic;

namespace Grotto.Core
{
    /// <summary>
    /// Enum-keyed state machine with enter/tick/exit hooks.
    ///
    /// Enum-keyed rather than class-per-state on purpose: the current state is then a
    /// value the debug overlay and the dev console can print, filter and force
    /// without reflection, which is most of what makes an AI bug findable.
    /// </summary>
    public sealed class StateMachine<TState> where TState : struct, Enum
    {
        private readonly Dictionary<TState, Action> _onEnter = new Dictionary<TState, Action>();
        private readonly Dictionary<TState, Action<float>> _onTick = new Dictionary<TState, Action<float>>();
        private readonly Dictionary<TState, Action> _onExit = new Dictionary<TState, Action>();

        /// <summary>Human-readable owner name, used in logs.</summary>
        public string Owner { get; }

        public TState Current { get; private set; }
        public TState Previous { get; private set; }

        /// <summary>Seconds spent in <see cref="Current"/>.</summary>
        public float TimeInState { get; private set; }

        /// <summary>Total transitions since construction — a cheap thrash detector.</summary>
        public int TransitionCount { get; private set; }

        /// <summary>Fires as (from, to) after the exit hook and before the enter hook.</summary>
        public event Action<TState, TState> Changed;

        public StateMachine(string owner, TState initial = default)
        {
            Owner = owner;
            Current = Previous = initial;
        }

        /// <summary>Registers hooks for a state. Any hook may be null.</summary>
        public StateMachine<TState> Configure(TState state, Action enter = null, Action<float> tick = null, Action exit = null)
        {
            if (enter != null) _onEnter[state] = enter;
            if (tick != null) _onTick[state] = tick;
            if (exit != null) _onExit[state] = exit;
            return this;
        }

        /// <summary>Runs the initial state's enter hook. Call once, after configuring.</summary>
        public void Begin(TState state)
        {
            Current = Previous = state;
            TimeInState = 0f;
            if (_onEnter.TryGetValue(state, out var enter)) enter();
        }

        /// <summary>
        /// Transitions to <paramref name="next"/>. A transition to the current state is
        /// ignored unless <paramref name="force"/> is set (re-entering a state is
        /// occasionally what you want — a fresh roam target, for instance).
        /// </summary>
        public void Transition(TState next, bool force = false)
        {
            if (!force && EqualityComparer<TState>.Default.Equals(next, Current)) return;

            var from = Current;
            if (_onExit.TryGetValue(from, out var exit)) exit();

            Previous = from;
            Current = next;
            TimeInState = 0f;
            TransitionCount++;

            Changed?.Invoke(from, next);

            if (_onEnter.TryGetValue(next, out var enter)) enter();

            GLog.Verbose(LogChannel.AI, $"{Owner}: {from} -> {next}");
        }

        public void Tick(float deltaTime)
        {
            TimeInState += deltaTime;
            if (_onTick.TryGetValue(Current, out var tick)) tick(deltaTime);
        }

        public bool Is(TState state) => EqualityComparer<TState>.Default.Equals(Current, state);
    }
}
