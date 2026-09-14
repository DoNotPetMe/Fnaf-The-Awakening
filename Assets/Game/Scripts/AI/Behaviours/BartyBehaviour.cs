using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI.Behaviours
{
    /// <summary>
    /// Bartholomew "Barty" Bellows — Prospector Bear, and the host of the show.
    ///
    /// The straightforward one, and the one who teaches the game's central lesson.
    /// He walks the public routes, he is stopped cold by a shut blast door, and he
    /// comes toward noise. Since closing that door is one of the loudest things in the
    /// facility, defending against Barty is what summons Barty.
    ///
    /// He commits to one adit at a time rather than re-deciding every hop, so that a
    /// player who checks CAM 01 and sees him gone can reasonably conclude he is coming
    /// round the south. Reading him is supposed to be possible.
    /// </summary>
    public sealed class BartyBehaviour : AnimatronicBehaviour
    {
        private NodeId _committedApproach;
        private float _commitmentRemaining;

        public override void Initialise(AnimatronicController owner, FacilityRuntime facility, RandomSource rng)
        {
            base.Initialise(owner, facility, rng);
            ChooseApproach();
        }

        public override float MovementPressure()
        {
            float pressure = base.MovementPressure();

            // A door cycling is unmistakable, and he is already halfway there.
            float doorNoise = Mathf.Max(
                Facility.Noise.GetLevel(Facility.NorthApproach),
                Facility.Noise.GetLevel(Facility.SouthApproach));
            pressure += doorNoise * 0.45f;

            return pressure;
        }

        public override NodeId ChooseNextNode(NodeId current)
        {
            _commitmentRemaining -= Time.deltaTime;
            if (_commitmentRemaining <= 0f || !_committedApproach.IsValid) ChooseApproach();

            // Within reach of his chosen adit, head straight for it.
            var toApproach = StepToward(current, _committedApproach);
            if (toApproach.IsValid)
            {
                int hops = Graph.HopsToStation(current);

                // Far out he meanders toward interesting sounds; close in he stops
                // sightseeing. The change of gear is the tell that he has committed.
                if (hops > 3 && Rng.Chance(0.45f))
                {
                    var wander = NoiseSeekingNeighbour(current);
                    if (wander.IsValid) return wander;
                }

                return toApproach;
            }

            return NoiseSeekingNeighbour(current);
        }

        public override void OnStepBlocked(NodeId desired, FacilityLink link)
        {
            // Something is shut in his face out on the map — try the other side.
            ChooseApproach(flip: true);
        }

        private void ChooseApproach(bool flip = false)
        {
            bool north = flip
                ? _committedApproach == Facility.SouthApproach
                : Rng.Chance(0.5f);

            _committedApproach = north ? Facility.NorthApproach : Facility.SouthApproach;
            _commitmentRemaining = Rng.Range(25f, 55f);

            GLog.Verbose(LogChannel.AI, $"Barty is working toward {_committedApproach}.");
        }

        public override string DebugSummary()
            => $"approach {_committedApproach} for {Mathf.Max(0f, _commitmentRemaining):0}s";
    }
}
