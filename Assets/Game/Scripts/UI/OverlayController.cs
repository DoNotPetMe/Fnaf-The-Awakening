using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Grotto.AI;
using Grotto.Audio;
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

        [Header("Jumpscare test")]
        [Tooltip("Opens the jumpscare picker with the toggle key at any point in a night.")]
        [SerializeField] private bool enableJumpscareTester = true;

        [SerializeField] private Key testerToggleKey = Key.J;

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

        private NightController _night;
        private AudioDirector _audio;

        // Where the camera came from, captured once per jumpscare rather than per
        // coroutine. Restarting a scare while one is already playing would otherwise
        // capture the *detached* transform as the thing to return to, and the camera
        // would never find its way back to the chair.
        private Transform _cameraHome;
        private Vector3 _cameraHomePosition;
        private Quaternion _cameraHomeRotation;
        private float _cameraHomeFov;
        private bool _cameraDetached;

        private RectTransform _testerGroup;
        private Text _testerName;
        private Text _testerHint;
        private readonly List<AnimatronicController> _testerCast = new List<AnimatronicController>(8);
        private int _testerIndex;
        private bool _testerOpen;

        private static readonly int HallucinationProperty = Shader.PropertyToID("_Hallucination");
        private static readonly int ScareProperty = Shader.PropertyToID("_Scare");
        private static readonly int BlackoutProperty = Shader.PropertyToID("_Blackout");

        private void Start()
        {
            _camera = Camera.main;
            ServiceLocator.TryGet(out _postFx);
            ServiceLocator.TryGet(out _ai);
            ServiceLocator.TryGet(out _night);
            ServiceLocator.TryGet(out _audio);

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
            RestoreCamera();
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

            BuildJumpscareTester();

            StartCoroutine(FadeTo(0f));
        }

        private void Update()
        {
            TickJumpscareTester();

            if (_overlayMaterial == null || _postFx == null) return;

            _overlayMaterial.SetFloat(HallucinationProperty, _postFx.HallucinationLevel);
            _overlayMaterial.SetFloat(ScareProperty, _postFx.ScareLevel);
            _overlayMaterial.SetFloat(BlackoutProperty, _postFx.BlackoutLevel);
        }

        // ---------------------------------------------------------------------
        // Jumpscare test picker
        // ---------------------------------------------------------------------

        /// <summary>
        /// A picker for firing any character's jumpscare on demand.
        ///
        /// Jumpscares are the one thing in the game that is genuinely hard to iterate
        /// on: reaching one honestly means surviving to the point where a specific
        /// character breaches, which can take most of a night and cannot be aimed at a
        /// particular character. This makes the presentation reviewable in isolation.
        ///
        /// It fires the *presentation* only — no AttackSignal — so the night carries
        /// on afterwards and you can fire the next one straight away.
        /// </summary>
        private void BuildJumpscareTester()
        {
            _testerGroup = UIFactory.Group(_canvas.transform, "Jumpscare Tester");

            var panel = UIFactory.Panel(_testerGroup, "Panel", new Color(0.04f, 0.03f, 0.03f, 0.92f));
            UIFactory.Anchor(panel.rectTransform, UIFactory.BottomCentre,
                new Vector2(0f, 70f), new Vector2(680f, 168f));

            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.55f, 0.16f, 0.14f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);

            var header = UIFactory.Label(panel.rectTransform, "Header",
                "JUMPSCARE TEST", 18, TextAnchor.UpperCenter, UIFactory.InkAlarm);
            UIFactory.Anchor(header.rectTransform, UIFactory.TopCentre,
                new Vector2(0f, -12f), new Vector2(660f, 24f));

            _testerName = UIFactory.Label(panel.rectTransform, "Name",
                "", 38, TextAnchor.MiddleCenter, UIFactory.Ink);
            UIFactory.Anchor(_testerName.rectTransform, UIFactory.Centre,
                new Vector2(0f, 8f), new Vector2(660f, 52f));

            _testerHint = UIFactory.Label(panel.rectTransform, "Hint",
                "\u2190 \u2192  choose        ENTER  trigger        J  close",
                17, TextAnchor.LowerCenter, UIFactory.InkDim);
            UIFactory.Anchor(_testerHint.rectTransform, UIFactory.BottomCentre,
                new Vector2(0f, 14f), new Vector2(660f, 24f));

            _testerGroup.gameObject.SetActive(false);
        }

        private void TickJumpscareTester()
        {
            if (!enableJumpscareTester || _testerGroup == null) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[testerToggleKey].wasPressedThisFrame) ToggleJumpscareTester();

            if (!_testerOpen) return;

            // The briefing screen also listens for Enter; let it have the key.
            if (_night != null && _night.CurrentPhase == NightController.Phase.Briefing) return;

            if (_testerCast.Count == 0)
            {
                _testerName.text = "NO CAST IN THIS SCENE";
                return;
            }

            if (keyboard.leftArrowKey.wasPressedThisFrame) StepTester(-1);
            if (keyboard.rightArrowKey.wasPressedThisFrame) StepTester(1);

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                FireSelectedJumpscare();
        }

        public void ToggleJumpscareTester()
        {
            _testerOpen = !_testerOpen;

            if (_testerOpen)
            {
                RefreshTesterCast();
                UpdateTesterLabel();
            }

            _testerGroup.gameObject.SetActive(_testerOpen);
        }

        private void RefreshTesterCast()
        {
            _testerCast.Clear();
            if (_ai == null) return;

            var cast = _ai.Cast;
            for (int i = 0; i < cast.Count; i++)
                if (cast[i] != null && cast[i].Definition != null) _testerCast.Add(cast[i]);

            _testerIndex = Mathf.Clamp(_testerIndex, 0, Mathf.Max(0, _testerCast.Count - 1));
        }

        private void StepTester(int direction)
        {
            if (_testerCast.Count == 0) return;

            _testerIndex = (_testerIndex + direction + _testerCast.Count) % _testerCast.Count;
            UpdateTesterLabel();
        }

        private void UpdateTesterLabel()
        {
            if (_testerCast.Count == 0)
            {
                _testerName.text = "NO CAST IN THIS SCENE";
                return;
            }

            var definition = _testerCast[_testerIndex].Definition;

            _testerName.text = definition.displayName.ToUpperInvariant();
            _testerName.color = definition.mapColor;

            _testerHint.text = _testerCast.Count > 1
                ? $"\u2190 \u2192  choose ({_testerIndex + 1}/{_testerCast.Count})        ENTER  trigger        J  close"
                : "ENTER  trigger        J  close";
        }

        private void FireSelectedJumpscare()
        {
            if (_testerCast.Count == 0) return;

            var controller = _testerCast[_testerIndex];
            GLog.Info(LogChannel.UI, $"Jumpscare test: {controller.DisplayName}.");

            // Picture from here, sound from the audio director. Neither path publishes
            // an AttackSignal, so the night is untouched.
            OnAttack(new AttackSignal(controller.Id, controller.CurrentNode));
            _audio?.PlayJumpscare();
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

            // A dormant character is standing at its home node on the far side of the
            // cave, which frames as an empty room. For a test, bring it to the player.
            if (rig != null && _testerOpen && _camera != null)
            {
                var front = _camera.transform.position + _camera.transform.forward * 1.4f;
                controller.transform.position = new Vector3(front.x, controller.transform.position.y, front.z);
                controller.transform.rotation = Quaternion.LookRotation(
                    -_camera.transform.forward, Vector3.up);
            }

            if (_camera == null || rig == null || rig.Head == null)
            {
                // Nothing to frame — fall back to a hard flash so the moment still lands.
                _postFx?.DebugScare(1f);
                yield break;
            }

            // Take the camera off the station rig for the duration.
            if (!_cameraDetached)
            {
                _cameraHome = _camera.transform.parent;
                _cameraHomePosition = _camera.transform.position;
                _cameraHomeRotation = _camera.transform.rotation;
                _cameraHomeFov = _camera.fieldOfView;
                _cameraDetached = true;
            }

            var originalPosition = _cameraHomePosition;
            var originalRotation = _cameraHomeRotation;
            float originalFov = _cameraHomeFov;

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

            RestoreCamera();
            _scareRoutine = null;
        }

        /// <summary>Puts the camera back in the chair, wherever the scare left it.</summary>
        private void RestoreCamera()
        {
            if (!_cameraDetached || _camera == null) return;

            _camera.transform.SetParent(_cameraHome, worldPositionStays: true);
            _camera.transform.position = _cameraHomePosition;
            _camera.transform.rotation = _cameraHomeRotation;
            _camera.fieldOfView = _cameraHomeFov;

            _cameraDetached = false;
        }

        /// <summary>Plays a jumpscare on demand. Used by <c>fx.jumpscare</c>.</summary>
        public void DebugJumpscare(string animatronicId)
            => OnAttack(new AttackSignal(animatronicId, NodeId.None));
    }
}
