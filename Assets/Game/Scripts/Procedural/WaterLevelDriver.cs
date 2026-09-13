using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.Procedural
{
    /// <summary>
    /// Moves the water table.
    ///
    /// One plane for the whole cave, which is not a simplification — a karst system
    /// has a single water table, and every chamber intersects it at the same
    /// elevation. That is why draining the sump also empties the Styx Channel, and it
    /// is the physical fact the entire Marlow/Echo tension is built on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterLevelDriver : MonoBehaviour
    {
        [Tooltip("World Y at water level 0 — the lowest point the pump can pull down to.")]
        [SerializeField] private float drainedY = -13f;

        [Tooltip("World Y at water level 1 — the control room floor.")]
        [SerializeField] private float floodedY = -1.1f;

        [Tooltip("How quickly the visible surface catches up. The simulation is instant; the water is not.")]
        [SerializeField] private float followLambda = 1.6f;

        [Header("Surface motion")]
        [SerializeField] private float rippleAmplitude = 0.02f;
        [SerializeField] private float rippleSpeed = 0.6f;

        private FacilityRuntime _facility;
        private float _displayedLevel;
        private Material _material;

        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");

        private void Start()
        {
            if (!ServiceLocator.TryGet(out FacilityRuntime facility))
            {
                enabled = false;
                return;
            }

            _facility = facility;
            _displayedLevel = _facility.Water.Level01;

            var renderer = GetComponentInChildren<Renderer>();
            if (renderer != null) _material = renderer.material;

            ApplyHeight(_displayedLevel);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _displayedLevel = MathUtil.ExpDecay(_displayedLevel, _facility.Water.Level01, followLambda, dt);

            ApplyHeight(_displayedLevel);

            // Scroll the surface. Faster while the pump is fighting it, so the player
            // can see the water is moving without looking at a gauge.
            if (_material == null || !_material.HasProperty(BaseMapSt)) return;

            float flow = _facility.Water.IsPumping ? 2.2f : 1f;
            var offset = _material.GetTextureOffset("_BaseMap");
            offset += new Vector2(0.013f, 0.009f) * rippleSpeed * flow * dt;
            _material.SetTextureOffset("_BaseMap", offset);
        }

        private void ApplyHeight(float level01)
        {
            float y = Mathf.Lerp(drainedY, floodedY, Mathf.Clamp01(level01));
            y += Mathf.Sin(Time.time * rippleSpeed) * rippleAmplitude;

            var position = transform.position;
            transform.position = new Vector3(position.x, y, position.z);
        }

        /// <summary>Sets the elevations from the layout, so geometry changes stay in sync.</summary>
        public void Configure(float drained, float flooded)
        {
            drainedY = drained;
            floodedY = flooded;
        }
    }
}
