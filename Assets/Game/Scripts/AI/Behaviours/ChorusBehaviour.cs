using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI.Behaviours
{
    /// <summary>
    /// The Chorus — whatever it is that the four of them have been building down in
    /// the Deep Gallery since the water came.
    ///
    /// It ignores every defence the facility has, because it does not try to get past
    /// a blast door: it leans on one until the door stops being a door. Light does
    /// nothing. Water does nothing. There is no route that does not have it on it.
    ///
    /// It tracks the facility the way you track a running engine, so the only thing
    /// that turns it around is a station that has genuinely stopped — fan off, pump
    /// off, monitor down, lights out, generator shut down. Doing that costs air and
    /// costs water level, and it is supposed to be the worst few minutes of the game.
    /// </summary>
    public sealed class ChorusBehaviour : AnimatronicBehaviour
    {
        /// <summary>Pressure applied to a door per second of leaning on it.</summary>
        private const float PressurePerSecond = 13f;

        /// <summary>Below this total facility activity it starts losing the thread.</summary>
        private const float SilenceThreshold = 0.12f;

        /// <summary>Seconds of near-silence before it gives up and goes back down.</summary>
        private const float SilenceToBreak = 6f;

        private float _silenceTimer;

        /// <summary>
        /// How much the facility is advertising itself: running machinery, burning
        /// lights and a live monitor all count.
        /// </summary>
        private float FacilityActivity()
        {
            float activity = Mathf.Clamp01(Facility.Noise.TotalEnergy / 5f);

            if (Facility.Power.Generator.IsSupplying) activity += 0.25f;
            if (Facility.Surveillance.MonitorUp) activity += 0.15f;
            if (Facility.Ventilation.IsRunning) activity += 0.2f;
            if (Facility.Water.IsPumping) activity += 0.2f;

            foreach (var node in Graph.Nodes)
            {
                if (!node.IsLit) continue;
                activity += 0.1f;
                break;
            }

            return activity;
        }

        public override float MovementPressure()
        {
            float activity = FacilityActivity();

            // Not the usual noise affinity — this is tracking, and it is close to total.
            float pressure = Mathf.Lerp(0.05f, 2.1f, Mathf.Clamp01(activity));

            // Being watched does not deter it in the slightest.
            if (Facility.Surveillance.MonitorUp) pressure += 0.3f;

            return pressure;
        }

        public override NodeId ChooseNextNode(NodeId current)
        {
            var step = StepTowardStation(current);
            return step.IsValid ? step : RandomNeighbour(current);
        }

        public override void OnThresholdTick(float deltaTime, FacilityLink linkToStation, IFacilityBarrier barrier)
        {
            float activity = FacilityActivity();

            if (activity < SilenceThreshold)
            {
                _silenceTimer += deltaTime;
                if (_silenceTimer >= SilenceToBreak)
                {
                    _silenceTimer = 0f;
                    GLog.Info(LogChannel.AI, "The Chorus lost the station in the silence.");
                    EventBus.Publish(new AlertSignal("...it has stopped.", AlertSeverity.Info));
                    Owner.DebugForceState(AnimatronicState.Retreat);
                }
                return;
            }

            _silenceTimer = 0f;

            // Not picking the lock. Just leaning.
            if (barrier == null || !barrier.IsBlocking) return;

            barrier.ApplyPressure(PressurePerSecond * deltaTime);

            // The groan of a loaded door is the warning the player gets.
            if (Rng.Chance(MathUtil.ProbabilityForStep(0.7f, deltaTime)))
                Facility.Noise.Emit(Owner.CurrentNode, 0.45f, NoiseKind.Impact);
        }

        public override string DebugSummary()
        {
            float activity = FacilityActivity();
            return activity < SilenceThreshold
                ? $"losing you ({_silenceTimer:0.0}/{SilenceToBreak:0}s)"
                : $"tracking, activity {activity:0.00}";
        }
    }
}
