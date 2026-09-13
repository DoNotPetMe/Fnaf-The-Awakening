using UnityEngine;
using Grotto.Core;
using Grotto.Procedural;

namespace Grotto.AI
{
    /// <summary>
    /// Builds a character's model when the scene loads.
    ///
    /// Same reasoning as the cave geometry: generating at runtime keeps the scene file
    /// text-only. It also means the model is always in step with the definition —
    /// change Vesper's colours or proportions in her asset and the next Play has the
    /// new build, with no re-export step and nothing to forget.
    /// </summary>
    [DefaultExecutionOrder(-780)]
    [RequireComponent(typeof(AnimatronicController))]
    [DisallowMultipleComponent]
    public sealed class AnimatronicModelSpawner : MonoBehaviour
    {
        [SerializeField] private AnimatronicDefinition definition;

        [Tooltip("Attach a ServoAnimator to the generated rig.")]
        [SerializeField] private bool addServoAnimator = true;

        private GameObject _model;

        public AnimatronicRig Rig { get; private set; }

        private void Awake() => Generate();

        private void OnDestroy() => Clear();

        public void Generate()
        {
            Clear();

            if (definition == null)
            {
                var controller = GetComponent<AnimatronicController>();
                definition = controller != null ? controller.Definition : null;
            }

            if (definition == null)
            {
                GLog.Error(LogChannel.AI, $"{name}: no definition to build a model from.", this);
                return;
            }

            _model = AnimatronicFactory.Build(definition.model, definition.displayName + " Model", transform);
            Rig = _model.GetComponent<AnimatronicRig>();

            if (addServoAnimator && Rig != null && _model.GetComponent<ServoAnimator>() == null)
                _model.AddComponent<ServoAnimator>();
        }

        public void Clear()
        {
            if (_model == null) return;

            if (Application.isPlaying) Destroy(_model);
            else DestroyImmediate(_model);

            _model = null;
            Rig = null;
        }

#if UNITY_EDITOR
        /// <summary>Previews the model in the editor without writing it into the scene.</summary>
        [ContextMenu("Preview model (not saved)")]
        public void PreviewInEditor()
        {
            Generate();
            if (_model == null) return;

            foreach (var child in _model.GetComponentsInChildren<Transform>(true))
                child.gameObject.hideFlags = HideFlags.DontSave;

            _model.hideFlags = HideFlags.DontSave;
        }

        [ContextMenu("Clear preview")]
        public void ClearPreview() => Clear();
#endif
    }
}
