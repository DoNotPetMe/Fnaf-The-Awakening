using UnityEngine;
using Grotto.Core;

namespace Grotto.Procedural
{
    /// <summary>
    /// Drives a generated rig with procedural motion instead of animation clips.
    ///
    /// The central idea is that servos move in *steps*. Head tracking here is
    /// quantised to discrete increments and arrives with a small overshoot, so the
    /// character snaps to look at you and settles, the way a real actuator does.
    /// That single detail does more for the uncanniness than any amount of smooth
    /// interpolation, and it is exactly what smooth interpolation destroys.
    ///
    /// Inputs are plain fields written by the AI layer each frame; this component
    /// knows nothing about nodes, states or nights.
    /// </summary>
    [RequireComponent(typeof(AnimatronicRig))]
    [DisallowMultipleComponent]
    public sealed class ServoAnimator : MonoBehaviour
    {
        [Header("Inputs (written by the AI layer)")]
        [Range(0f, 1f)] public float Speed01;

        [Tooltip("Rises at a threshold or mid-attack. Drives jaw chatter and servo noise.")]
        [Range(0f, 1f)] public float Agitation01;

        [Tooltip("What the head tries to face. Usually the player's camera.")]
        public Transform LookTarget;

        [Tooltip("Eyes go dark when the character is dormant or has lost power.")]
        public bool EyesLit = true;

        [Header("Gait")]
        [SerializeField] private float strideFrequency = 1.5f;
        [SerializeField] private float strideAmplitude = 26f;
        [SerializeField] private float armSwingAmplitude = 18f;
        [SerializeField] private float bobAmplitude = 0.035f;

        [Header("Servos")]
        [Tooltip("Degrees per discrete step. Larger reads as older, cheaper hardware.")]
        [SerializeField] private float servoStepDegrees = 4.5f;

        [SerializeField] private float headTurnSpeed = 220f;
        [SerializeField] private float maxHeadYaw = 78f;
        [SerializeField] private float maxHeadPitch = 26f;

        [Tooltip("Fraction of a step the head overshoots before settling.")]
        [Range(0f, 1f)] [SerializeField] private float overshoot = 0.35f;

        [Header("Idle")]
        [SerializeField] private float swayAmplitude = 1.6f;
        [SerializeField] private float jitterAmplitude = 0.9f;

        private AnimatronicRig _rig;
        private float _phase;
        private float _currentYaw;
        private float _currentPitch;
        private float _targetYaw;
        private float _targetPitch;
        private float _yawVelocity;
        private float _jawOpen;
        private float _seed;
        private Vector3 _rootRestPosition;

        private Quaternion[] _restRotations;
        private Transform[] _bones;

        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private Color _eyeBaseEmission = Color.white;

        private void Awake()
        {
            _rig = GetComponent<AnimatronicRig>();
            _seed = Random.value * 100f;

            _bones = _rig.AllBones();
            _restRotations = new Quaternion[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
                _restRotations[i] = _bones[i] != null ? _bones[i].localRotation : Quaternion.identity;

            if (_rig.Hips != null) _rootRestPosition = _rig.Hips.localPosition;

            if (_rig.EyeMaterial != null && _rig.EyeMaterial.HasProperty(EmissionColor))
                _eyeBaseEmission = _rig.EyeMaterial.GetColor(EmissionColor);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            ResetToRest();
            TickGait(dt);
            TickHead(dt);
            TickJaw(dt);
            TickIdleJitter();
            TickEyes(dt);
        }

        private void ResetToRest()
        {
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].localRotation = _restRotations[i];
        }

        private void TickGait(float dt)
        {
            // The phase only advances while moving, so a stopped character is properly
            // still rather than marching on the spot.
            _phase += dt * strideFrequency * Mathf.PI * 2f * Mathf.Max(0.05f, Speed01);

            float swing = Mathf.Sin(_phase) * strideAmplitude * Speed01;
            float counter = Mathf.Sin(_phase + Mathf.PI) * strideAmplitude * Speed01;

            Rotate(_rig.ThighLeft, swing, 0f, 0f);
            Rotate(_rig.ThighRight, counter, 0f, 0f);

            // Knees only bend one way, and only on the backswing.
            Rotate(_rig.ShinLeft, Mathf.Max(0f, -swing) * 0.9f, 0f, 0f);
            Rotate(_rig.ShinRight, Mathf.Max(0f, -counter) * 0.9f, 0f, 0f);

            Rotate(_rig.FootLeft, -swing * 0.25f, 0f, 0f);
            Rotate(_rig.FootRight, -counter * 0.25f, 0f, 0f);

            // Arms counter-swing.
            Rotate(_rig.UpperArmLeft, counter * (armSwingAmplitude / strideAmplitude), 0f, 0f);
            Rotate(_rig.UpperArmRight, swing * (armSwingAmplitude / strideAmplitude), 0f, 0f);
            Rotate(_rig.ForearmLeft, -Mathf.Abs(counter) * 0.3f, 0f, 0f);
            Rotate(_rig.ForearmRight, -Mathf.Abs(swing) * 0.3f, 0f, 0f);

            // Hips roll and bob at twice stride frequency.
            if (_rig.Hips != null)
            {
                _rig.Hips.localPosition = _rootRestPosition +
                    Vector3.up * (Mathf.Abs(Mathf.Sin(_phase)) * bobAmplitude * Speed01);
                Rotate(_rig.Hips, 0f, Mathf.Sin(_phase) * 4f * Speed01, Mathf.Sin(_phase) * 3f * Speed01);
            }

            if (_rig.Tail != null)
                Rotate(_rig.Tail, 0f, Mathf.Sin(_phase * 0.7f) * 9f * (0.3f + Speed01), 0f);
        }

        private void TickHead(float dt)
        {
            if (_rig.Head == null) return;

            if (LookTarget != null)
            {
                var toTarget = LookTarget.position - _rig.Head.position;
                var local = transform.InverseTransformDirection(toTarget.normalized);

                _targetYaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxHeadYaw, maxHeadYaw);
                _targetPitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg,
                    -maxHeadPitch, maxHeadPitch);
            }
            else
            {
                // Nothing to look at: a slow idle scan.
                _targetYaw = Mathf.Sin(Time.time * 0.28f + _seed) * maxHeadYaw * 0.4f;
                _targetPitch = 0f;
            }

            // Quantise to servo steps. This is what makes it read as a machine.
            float step = Mathf.Max(0.5f, servoStepDegrees);
            float steppedYaw = Mathf.Round(_targetYaw / step) * step;
            float steppedPitch = Mathf.Round(_targetPitch / step) * step;

            float previous = _currentYaw;
            _currentYaw = Mathf.MoveTowards(_currentYaw, steppedYaw, headTurnSpeed * dt);
            _currentPitch = Mathf.MoveTowards(_currentPitch, steppedPitch, headTurnSpeed * dt);

            // A little inertia past the stop, then settle — the actuator ringing.
            _yawVelocity = MathUtil.ExpDecay(_yawVelocity, (_currentYaw - previous) / Mathf.Max(dt, 0.0001f), 14f, dt);
            float ring = Mathf.Clamp(_yawVelocity * 0.012f * overshoot, -6f, 6f);

            // Split the turn between neck and head so it does not look like a bobblehead.
            Rotate(_rig.Neck, _currentPitch * 0.35f, _currentYaw * 0.4f, 0f);
            Rotate(_rig.Head, _currentPitch * 0.65f, _currentYaw * 0.6f + ring, 0f);
        }

        private void TickJaw(float dt)
        {
            if (_rig.Jaw == null) return;

            // Idle: a slow breathing-like cycle it has no reason to have.
            float idle = (Mathf.Sin(Time.time * 1.1f + _seed) * 0.5f + 0.5f) * 0.12f;

            // Agitated: fast irregular chatter.
            float chatter = Agitation01 *
                (Mathf.PerlinNoise(Time.time * 11f, _seed) * 0.8f + 0.2f);

            float target = Mathf.Clamp01(idle + chatter);
            _jawOpen = MathUtil.ExpDecay(_jawOpen, target, 16f, dt);

            Rotate(_rig.Jaw, _jawOpen * 26f, 0f, 0f);
        }

        private void TickIdleJitter()
        {
            if (jitterAmplitude <= 0f) return;

            float t = Time.time;
            float amount = jitterAmplitude * (0.35f + Agitation01);

            // Every joint twitches slightly out of phase — old servos hunting for
            // position. Cheap, and the reason a still character never looks frozen.
            for (int i = 0; i < _bones.Length; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;

                float n = Mathf.PerlinNoise(t * 2.3f + i * 7.7f, _seed + i) - 0.5f;
                bone.localRotation *= Quaternion.Euler(n * amount, n * amount * 0.6f, n * amount * 0.4f);
            }

            // Whole-body sway, so weight shifts even when standing.
            if (_rig.Spine != null)
            {
                Rotate(_rig.Spine,
                    Mathf.Sin(t * 0.6f + _seed) * swayAmplitude * 0.4f,
                    Mathf.Sin(t * 0.43f + _seed) * swayAmplitude,
                    Mathf.Sin(t * 0.51f + _seed) * swayAmplitude * 0.3f);
            }
        }

        private void TickEyes(float dt)
        {
            if (_rig.EyeMaterial == null || !_rig.EyeMaterial.HasProperty(EmissionColor)) return;

            float target;
            if (!EyesLit)
            {
                target = 0f;
            }
            else
            {
                // A failing supply: mostly steady, with occasional dropouts.
                float flicker = Mathf.PerlinNoise(Time.time * 6f, _seed) > 0.16f ? 1f : 0.25f;
                target = Mathf.Lerp(0.75f, 1.6f, Agitation01) * flicker;
            }

            var current = _rig.EyeMaterial.GetColor(EmissionColor);
            var wanted = _eyeBaseEmission * target;
            _rig.EyeMaterial.SetColor(EmissionColor, Color.Lerp(current, wanted, 1f - Mathf.Exp(-18f * dt)));

            if (_rig.EyeRenderers == null) return;
            for (int i = 0; i < _rig.EyeRenderers.Length; i++)
                if (_rig.EyeRenderers[i] != null) _rig.EyeRenderers[i].enabled = EyesLit;
        }

        private static void Rotate(Transform bone, float pitch, float yaw, float roll)
        {
            if (bone == null) return;
            bone.localRotation *= Quaternion.Euler(pitch, yaw, roll);
        }
    }
}
