using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI.Behaviours
{
    /// <summary>
    /// Echo — the Salamander, who sang the closing number and never left the water.
    ///
    /// Marlow's mirror. She needs the Styx Channel deep enough to swim, so she is the
    /// price of *not* running the pump. Between them the water level stops being a
    /// meter to keep low and becomes a dial with a threat at each end.
    ///
    /// The grate is not a wall to her, only odds. She is flat, she is patient, and a
    /// full basin floats the grate off its seat — so the very condition that lets her
    /// reach the station is the one that weakens the thing meant to stop her.
    /// </summary>
    public sealed class EchoBehaviour : AnimatronicBehaviour
    {
        private int _grateAttempts;

        private NodeId Deep => Facility.DeepNode;

        /// <summary>The deep water she falls back to when the channel drops.</summary>
        private NodeId Shelter => Owner.RetreatNode;

        public override float MovementPressure()
        {
            float pressure = base.MovementPressure();

            // Deeper water, freer movement.
            float depth = MathUtil.Remap01(
                Facility.Water.Level01, Facility.Gates.swimmable, 1f);
            pressure *= Mathf.Lerp(0.7f, 1.75f, depth);

            return pressure;
        }

        public override NodeId ChooseNextNode(NodeId current)
        {
            var toStation = StepTowardStation(current);
            if (toStation.IsValid) return toStation;

            // Channel too shallow. Hold in the deep water and wait for the pump to stop.
            var retreat = StepToward(current, Facility.Water.Level01 > 0.4f ? Shelter : Deep);
            return retreat.IsValid ? retreat : NodeId.None;
        }

        /// <summary>
        /// The grate is a probability, not a barrier. Each attempt rolls against how
        /// well the bolts are holding, which decays as the basin fills.
        /// </summary>
        public override bool CanBreach(bool defaultOpen, FacilityLink link, IFacilityBarrier barrier)
        {
            if (defaultOpen) return true;
            if (!(barrier is SumpGrate grate)) return false;

            // One roll per attack-window's worth of effort, not one per frame.
            _grateAttempts++;
            if (_grateAttempts < 60) return false;
            _grateAttempts = 0;

            bool through = Rng.Chance(grate.SwimmerPassChance * 0.25f);
            if (through)
            {
                GLog.Info(LogChannel.AI, "Echo worked the grate off its seat.");
                EventBus.Publish(new ScareSignal(0.6f, "echo-grate"));
                Facility.Noise.Emit(link.A, 0.8f, NoiseKind.Water);
            }
            return through;
        }

        public override bool IsStranded()
        {
            // Not literally unreachable — she simply has nothing to do until it rains.
            return Facility.Water.Level01 < Facility.Gates.swimmable * 0.85f
                   && !HasRouteToStation(Owner.CurrentNode);
        }

        public override string DebugSummary()
        {
            float water = Facility.Water.Level01;
            return water >= Facility.Gates.swimmable
                ? $"channel open ({water:0.00})"
                : $"beached ({water:0.00} < {Facility.Gates.swimmable:0.00})";
        }
    }
}
