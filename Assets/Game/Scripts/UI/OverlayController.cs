using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Grotto.AI;
using Grotto.Core;
using Grotto.Procedural;
using Grotto.Rendering;

namespace Grotto.UI
{
    /// <summary>
    /// Everything drawn over the top of the world: the condition overlay, the
    /// jumpscare, and the fades at either end of a night.
    ///
    /// The jumpscare is a camera move, not a video clip or a sprite. The attacking
    /// character already exists as a rigged model standing at the threshold, so the
    /// scare cuts the player's own camera to a hand's width from its face, shakes,
    /// and lets the servo animator keep driving the jaw. That costs nothing to
    /// author, is correct for whichever character actually got in, and — because the
    /// head is genuinely there — it frames differently every time.
    ///
    /// Accessibility is honoured here rather than bolted on: photosensitive mode caps
    /// the shake and the flash, and reduced jumpscare audio is handled by the audio
    /// director. Neither changes the simulation.
    /// </summary>
    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    public sealed class OverlayController : MonoBehaviour
    {
        [Header("Jumpscare")]
        [Tooltip("How close the camera ends up to the animatronic's head, in metres.")]
        [SerializeField] private float scareDistance = 0.55f;

        [SerializeField] private float scareShakeAmplitude = 0.09f;
        [SerializeField] private float scareShakeFrequency = 42f;
        [SerializeField] private float scareFovPunch = 22f;

        [Header("Fades")]
        [SerializeField] private float fadeSeconds = 1.2f;

        private Canvas _canvas;
        private Image _conditionOverlay;
        private Image _fade;
        private Text _outcomeText;
        private Material _overlayMaterial;

        private Camera _camera;
        private PostFxController _postFx;
        private AIDirector _ai;

        private Coroutine _scareRoutine;
        private bool _photosensitive;

        private static readonly int HallucinationProperty = Shader.PropertyToID("_Hallucination");
        private static readonly int ScareProperty = Shader.PropertyToID("_Scare");
        private static readonly int BlackoutProperty = Shader.PropertyToID("_Blackout");

        private void Start()
        {
            _camera = Camera.main;
            ServiceLocator.TryGet(out _postFx);
            ServiceLocator.TryGet(out _ai);

            if (ServiceLocator.TryGet(out SaveSystem save))
                _photosensitive = save.Data.settings.photosensitiveMode;

            Build();

            EventBus.Subscribe<AttackSignal>(OnAttack);
            EventBus.Subscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Subscribe<NightStartedSignal>(OnNightStarted);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<AttackSignal>(OnAttack);
            EventBus.Unsubscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Unsubscribe<NightStartedSignal>(OnNightStarted);

            if (_overlayMaterial != null) Destroy(_overlayMaterial);
        }

        private void Build()
        {
            _canvas = UIFactory.CreateCanvas("Station Overlay", 200, transform, withRaycaster: false);

            var shader = Shader.Find("Grotto/StationOverlay");
            if (shader != null)
            {
                _overlayMaterial = new Material(shader) { name = "StationOverlay (runtime)" };
            }
            else
            {
                GLog.Warn(LogChannel.UI, "Grotto/StationOverlay did not compile; condition overlay disabled.");
            }

            _conditionOverlay = UIFactory.Panel(_canvas.transform, "Condition", Color.white);
            UIFactory.Stretch(_conditionOverlay.rectTransform);
            _conditionOverlay.sprite = UIFactory.BuiltinSprite;
            if (_overlayMaterial != null) _conditionOverlay.material = _overlayMaterial;
            else _conditionOverlay.enabled = false;

            _fade = UIFactory.Panel(_canvas.transform, "Fade", new Color(0f, 0f, 0f, 1f));
            UIFactory.Stretch(_fade.rectTransform);

            _outcomeText = UIFactory.Label(_canvas.transform, "Outcome", "", 72, TextAnchor.MiddleCenter);
            UIFactory.Stretch(_outcomeText.rectTransform);
            _outcomeText.color = new Color(1f, 1f, 1f, 0f);

            StartCoroutine(FadeTo(0f));
        }

        private void Update()
        {
            if (_overlayMaterial == null || _postFx == null) return;

            _overlayMaterial.SetFloat(HallucinationProperty, _postFx.HallucinationLevel);
            _overlayMaterial.SetFloat(ScareProperty, _postFx.ScareLevel);
            _overlayMaterial.SetFloat(BlackoutProperty, _postFx.BlackoutLevel);
        }

        // ---------------------------------------------------------------------
        // Night flow
        // ---------------------------------------------------------------------

        private void OnNightStarted(NightStartedSignal signal)
        {
            _outcomeText.color = new Color(1f, 1f, 1f, 0f);
            StartCoroutine(FadeTo(0f));
        }

        private void OnNightEnded(NightEndedSignal signal)
        {
            string message = signal.Outcome switch
            {
                NightOutcome.Survived => "6 AM",
                NightOutcome.Flooded => "THE ROOM WENT UNDER",
                NightOutcome.Suffocated => "THE AIR RAN OUT",
                NightOutcome.Killed => "",
                _ => ""
            };

            StartCoroutine(EndNightSequence(message, signal.Outcome == NightOutcome.Survived));
        }

        private IEnumerator EndNightSequence(string message, bool survived)
        {
            // Let a jumpscare finish playing before anything fades.
            if (!survived) yield return new WaitForSecondsRealtime(1.6f);

            if (!string.IsNullOrEmpty(message))
            {
                _outcomeText.text = message;
                _outcomeText.color = survived ? UIFactory.Ink : UIFactory.InkAlarm;

                float t = 0f;
                while (t < 1f)
                {
                    t += Time.unscaledDeltaTime * 2f;
                    var colour = _outcomeText.color;
                    colour.a = Mathf.Clamp01(t);
                    _outcomeText.color = colour;
                    yield return null;
                }
            }

            yield return new WaitForSecondsRealtime(survived ? 2.5f : 1.2f);
            yield return FadeTo(1f);
        }

        private IEnumerator FadeTo(float target)
        {
            var colour = _fade.color;
            float start = colour.a;
            float elapsed = 0f;

            while (elapsed < fadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                colour.a = Mathf.Lerp(start, target, elapsed / fadeSeconds);
                _fade.color = colour;
                yield return null;
            }

            colour.a = target;
            _fade.color = colour;
        }

        // ---------------------------------------------------------------------
        // Jumpscare
        // ---------------------------------------------------------------------

        private void OnAttack(AttackSignal signal)
        {
            if (DebugFlags.IsDevBuild && DebugFlags.DisableJumpscares)
            {
                GLog.Info(LogChannel.UI, $"Jumpscare from '{signal.AnimatronicId}' suppressed by debug flag.");
                return;
            }

            if (_scareRoutine != null) StopCoroutine(_scareRoutine);
            _scareRoutine = StartCoroutine(JumpscareSequence(signal));
        }

        private IEnumerator JumpscareSequence(AttackSignal signal)
        {
            var controller = _ai != null ? _ai.Find(signal.AnimatronicId) : null;
            var rig = controller != null ? controller.GetComponentInChildren<AnimatronicRig>() : null;

            if (_camera == null || rig == null || rig.Head == null)
            {
                // Nothing to frame — fall back to a hard flash so the moment still lands.
                _postFx?.DebugScare(1f);
                yield break;
            }

            // Take the camera off the station rig for the duration.
            var originalParent = _camera.transform.parent;
            var originalPosition = _camera.transform.position;
            var originalRotation = _camera.transform.rotation;
            float originalFov = _camera.fieldOfView;

            _camera.transform.SetParent(null, worldPositionStays: true);

            float shake = _photosensitive ? scareShakeAmplitude * 0.3f : scareShakeAmplitude;
            float punch = _photosensitive ? scareFovPunch * 0.35f : scareFovPunch;

            _postFx?.DebugScare(_photosensitive ? 0.45f : 1f);

            const float duration = 1.5f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                var head = rig.Head;

                // Sit just off the face, looking straight into it. The head is being
                // driven by the servo animator the whole time, so the framing moves.
                var forward = head.forward;
                var target = head.position + forward * scareDistance;

                // Snap in fast, then hold.
                float approach = 1f - Mathf.Exp(-18f * elapsed);
                var basePosition = Vector3.Lerp(originalPosition, target, approach);

                var offset = new Vector3(
                    Mathf.Sin(elapsed * scareShakeFrequency * 1.07f),
                    Mathf.Sin(elapsed * scareShakeFrequency * 0.93f + 1.7f),
                    Mathf.Sin(elapsed * scareShakeFrequency * 1.31f + 3.1f)) * shake * (1f - t * 0.4f);

                _camera.transform.position = basePosition + offset;

                var lookRotation = Quaternion.LookRotation(head.position - _camera.transform.position, Vector3.up);
                _camera.transform.rotation = Quaternion.Slerp(originalRotation, lookRotation, approach);

                _camera.fieldOfView = originalFov + punch * Mathf.Sin(t * Mathf.PI);

                yield return null;
            }

            _camera.transform.SetParent(originalParent, worldPositionStays: true);
            _camera.transform.position = originalPosition;
            _camera.transform.rotation = originalRotation;
            _camera.fieldOfView = originalFov;

            _scareRoutine = null;
        }

        /// <summary>Plays a jumpscare on demand. Used by <c>fx.jumpscare</c>.</summary>
        public void DebugJumpscare(string animatronicId)
            => OnAttack(new AttackSignal(animatronicId, NodeId.None));
    }
}
