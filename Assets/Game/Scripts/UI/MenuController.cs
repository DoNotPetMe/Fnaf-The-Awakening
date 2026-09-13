using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Grotto.Core;
using Grotto.Player;

namespace Grotto.UI
{
    /// <summary>
    /// Night select, pause and the post-night summary.
    ///
    /// One component for all three because they are the same screen with different
    /// contents: a dimmed backdrop over a stopped game with a column of buttons. The
    /// pause menu stops the clock through <see cref="Time.timeScale"/> but reads input
    /// through unscaled time, so the game is genuinely frozen rather than merely slow.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class MenuController : MonoBehaviour
    {
        private enum Screen { None, NightSelect, Paused, Summary }

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _content;
        private Text _title;
        private Text _subtitle;
        private readonly List<GameObject> _buttons = new List<GameObject>(8);

        private NightController _night;
        private StationController _station;
        private SaveSystem _save;
        private Screen _screen = Screen.None;

        public bool IsOpen => _screen != Screen.None;

        private void Start()
        {
            ServiceLocator.TryGet(out _night);
            ServiceLocator.TryGet(out _station);
            ServiceLocator.TryGet(out _save);

            Build();

            EventBus.Subscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Subscribe<NightStartedSignal>(OnNightStarted);

            ServiceLocator.Register(this);
            Close();
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<NightEndedSignal>(OnNightEnded);
            EventBus.Unsubscribe<NightStartedSignal>(OnNightStarted);
            ServiceLocator.Unregister(this);
        }

        private void Build()
        {
            _canvas = UIFactory.CreateCanvas("Menus", 300, transform);
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();

            var backdrop = UIFactory.Panel(_canvas.transform, "Backdrop", new Color(0.01f, 0.012f, 0.015f, 0.93f));
            UIFactory.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            _content = UIFactory.Group(_canvas.transform, "Content");
            UIFactory.Anchor(_content, UIFactory.Centre, Vector2.zero, new Vector2(820f, 720f));

            _title = UIFactory.Label(_content, "Title", "", 64, TextAnchor.UpperCenter);
            UIFactory.Anchor(_title.rectTransform, UIFactory.TopCentre, new Vector2(0f, 0f), new Vector2(820f, 80f));

            _subtitle = UIFactory.Label(_content, "Subtitle", "", 22, TextAnchor.UpperCenter, UIFactory.InkDim);
            UIFactory.Anchor(_subtitle.rectTransform, UIFactory.TopCentre, new Vector2(0f, -88f), new Vector2(820f, 120f));
        }

        private void Update()
        {
            if (_station?.Input == null) return;

            if (_station.Input.Pause.WasPressedThisFrame())
            {
                if (_screen == Screen.Paused) Close();
                else if (_screen == Screen.None) OpenPause();
            }
        }

        // ---------------------------------------------------------------------
        // Screens
        // ---------------------------------------------------------------------

        public void OpenNightSelect()
        {
            ClearButtons();
            _screen = Screen.NightSelect;

            _title.text = "THE AWAKENING";
            _title.color = UIFactory.Ink;
            _subtitle.text =
                "GROTTO SPRINGS FAMILY FUN CAVERNS — MARROW HOLLOW\n" +
                "Reclamation site monitor, 11 PM to 6 AM.";

            int unlocked = _save?.Data.highestNightUnlocked ?? 1;
            float y = -230f;

            for (int night = 1; night <= Mathf.Min(6, unlocked); night++)
            {
                int captured = night;
                AddButton($"NIGHT {night}", ref y, () => { Close(); _night?.StartNight(captured); });
            }

            if (_save != null && _save.Data.customNightUnlocked)
                AddButton("CUSTOM NIGHT", ref y, () => { Close(); _night?.StartNight(7); });

            AddButton("QUIT", ref y, Quit);

            Show();
        }

        public void OpenPause()
        {
            ClearButtons();
            _screen = Screen.Paused;

            _title.text = "PAUSED";
            _title.color = UIFactory.Ink;
            _subtitle.text = _night != null && _night.CurrentDefinition != null
                ? $"{_night.CurrentDefinition.displayName} — {_night.Clock.DisplayHour}"
                : "";

            float y = -230f;
            AddButton("RESUME", ref y, Close);
            AddButton("RESTART NIGHT", ref y, () =>
            {
                int night = _night != null ? _night.CurrentNight : 1;
                Close();
                _night?.StartNight(night);
            });
            AddButton("ABANDON NIGHT", ref y, () =>
            {
                Close();
                _night?.RequestOutcome(NightOutcome.Aborted);
            });
            AddButton("QUIT", ref y, Quit);

            Show();
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

            ClearButtons();
            _screen = Screen.Summary;

            bool survived = signal.Outcome == NightOutcome.Survived;

            _title.text = survived ? "6 AM" : "SHIFT ENDED";
            _title.color = survived ? UIFactory.Ink : UIFactory.InkAlarm;
            _subtitle.text = Describe(signal);

            float y = -260f;

            if (survived)
            {
                int next = signal.Night + 1;
                if (next <= 6)
                    AddButton($"NIGHT {next}", ref y, () => { Close(); _night?.StartNight(next); });
            }
            else
            {
                AddButton("TRY AGAIN", ref y, () => { Close(); _night?.StartNight(signal.Night); });
            }

            AddButton("NIGHT SELECT", ref y, OpenNightSelect);
            AddButton("QUIT", ref y, Quit);

            Show();
        }

        private string Describe(NightEndedSignal signal)
        {
            string reason = signal.Outcome switch
            {
                NightOutcome.Survived => "You made it to six. The survey crew arrives at eight.",
                NightOutcome.Killed => "Something reached the control room.",
                NightOutcome.Flooded => "The spring won. The pump was never going to hold it alone.",
                NightOutcome.Suffocated => "The air went, and then so did you.",
                _ => "Shift abandoned."
            };

            var record = _save?.Data.GetOrCreateRecord(signal.Night);
            string stats = record == null ? "" :
                $"\n\nAttempts: {record.attempts}    Deaths: {record.deaths}" +
                $"\nBest: {Mathf.FloorToInt(record.bestSurvivalSeconds / 60f)}m {Mathf.FloorToInt(record.bestSurvivalSeconds % 60f)}s of night time";

            return reason + stats;
        }

        // ---------------------------------------------------------------------

        private void AddButton(string label, ref float y, System.Action onClick)
        {
            var button = UIFactory.TextButton(_content, "Btn_" + label, label,
                new Vector2(460f, 62f), onClick, 28);

            UIFactory.Anchor((RectTransform)button.transform, UIFactory.TopCentre,
                new Vector2(0f, y), new Vector2(460f, 62f));

            y -= 74f;
            _buttons.Add(button.gameObject);
        }

        private void ClearButtons()
        {
            for (int i = 0; i < _buttons.Count; i++)
                if (_buttons[i] != null) Destroy(_buttons[i]);
            _buttons.Clear();
        }

        private void Show()
        {
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _group.interactable = true;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (_station != null) _station.enabled = false;
        }

        public void Close()
        {
            _screen = Screen.None;
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            Time.timeScale = 1f;

            if (_station != null) _station.enabled = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
