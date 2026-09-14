using System;
using UnityEngine;
using Grotto.Core;

namespace Grotto.Facility
{
    /// <summary>
    /// A fixed floodlight covering one node.
    ///
    /// Light is the answer to the cable chase, which has no door. It is also the
    /// cheapest system in the facility to run, which is the trap — a player who
    /// leaves every light on all night has spent their whole power budget on the one
    /// threat that was never going to kill them first.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Floodlight : MonoBehaviour, IPowerConsumer
    {
        [Tooltip("Node this fixture illuminates. Sets FacilityNode.IsLit while on.")]
        [SerializeField] private string nodeId = "CHASE";

        [SerializeField] private string displayName = "Chase floodlight";

        [Tooltip("Lights switched on and off with this fixture.")]
        [SerializeField] private Light[] lights = new Light[0];

        [Tooltip("Emissive geometry driven alongside the lights.")]
        [SerializeField] private Renderer[] emissiveRenderers = new Renderer[0];

        [Tooltip("Old ballasts. A little flicker on the intensity sells the age of the place.")]
        [SerializeField] private bool flicker = true;

        [SerializeField] private float flickerAmount = 0.08f;

        private FacilityTuning _tuning;
        private FacilityNode _node;
        private NodeId _id;
        private bool _on;
        private bool _powered = true;
        private float[] _baseIntensities;
        private float _flickerSeed;

        public bool IsOn => _on;

        /// <summary>The light is commanded on *and* the power is there to do it.</summary>
        public bool IsIlluminating => _on && _powered;

        public event Action<bool> Toggled;

        /// <summary>The node this fitting lights. Read by the station to wire its switches.</summary>
        public string NodeId => nodeId;

        /// <summary>Wires this fitting from code. See <see cref="BlastDoor.Configure"/>.</summary>
        public void Configure(string node, string label, Light[] fixtures,
            bool flicker, float flickerAmount)
        {
            nodeId = node;
            displayName = label;
            lights = fixtures ?? new Light[0];
            this.flicker = flicker;
            this.flickerAmount = flickerAmount;
        }

        private void Awake()
        {
            _id = new NodeId(nodeId);
            _flickerSeed = UnityEngine.Random.value * 100f;

            _baseIntensities = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) _baseIntensities[i] = lights[i].intensity;
        }

        private void Start()
        {
            var runtime = FacilityRuntime.Instance;
            if (runtime == null)
            {
                enabled = false;
                return;
            }

            _tuning = runtime.Tuning;
            _node = runtime.Graph.Node(_id);

            if (_node == null)
                GLog.Warn(LogChannel.Facility, $"{name}: no node '{nodeId}' in the layout; this fixture lights nothing.");

            runtime.Power.Register(this);
            ApplyVisualState();
        }

        private void OnDestroy()
        {
            FacilityRuntime.Instance?.Power.Unregister(this);
            if (_node != null) _node.IsLit = false;
        }

        private void Update()
        {
            if (_node != null) _node.IsLit = IsIlluminating;

            if (!flicker || !IsIlluminating) return;

            // Two out-of-phase sine waves read as an unsteady ballast rather than a pulse.
            float t = Time.time + _flickerSeed;
            float wobble = 1f + (Mathf.Sin(t * 17.3f) * 0.6f + Mathf.Sin(t * 41.7f) * 0.4f) * flickerAmount;

            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].intensity = _baseIntensities[i] * wobble;
        }

        public void SetOn(bool on)
        {
            if (_on == on) return;
            _on = on;
            ApplyVisualState();
            Toggled?.Invoke(on);
        }

        public void Toggle() => SetOn(!_on);

        private void ApplyVisualState()
        {
            bool live = IsIlluminating;

            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null) continue;
                lights[i].enabled = live;
                lights[i].intensity = _baseIntensities[i];
            }

            for (int i = 0; i < emissiveRenderers.Length; i++)
            {
                var renderer = emissiveRenderers[i];
                if (renderer == null || renderer.sharedMaterial == null) continue;

                // Instanced so switching one fixture does not relight every fixture.
                var material = renderer.material;
                if (live) material.EnableKeyword("_EMISSION");
                else material.DisableKeyword("_EMISSION");
            }

            if (_node != null) _node.IsLit = live;
        }

        public string PowerLabel => displayName;
        public float LoadKilowatts => _on && _tuning != null ? _tuning.floodlightKilowatts : 0f;
        public bool IsEssential => false;

        public void SetPowered(bool powered)
        {
            if (_powered == powered) return;
            _powered = powered;
            ApplyVisualState();
        }
    }
}
