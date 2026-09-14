using System;
using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>What the building is doing to you right now.</summary>
    public enum NightEventKind
    {
        None,
        /// <summary>Inflow spikes. The pump is suddenly not enough.</summary>
        SpringSurge,
        /// <summary>The set staggers. Less power than the board is asking for.</summary>
        Brownout,
        /// <summary>A duct collapses somewhere. Air goes twice as fast.</summary>
        VentFault,
        /// <summary>Movement in the deep. Everything hears it and comes up.</summary>
        Tremor,
        /// <summary>The multiplexer degrades. Every camera goes snowy at once.</summary>
        FeedDecay
    }

    /// <summary>
    /// Timed disturbances that give a night a shape.
    ///
    /// Without these a night is a monotone: the same five systems drifting at the same
    /// rate for six hours, with the only variation coming from where the cast happens
    /// to be. The player settles into a loop within about ninety seconds and then
    /// executes it until dawn. That is a system, not a night.
    ///
    /// An event breaks the loop by making one resource briefly, sharply wrong. The
    /// pump stops keeping up; the board browns out and the doors get slow; the air
    /// halves. Each lasts under two minutes and each is announced a few seconds before
    /// it lands, so the player gets to *choose* what to sacrifice rather than simply
    /// being hit. That choice is the entire point — an unannounced event is a tax, and
    /// an announced one is a decision.
    ///
    /// Scheduling is deterministic from the night's seed, so a seeded replay runs the
    /// same night, and the dev console can force any of them.
    /// </summary>
    public sealed class NightEvents
    {
        /// <summary>One scheduled disturbance.</summary>
        private readonly struct Scheduled
        {
            public readonly NightEventKind Kind;
            public readonly float AtProgress01;
            public readonly float Seconds;

            public Scheduled(NightEventKind kind, float atProgress01, float seconds)
            {
                Kind = kind;
                AtProgress01 = atProgress01;
                Seconds = seconds;
            }
        }

        /// <summary>Seconds of warning before an event lands.</summary>
        public const float WarningSeconds = 6f;

        private readonly List<Scheduled> _schedule = new List<Scheduled>(8);
        private int _next;

        private NightEventKind _active = NightEventKind.None;
        private float _remaining;
        private float _duration;

        private NightEventKind _warning = NightEventKind.None;
        private float _warningRemaining;

        public NightEventKind Active => _active;

        /// <summary>0..1 through the active event; 0 when nothing is running.</summary>
        public float Progress01 => _duration <= 0f ? 0f : 1f - Mathf.Clamp01(_remaining / _duration);

        /// <summary>Seconds left of the active event.</summary>
        public float Remaining => _remaining;

        /// <summary>The event about to land, or None.</summary>
        public NightEventKind Warning => _warning;

        /// <summary>Seconds until the warned event lands.</summary>
        public float WarningRemaining => _warningRemaining;

        /// <summary>Fires a few seconds before an event begins, so the player can act.</summary>
        public event Action<NightEventKind> Warned;

        public event Action<NightEventKind> Began;
        public event Action<NightEventKind> Ended;

        // ---- Multipliers the runtime reads -----------------------------------
        //
        // These are the whole external surface of the system: everything else it does
        // is presentation. FacilityRuntime folds them into the scales it already
        // passes down, so no individual system needs to know events exist.

        public float WaterInflowMultiplier => _active == NightEventKind.SpringSurge ? 2.4f : 1f;
        public float AirDecayMultiplier => _active == NightEventKind.VentFault ? 2.2f : 1f;

        /// <summary>Fraction of the generator's rating available. Below 1 during a brownout.</summary>
        public float PowerCapacityMultiplier => _active == NightEventKind.Brownout ? 0.62f : 1f;

        /// <summary>Extra camera wear per hour, multiplied into the surveillance decay.</summary>
        public float FeedWearMultiplier => _active == NightEventKind.FeedDecay ? 3.5f : 1f;

        /// <summary>
        /// Builds the night's schedule.
        ///
        /// One event per hour from the second onward, so the first hour is always
        /// clean — a night that opens with a surge teaches the player nothing except
        /// that the game is unfair. Kinds are drawn without immediate repeats, and the
        /// count scales with the night number so night one gets two disturbances and
        /// night six gets five.
        /// </summary>
        public void ResetForNight(RandomSource nightRng, int night, float intensity01 = 1f)
        {
            _schedule.Clear();
            _next = 0;

            _active = NightEventKind.None;
            _warning = NightEventKind.None;
            _remaining = 0f;
            _duration = 0f;
            _warningRemaining = 0f;

            if (nightRng == null || intensity01 <= 0f) return;

            int count = Mathf.Clamp(1 + night / 2, 1, 5);
            count = Mathf.Max(1, Mathf.RoundToInt(count * Mathf.Clamp01(intensity01)));

            var kinds = new List<NightEventKind>
            {
                NightEventKind.SpringSurge,
                NightEventKind.Brownout,
                NightEventKind.VentFault,
                NightEventKind.Tremor,
                NightEventKind.FeedDecay
            };

            var previous = NightEventKind.None;

            for (int i = 0; i < count; i++)
            {
                // Spread across the back five sixths of the night, jittered within the
                // slot so the player cannot set a watch by them.
                float slot = (i + 1f) / (count + 1f);
                float at = Mathf.Lerp(0.18f, 0.92f, slot) + nightRng.Range(-0.04f, 0.04f);

                var kind = kinds[nightRng.Range(0, kinds.Count)];
                if (kind == previous) kind = kinds[(kinds.IndexOf(kind) + 1) % kinds.Count];
                previous = kind;

                _schedule.Add(new Scheduled(kind, Mathf.Clamp01(at), DurationOf(kind, nightRng)));
            }

            _schedule.Sort((a, b) => a.AtProgress01.CompareTo(b.AtProgress01));

            GLog.Info(LogChannel.Facility, $"Night events scheduled: {count}.");
        }

        private static float DurationOf(NightEventKind kind, RandomSource rng) => kind switch
        {
            // Long enough that ignoring it costs you, short enough that the right
            // answer is usually to ride it out rather than to panic.
            NightEventKind.SpringSurge => rng.Range(55f, 80f),
            NightEventKind.Brownout => rng.Range(38f, 55f),
            NightEventKind.VentFault => rng.Range(45f, 70f),
            NightEventKind.FeedDecay => rng.Range(50f, 75f),
            // A tremor is an instant, not a state. It exists to move the cast.
            NightEventKind.Tremor => 3f,
            _ => 30f
        };

        public void Tick(float realDelta, float nightProgress01)
        {
            if (_warning != NightEventKind.None)
            {
                _warningRemaining -= realDelta;
                if (_warningRemaining <= 0f)
                {
                    var kind = _warning;
                    _warning = NightEventKind.None;
                    Begin(kind);
                }
            }

            if (_active != NightEventKind.None)
            {
                _remaining -= realDelta;
                if (_remaining <= 0f) End();
                return;
            }

            // Nothing running and nothing warned: see whether the next one is due.
            if (_warning != NightEventKind.None || _next >= _schedule.Count) return;

            if (nightProgress01 < _schedule[_next].AtProgress01) return;

            _warning = _schedule[_next].Kind;
            _warningRemaining = WarningSeconds;

            GLog.Info(LogChannel.Facility, $"Night event inbound: {_warning}.");
            Warned?.Invoke(_warning);
        }

        private void Begin(NightEventKind kind)
        {
            // The scheduled entry is consumed here rather than when it was warned, so a
            // forced event from the dev console does not eat a scheduled one.
            if (_next < _schedule.Count && _schedule[_next].Kind == kind)
            {
                _duration = _schedule[_next].Seconds;
                _next++;
            }
            else
            {
                _duration = DurationOf(kind, new RandomSource(unchecked(Environment.TickCount)));
            }

            _active = kind;
            _remaining = _duration;

            GLog.Info(LogChannel.Facility, $"Night event: {kind} for {_duration:0}s.");
            Began?.Invoke(kind);
        }

        private void End()
        {
            var kind = _active;
            _active = NightEventKind.None;
            _remaining = 0f;
            _duration = 0f;

            GLog.Info(LogChannel.Facility, $"Night event over: {kind}.");
            Ended?.Invoke(kind);
        }

        /// <summary>Starts one now, skipping the warning. Used by <c>night.event</c>.</summary>
        public void Force(NightEventKind kind)
        {
            if (kind == NightEventKind.None)
            {
                if (_active != NightEventKind.None) End();
                _warning = NightEventKind.None;
                return;
            }

            _warning = NightEventKind.None;
            Begin(kind);
        }

        /// <summary>The line the HUD shows. Written as the building would report it.</summary>
        public static string Describe(NightEventKind kind) => kind switch
        {
            NightEventKind.SpringSurge => "INFLOW SURGE — the pump is not keeping up",
            NightEventKind.Brownout => "BROWNOUT — the set is down on power",
            NightEventKind.VentFault => "DUCT FAULT — air is going twice as fast",
            NightEventKind.Tremor => "TREMOR — something moved in the deep",
            NightEventKind.FeedDecay => "MULTIPLEXER FAULT — every feed is degrading",
            _ => ""
        };

        /// <summary>The one-line warning, a few seconds before it lands.</summary>
        public static string Warn(NightEventKind kind) => kind switch
        {
            NightEventKind.SpringSurge => "Gauge climbing fast",
            NightEventKind.Brownout => "The set is labouring",
            NightEventKind.VentFault => "Duct pressure dropping",
            NightEventKind.Tremor => "Geophones lifting",
            NightEventKind.FeedDecay => "Multiplexer running hot",
            _ => ""
        };

        /// <summary>Which resource the player should be looking at. Used to colour the banner.</summary>
        public static AlertSeverity SeverityOf(NightEventKind kind) => kind switch
        {
            NightEventKind.Tremor => AlertSeverity.Critical,
            NightEventKind.None => AlertSeverity.Info,
            _ => AlertSeverity.Warning
        };
    }
}
