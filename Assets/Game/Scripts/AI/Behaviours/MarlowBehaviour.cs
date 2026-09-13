using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI.Behaviours
{
    /// <summary>
    /// Marlow — the Mole, from the "Down Below" segment of the show.
    ///
    /// He is the price of running the pump. His only way into the control room is to
    /// dig the silt between the generator bay and the sump basin, and silt has to be
    /// dry to dig. Keep the basin drained and Marlow has a road; let it fill and he
    /// has nothing.
    ///
    /// There is a deliberate middle band. Between the diggable and wadeable marks he
    /// can still get into the basin and back out, but cannot break through the floor —
    /// so the player sees him sitting under the station on CAM 03, unable to act. That
    /// band is where the game teaches you the water dial has a *shape*, not a switch.
    /// </summary>
    public sealed class MarlowBehaviour : AnimatronicBehaviour
    {
        private static readonly NodeId Sump = new NodeId("SUMP");
        private static readonly NodeId Generator = new NodeId("GEN");
        private static readonly NodeId Workshop = new NodeId("WORKSHOP");

        public override float MovementPressure()
        {
            float pressure = base.MovementPressure();

            // The drier the silt, the faster he works.
            float water = Facility.Water.Level01;
            float dryness = Mathf.Clamp01(1f - water / Mathf.Max(0.01f, GrottoSpringsLayout.SumpDiggable));
            pressure *= Mathf.Lerp(0.6f, 1.6f, dryness);

            return pressure;
        }

        public override NodeId ChooseNextNode(NodeId current)
        {
            // Flushed out: the basin came up around him while he was working.
            if (current == Sump && Facility.Water.Level01 > GrottoSpringsLayout.SumpWadeable * 0.95f)
            {
                var out_ = StepToward(current, Generator);
                if (out_.IsValid) return out_;
            }

            var toStation = StepTowardStation(current);
            if (toStation.IsValid) return toStation;

            // No road tonight — potter about the service side making noise.
            var loiter = Rng.Chance(0.5f) ? Generator : Workshop;
            var step = StepToward(current, loiter);
            return step.IsValid ? step : RandomNeighbour(current);
        }

        /// <summary>
        /// A bolted grate stops him outright, and so does a wet basin — you cannot
        /// tunnel through standing water. This is the rule that creates the middle band.
        /// </summary>
        public override bool CanBreach(bool defaultOpen, FacilityLink link, IFacilityBarrier barrier)
        {
            if (!defaultOpen) return false;
            return Facility.Water.Level01 <= GrottoSpringsLayout.SumpDiggable;
        }

        public override bool IsStranded() => !HasRouteToStation(Owner.CurrentNode);

        public override string DebugSummary()
        {
            float water = Facility.Water.Level01;
            string gate = water <= GrottoSpringsLayout.SumpDiggable ? "can dig"
                : water <= GrottoSpringsLayout.SumpWadeable ? "basin only"
                : "shut out";
            return $"water {water:0.00} — {gate}";
        }
    }
}
