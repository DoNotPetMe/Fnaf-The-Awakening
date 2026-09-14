using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.UI
{
    /// <summary>
    /// Puts a live view of the chosen site behind the front-end menus.
    ///
    /// The scene is already fully built when the title screen opens — the geometry,
    /// the fixtures and the cast are spawned at load — so the alternative to a
    /// backdrop image is a black rectangle in front of a cavern nobody is looking at.
    /// This takes the main camera, parks it in the site's headline room and pans it
    /// slowly across the space, with a warm key light and a cold fill so the rock
    /// actually reads.
    ///
    /// It does three useful things at once: the menu stops being a flat colour, the
    /// site picker shows you what you just picked, and the shot doubles as a smoke
    /// test — if the geometry failed to generate you can see that from the title
    /// screen instead of finding out after pressing Begin.
    ///
    /// Everything it touches is restored when the night starts: the camera goes back
    /// to the pose <see cref="Player.StationController"/> gave it, and the lights are
    /// destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleStage : MonoBehaviour
    {
        [Tooltip("Degrees of yaw swept across the whole cycle.")]
        [SerializeField] private float sweepDegrees = 26f;

        [Tooltip("Seconds for one there-and-back sweep.")]
        [SerializeField] private float sweepSeconds = 44f;

        [Tooltip("Metres the camera drifts on the site's long axis.")]
        [SerializeField] private float driftMetres = 1.6f;

        private Camera _camera;
        private Transform _cameraTransform;
        private Transform _cameraParent;
        private Vector3 _restPosition;
        private Quaternion _restRotation;
        private float _restFov;

        private GameObject _lights;
        private Vector3 _anchor;
        private Quaternion _facing;
        private float _phase;
        private bool _active;

        public bool IsActive => _active;

        private void OnDestroy() => Restore();

        public void SetActive(bool active)
        {
            if (active == _active) return;

            if (active) Engage();
            else Restore();
        }

        private void LateUpdate()
        {
            if (!_active || _cameraTransform == null) return;

            // Unscaled: the pause menu freezes the game, and a frozen title shot looks
            // like the build has hung.
            _phase += Time.unscaledDeltaTime / Mathf.Max(1f, sweepSeconds);

            float t = Mathf.Sin(_phase * Mathf.PI * 2f);
            float ease = Mathf.Sin(_phase * Mathf.PI * 2f + 1.2f);

            _cameraTransform.SetPositionAndRotation(
                _anchor + _facing * new Vector3(ease * driftMetres, 0f, 0f),
                _facing * Quaternion.Euler(0f, t * sweepDegrees * 0.5f, t * 0.6f));
        }

        // ---------------------------------------------------------------------

        private void Engage()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                GLog.Warn(LogChannel.UI, "No main camera; the title screen has no backdrop.");
                return;
            }

            var runtime = FacilityRuntime.Instance;
            var layout = runtime != null ? runtime.Layout : null;
            var graph = runtime != null ? runtime.Graph : null;
            if (graph == null) return;

            var node = PickStage(layout, graph);
            if (node == null) return;

            _cameraTransform = _camera.transform;
            _cameraParent = _cameraTransform.parent;
            _restPosition = _cameraTransform.position;
            _restRotation = _cameraTransform.rotation;
            _restFov = _camera.fieldOfView;

            // Detached, so the station rig's own pose is untouched and restoring is a
            // straight re-parent rather than an inverse transform.
            _cameraTransform.SetParent(null, worldPositionStays: true);

            // Stand back along the room's long axis, a third of the way up, looking at
            // a point slightly below centre — the composition a location photographer
            // would pick, and the one that shows the floor and the ceiling at once.
            var size = node.Size;
            bool longOnZ = size.z >= size.x;

            var back = longOnZ ? new Vector3(0f, 0f, -1f) : new Vector3(-1f, 0f, 0f);
            float distance = (longOnZ ? size.z : size.x) * 0.42f;

            _anchor = node.Position + back * distance + new Vector3(0f, size.y * 0.16f, 0f);

            var target = node.Position + new Vector3(0f, -size.y * 0.08f, 0f);
            _facing = Quaternion.LookRotation((target - _anchor).normalized, Vector3.up);

            _camera.fieldOfView = 46f;   // longer than the seated 68, for a composed shot

            BuildLights(node);

            _phase = 0f;
            _active = true;

            GLog.Info(LogChannel.UI, $"Title backdrop: {node.DisplayName}.");
        }

        /// <summary>
        /// The room to show. The site's default camera node is the one its author
        /// chose as the headline shot, so use that; fall back to the largest room with
        /// a camera, then to anything at all.
        /// </summary>
        private static FacilityNode PickStage(FacilityLayout layout, FacilityGraph graph)
        {
            if (layout != null && !string.IsNullOrWhiteSpace(layout.defaultCameraNode))
            {
                var preferred = graph.Node(new NodeId(layout.defaultCameraNode));
                if (preferred != null) return preferred;
            }

            FacilityNode best = null;
            float bestVolume = -1f;

            foreach (var node in graph.Nodes)
            {
                if (!node.HasCamera) continue;

                float volume = node.Size.x * node.Size.y * node.Size.z;
                if (volume <= bestVolume) continue;

                bestVolume = volume;
                best = node;
            }

            if (best != null) return best;

            foreach (var node in graph.Nodes) return node;
            return null;
        }

        private void BuildLights(FacilityNode node)
        {
            _lights = new GameObject("[TitleLights]");
            _lights.transform.position = node.Position;

            // Key: warm, high, raking across the space from the camera's shoulder.
            var key = new GameObject("Key").AddComponent<Light>();
            key.transform.SetParent(_lights.transform, worldPositionStays: false);
            key.transform.localPosition = new Vector3(node.Size.x * 0.22f, node.Size.y * 0.36f, -node.Size.z * 0.18f);
            key.transform.rotation = Quaternion.Euler(28f, -34f, 0f);
            key.type = LightType.Spot;
            key.spotAngle = 92f;
            key.innerSpotAngle = 30f;
            key.range = Mathf.Max(node.Size.x, node.Size.z) * 1.8f;
            key.intensity = 3.4f;
            key.color = new Color(1f, 0.86f, 0.66f);
            key.shadows = LightShadows.Soft;

            // Fill: cold, low, opposite side. Keeps the shadow side from going to
            // pure black, which is what makes a dark shot read as murky rather than
            // moody.
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.transform.SetParent(_lights.transform, worldPositionStays: false);
            fill.transform.localPosition = new Vector3(-node.Size.x * 0.3f, node.Size.y * 0.1f, node.Size.z * 0.25f);
            fill.type = LightType.Point;
            fill.range = Mathf.Max(node.Size.x, node.Size.z) * 1.5f;
            fill.intensity = 1.1f;
            fill.color = new Color(0.42f, 0.58f, 0.78f);
            fill.shadows = LightShadows.None;

            // Rim: a hard, dim edge from behind, to separate the far wall from the near.
            var rim = new GameObject("Rim").AddComponent<Light>();
            rim.transform.SetParent(_lights.transform, worldPositionStays: false);
            rim.transform.localPosition = new Vector3(0f, node.Size.y * 0.3f, node.Size.z * 0.42f);
            rim.type = LightType.Spot;
            rim.transform.rotation = Quaternion.Euler(18f, 180f, 0f);
            rim.spotAngle = 70f;
            rim.range = Mathf.Max(node.Size.x, node.Size.z) * 1.2f;
            rim.intensity = 1.6f;
            rim.color = new Color(0.78f, 0.82f, 1f);
            rim.shadows = LightShadows.None;
        }

        private void Restore()
        {
            if (_lights != null)
            {
                if (Application.isPlaying) Destroy(_lights);
                else DestroyImmediate(_lights);
                _lights = null;
            }

            if (_cameraTransform != null)
            {
                _cameraTransform.SetParent(_cameraParent, worldPositionStays: true);
                _cameraTransform.SetPositionAndRotation(_restPosition, _restRotation);
                if (_camera != null) _camera.fieldOfView = _restFov;
            }

            _cameraTransform = null;
            _cameraParent = null;
            _camera = null;
            _active = false;
        }
    }
}
