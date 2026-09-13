using UnityEngine;

namespace Grotto.Procedural
{
    /// <summary>
    /// Bone references for a generated animatronic.
    ///
    /// Rigid parts parented to a transform hierarchy, not a skinned mesh. That is not
    /// a shortcut — an animatronic *is* a rigid mechanism. Moulded shell over a steel
    /// frame does not deform, it pivots, and a hierarchy of rigid parts reproduces
    /// that exactly while costing nothing to build and nothing to skin.
    /// </summary>
    public sealed class AnimatronicRig : MonoBehaviour
    {
        [Header("Spine")]
        public Transform Hips;
        public Transform Spine;
        public Transform Chest;
        public Transform Neck;
        public Transform Head;
        public Transform Jaw;

        [Header("Arms")]
        public Transform ShoulderLeft;
        public Transform ShoulderRight;
        public Transform UpperArmLeft;
        public Transform UpperArmRight;
        public Transform ForearmLeft;
        public Transform ForearmRight;
        public Transform HandLeft;
        public Transform HandRight;

        [Header("Legs")]
        public Transform ThighLeft;
        public Transform ThighRight;
        public Transform ShinLeft;
        public Transform ShinRight;
        public Transform FootLeft;
        public Transform FootRight;

        [Header("Extras")]
        public Transform Tail;
        public Renderer[] EyeRenderers;
        public Material EyeMaterial;

        /// <summary>Eye height, for camera framing and for the jumpscare to aim at.</summary>
        public float EyeHeight => Head != null ? Head.position.y - transform.position.y : 1.6f;

        /// <summary>Every bone in a fixed order, for the servo animator's idle jitter.</summary>
        public Transform[] AllBones()
        {
            return new[]
            {
                Hips, Spine, Chest, Neck, Head, Jaw,
                ShoulderLeft, ShoulderRight, UpperArmLeft, UpperArmRight,
                ForearmLeft, ForearmRight, HandLeft, HandRight,
                ThighLeft, ThighRight, ShinLeft, ShinRight, FootLeft, FootRight,
                Tail
            };
        }
    }
}
