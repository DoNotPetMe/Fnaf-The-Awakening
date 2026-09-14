using System.Collections.Generic;
using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI
{
    /// <summary>
    /// Builds the cast at load, from the character assets in Resources, and hands it
    /// to the director.
    ///
    /// The scene used to carry five wired-up GameObjects. That was fine with one map
    /// and is wrong with three: a character's starting room is a property of the
    /// *site*, and the site is chosen at the menu, so a baked cast is a cast standing
    /// in the wrong building. Spawning here means the scene holds no character
    /// references at all and a new site needs no scene rebuild.
    ///
    /// Runs at -790: after the facility has a graph and its fixtures, before the
    /// director's own Awake at -700, which is the ordering
    /// <see cref="AIDirector.RegisterCast"/> relies on.
    /// </summary>
    [DefaultExecutionOrder(-790)]
    [DisallowMultipleComponent]
    public sealed class CastSpawner : MonoBehaviour
    {
        /// <summary>Resources sub-folder holding the AnimatronicDefinition assets.</summary>
        public const string ResourceFolder = "Cast";

        private const string RootName = "[Cast]";

        [Tooltip("Leave empty to load every character from Resources/Cast.")]
        [SerializeField] private List<AnimatronicDefinition> overrideCast = new List<AnimatronicDefinition>();

        [Tooltip("Attach a ServoAnimator to each generated model.")]
        [SerializeField] private bool animateServos = true;

        private GameObject _root;
        private readonly List<AnimatronicController> _controllers = new List<AnimatronicController>(8);

        public IReadOnlyList<AnimatronicController> Controllers => _controllers;

        private void Awake() => Build();

        private void OnDestroy() => Clear();

        public void Build()
        {
            Clear();

            var definitions = ResolveDefinitions();
            if (definitions.Count == 0)
            {
                GLog.Error(LogChannel.AI,
                    $"No AnimatronicDefinition assets under Resources/{ResourceFolder}. " +
                    "Run Tools > Grotto > Rebuild Settings Assets.");
                return;
            }

            var runtime = FacilityRuntime.Instance;
            var layout = runtime != null ? runtime.Layout : FacilityLayout.LoadDefault();
            var graph = runtime != null ? runtime.Graph : layout?.BuildGraph();

            // Inactive until the whole cast is wired: AddComponent on a live object
            // runs Awake immediately, which would beat the Configure call that says
            // which character this is.
            _root = new GameObject(RootName);
            _root.SetActive(false);
            _root.transform.SetParent(transform, worldPositionStays: false);

            for (int i = 0; i < definitions.Count; i++)
            {
                var definition = definitions[i];
                if (definition == null) continue;

                var go = new GameObject(definition.displayName);
                go.transform.SetParent(_root.transform, worldPositionStays: false);
                go.tag = "Animatronic";

                // Start at this site's home room for the character, falling back to the
                // definition's own when the layout does not place it.
                var placement = layout != null ? layout.FindPlacement(definition.id) : null;
                string home = placement != null && !string.IsNullOrWhiteSpace(placement.homeNode)
                    ? placement.homeNode
                    : definition.homeNode;

                if (graph != null) go.transform.position = graph.PositionOf(new NodeId(home));

                go.AddComponent<AnimatronicController>().Configure(definition);
                go.AddComponent<AnimatronicModelSpawner>().Configure(definition, animateServos);

                _controllers.Add(go.GetComponent<AnimatronicController>());
            }

            // Deterministic order, so a seeded night ticks the cast identically no
            // matter what order Resources handed the assets over in.
            _controllers.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));

            _root.SetActive(true);

            var director = GetComponentInParent<AIDirector>();
            if (director == null) director = FindAnyObjectByType<AIDirector>();

            if (director != null) director.RegisterCast(_controllers);
            else GLog.Warn(LogChannel.AI, "Cast built, but there is no AIDirector to run it.");

            GLog.Info(LogChannel.AI,
                $"Cast built: {_controllers.Count} character(s) at {(layout != null ? layout.siteName : "unknown site")}.");
        }

        public void Clear()
        {
            _controllers.Clear();

            if (_root == null)
            {
                var existing = transform.Find(RootName);
                if (existing != null) _root = existing.gameObject;
            }
            if (_root == null) return;

            if (Application.isPlaying) Destroy(_root);
            else DestroyImmediate(_root);

            _root = null;
        }

        private List<AnimatronicDefinition> ResolveDefinitions()
        {
            if (overrideCast.Count > 0) return overrideCast;

            var loaded = Resources.LoadAll<AnimatronicDefinition>(ResourceFolder);
            var list = new List<AnimatronicDefinition>(loaded.Length);
            for (int i = 0; i < loaded.Length; i++) list.Add(loaded[i]);
            return list;
        }
    }
}
