using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// A physical surveillance camera in the cave.
    ///
    /// Holds its own render target and stays switched off until the surveillance
    /// system selects it. Allocating the texture lazily and releasing it on destroy
    /// keeps fifteen cameras from costing fifteen render targets' worth of VRAM for
    /// a game that can only ever show one of them.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public sealed class CameraNode : MonoBehaviour, ICameraFeed
    {
        [Tooltip("Layout node this camera covers.")]
        [SerializeField] private string nodeId = "GRAND";

        [Header("Feed")]
        [SerializeField] private int width = 480;
        [SerializeField] private int height = 360;

        [Tooltip("Period cameras were cheap and grainy. 16 bits is plenty and it halves the memory.")]
        [SerializeField] private int depthBits = 16;

        [Tooltip("Slow sweep, so a camera left alone still shows you something new.")]
        [SerializeField] private bool sweep = true;
        [SerializeField] private float sweepDegrees = 22f;
        [SerializeField] private float sweepSeconds = 14f;

        private Camera _camera;
        private RenderTexture _target;
        private NodeId _id;
        private Quaternion _restRotation;
        private float _sweepPhase;

        public NodeId Node => _id;

        public Texture Texture
        {
            get
            {
                EnsureTarget();
                return _target;
            }
        }

        /// <summary>Wires this camera from code. See <see cref="BlastDoor.Configure"/>.</summary>
        public void Configure(string node, int feedWidth, int feedHeight,
            bool sweep, float sweepDegrees, float sweepSeconds)
        {
            nodeId = node;
            width = feedWidth;
            height = feedHeight;
            this.sweep = sweep;
            this.sweepDegrees = sweepDegrees;
            this.sweepSeconds = sweepSeconds;
        }

        private void Awake()
        {
            _id = new NodeId(nodeId);
            _camera = GetComponent<Camera>();
            _camera.enabled = false;
            _restRotation = transform.localRotation;
            _sweepPhase = Random.value * Mathf.PI * 2f;
        }

        private void Start()
        {
            var runtime = FacilityRuntime.Instance;
            if (runtime == null)
            {
                enabled = false;
                return;
            }
            runtime.Surveillance.RegisterFeed(this);
        }

        private void OnDestroy()
        {
            FacilityRuntime.Instance?.Surveillance.UnregisterFeed(this);

            if (_target == null) return;
            if (_camera != null) _camera.targetTexture = null;
            _target.Release();
            Destroy(_target);
            _target = null;
        }

        private void Update()
        {
            if (!sweep || _camera == null || !_camera.enabled) return;

            _sweepPhase += Time.deltaTime * (Mathf.PI * 2f / Mathf.Max(1f, sweepSeconds));
            float yaw = Mathf.Sin(_sweepPhase) * sweepDegrees * 0.5f;
            transform.localRotation = _restRotation * Quaternion.Euler(0f, yaw, 0f);
        }

        public void SetRendering(bool rendering)
        {
            if (_camera == null) return;

            if (rendering) EnsureTarget();
            _camera.enabled = rendering;
        }

        private void EnsureTarget()
        {
            if (_target != null) return;

            _target = new RenderTexture(Mathf.Max(64, width), Mathf.Max(64, height), depthBits, RenderTextureFormat.Default)
            {
                name = $"CamFeed_{_id}",
                // Point filtering keeps the picture crunchy instead of softening it into mush.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false
            };
            _target.Create();

            if (_camera != null) _camera.targetTexture = _target;
            GLog.Verbose(LogChannel.Facility, $"Allocated feed texture for {_id}.");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var cam = GetComponent<Camera>();
            if (cam == null) return;
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawFrustum(Vector3.zero, cam.fieldOfView, 8f, 0.3f, cam.aspect);
        }
#endif
    }
}
