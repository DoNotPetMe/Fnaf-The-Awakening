using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Procedural
{
    /// <summary>
    /// Generates the cave when the scene loads.
    ///
    /// Deliberately at runtime rather than baked into the scene asset. A generated
    /// cavern is hundreds of thousands of vertices, and Unity serialises any mesh a
    /// scene references but that is not a project asset *into the scene file* — which
    /// would turn a reviewable text scene into a multi-megabyte binary blob and defeat
    /// the point of generating it in the first place.
    ///
    /// Generating on load costs a fraction of a second, is deterministic from the
    /// seed, and keeps the whole repository diffable.
    ///
    /// The editor preview builds into a <see cref="HideFlags.DontSave"/> hierarchy, so
    /// a designer can look at the geometry without it ever reaching the scene file.
    /// </summary>
    [DefaultExecutionOrder(-800)]
    [DisallowMultipleComponent]
    public sealed class FacilityGeometrySpawner : MonoBehaviour
    {
        [SerializeField] private FacilityLayout layout;

        [Tooltip("Changes every rock face, stalactite and scatter. The topology is unaffected.")]
        [SerializeField] private int seed = 1337;

        [Tooltip("Log how long generation took and how many triangles it produced.")]
        [SerializeField] private bool reportTiming = true;

        private GameObject _root;

        public GameObject GeneratedRoot => _root;

        private void Awake() => Generate();

        private void OnDestroy() => ClearGenerated();

        public void Generate()
        {
            ClearGenerated();

            if (layout == null) layout = FacilityLayout.LoadDefault();
            if (layout == null)
            {
                GLog.Error(LogChannel.Procedural, "No layout to build the facility from.");
                return;
            }

            var stopwatch = reportTiming ? System.Diagnostics.Stopwatch.StartNew() : null;

            _root = FacilityBuilder.Build(layout, transform, seed);

            if (stopwatch != null)
            {
                stopwatch.Stop();
                GLog.Info(LogChannel.Procedural,
                    $"Facility geometry generated in {stopwatch.ElapsedMilliseconds} ms.");
            }
        }

        public void ClearGenerated()
        {
            if (_root == null)
            {
                // Recover a preview left behind by a previous editor session.
                var existing = transform.Find(FacilityBuilder.GeometryRootName);
                if (existing != null) _root = existing.gameObject;
            }

            if (_root == null) return;

            if (Application.isPlaying) Destroy(_root);
            else DestroyImmediate(_root);

            _root = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Builds a preview in the editor. Marked DontSave throughout, so it shows in
        /// the Scene view and is discarded rather than written into the scene file.
        /// </summary>
        [ContextMenu("Preview geometry (not saved)")]
        public void PreviewInEditor()
        {
            Generate();
            if (_root == null) return;

            foreach (var child in _root.GetComponentsInChildren<Transform>(true))
                child.gameObject.hideFlags = HideFlags.DontSave;

            _root.hideFlags = HideFlags.DontSave;
            GLog.Info(LogChannel.Procedural, "Preview built. It will not be saved with the scene.");
        }

        [ContextMenu("Clear preview")]
        public void ClearPreview() => ClearGenerated();
#endif
    }
}
