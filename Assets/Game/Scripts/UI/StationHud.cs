using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;
using Grotto.Player;

namespace Grotto.UI
{
    /// <summary>
    /// The instrument panel.
    ///
    /// Built entirely in code at startup and driven from the facility systems. Every
    /// readout answers a question the player has to act on within the next few
    /// seconds — how much diesel is left, how close the load is to tripping, which way
    /// the water is going — and nothing on it is decorative.
    ///
    /// The water gauge carries tick marks at the two thresholds that matter. That is
    /// the single most important piece of UI in the game: it turns "the water level"
    /// from a number into a decision with two named edges.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    [DisallowMultipleComponent]
    public sealed class StationHud : MonoBehaviour
    {
        private FacilityRuntime _facility;
        private NightController _night;
        private StationController _station;
        private AIDirector _ai;
        private Headlamp _headlamp;

        private Canvas _canvas;
        private RectTransform _roomGroup;
        private RectTransform _monitorGroup;
        private CanvasGroup _canvasGroup;
        private MenuController _menu;
        private CanvasGroup _monitorCanvasGroup;
        private MonitorScreen _monitor;

        private Text _clock;
        private Text _nightLabel;
        private Text _alert;
        private float _alertRemaining;

        private Image _fuelFill; private Text _fuelCaption;
        private Image _loadFill; private Text _loadCaption;
        private Image _airFill; private Text _airCaption;
        private Image _waterFill; private Text _waterCaption;
        private Image _batteryFill; private Text _batteryCaption;
        private Image _lampFill; private Text _lampCaption;

        private RectTransform _briefingGroup;
        private Text _briefingTitle;
        private Text _briefingBody;
        private Text _briefingPrompt;
        private bool _briefingShowing;

        private Text _systemsLine;
        private Text _occupiedLabel;
        private Image _occupiedFill;
        private RectTransform _occupiedGroup;

        private void Start()
        {
            if (!ServiceLocator.TryGet(out _facility))
            {
                GLog.Error(LogChannel.UI, "StationHud found no FacilityRuntime.");
                enabled = false;
                return;
            }

            ServiceLocator.TryGet(out _night);
            ServiceLocator.TryGet(out _station);
            ServiceLocator.TryGet(out _ai);
            _headlamp = FindFirstObjectByType<Headlamp>();

            Build();

            EventBus.Subscribe<AlertSignal>(OnAlert);
            _facility.Surveillance.MonitorToggled += OnMonitorToggled;

            OnMonitorToggled(_facility.Surveillance.MonitorUp);
        }

        private void OnDestroy()
        {
            EventBus.Unsubscribe<AlertSignal>(OnAlert);
            if (_facility != null) _facility.Surveillance.MonitorToggled -= OnMonitorToggled;
        }

        // ---------------------------------------------------------------------
        // Construction
        // ---------------------------------------------------------------------

        private void Build()
        {
            _canvas = UIFactory.CreateCanvas("Station HUD", 100, transform);

            // The whole panel fades out behind the front-end screens. Without this the
            // fuel gauge and the clock sit on top of the title card, which reads as a
            // bug even though every number on them is correct.
            _canvasGroup = _canvas.gameObject.AddComponent<CanvasGroup>();

            _roomGroup = UIFactory.Group(_canvas.transform, "Room");
            _monitorGroup = UIFactory.Group(_canvas.transform, "Monitor");

            _monitorCanvasGroup = _monitorGroup.gameObject.AddComponent<CanvasGroup>();
            _monitorCanvasGroup.alpha = 0f;
            _monitorCanvasGroup.blocksRaycasts = false;

            _monitor = _monitorGroup.gameObject.AddComponent<MonitorScreen>();
            _monitor.Build(_monitorGroup, _facility, _ai);

            BuildClock();
            BuildGauges();
            BuildSystemsLine();
            BuildAlertStrip();
            BuildOccupiedBar();
            BuildBriefing();
        }

        /// <summary>
        /// The shift orders, shown before the clock starts.
        ///
        /// This is the game's tutorial. It is not a separate mode or a scripted
        /// sequence — it is the briefing the night already had, given enough room to
        /// actually be read, with the control list beside it. The clock is held while
        /// it is up, so nobody gets dropped into 12 AM mid-sentence.
        /// </summary>
        private void BuildBriefing()
        {
            _briefingGroup = UIFactory.Group(_canvas.transform, "Briefing");
            _briefingGroup.SetAsLastSibling();

            var backdrop = UIFactory.Panel(_briefingGroup, "Backdrop", new Color(0.015f, 0.018f, 0.022f, 0.95f));
            UIFactory.Stretch(backdrop.rectTransform);

            _briefingTitle = UIFactory.Label(_briefingGroup, "Title", "", 46, TextAnchor.UpperCenter);
            UIFactory.Anchor(_briefingTitle.rectTransform, UIFactory.TopCentre,
                new Vector2(0f, -60f), new Vector2(1400f, 60f));

            var subtitle = UIFactory.Label(_briefingGroup, "Subtitle",
                "GROTTO SPRINGS FAMILY FUN CAVERNS  \u2014  RECLAMATION SITE MONITOR",
                18, TextAnchor.UpperCenter, UIFactory.InkDim);
            UIFactory.Anchor(subtitle.rectTransform, UIFactory.TopCentre,
                new Vector2(0f, -118f), new Vector2(1400f, 26f));

            // Left: the shift orders.
            _briefingBody = UIFactory.Label(_briefingGroup, "Body", "", 24,
                TextAnchor.UpperLeft, UIFactory.Ink, wrap: true);
            var bodyRect = _briefingBody.rectTransform;
            bodyRect.anchorMin = new Vector2(0.06f, 0.28f);
            bodyRect.anchorMax = new Vector2(0.47f, 0.80f);
            bodyRect.offsetMin = bodyRect.offsetMax = Vector2.zero;

            // Right: what the switches on the desk do.
            var controlsTitle = UIFactory.Label(_briefingGroup, "ControlsTitle",
                "THE DESK", 20, TextAnchor.UpperLeft, UIFactory.InkCold);
            var controlsTitleRect = controlsTitle.rectTransform;
            controlsTitleRect.anchorMin = new Vector2(0.53f, 0.76f);
            controlsTitleRect.anchorMax = new Vector2(0.94f, 0.80f);
            controlsTitleRect.offsetMin = controlsTitleRect.offsetMax = Vector2.zero;

            var controls = UIFactory.Label(_briefingGroup, "Controls", ControlsReference(), 19,
                TextAnchor.UpperLeft, UIFactory.InkDim, wrap: true);
            var controlsRect = controls.rectTransform;
            controlsRect.anchorMin = new Vector2(0.53f, 0.26f);
            controlsRect.anchorMax = new Vector2(0.96f, 0.755f);
            controlsRect.offsetMin = controlsRect.offsetMax = Vector2.zero;

            // Bottom: the one thing that will kill you first.
            var warning = UIFactory.Label(_briefingGroup, "Warning", WarningText(), 20,
                TextAnchor.UpperCenter, UIFactory.InkWarn, wrap: true);
            var warningRect = warning.rectTransform;
            warningRect.anchorMin = new Vector2(0.08f, 0.11f);
            warningRect.anchorMax = new Vector2(0.92f, 0.24f);
            warningRect.offsetMin = warningRect.offsetMax = Vector2.zero;

            _briefingPrompt = UIFactory.Label(_briefingGroup, "Prompt",
                "PRESS  ENTER  TO BEGIN THE SHIFT", 26, TextAnchor.LowerCenter);
            UIFactory.Anchor(_briefingPrompt.rectTransform, UIFactory.BottomCentre,
                new Vector2(0f, 44f), new Vector2(1200f, 40f));

            _briefingGroup.gameObject.SetActive(false);
        }

        private static string ControlsReference()
        {
            return
                "<color=#EBA82E>SPACE</color>   raise / lower the monitor\n" +
                "<color=#EBA82E>Q  E</color>      previous / next camera\n" +
                "<color=#EBA82E>MOUSE</color>   look around the room\n\n" +
                "<color=#EBA82E>A  D</color>      north / south blast door\n" +
                "<color=#EBA82E>1 2 3</color>     floodlights: north, south, cable chase\n" +
                "<color=#EBA82E>G</color>          sump grate bolts\n\n" +
                "<color=#EBA82E>F</color>          ventilation fan: off / low / purge\n" +
                "<color=#EBA82E>P</color>          sump pump\n" +
                "<color=#EBA82E>L</color>          cap lamp\n\n" +
                "<color=#EBA82E>R</color> (hold)  reset the main breaker\n" +
                "<color=#EBA82E>T   Y</color>      pour a jerry can / crank the generator\n\n" +
                "<color=#6B7280>ESC pause    ` console    F3 overlay    J jumpscare test</color>";
        }

        private static string WarningText()
        {
            return
                "Everything runs off one eight kilowatt generator. Watch the LOAD gauge \u2014 " +
                "hold it over the line and the breaker opens, and an open breaker drops <b>both</b> doors.\n" +
                "The WATER gauge has two marks on it. Below <b>DIG</b> something can tunnel into the sump; " +
                "above <b>SWIM</b> something else can get up the intake. There is no setting that is safe from both.";
        }

        private void BuildClock()
        {
            _clock = UIFactory.Label(_roomGroup, "Clock", "12 AM", 56, TextAnchor.UpperCenter);
            UIFactory.Anchor(_clock.rectTransform, UIFactory.TopCentre, new Vector2(0f, -18f), new Vector2(420f, 70f));

            _nightLabel = UIFactory.Label(_roomGroup, "NightLabel", "", 20, TextAnchor.UpperCenter, UIFactory.InkDim);
            UIFactory.Anchor(_nightLabel.rectTransform, UIFactory.TopCentre, new Vector2(0f, -84f), new Vector2(420f, 26f));
        }

        private void BuildGauges()
        {
            var block = UIFactory.Group(_roomGroup, "Gauges");
            UIFactory.Anchor(block, UIFactory.BottomLeft, new Vector2(26f, 26f), new Vector2(400f, 270f));

            var size = new Vector2(400f, 30f);
            float y = 0f;

            Image Row(string caption, Color colour, out Text text)
            {
                var fill = UIFactory.Gauge(block, caption, size, colour, out text);
                var container = (RectTransform)fill.transform.parent;
                UIFactory.Anchor(container, UIFactory.BottomLeft, new Vector2(0f, y), size);
                y += size.y + 8f;
                return fill;
            }

            _lampFill = Row("CAP LAMP", new Color(0.55f, 0.5f, 0.2f), out _lampCaption);
            _batteryFill = Row("BATTERY", new Color(0.3f, 0.5f, 0.55f), out _batteryCaption);
            _waterFill = Row("WATER", new Color(0.18f, 0.45f, 0.55f), out _waterCaption);
            _airFill = Row("AIR", new Color(0.3f, 0.55f, 0.35f), out _airCaption);
            _loadFill = Row("LOAD", new Color(0.6f, 0.45f, 0.15f), out _loadCaption);
            _fuelFill = Row("DAY TANK", new Color(0.55f, 0.32f, 0.14f), out _fuelCaption);

            AddWaterThresholdTicks();
        }

        /// <summary>
        /// Marks the diggable and swimmable lines on the water gauge.
        ///
        /// Without these the player is tuning an invisible dial by trial and death.
        /// With them, "keep it between the ticks" is a strategy they can form in the
        /// first thirty seconds and then discover is impossible to hold all night.
        /// </summary>
        private void AddWaterThresholdTicks()
        {
            var track = (RectTransform)_waterFill.transform.parent;

            void Tick(float at01, Color colour, string label)
            {
                var mark = UIFactory.Panel(track, "Tick_" + label, colour);
                var rect = mark.rectTransform;
                rect.anchorMin = new Vector2(at01, 0f);
                rect.anchorMax = new Vector2(at01, 1f);
                rect.pivot = UIFactory.Centre;
                rect.sizeDelta = new Vector2(2f, -4f);
                rect.anchoredPosition = Vector2.zero;

                var caption = UIFactory.Label(track, "TickLabel_" + label, label, 10,
                    TextAnchor.LowerCenter, colour);
                var captionRect = caption.rectTransform;
                captionRect.anchorMin = captionRect.anchorMax = new Vector2(at01, 1f);
                captionRect.pivot = new Vector2(0.5f, 0f);
                captionRect.sizeDelta = new Vector2(90f, 14f);
                captionRect.anchoredPosition = new Vector2(0f, 1f);
            }

            // Read from the live site, so a different map's gauge shows its own marks.
            var gates = _facility.Gates;
            Tick(gates.diggable, new Color(0.85f, 0.55f, 0.2f), "DIG");
            Tick(gates.swimmable, new Color(0.35f, 0.7f, 0.85f), "SWIM");
        }

        private void BuildSystemsLine()
        {
            _systemsLine = UIFactory.Label(_roomGroup, "Systems", "", 20, TextAnchor.LowerRight);
            UIFactory.Anchor(_systemsLine.rectTransform, UIFactory.BottomRight,
                new Vector2(-26f, 26f), new Vector2(640f, 140f));
        }

        private void BuildAlertStrip()
        {
            _alert = UIFactory.Label(_roomGroup, "Alert", "", 24, TextAnchor.LowerCenter);
            UIFactory.Anchor(_alert.rectTransform, UIFactory.BottomCentre,
                new Vector2(0f, 200f), new Vector2(1100f, 90f));
            _alert.color = new Color(1f, 1f, 1f, 0f);
        }

        private void BuildOccupiedBar()
        {
            _occupiedGroup = UIFactory.Group(_roomGroup, "Occupied");
            UIFactory.Anchor(_occupiedGroup, UIFactory.Centre, Vector2.zero, new Vector2(560f, 90f));

            _occupiedLabel = UIFactory.Label(_occupiedGroup, "Label", "", 26, TextAnchor.UpperCenter, UIFactory.InkWarn);
            UIFactory.Anchor(_occupiedLabel.rectTransform, UIFactory.TopCentre, Vector2.zero, new Vector2(560f, 34f));

            _occupiedFill = UIFactory.Gauge(_occupiedGroup, "", new Vector2(520f, 22f),
                UIFactory.InkWarn, out _);
            UIFactory.Anchor((RectTransform)_occupiedFill.transform.parent, UIFactory.BottomCentre,
                new Vector2(0f, 8f), new Vector2(520f, 22f));

            _occupiedGroup.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------
        // Drive
        // ---------------------------------------------------------------------

        private void Update()
        {
            if (_facility == null) return;

            float dt = Time.unscaledDeltaTime;

            // Unscaled, and the fade runs even when paused: a HUD that freezes
            // half-transparent behind a menu looks broken.
            if (_menu == null) ServiceLocator.TryGet(out _menu);
            float targetAlpha = _menu != null && _menu.IsFrontEnd ? 0f : 1f;
            _canvasGroup.alpha = MathUtil.ExpDecay(_canvasGroup.alpha, targetAlpha, 12f, dt);
            _canvasGroup.blocksRaycasts = targetAlpha > 0.5f;

            if (targetAlpha <= 0f && _canvasGroup.alpha < 0.01f) return;

            dt = Time.deltaTime;

            UpdateBriefing();
            UpdateClock();
            UpdateGauges();
            UpdateSystemsLine();
            UpdateOccupied();
            UpdateAlert(dt);
            UpdateMonitorBlend(dt);
        }

        private void UpdateBriefing()
        {
            bool shouldShow = _night != null && _night.CurrentPhase == NightController.Phase.Briefing;

            if (shouldShow != _briefingShowing)
            {
                _briefingShowing = shouldShow;
                _briefingGroup.gameObject.SetActive(shouldShow);

                if (shouldShow)
                {
                    var definition = _night.CurrentDefinition;

                    _briefingTitle.text = definition != null
                        ? definition.displayName.ToUpperInvariant()
                        : "SHIFT ORDERS";

                    _briefingBody.text = definition != null ? definition.briefing : "";

                    // Hold the clock until they say they are ready.
                    _night.HoldBriefing = true;
                }
            }

            if (!shouldShow) return;

            // Pulse the prompt so it reads as waiting for input rather than frozen.
            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 3f);
            _briefingPrompt.color = new Color(UIFactory.Ink.r, UIFactory.Ink.g, UIFactory.Ink.b, pulse);

            var keyboard = Keyboard.current;
            bool dismissed =
                (keyboard != null && (keyboard.enterKey.wasPressedThisFrame
                                      || keyboard.numpadEnterKey.wasPressedThisFrame
                                      || keyboard.spaceKey.wasPressedThisFrame))
                || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);

            if (dismissed) _night.SkipBriefing();
        }

        private void UpdateClock()
        {
            if (_night == null) return;

            _clock.text = _night.Clock.DisplayHour;

            // The last hour is the one that kills people. Say so.
            _clock.color = _night.Clock.Hour >= 5 ? UIFactory.InkWarn : UIFactory.Ink;

            _nightLabel.text = _night.CurrentDefinition != null
                ? _night.CurrentDefinition.displayName.ToUpperInvariant()
                : "";
        }

        private void UpdateGauges()
        {
            var power = _facility.Power;
            var generator = power.Generator;

            _fuelFill.fillAmount = generator.FuelFraction;
            _fuelFill.color = generator.FuelFraction < 0.18f
                ? UIFactory.InkAlarm
                : new Color(0.55f, 0.32f, 0.14f);
            _fuelCaption.text = $"DAY TANK   {generator.FuelLitres:0} L   ({generator.SpareCans} can)";

            // Load is shown against the continuous rating, so 1.0 is the line, not the
            // maximum. Anything past it is already on borrowed time.
            float loadFraction = power.LoadFraction;
            _loadFill.fillAmount = Mathf.Clamp01(loadFraction);
            _loadFill.color = loadFraction > 1f ? UIFactory.InkAlarm
                : loadFraction > 0.85f ? UIFactory.InkWarn
                : new Color(0.6f, 0.45f, 0.15f);

            string loadSuffix = power.BreakerOpen ? "  BREAKER OPEN"
                : power.OverloadProgress01 > 0f ? $"  TRIP IN {(1f - power.OverloadProgress01) * 100f:0}%"
                : "";
            _loadCaption.text = $"LOAD   {power.TotalLoadKilowatts:0.0} kW{loadSuffix}";

            var ventilation = _facility.Ventilation;
            _airFill.fillAmount = ventilation.AirQuality01;
            _airFill.color = ventilation.IsCritical ? UIFactory.InkAlarm
                : ventilation.HallucinationPressure01 > 0f ? UIFactory.InkWarn
                : new Color(0.3f, 0.55f, 0.35f);
            _airCaption.text = $"AIR   {ventilation.AirQuality01 * 100f:0}%   FAN {ventilation.Mode.ToString().ToUpperInvariant()}";

            var water = _facility.Water;
            _waterFill.fillAmount = water.Level01;
            _waterFill.color = water.Level01 > 0.85f ? UIFactory.InkAlarm
                : new Color(0.18f, 0.45f, 0.55f);

            string trend = water.NetRatePerHour > 0.01f ? "RISING"
                : water.NetRatePerHour < -0.01f ? "FALLING" : "HOLDING";
            _waterCaption.text = $"WATER   {water.GaugeFeet:0.0} ft   {trend}";

            _batteryFill.fillAmount = power.BatteryCharge01;
            _batteryFill.color = power.BatteryCharge01 < 0.25f ? UIFactory.InkAlarm : new Color(0.3f, 0.5f, 0.55f);
            _batteryCaption.text = $"BATTERY   {power.BatteryCharge01 * 100f:0}%";

            if (_headlamp != null)
            {
                _lampFill.fillAmount = _headlamp.Charge01;
                _lampFill.color = _headlamp.IsBrowningOut ? UIFactory.InkAlarm : new Color(0.55f, 0.5f, 0.2f);
                _lampCaption.text = $"CAP LAMP   {_headlamp.Charge01 * 100f:0}%   {(_headlamp.IsOn ? "ON" : "OFF")}";
            }
        }

        private void UpdateSystemsLine()
        {
            var doorNorth = _facility.GetBarrier(FacilityBarriers.DoorNorth) as BlastDoor;
            var doorSouth = _facility.GetBarrier(FacilityBarriers.DoorSouth) as BlastDoor;
            var grate = _facility.GetBarrier(FacilityBarriers.SumpGrate) as SumpGrate;

            string DoorState(BlastDoor door) => door == null ? "<color=#666666>--</color>"
                : door.State == BlastDoor.DoorState.Buckled ? "<color=#EB4238>FAILED</color>"
                : door.IsCommandedClosed ? "<color=#EBA82E>SHUT</color>"
                : "<color=#8A8A8A>OPEN</color>";

            var builder = new System.Text.StringBuilder(256);
            builder.AppendLine($"[A] NORTH DOOR  {DoorState(doorNorth)}");
            builder.AppendLine($"[D] SOUTH DOOR  {DoorState(doorSouth)}");
            builder.AppendLine($"[G] SUMP GRATE  {(grate != null && grate.IsEnergised ? "<color=#EBA82E>BOLTED</color>" : "<color=#8A8A8A>OPEN</color>")}");
            builder.AppendLine($"[F] FAN  {_facility.Ventilation.Mode.ToString().ToUpperInvariant()}   " +
                               $"[P] PUMP  {(_facility.Water.IsPumping ? "<color=#EBA82E>RUN</color>" : "OFF")}");
            builder.Append("[SPACE] MONITOR   [R] BREAKER   [T] REFUEL   [Y] CRANK");

            _systemsLine.text = builder.ToString();
        }

        private void UpdateOccupied()
        {
            var power = _facility.Power;
            var generator = power.Generator;

            bool occupied = power.IsPlayerOccupied;
            _occupiedGroup.gameObject.SetActive(occupied);
            if (!occupied) return;

            if (power.IsResettingBreaker)
            {
                _occupiedLabel.text = "HOLDING BREAKER LEVER — DO NOT LET GO";
                _occupiedFill.fillAmount = power.ResetProgress01;
            }
            else if (generator.IsRefuelling)
            {
                _occupiedLabel.text = "POURING FUEL";
                _occupiedFill.fillAmount = generator.ActionProgress01;
            }
            else
            {
                _occupiedLabel.text = "CRANKING — EVERYTHING CAN HEAR THIS";
                _occupiedFill.fillAmount = generator.ActionProgress01;
            }
        }

        private void OnAlert(AlertSignal signal)
        {
            _alert.text = signal.Message;
            _alert.color = UIFactory.ColorFor(signal.Severity);
            _alertRemaining = signal.Severity == AlertSeverity.Critical ? 5.5f : 3.5f;
        }

        private void UpdateAlert(float dt)
        {
            if (_alertRemaining <= 0f)
            {
                var faded = _alert.color;
                faded.a = Mathf.MoveTowards(faded.a, 0f, dt * 2f);
                _alert.color = faded;
                return;
            }

            _alertRemaining -= dt;

            var colour = _alert.color;
            colour.a = _alertRemaining < 0.8f ? _alertRemaining / 0.8f : 1f;
            _alert.color = colour;
        }

        private void OnMonitorToggled(bool up)
        {
            _monitorCanvasGroup.blocksRaycasts = up;

            // The plan is mouse-driven, so the cursor comes back while the monitor is
            // up and goes away again when the player is looking at the room.
            Cursor.lockState = up ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = up;
        }

        private void UpdateMonitorBlend(float dt)
        {
            float target = _facility.Surveillance.MonitorUp ? 1f : 0f;
            _monitorCanvasGroup.alpha = MathUtil.ExpDecay(_monitorCanvasGroup.alpha, target, 14f, dt);

            // Gauges stay readable behind the monitor, just dimmed — the player should
            // never have to drop the monitor to check the fuel.
            var roomGroup = _roomGroup.GetComponent<CanvasGroup>();
            if (roomGroup == null) roomGroup = _roomGroup.gameObject.AddComponent<CanvasGroup>();
            roomGroup.alpha = Mathf.Lerp(1f, 0.55f, _monitorCanvasGroup.alpha);
        }
    }
}
