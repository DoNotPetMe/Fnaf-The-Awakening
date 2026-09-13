using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>What a place in the cavern physically is. Drives geometry and audio.</summary>
    public enum NodeKind
    {
        /// <summary>The player's monitoring station. Exactly one per layout.</summary>
        Station,
        /// <summary>A large show cavern.</summary>
        Cavern,
        /// <summary>A built room — concrete, drywall, fluorescent.</summary>
        Room,
        /// <summary>A walkable tunnel between spaces.</summary>
        Adit,
        /// <summary>A natural squeeze only something small and limber gets through.</summary>
        Crawlway,
        /// <summary>A vertical shaft.</summary>
        Shaft,
        /// <summary>Permanently or seasonally under water.</summary>
        Watercourse,
        /// <summary>Below the station — the sump basin the pump draws from.</summary>
        Sump
    }

    /// <summary>
    /// How a body can move. An animatronic's capability mask is intersected with a
    /// link's allowed mask, which is what makes the cast route through the cave
    /// differently instead of all walking the same corridor.
    /// </summary>
    [Flags]
    public enum TraversalMask
    {
        None    = 0,
        /// <summary>Upright on two legs along a floor.</summary>
        Walk    = 1 << 0,
        /// <summary>Flat, through a squeeze — needs a light frame.</summary>
        Crawl   = 1 << 1,
        /// <summary>Vertical, needs claws or hooks.</summary>
        Climb   = 1 << 2,
        /// <summary>Submerged. Needs a sealed shell.</summary>
        Swim    = 1 << 3,
        /// <summary>Through silt and breakdown. Marlow only.</summary>
        Burrow  = 1 << 4,
        Any     = ~0
    }

    /// <summary>Broad map region, used to group the minimap and the seismograph.</summary>
    public enum FacilityZone { Station, Service, Show, Upper, Deep }

    /// <summary>A place. Immutable topology plus a little runtime state.</summary>
    [Serializable]
    public sealed class FacilityNode
    {
        public NodeId Id;
        public string DisplayName;
        public NodeKind Kind;
        public FacilityZone Zone;

        /// <summary>Centre of the space in world units. Also where the camera looks.</summary>
        public Vector3 Position;

        /// <summary>Rough interior extents, used by the geometry builder and audio reverb.</summary>
        public Vector3 Size;

        /// <summary>Whether a surveillance camera covers this node at all.</summary>
        public bool HasCamera;

        /// <summary>Fixed lighting present even with the floodlights off.</summary>
        public bool HasAmbientLight;

        /// <summary>
        /// How much of a sound made here reaches the station directly through rock.
        /// The seismograph reads this; it is why the Deep Gallery is felt, not heard.
        /// </summary>
        [Range(0f, 1f)] public float StationCoupling = 0.2f;

        // ---- Runtime ----------------------------------------------------------

        /// <summary>True while a floodlight is actually illuminating this node.</summary>
        [NonSerialized] public bool IsLit;

        /// <summary>Current noise level, written by <see cref="NoiseField"/>.</summary>
        [NonSerialized] public float NoiseLevel;

        public override string ToString() => $"{Id} ({DisplayName})";
    }

    /// <summary>
    /// A connection between two nodes.
    ///
    /// Links carry the interesting rules: which bodies fit, what the water has to be
    /// doing, whether a door stands in the way, and how much sound survives the trip.
    /// </summary>
    [Serializable]
    public sealed class FacilityLink
    {
        public NodeId A;
        public NodeId B;

        /// <summary>Movement types allowed through this link.</summary>
        public TraversalMask Allowed = TraversalMask.Walk;

        /// <summary>Seconds an animatronic spends crossing. Longer links read as distance.</summary>
        public float TraverseSeconds = 4f;

        /// <summary>Fraction of sound that survives the crossing, per hop.</summary>
        [Range(0f, 1f)] public float NoiseTransmission = 0.55f;

        /// <summary>
        /// Water level (0..1) this link needs to be *at least*. A swim route through
        /// the Styx Channel does not exist when the channel is drained.
        /// </summary>
        [Range(0f, 1f)] public float MinWater;

        /// <summary>
        /// Water level this link tolerates *at most*. A walking route drowns out.
        /// </summary>
        [Range(0f, 1f)] public float MaxWater = 1f;

        /// <summary>
        /// Id of the door or grate guarding this link, if any. Matched against
        /// <see cref="IFacilityBarrier.BarrierId"/>.
        /// </summary>
        public string BarrierId;

        /// <summary>
        /// Light-averse characters will not enter node B through this link while B is lit.
        /// </summary>
        public bool LightDeters;

        /// <summary>One-way links model drops and chutes — you can go down but not back up.</summary>
        public bool OneWay;

        public bool Connects(NodeId from, NodeId to)
            => (A == from && B == to) || (!OneWay && A == to && B == from);

        public NodeId Other(NodeId from) => A == from ? B : A;

        public override string ToString() => $"{A} {(OneWay ? "->" : "<->")} {B}";
    }

    /// <summary>A door, grate or shutter that can stand in an animatronic's way.</summary>
    public interface IFacilityBarrier
    {
        /// <summary>Matches <see cref="FacilityLink.BarrierId"/>.</summary>
        string BarrierId { get; }

        /// <summary>True while the barrier actually stops a body passing.</summary>
        bool IsBlocking { get; }

        /// <summary>Something is leaning on it. Used by Chorus, who does not take no for an answer.</summary>
        void ApplyPressure(float amount);
    }
}
