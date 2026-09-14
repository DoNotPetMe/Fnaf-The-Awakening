using UnityEngine;

namespace Grotto.Procedural
{
    /// <summary>
    /// Per-character import settings, saved next to the model.
    ///
    /// Everything here is something the importer cannot work out from the file and
    /// that you set once by looking at the result. Kept as a separate asset rather
    /// than baked into the prefab so re-importing the model — which Unity does
    /// whenever the source file changes — never discards it.
    /// </summary>
    [CreateAssetMenu(menuName = "Grotto/Model Import Settings", fileName = "character_import")]
    public sealed class ModelImportSettings : ScriptableObject
    {
        [Header("Orientation")]
        [Tooltip("Rotation applied before fitting. Most models need 0 or 180 on Y — " +
                 "if the character has its back to you, put 180 here.")]
        public Vector3 rotationEuler;

        [Header("Scale")]
        [Tooltip("Measure the model and scale it to the character's authored height. " +
                 "Turn this off only if the model is deliberately not person-shaped.")]
        public bool autoFit = true;

        [Tooltip("Used instead of auto-fit when that is off.")]
        [Min(0.0001f)] public float manualScale = 1f;

        [Tooltip("Nudge after fitting. Y lifts it off the floor; Z moves it forward.")]
        public Vector3 positionOffset;

        [Header("Eyes")]
        [Tooltip("Add glowing lamps to the head when the model has no eye geometry of " +
                 "its own. The glow is how a character is identified in the dark.")]
        public bool addEyeLamps = true;

        [Header("Notes")]
        [TextArea(2, 6)]
        [Tooltip("Where this model came from and under what licence. Fill this in — " +
                 "it is what the credits screen and a takedown request both need.")]
        public string attribution = "";
    }
}
