using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI
{
    /// <summary>
    /// Per-character decision making, separate from the movement machinery in
    /// <see cref="AnimatronicController"/>.
    ///
    /// The controller owns *how* a character moves — transit timing, thresholds,
    /// attack windows, the state machine. A behaviour only answers two questions:
    /// where would you like to go next, and how badly do you want to go right now.
    /// Adding a sixth animatronic is therefore one subclass, not a new pass through
    /// the controller.
    /// </summary>
    public abstract class AnimatronicBehaviour
    {
        protected AnimatronicController Owner { get; private set; }
        protected FacilityRuntime Facility { get; private set; }
        protected RandomSource Rng { get; private set; }

        protected AnimatronicDefinition Definition => Owner.Definition;
        protected FacilityGraph Graph => Facility.Graph;
        protected TraversalMask Capability => Definition.traversal;
        protected NodeId Station => Facility.StationNode;

        private readonly List<NodeId> _pathScratch = new List<NodeId>(16);
        private readonly List<NodeId> _neighbourScratch = new List<NodeId>(8);

        /// <summary>
        /// Routing ignores doors and light: a character should still walk up to a shut
        /// door rather than deciding the station is unreachable and going home. The
        /// controller validates the individual step before committing to it.
        /// </summary>
        private FacilityGraph.LinkFilter _routingFilter;

        public virtual void Initialise(AnimatronicController owner, FacilityRuntime facility, RandomSource rng)
        {
            Owner = owner;
            Facility = facility;
            Rng = rng;
            _routingFilter = facility.MakeFilter(Capability, respectsBarriers: false, lightAverse: false);
        }

        /// <summary>
        /// Multiplier on the effective AI level for the next roll. 1 is neutral.
        /// This is where each character's relationship with the facility lives.
        /// </summary>
        public virtual float MovementPressure()
        {
            float pressure = 1f;

            // Noise affinity. Positive is drawn to it, negative is disturbed by it.
            float localNoise = Facility.Noise.GetLevel(Owner.CurrentNode);
            float stationNoise = Facility.Noise.GetLevel(Station);
            float heard = Mathf.Max(localNoise * 0.4f, stationNoise * 0.8f);
            pressure += Definition.noiseAffinity * heard;

            // Bolder while the player is looking at a camera instead of the room.
            if (Facility.Surveillance.MonitorUp)
                pressure += Definition.monitorBoldness;

            return Mathf.Max(0.05f, pressure);
        }

        /// <summary>Where to go next, or <see cref="NodeId.None"/> to stay put this opportunity.</summary>
        public abstract NodeId ChooseNextNode(NodeId current);

        /// <summary>Called after the character finishes moving into a node.</summary>
        public virtual void OnArrived(NodeId node) { }

        /// <summary>
        /// Called when the controller refused a step because a door, a grate or a light
        /// was in the way. The default is to do nothing and try again next opportunity.
        /// </summary>
        public virtual void OnStepBlocked(NodeId desired, FacilityLink link) { }

        /// <summary>Called every frame while the character is at a threshold node.</summary>
        public virtual void OnThresholdTick(float deltaTime, FacilityLink linkToStation, IFacilityBarrier barrier) { }

        /// <summary>
        /// Last word on whether the character gets through the threshold this frame.
        ///
        /// <paramref name="defaultOpen"/> is what the facility's traversal rule already
        /// decided. Most characters accept it; Echo overrides it because a bolted grate
        /// is a probability to her rather than a wall.
        /// </summary>
        public virtual bool CanBreach(bool defaultOpen, FacilityLink link, IFacilityBarrier barrier) => defaultOpen;

        /// <summary>
        /// True when conditions have made this character's route impossible — Echo with
        /// the channel drained, Marlow with the basin full. Stranded characters idle
        /// instead of pointlessly re-pathing.
        /// </summary>
        public virtual bool IsStranded() => false;

        /// <summary>One line for the debug overlay.</summary>
        public virtual string DebugSummary() => string.Empty;

        // ---------------------------------------------------------------------
        // Helpers available to every behaviour
        // ---------------------------------------------------------------------

        /// <summary>Next hop along the shortest route to the station, ignoring doors.</summary>
        protected NodeId StepTowardStation(NodeId current) => StepToward(current, Station);

        protected NodeId StepToward(NodeId current, NodeId target)
        {
            if (current == target) return NodeId.None;
            if (!Graph.TryFindPath(current, target, _routingFilter, _pathScratch)) return NodeId.None;
            return _pathScratch.Count > 0 ? _pathScratch[0] : NodeId.None;
        }

        /// <summary>True when any route to the station exists under this body's capabilities.</summary>
        protected bool HasRouteToStation(NodeId current)
            => current == Station || Graph.TryFindPath(current, Station, _routingFilter, _pathScratch);

        protected int HopsToStation(NodeId current)
        {
            if (current == Station) return 0;
            return Graph.TryFindPath(current, Station, _routingFilter, _pathScratch) ? _pathScratch.Count : -1;
        }

        /// <summary>A neighbour picked at random from those currently reachable.</summary>
        protected NodeId RandomNeighbour(NodeId current)
        {
            Graph.GetNeighbours(current, _routingFilter, _neighbourScratch);
            return _neighbourScratch.Count == 0 ? NodeId.None : Rng.Pick(_neighbourScratch);
        }

        /// <summary>
        /// Picks a neighbour by noise, weighted by this character's affinity. A positive
        /// affinity walks toward the loudest neighbour; a negative one walks away.
        /// </summary>
        protected NodeId NoiseSeekingNeighbour(NodeId current)
        {
            Graph.GetNeighbours(current, _routingFilter, _neighbourScratch);
            if (_neighbourScratch.Count == 0) return NodeId.None;

            float affinity = Definition.noiseAffinity;
            var best = NodeId.None;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < _neighbourScratch.Count; i++)
            {
                var candidate = _neighbourScratch[i];
                float noise = Facility.Noise.GetLevel(candidate);

                // A little randomness stops the cast walking identical lines every night.
                float score = noise * affinity + Rng.Range(-0.15f, 0.15f);

                // Always weight the station direction a little, or they never arrive.
                int hops = Graph.HopsToStation(candidate);
                if (hops >= 0) score += Mathf.Max(0f, 0.6f - hops * 0.08f);

                if (score <= bestScore) continue;
                bestScore = score;
                best = candidate;
            }

            return best;
        }

        /// <summary>True when this node is one the character can strike the station from.</summary>
        public bool IsAttackNode(NodeId node)
        {
            var nodes = Definition.attackNodes;
            for (int i = 0; i < nodes.Count; i++)
                if (new NodeId(nodes[i]) == node) return true;
            return false;
        }

        /// <summary>Creates the behaviour instance a definition asks for.</summary>
        public static AnimatronicBehaviour Create(BehaviourKind kind)
        {
            switch (kind)
            {
                case BehaviourKind.Ceiling: return new Behaviours.VesperBehaviour();
                case BehaviourKind.Burrower: return new Behaviours.MarlowBehaviour();
                case BehaviourKind.Swimmer: return new Behaviours.EchoBehaviour();
                case BehaviourKind.Composite: return new Behaviours.ChorusBehaviour();
                case BehaviourKind.Host:
                default: return new Behaviours.BartyBehaviour();
            }
        }
    }
}
