using System;
using UnityEngine;

namespace Grotto.Procedural
{
    /// <summary>Which animal the costume is meant to be.</summary>
    public enum Species
    {
        /// <summary>Barty. Heavy, barrel-chested, prospector's hat.</summary>
        Bear,
        /// <summary>Vesper. Light frame, folded wings, ears bigger than her head.</summary>
        Bat,
        /// <summary>Marlow. Squat, wide shoulders, digging claws, welding goggles.</summary>
        Mole,
        /// <summary>Echo. Low, long, tailed, with a spinal frill.</summary>
        Salamander,
        /// <summary>Chorus. Parts from all four, assembled by something with no hands.</summary>
        Composite
    }

    /// <summary>
    /// Proportions and colours for one character's generated model.
    ///
    /// Lives in <c>Grotto.Procedural</c> rather than next to the AI definition that
    /// uses it, so the mesh factory never has to know that animatronics have
    /// behaviours — the dependency runs one way, AI to Procedural.
    /// </summary>
    [Serializable]
    public class AnimatronicModelSpec
    {
        [Header("Build")]
        public Species species = Species.Bear;

        [Tooltip("Overall height in metres, floor to the top of the skull.")]
        [Range(0.8f, 3f)] public float height = 1.95f;

        [Tooltip("Torso and limb thickness. 1 is the reference build.")]
        [Range(0.5f, 2f)] public float bulk = 1f;

        [Tooltip("Head size relative to the body. Mascots are always over 1.")]
        [Range(0.6f, 2f)] public float headScale = 1.15f;

        [Header("Colour")]
        public Color shellPrimary = new Color(0.45f, 0.27f, 0.15f);
        public Color shellSecondary = new Color(0.72f, 0.55f, 0.32f);
        public Color fabric = new Color(0.35f, 0.12f, 0.14f);
        public Color eyeGlow = new Color(1f, 0.78f, 0.35f);

        [Header("Condition")]
        [Tooltip("0 is showroom, 1 is thirty years under a hill. Drives grime and missing panels.")]
        [Range(0f, 1f)] public float wear = 0.65f;

        [Tooltip("Panels are missing and the frame shows through.")]
        public bool exposedEndoskeleton = true;

        [Tooltip("Seed for the per-character random details.")]
        public int seed = 1;

        /// <summary>Scales a reference-build measurement to this character's height.</summary>
        public float Scaled(float referenceMetres) => referenceMetres * (height / 1.95f);
    }
}
