using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Grotto.Core;
using Grotto.Facility;
using Grotto.Player;

namespace Grotto.UI
{
    /// <summary>
    /// Every screen that is not the station itself: the title, the site picker, the
    /// night select, settings, the cast dossiers, the pause menu and the post-night
    /// summary.
    ///
    /// One component for all of them because they are the same object — a canvas over
    /// a stopped game, a heading, and a column of things to press. Splitting that into
    /// seven MonoBehaviours would buy nothing and cost a shared screen stack, a shared
    /// palette and a single place to ask "is a menu open".
    ///
    /// Screens are rebuilt from scratch on every transition rather than shown and
    /// hidden. Menus here are small, they are never on screen during play, and a
    /// rebuilt screen cannot show a stale unlock count or a setting the player changed
    /// on another screen — which is the bug that hiding-and-showing always eventually
    /// produces.
    ///
    /// The pause menu stops the clock with <see cref="Time.timeScale"/> but reads
    /// input in unscaled time, so the game is genuinely frozen rather than merely slow.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed partial class MenuController : MonoBehaviour
    {
        private enum Screen
        {
            None,
            Title,
            Sites,
            NightSelect,
            Settings,
            Cast,
            Paused,
            Summary
        }

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _root;
        private GameObject _screenRoot;

        private NightController _night;
        private StationController _station;
        private SaveSystem _save;
        private TitleStage _stage;

        private Screen _screen = Screen.None;
        private readonly Stack<Screen> _back = new Stack<Screen>(4);

        public bool IsOpen => _screen != Screen.None;

        /// <summary>True on the screens that are the front end rather than a pause overlay.</summary>
        public bool IsFrontEnd =>
            _screen == Screen.Title || _screen == Screen.Sites ||
            _screen == Screen.NightSelect || _screen == Screen.Settings || _screen == Screen.Cast;

        private SettingsData Settings => _save != null ? _save.Data.settings : new SettingsData();

        // ---------------------------------------------------------------------
        // Lifecycle
        // ---------------------------------------------------------------------

        private void Start()
        {
            ServiceLocator.TryGet(out _night);
            ServiceLocator.TryGet(out _station);
            ServiceLocator.TryGet(out _save);

            _stage = GetComponent<TitleStage>();
            if (_stage == null) _stage = gameObject.AddComponent<TitleStage>();

            BuildCanvas();

            EventBus.Subscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Subscribe<NightStartedSignal>(OnNightStarted);

            ServiceLocator.Register(this);

            // A cold boot with no night requested lands on the title screen. A scene
            // that started a night — the front end's request, or a pinned development
            // night — goes straight to the station.
            if (_night == null || _night.CurrentPhase == NightController.Phase.Idle) OpenTitle();
            else Close();
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Unsubscribe<NightStartedSignal>(OnNightStarted);
            ServiceLocator.Unregister(this);
        }

        private void BuildCanvas()
        {
            _canvas = UIFactory.CreateCanvas("Menus", 300, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();

            _root = UIFactory.Group(_canvas.transform, "Screen");
        }

        private void Update()
        {
            if (_station?.Input == null) return;

            if (!_station.Input.Pause.WasPressedThisFrame()) return;

            // Escape backs out of a front-end screen, and pauses or resumes in play.
            if (IsFrontEnd)
            {
                if (_back.Count > 0) Back();
                return;
            }

            if (_screen == Screen.Paused) Close();
            else if (_screen == Screen.None) OpenPause();
        }

        // ---------------------------------------------------------------------
        // Screen plumbing
        // ---------------------------------------------------------------------

        /// <summary>Tears down the current screen and starts a fresh one.</summary>
        private RectTransform BeginScreen(Screen screen)
        {
            if (_screenRoot != null) Destroy(_screenRoot);

            _screen = screen;
            _screenRoot = new GameObject(screen.ToString(), typeof(RectTransform));
            _screenRoot.transform.SetParent(_root, worldPositionStays: false);
            _screenRoot.layer = _root.gameObject.layer;

            return UIFactory.Stretch((RectTransform)_screenRoot.transform);
        }

        /// <summary>Remembers where we came from, then builds the screen we are going to.</summary>
        private void Navigate(System.Action build)
        {
            _back.Push(_screen);
            build();
        }

        /// <summary>
        /// Returns to whatever pushed the current screen. The stack is a trail rather
        /// than a history, so going back pops without pushing.
        /// </summary>
        private void Back()
        {
            var previous = _back.Count > 0 ? _back.Pop() : Screen.Title;

            switch (previous)
            {
                case Screen.Sites: BuildSitesScreen(); break;
                case Screen.NightSelect: BuildNightSelectScreen(); break;
                case Screen.Settings: BuildSettingsScreen(); break;
                case Screen.Cast: BuildCastScreen(); break;

                // Settings opened from the pause menu goes back to the pause menu, not
                // out to the title screen and a scene the player has not left.
                case Screen.Paused: OpenPause(); break;

                default: BuildTitleScreen(); break;
            }
        }

        private void Show(bool frontEnd)
        {
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _group.interactable = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_station != null) _station.SetControlSuspended(true);

            if (_stage != null) _stage.SetActive(frontEnd);
        }

        public void Close()
        {
            _screen = Screen.None;
            _back.Clear();

            if (_screenRoot != null)
            {
                Destroy(_screenRoot);
                _screenRoot = null;
            }

            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            Time.timeScale = 1f;

            if (_station != null) _station.SetControlSuspended(false);
            if (_stage != null) _stage.SetActive(false);

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        // ---------------------------------------------------------------------
        // Entry points
        // ---------------------------------------------------------------------

        public void OpenTitle()
        {
            _back.Clear();
            BuildTitleScreen();
        }

        /// <summary>
        /// Kept for the dev console and for anything that wants the old behaviour of
        /// jumping straight to the list of nights.
        /// </summary>
        public void OpenNightSelect()
        {
            _back.Clear();
            _back.Push(Screen.Title);
            BuildNightSelectScreen();
        }

        public void OpenPause()
        {
            var screen = BeginScreen(Screen.Paused);
            Scrim(screen, 0.93f);

            string subtitle = _night != null && _night.CurrentDefinition != null
                ? $"{_night.CurrentDefinition.displayName.ToUpperInvariant()}  ·  {_night.Clock.DisplayHour}"
                : "";

            Heading(screen, "PAUSED", subtitle);

            var column = Column(screen, new Vector2(0f, -300f), UIFactory.TopCentre);

            AddButton(column, "RESUME", Close);

            AddButton(column, "RESTART NIGHT", () =>
            {
                int night = _night != null ? _night.CurrentNight : 1;
                Close();
                _night?.StartNight(night);
            });

            AddButton(column, "SETTINGS", () => Navigate(BuildSettingsScreen));

            AddButton(column, "ABANDON NIGHT", () =>
            {
                Close();
                _night?.RequestOutcome(NightOutcome.Aborted);
            });

            AddButton(column, "QUIT TO TITLE", SessionRequest.ReturnToTitle);

            Show(frontEnd: false);
            Time.timeScale = 0f;
        }

        private void OnNightStarted(NightStartedSignal signal) => Close();

        private void OnNightEnded(NightEndedSignal signal)
        {
            // Let the jumpscare and the fade play out before the summary appears.
            StartCoroutine(ShowSummaryAfter(signal, 4.5f));
        }

        private System.Collections.IEnumerator ShowSummaryAfter(NightEndedSignal signal, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);

            var screen = BeginScreen(Screen.Summary);
            Scrim(screen, 0.95f);

            bool survived = signal.Outcome == NightOutcome.Survived;
            var layout = FacilityRuntime.Instance != null ? FacilityRuntime.Instance.Layout : null;

            Heading(screen,
                survived ? "6 AM" : "SHIFT ENDED",
                layout != null ? layout.siteName.ToUpperInvariant() : "",
                survived ? UIFactory.Ink : UIFactory.InkAlarm);

            var body = UIFactory.Label(screen, "Body", Describe(signal), 21,
                TextAnchor.UpperCenter, UIFactory.InkDim, wrap: true);
            UIFactory.Anchor(body.rectTransform, UIFactory.TopCentre,
                new Vector2(0f, -220f), new Vector2(900f, 180f));

            var column = Column(screen, new Vector2(0f, -420f), UIFactory.TopCentre);

            if (survived)
            {
                int next = signal.Night + 1;
                if (next <= 6)
                {
                    AddButton(column, $"NIGHT {next}", () =>
                    {
                        Close();
                        _night?.StartNight(next);
                    });
                }
            }
            else
            {
                AddButton(column, "TRY AGAIN", () =>
                {
                    Close();
                    _night?.StartNight(signal.Night);
                });
            }

            AddButton(column, "CHANGE NIGHT", () =>
            {
                _back.Clear();
                _back.Push(Screen.Title);
                BuildNightSelectScreen();
            });

            AddButton(column, "TITLE SCREEN", SessionRequest.ReturnToTitle);
            AddButton(column, "QUIT", Quit);

            Show(frontEnd: false);
        }

        private string Describe(NightEndedSignal signal)
        {
            string reason = signal.Outcome switch
            {
                NightOutcome.Survived => "You made it to six. The survey crew arrives at eight.",
                NightOutcome.Killed => "Something reached the control room.",
                NightOutcome.Flooded => "The water won. The pump was never going to hold it alone.",
                NightOutcome.Suffocated => "The air went, and then so did you.",
                _ => "Shift abandoned."
            };

            var record = _save?.Data.GetOrCreateRecord(signal.Night);
            if (record == null) return reason;

            int minutes = Mathf.FloorToInt(record.bestSurvivalSeconds / 60f);
            int seconds = Mathf.FloorToInt(record.bestSurvivalSeconds % 60f);

            return reason +
                   $"\n\nAttempts {record.attempts}     Deaths {record.deaths}     " +
                   $"Best {minutes}m {seconds:00}s of night time";
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
