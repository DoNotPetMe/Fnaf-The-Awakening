using System.Text;
using UnityEngine;
using Grotto.AI;
using Grotto.Core;
using Grotto.Facility;

namespace Grotto.DevTools
{
    /// <summary>
    /// The F3 panel: every number that matters, on one screen, updated live.
    ///
    /// This is the tool that turns "it felt unfair" into a bug report. When a player
    /// dies and cannot say why, the answer is almost always in here — a character was
    /// at a threshold the whole time, or the water crossed a gate thirty seconds ago,
    /// or the breaker was two hundred milliseconds from tripping.
    ///
    /// Rendered with IMGUI for the same reason as the console: it has to work when the
    /// rest of the game does not. It costs nothing when hidden, because
    /// <see cref="OnGUI"/> returns immediately.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class DebugOverlay : MonoBehaviour
    {
        private const int FrameSamples = 120;

        [SerializeField] private KeyCode toggleKey = KeyCode.F3;
        [SerializeField] private float panelWidth = 520f;

        private readonly float[] _frameTimes = new float[FrameSamples];
        private int _frameIndex;

        private readonly StringBuilder _builder = new StringBuilder(2048);
        private GUIStyle _panelStyle;
        private GUIStyle _textStyle;
        private Texture2D _background;

        private FacilityRuntime _facility;
        private NightController _night;
        private AIDirector _ai;
        private PhantomCotton _phantom;

        private void Start()
        {
            ServiceLocator.TryGet(out _facility);
            ServiceLocator.TryGet(out _night);
            ServiceLocator.TryGet(out _ai);
            ServiceLocator.TryGet(out _phantom);

            if (!DevConsole.Available) enabled = false;
        }

        private void Update()
        {
            _frameTimes[_frameIndex] = Time.unscaledDeltaTime;
            _frameIndex = (_frameIndex + 1) % FrameSamples;
        }

        private void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == toggleKey)
            {
                DebugFlags.ShowDebugOverlay = !DebugFlags.ShowDebugOverlay;
                DebugFlags.NotifyChanged();
                e.Use();
            }

            if (!DebugFlags.ShowDebugOverlay) return;

            EnsureStyles();
            Compose();

            var content = new GUIContent(_builder.ToString());
            float height = _textStyle.CalcHeight(content, panelWidth - 20f) + 16f;

            var rect = new Rect(Screen.width - panelWidth - 12f, 12f, panelWidth, height);
            GUI.depth = -900;
            GUI.Box(rect, GUIContent.none, _panelStyle);
            GUI.Label(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f),
                content, _textStyle);
        }

        // ---------------------------------------------------------------------

        private void Compose()
        {
            _builder.Clear();

            ComposePerformance();
            ComposeNight();
            ComposePower();
            ComposeEnvironment();
            ComposeCast();
            ComposeFlags();
        }

        private void ComposePerformance()
        {
            // Worst frame in the window matters more than the average: a 4 ms mean with
            // a 40 ms spike is a stutter the player felt and the average hid.
            float total = 0f, worst = 0f;
            for (int i = 0; i < FrameSamples; i++)
            {
                total += _frameTimes[i];
                worst = Mathf.Max(worst, _frameTimes[i]);
            }

            float mean = total / FrameSamples;

            _builder.Append($"<b>PERF</b>  {1f / Mathf.Max(0.0001f, mean):0} fps   ")
                    .Append($"mean {mean * 1000f:0.0} ms   worst {worst * 1000f:0.0} ms   ")
                    .Append($"heap {System.GC.GetTotalMemory(false) / (1024 * 1024)} MB\n");
        }

        private void ComposeNight()
        {
            if (_night == null) return;

            _builder.Append($"\n<b>NIGHT</b> {_night.CurrentNight}  {_night.CurrentPhase}  ")
                    .Append($"{_night.Clock.DisplayHour}  ")
                    .Append($"{_night.Clock.NightProgress01 * 100f:0}%  ")
                    .Append($"seed {_night.Rng?.Seed ?? 0}");

            if (!Mathf.Approximately(DebugFlags.ClockScale, 1f))
                _builder.Append($"  <color=#FFD15A>x{DebugFlags.ClockScale:0.##}</color>");

            _builder.Append('\n');
        }

        private void ComposePower()
        {
            if (_facility == null) return;

            var power = _facility.Power;
            var generator = power.Generator;

            string stateColour = power.State switch
            {
                PowerState.Blackout => "#FF6B61",
                PowerState.Tripped => "#FF6B61",
                PowerState.Overloaded => "#FFD15A",
                _ => "#9BD17A"
            };

            _builder.Append($"\n<b>POWER</b> <color={stateColour}>{power.State}</color>   ")
                    .Append($"{power.TotalLoadKilowatts:0.00} kW ({power.LoadFraction * 100f:0}%)   ")
                    .Append($"fuel {generator.FuelLitres:0.0} L   ")
                    .Append($"cans {generator.SpareCans}   ")
                    .Append($"batt {power.BatteryCharge01 * 100f:0}%\n");

            if (power.OverloadProgress01 > 0f)
                _builder.Append($"      <color=#FFD15A>overload {power.OverloadProgress01 * 100f:0}% to trip</color>\n");

            if (power.IsResettingBreaker)
                _builder.Append($"      reset {power.ResetProgress01 * 100f:0}%\n");

            if (generator.IsBusy)
                _builder.Append($"      {(generator.IsRefuelling ? "refuelling" : "cranking")} " +
                                $"{generator.ActionProgress01 * 100f:0}%\n");
        }

        private void ComposeEnvironment()
        {
            if (_facility == null) return;

            var air = _facility.Ventilation;
            var water = _facility.Water;

            _builder.Append($"\n<b>AIR</b>   {air.AirQuality01 * 100f:0}%  fan {air.Mode}");
            if (air.HallucinationPressure01 > 0f)
                _builder.Append($"  <color=#FFD15A>halluc {air.HallucinationPressure01:0.00}</color>");
            if (air.SuffocationProgress01 > 0f)
                _builder.Append($"  <color=#FF6B61>suffoc {air.SuffocationProgress01 * 100f:0}%</color>");
            _builder.Append('\n');

            // The gate states are the single most useful line on this panel.
            _builder.Append($"<b>WATER</b> {water.Level01:0.000}  ")
                    .Append($"{(water.NetRatePerHour > 0f ? "RISING" : water.NetRatePerHour < 0f ? "falling" : "hold")} ")
                    .Append($"{water.NetRatePerHour:+0.000;-0.000}/h  ")
                    .Append($"pump {(water.IsPumping ? "RUN" : "off")}");
            if (water.IsCavitating) _builder.Append(" <color=#FF6B61>CAVITATING</color>");
            _builder.Append('\n');

            _builder.Append("      gates: ")
                    .Append(water.SumpIsDry
                        ? "<color=#FF6B61>sump DRY (marlow)</color>"
                        : "sump wet")
                    .Append("   ")
                    .Append(water.ChannelIsSwimmable
                        ? "<color=#FF6B61>channel DEEP (echo)</color>"
                        : "channel shallow")
                    .Append('\n');

            var loudest = _facility.Noise.Loudest(out float level);
            _builder.Append($"<b>NOISE</b> loudest {loudest} {level:0.00}   ")
                    .Append($"total {_facility.Noise.TotalEnergy:0.0}\n");
        }

        private void ComposeCast()
        {
            if (_ai == null) return;

            _builder.Append("\n<b>CAST</b>\n");

            foreach (var controller in _ai.Cast)
            {
                if (controller == null || controller.AiLevel <= 0) continue;

                string colour = controller.State switch
                {
                    AnimatronicState.Attack => "#FF6B61",
                    AnimatronicState.Threshold => "#FFD15A",
                    AnimatronicState.Stalk => "#FFB55A",
                    AnimatronicState.Stranded => "#6B7280",
                    _ => "#C8CCD0"
                };

                string where = controller.IsInTransit
                    ? $"{controller.CurrentNode}→{controller.TransitTarget} {controller.TransitProgress01 * 100f:0}%"
                    : controller.CurrentNode.Key;

                _builder.Append($"  <color={colour}>{controller.Id,-9}</color>")
                        .Append($"L{controller.AiLevel,-3}")
                        .Append($"{controller.State,-10}")
                        .Append($"{where,-22}");

                if (controller.State == AnimatronicState.Threshold)
                    _builder.Append($"<color=#FF6B61>strike {controller.AttackProgress01 * 100f:0}%</color>");
                else
                    _builder.Append($"roll {controller.NextRollIn:0.0}s @{controller.CurrentRollChance01 * 100f:0}%");

                _builder.Append('\n');

                string note = controller.BehaviourSummary;
                if (!string.IsNullOrEmpty(note))
                    _builder.Append($"             <color=#7A8088>{note}</color>\n");
            }

            if (_phantom != null && _phantom.Pressure01 > 0f)
                _builder.Append($"  <color=#9BB1D1>cotton</color>   pressure {_phantom.Pressure01:0.00}\n");
        }

        private void ComposeFlags()
        {
            var active = new StringBuilder(128);

            if (DebugFlags.GodMode) active.Append(" god");
            if (DebugFlags.FreezeAI) active.Append(" freezeAI");
            if (DebugFlags.InfinitePower) active.Append(" infPower");
            if (DebugFlags.FreezeEnvironment) active.Append(" freezeEnv");
            if (DebugFlags.DisableJumpscares) active.Append(" noScares");
            if (DebugFlags.AllCamerasOnline) active.Append(" allCams");
            if (DebugFlags.ForcedSeed.HasValue) active.Append($" seed:{DebugFlags.ForcedSeed.Value}");

            if (active.Length > 0)
                _builder.Append($"\n<color=#FFD15A><b>OVERRIDES</b>{active}</color>\n");

            _builder.Append($"\n<color=#6B7280>F3 overlay   ` console   " +
                            $"{ConsoleLogCapture.ErrorCount} errors</color>");
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null && _background != null) return;

            _background = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _background.SetPixel(0, 0, new Color(0.02f, 0.025f, 0.03f, 0.88f));
            _background.Apply();
            _background.hideFlags = HideFlags.HideAndDontSave;

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _background;
            _panelStyle.border = new RectOffset(0, 0, 0, 0);

            _textStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 12,
                wordWrap = false,
                alignment = TextAnchor.UpperLeft
            };
            _textStyle.normal.textColor = new Color(0.82f, 0.85f, 0.88f);

            // A monospaced face keeps the columns aligned; fall back silently if the
            // platform has none.
            var mono = Font.CreateDynamicFontFromOSFont(
                new[] { "Consolas", "Menlo", "DejaVu Sans Mono", "Courier New" }, 12);
            if (mono != null) _textStyle.font = mono;
        }
    }
}
