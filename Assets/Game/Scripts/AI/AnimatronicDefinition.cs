using System.Collections.Generic;
using UnityEngine;
using Grotto.Facility;
using Grotto.Procedural;

namespace Grotto.AI
{
    /// <summary>Which behaviour class drives this character.</summary>
    public enum BehaviourKind
    {
        /// <summary>Barty. Walks the show floor, respects doors, comes when he hears you.</summary>
        Host,
        /// <summary>Vesper. The crawlways and the cable chase; light turns her back, quiet speeds her up.</summary>
        Ceiling,
        /// <summary>Marlow. Digs the sump when the basin is dry.</summary>
        Burrower,
        /// <summary>Echo. Swims the channel when it is deep.</summary>
        Swimmer,
        /// <summary>Chorus. Does not respect doors — leans on them until they fail.</summary>
        Composite
    }

    /// <summary>
    /// Everything that makes one animatronic different from another.
    ///
    /// The cast is deliberately *not* five reskins of one chase behaviour. Each
    /// character is gated by a different facility system, so the player's defence
    /// against one is what exposes them to the next:
    ///
    ///   Barty  — doors stop him, but the door is loud and he comes to noise
    ///   Vesper — light stops her, and light is the one thing noise cannot help with
    ///   Marlow — needs the sump dry, so he is the price of running the pump
    ///   Echo   — needs the channel deep, so she is the price of not running it
    ///   Chorus — stopped by nothing except a facility that is completely dark and silent
    /// </summary>
    [CreateAssetMenu(menuName = "Grotto/Animatronic", fileName = "Animatronic_")]
    public sealed class AnimatronicDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Lowercase, stable. Used by night definitions, saves and the dev console.")]
        public string id = "barty";

        public string displayName = "Bartholomew Bellows";

        [TextArea(3, 8)]
        public string dossier = "";

        public BehaviourKind behaviour = BehaviourKind.Host;

        [Header("Movement")]
        [Tooltip("What this body can physically do. Intersected with each link's allowed mask.")]
        public TraversalMask traversal = TraversalMask.Walk;

        [Tooltip("Node the character starts the night in.")]
        public string homeNode = "STAGE";

        [Tooltip("Node it falls back to when it gives up an approach. Blank means home.")]
        public string retreatNode = "";

        [Tooltip("Seconds between movement opportunities at AI level 10. Faster characters use less.")]
        [Range(2f, 30f)] public float movementIntervalSeconds = 8f;

        [Tooltip("Random +/- fraction applied to each interval so the cast never syncs up.")]
        [Range(0f, 0.6f)] public float intervalJitter = 0.25f;

        [Tooltip("World movement speed between nodes. Presentation only.")]
        public float moveSpeed = 2.2f;

        [Header("Reactions")]
        [Tooltip("A shut door or a locked grate stops this character outright.")]
        public bool respectsBarriers = true;

        [Tooltip("Will not enter a lit node through a light-deterring link.")]
        public bool lightAverse;

        [Tooltip("How strongly noise pulls this character toward it. Negative means noise repels.")]
        [Range(-1f, 1f)] public float noiseAffinity = 0.6f;

        [Tooltip("Extra movement chance while the player has the monitor up.")]
        [Range(0f, 1f)] public float monitorBoldness = 0.25f;

        [Header("Attack")]
        [Tooltip("Nodes this character can strike the station from. Usually its side of a door.")]
        public List<string> attackNodes = new List<string>();

        [Tooltip("Seconds spent at the threshold before striking, once the way is open.")]
        [Range(0.5f, 8f)] public float attackWindowSeconds = 2.4f;

        [Tooltip("Seconds it will wait at a blocked threshold before giving up.")]
        [Range(2f, 60f)] public float patienceSeconds = 12f;

        [Tooltip("Seconds it stays away after a failed approach.")]
        [Range(2f, 90f)] public float retreatCooldownSeconds = 18f;

        [Header("Presentation")]
        public AnimatronicModelSpec model = new AnimatronicModelSpec();

        [Tooltip("Colour used on the map and the debug overlay.")]
        public Color mapColor = new Color(0.9f, 0.55f, 0.2f);

        /// <summary>Resolved retreat node, falling back to home.</summary>
        public string EffectiveRetreatNode =>
            string.IsNullOrWhiteSpace(retreatNode) ? homeNode : retreatNode;

        private void OnValidate()
        {
            if (!string.IsNullOrEmpty(id)) id = id.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(displayName)) displayName = id;
            if (traversal == TraversalMask.None) traversal = TraversalMask.Walk;
        }
    }
}
