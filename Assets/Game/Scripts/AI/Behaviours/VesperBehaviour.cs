using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI.Behaviours
{
    /// <summary>
    /// Vesper — the Cave Bat, and the reason the cable chase has no door.
    ///
    /// She uses the karst crawlways above the ceiling, where there are no cameras at
    /// all: the seismograph is the only warning you get. Doors are irrelevant to her.
    /// Light is not — she will not put her head into a lit chase.
    ///
    /// Her signature inversion is noise. She navigates by ear, so a loud facility
    /// confuses her and a quiet one lets her move freely. Running the fan is therefore
    /// a defence against Vesper and an invitation to Barty at the same time, which is
    /// the shape of every real decision in this game.
    ///
    /// Turned back three times, she drops out of the karst into the Midway and starts
    /// the long climb again — so holding the light is a genuine reprieve, not a stall.
    /// </summary>
    public sealed class VesperBehaviour : AnimatronicBehaviour
    {
        private static readonly NodeId Chase = new NodeId("CHASE");
        private static readonly NodeId CrawlB = new NodeId("CRAWL_B");
        private static readonly NodeId CrawlA = new NodeId("CRAWL_A");
        private static readonly NodeId Midway = new NodeId("MIDWAY");

        private const int RebuffsBeforeReset = 3;

        private int _rebuffs;
        private bool _resetting;

        public override float MovementPressure()
        {
            float pressure = base.MovementPressure();

            // Quiet is her element. A silent facility roughly doubles her rate; a
            // facility running the fan and the pump barely lets her move at all.
            float ambient = Mathf.Clamp01(Facility.Noise.TotalEnergy / 4f);
            pressure *= Mathf.Lerp(1.8f, 0.45f, ambient);

            return pressure;
        }

        public override NodeId ChooseNextNode(NodeId current)
        {
            if (_resetting)
            {
                // Falling back out of the ceiling to begin the climb again.
                var down = StepToward(current, Midway);
                if (down.IsValid) return down;

                _resetting = false;
                _rebuffs = 0;
            }

            // If the chase is lit, hold in the crawlway rather than crowding the mouth.
            var chaseNode = Graph.Node(Chase);
            if (chaseNode != null && chaseNode.IsLit && current == CrawlB)
                return NodeId.None;

            var toChase = StepToward(current, Chase);
            return toChase.IsValid ? toChase : RandomNeighbour(current);
        }

        public override void OnStepBlocked(NodeId desired, FacilityLink link)
        {
            if (desired != Chase && !link.LightDeters) return;

            _rebuffs++;
            GLog.Verbose(LogChannel.AI, $"Vesper turned back by the light ({_rebuffs}/{RebuffsBeforeReset}).");

            if (_rebuffs < RebuffsBeforeReset) return;

            // Enough. She lets go and drops into the Midway.
            _resetting = true;
            _rebuffs = 0;
            Facility.Noise.Emit(CrawlA, 0.55f, NoiseKind.Impact);
            EventBus.Publish(new ScareSignal(0.3f, "vesper-drop"));
        }

        public override void OnArrived(NodeId node)
        {
            if (node == Midway) _resetting = false;
        }

        public override string DebugSummary()
            => _resetting ? "dropping to Midway" : $"rebuffs {_rebuffs}/{RebuffsBeforeReset}";
    }
}
