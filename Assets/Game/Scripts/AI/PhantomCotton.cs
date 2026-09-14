using UnityEngine;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.AI
{
    public enum HallucinationKind
    {
        /// <summary>A figure is standing in the camera feed. It will not be there next time.</summary>
        PhantomOnCamera,
        /// <summary>The feed corrupts and the camera needs a reboot.</summary>
        FeedCorruption,
        /// <summary>The seismograph reports a contact that never happened.</summary>
        FalseContact,
        /// <summary>Something is in the control room with you. It is not.</summary>
        Presence
    }

    public readonly struct HallucinationSignal
    {
        public readonly HallucinationKind Kind;
        public readonly NodeId Node;
        public readonly float Intensity;

        public HallucinationSignal(HallucinationKind kind, NodeId node, float intensity)
        {
            Kind = kind; Node = node; Intensity = intensity;
        }
    }

    /// <summary>
    /// Cotton — the one that is not really there.
    ///
    /// Cotton was the walk-around mascot; there was never an animatronic. What comes
    /// out of bad air wearing her head is the game's answer to a problem most horror
    /// games have: once a player learns the rules, the cameras become a spreadsheet
    /// and stop being frightening.
    ///
    /// So the instrument itself degrades. Let the air go and the monitor starts
    /// lying — a figure in a feed that is empty next sweep, a camera that corrupts and
    /// costs six seconds to reboot, a seismic contact from a gallery with nothing in
    /// it. Cotton cannot kill you. She makes the things that can kill you unreadable,
    /// and the fix for her — the fan — is loud.
    /// </summary>
    [DefaultExecutionOrder(-690)]
    [DisallowMultipleComponent]
    public sealed class PhantomCotton : MonoBehaviour
    {
        [Header("Rate")]
        [Tooltip("Seconds between checks at full hallucination pressure.")]
        [Range(2f, 30f)] [SerializeField] private float minimumInterval = 6f;

        [Tooltip("Seconds between checks at the very edge of bad air.")]
        [Range(10f, 120f)] [SerializeField] private float maximumInterval = 34f;

        [Tooltip("Chance a due check actually produces something.")]
        [Range(0f, 1f)] [SerializeField] private float triggerChance = 0.75f;

        [Header("Weighting")]
        [Tooltip("A corrupted feed costs a reboot, so it stays rarer than a harmless apparition.")]
        [Range(0f, 1f)] [SerializeField] private float corruptionWeight = 0.2f;

        [Range(0f, 1f)] [SerializeField] private float presenceWeight = 0.15f;
        [Range(0f, 1f)] [SerializeField] private float falseContactWeight = 0.25f;

        private FacilityRuntime _facility;
        private RandomSource _rng;
        private float _timer;
        private float _nextCheckIn = 20f;

        /// <summary>Node a phantom is currently showing in, for the monitor to draw.</summary>
        public NodeId ActivePhantomNode { get; private set; } = NodeId.None;

        /// <summary>Seconds the current apparition has left.</summary>
        public float PhantomRemaining { get; private set; }

        /// <summary>Current hallucination pressure, echoed for the debug overlay.</summary>
        public float Pressure01 => _facility != null ? _facility.Ventilation.HallucinationPressure01 : 0f;

        private void Start()
        {
            if (!ServiceLocator.TryGet(out FacilityRuntime facility))
            {
                enabled = false;
                return;
            }
            _facility = facility;

            _rng = ServiceLocator.TryGet(out NightController night) && night.Rng != null
                ? night.Rng.Fork(9173)
                : new RandomSource(9173);

            ServiceLocator.Register(this);
        }

        private void OnDestroy() => ServiceLocator.Unregister(this);

        private void Update()
        {
            float dt = Time.deltaTime;

            if (PhantomRemaining > 0f)
            {
                PhantomRemaining -= dt;
                if (PhantomRemaining <= 0f) ActivePhantomNode = NodeId.None;
            }

            float pressure = Pressure01;
            if (pressure <= 0.01f || (DebugFlags.IsDevBuild && DebugFlags.FreezeAI))
            {
                _timer = 0f;
                return;
            }

            _timer += dt;
            if (_timer < _nextCheckIn) return;

            _timer = 0f;
            _nextCheckIn = Mathf.Lerp(maximumInterval, minimumInterval, pressure) * _rng.Range(0.75f, 1.25f);

            if (!_rng.Chance(triggerChance * pressure)) return;

            Trigger(ChooseKind(pressure), pressure);
        }

        private HallucinationKind ChooseKind(float pressure)
        {
            // The costly ones only start appearing once the air is genuinely bad.
            float roll = _rng.NextFloat();
            float corruption = corruptionWeight * pressure;
            float presence = presenceWeight * pressure;

            if (roll < presence) return HallucinationKind.Presence;
            if (roll < presence + corruption) return HallucinationKind.FeedCorruption;
            if (roll < presence + corruption + falseContactWeight) return HallucinationKind.FalseContact;
            return HallucinationKind.PhantomOnCamera;
        }

        private void Trigger(HallucinationKind kind, float pressure)
        {
            var node = PickNode(kind);

            switch (kind)
            {
                case HallucinationKind.PhantomOnCamera:
                    ActivePhantomNode = node;
                    PhantomRemaining = _rng.Range(2.5f, 5.5f);
                    break;

                case HallucinationKind.FeedCorruption:
                    // Real consequence: that camera is snow until it is rebooted.
                    _facility.Surveillance.BeginReboot(_facility.Surveillance.ActiveNode);
                    EventBus.Publish(new AlertSignal("Feed fault — reboot required.", AlertSeverity.Warning));
                    break;

                case HallucinationKind.FalseContact:
                    // A real noise event at a node nothing is actually in. The
                    // seismograph cannot tell the difference, which is the point.
                    _facility.Noise.Emit(node, _rng.Range(0.35f, 0.7f), NoiseKind.Footstep);
                    break;

                case HallucinationKind.Presence:
                    EventBus.Publish(new ScareSignal(Mathf.Lerp(0.4f, 0.9f, pressure), "cotton-presence"));
                    break;
            }

            GLog.Info(LogChannel.AI, $"Cotton: {kind} at {node} (pressure {pressure:0.00}).");
            EventBus.Publish(new HallucinationSignal(kind, node, pressure));
        }

        private NodeId PickNode(HallucinationKind kind)
        {
            switch (kind)
            {
                case HallucinationKind.Presence:
                    return _facility.StationNode;

                case HallucinationKind.FalseContact:
                {
                    // Favour the blind spot — a contact you cannot go and check is worse.
                    // Whichever node the site wired as its deep gallery, not the grotto's.
                    var blind = _facility.DeepNode;
                    if (_rng.Chance(0.5f) && _facility.Graph.Contains(blind)) return blind;
                    break;
                }
            }

            // Anywhere with a camera, so the player actually sees it.
            var order = _facility.Surveillance.CameraOrder;
            if (order.Count == 0) return _facility.StationNode;
            return order[_rng.Range(0, order.Count)];
        }

        /// <summary>Forces an apparition. Used by <c>fx.hallucinate</c>.</summary>
        public void DebugTrigger(HallucinationKind kind)
            => Trigger(kind, Mathf.Max(0.5f, Pressure01));
    }
}
