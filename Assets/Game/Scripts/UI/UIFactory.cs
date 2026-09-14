using UnityEngine;
using UnityEngine.UI;
using Grotto.Core;

namespace Grotto.UI
{
    /// <summary>
    /// Builders for the station interface.
    ///
    /// The UI is constructed in code rather than authored as prefabs, for the same
    /// reason the map is: a .prefab is opaque YAML that cannot be reviewed, and this
    /// interface is a fixed instrument panel, not a screen flow a designer will
    /// iterate on visually.
    ///
    /// Text uses <see cref="Text"/> with Unity's built-in font rather than
    /// TextMeshPro. TMP needs its Essential Resources imported through a dialog before
    /// it will render anything, and a freshly cloned repository that shows a screen of
    /// missing-font errors is a bad first five minutes. Every label in the game goes
    /// through <see cref="Label"/>, so moving to TMP later is a change to one method.
    /// </summary>
    public static class UIFactory
    {
        // The station's palette: amber phosphor on near-black, with a cold accent for
        // anything the player did not choose.
        public static readonly Color Ink = new Color(0.92f, 0.72f, 0.36f);
        public static readonly Color InkDim = new Color(0.62f, 0.49f, 0.26f);
        public static readonly Color InkWarn = new Color(0.96f, 0.66f, 0.18f);
        public static readonly Color InkAlarm = new Color(0.92f, 0.26f, 0.22f);
        public static readonly Color InkCold = new Color(0.45f, 0.78f, 0.86f);
        public static readonly Color PanelBack = new Color(0.035f, 0.038f, 0.045f, 0.88f);
        public static readonly Color PanelEdge = new Color(0.16f, 0.15f, 0.13f, 1f);

        private static Font _font;

        public static Font DefaultFont
        {
            get
            {
                if (_font != null) return _font;

                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                if (_font == null)
                {
                    // Last resort on a platform with neither built-in.
                    _font = Font.CreateDynamicFontFromOSFont("Courier New", 16);
                    GLog.Warn(LogChannel.UI, "Falling back to an OS font; text metrics may differ.");
                }

                return _font;
            }
        }

        // ---------------------------------------------------------------------
        // Structure
        // ---------------------------------------------------------------------

        public static Canvas CreateCanvas(string canvasName, int sortOrder, Transform parent = null,
            bool withRaycaster = true)
        {
            var go = new GameObject(canvasName, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            go.layer = LayerMask.NameToLayer("UI");

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Favour width slightly: the monitor is the widest element and must not
            // crop on an ultrawide.
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.4f;

            if (withRaycaster) go.AddComponent<GraphicRaycaster>();

            return canvas;
        }

        /// <summary>An empty stretch-to-parent rect, for grouping.</summary>
        public static RectTransform Group(Transform parent, string groupName)
        {
            var go = new GameObject(groupName, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.layer = LayerMask.NameToLayer("UI");

            var rect = (RectTransform)go.transform;
            Stretch(rect);
            return rect;
        }

        public static Image Panel(Transform parent, string panelName, Color color)
        {
            var go = new GameObject(panelName, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.layer = LayerMask.NameToLayer("UI");

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        public static Text Label(Transform parent, string labelName, string content,
            int fontSize = 22, TextAnchor anchor = TextAnchor.UpperLeft, Color? color = null,
            bool wrap = false)
        {
            var go = new GameObject(labelName, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.layer = LayerMask.NameToLayer("UI");

            var text = go.AddComponent<Text>();
            text.font = DefaultFont;
            text.fontSize = fontSize;
            text.text = content;
            text.alignment = anchor;
            text.color = color ?? Ink;
            text.raycastTarget = false;
            text.horizontalOverflow = wrap ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;

            return text;
        }

        public static RawImage Feed(Transform parent, string feedName, Material material = null)
        {
            var go = new GameObject(feedName, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.layer = LayerMask.NameToLayer("UI");

            var image = go.AddComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            if (material != null) image.material = material;

            return image;
        }

        public static Button TextButton(Transform parent, string buttonName, string content,
            Vector2 size, System.Action onClick, int fontSize = 26)
        {
            var background = Panel(parent, buttonName, PanelBack);
            background.raycastTarget = true;

            var rect = background.rectTransform;
            rect.sizeDelta = size;

            var outline = background.gameObject.AddComponent<Outline>();
            outline.effectColor = PanelEdge;
            outline.effectDistance = new Vector2(2f, -2f);

            var label = Label(rect, "Label", content, fontSize, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform);

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.4f, 1.3f, 1.1f);
            colors.pressedColor = new Color(0.7f, 0.65f, 0.5f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            if (onClick != null) button.onClick.AddListener(() => onClick());

            return button;
        }

        /// <summary>
        /// A horizontal bar gauge. Returns the fill image; drive it by setting
        /// <c>fillAmount</c>. Uses a filled Image rather than a Slider because nothing
        /// on this panel is interactive and a Slider brings a raycast target with it.
        /// </summary>
        public static Image Gauge(Transform parent, string gaugeName, Vector2 size,
            Color fillColor, out Text caption)
        {
            var container = Panel(parent, gaugeName, new Color(0f, 0f, 0f, 0.55f));
            container.rectTransform.sizeDelta = size;

            var outline = container.gameObject.AddComponent<Outline>();
            outline.effectColor = PanelEdge;
            outline.effectDistance = new Vector2(1.5f, -1.5f);

            var fill = Panel(container.transform, "Fill", fillColor);
            var fillRect = fill.rectTransform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            fillRect.pivot = new Vector2(0f, 0.5f);

            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 1f;
            // A filled Image needs a sprite; the built-in UI sprite is always present.
            fill.sprite = BuiltinSprite;

            caption = Label(container.transform, "Caption", gaugeName, 16, TextAnchor.MiddleLeft);
            var captionRect = caption.rectTransform;
            Stretch(captionRect);
            captionRect.offsetMin = new Vector2(8f, 0f);
            captionRect.offsetMax = new Vector2(-8f, 0f);

            return fill;
        }

        /// <summary>
        /// A framed panel with a title strip. The unit the front-end screens are built
        /// from — a site card, a character dossier, a settings block are all this.
        /// </summary>
        public static RectTransform Card(Transform parent, string cardName, string heading,
            Vector2 size, out Text headingLabel, Color? accent = null)
        {
            var background = Panel(parent, cardName, new Color(0.045f, 0.048f, 0.055f, 0.94f));
            background.rectTransform.sizeDelta = size;
            background.raycastTarget = true;

            var outline = background.gameObject.AddComponent<Outline>();
            outline.effectColor = accent ?? PanelEdge;
            outline.effectDistance = new Vector2(2f, -2f);

            // A colour strip down the left edge. Cheap, and it does more to make a list
            // of cards readable than any amount of typography.
            var strip = Panel(background.transform, "Accent", accent ?? InkDim);
            Anchor(strip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(4f, size.y));

            headingLabel = Label(background.transform, "Heading", heading, 26, TextAnchor.UpperLeft,
                accent ?? Ink);
            Anchor(headingLabel.rectTransform, TopLeft, new Vector2(20f, -14f), new Vector2(size.x - 40f, 32f));

            return background.rectTransform;
        }

        /// <summary>A thin horizontal rule. Used to break a column of controls into blocks.</summary>
        public static Image Rule(Transform parent, string ruleName, float width, Color? color = null)
        {
            var rule = Panel(parent, ruleName, color ?? new Color(0.22f, 0.20f, 0.17f, 1f));
            rule.rectTransform.sizeDelta = new Vector2(width, 1.5f);
            return rule;
        }

        /// <summary>
        /// A labelled value slider.
        ///
        /// Built from a Slider rather than a filled Image because unlike the station
        /// gauges this one *is* interactive. The caller gets the slider back and hooks
        /// onValueChanged itself; the value readout updates on its own.
        /// </summary>
        public static Slider ValueSlider(Transform parent, string sliderName, string caption,
            Vector2 size, float value, float min, float max, System.Action<float> onChanged,
            System.Func<float, string> format = null)
        {
            var row = Group(parent, sliderName);
            row.sizeDelta = size;

            var label = Label(row, "Caption", caption, 20, TextAnchor.MiddleLeft, InkDim);
            Anchor(label.rectTransform, TopLeft, new Vector2(0f, 0f), new Vector2(size.x * 0.45f, size.y));

            var readout = Label(row, "Value", "", 20, TextAnchor.MiddleRight, Ink);
            Anchor(readout.rectTransform, TopRight, new Vector2(0f, 0f), new Vector2(size.x * 0.16f, size.y));

            float trackWidth = size.x * 0.34f;

            var track = Panel(row, "Track", new Color(0f, 0f, 0f, 0.6f));
            Anchor(track.rectTransform, TopLeft, new Vector2(size.x * 0.46f, -size.y * 0.5f + 4f),
                new Vector2(trackWidth, 8f));
            track.rectTransform.pivot = new Vector2(0f, 0.5f);
            track.raycastTarget = true;

            var fill = Panel(track.transform, "Fill", Ink);
            var fillRect = Stretch(fill.rectTransform);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fill.sprite = BuiltinSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            var handle = Panel(track.transform, "Handle", Ink);
            handle.rectTransform.sizeDelta = new Vector2(10f, 22f);

            var slider = track.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.fillRect = fillRect;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.SetValueWithoutNotify(Mathf.Clamp(value, min, max));

            format ??= v => $"{Mathf.RoundToInt(Mathf.InverseLerp(min, max, v) * 100f)}%";
            readout.text = format(slider.value);

            slider.onValueChanged.AddListener(v =>
            {
                readout.text = format(v);
                onChanged?.Invoke(v);
            });

            return slider;
        }

        /// <summary>
        /// An on/off row. A button rather than a Toggle: a Toggle needs a checkmark
        /// graphic and gives nothing back for it, and the whole row being clickable is
        /// a better target than a 20px box.
        /// </summary>
        public static Button ToggleRow(Transform parent, string toggleName, string caption,
            Vector2 size, bool value, System.Action<bool> onChanged, string note = "")
        {
            bool state = value;

            var background = Panel(parent, toggleName, new Color(0f, 0f, 0f, 0.35f));
            background.rectTransform.sizeDelta = size;
            background.raycastTarget = true;

            var label = Label(background.transform, "Caption", caption, 20, TextAnchor.MiddleLeft, InkDim);
            Anchor(label.rectTransform, TopLeft, new Vector2(12f, 0f), new Vector2(size.x * 0.62f, size.y));

            var readout = Label(background.transform, "State", "", 20, TextAnchor.MiddleRight);
            Anchor(readout.rectTransform, TopRight, new Vector2(-12f, 0f), new Vector2(size.x * 0.3f, size.y));

            void Paint()
            {
                readout.text = state ? "ON" : "OFF";
                readout.color = state ? Ink : new Color(0.42f, 0.40f, 0.38f);
            }
            Paint();

            if (!string.IsNullOrEmpty(note))
            {
                var hint = Label(background.transform, "Note", note, 15, TextAnchor.LowerLeft,
                    new Color(0.45f, 0.42f, 0.38f));
                Anchor(hint.rectTransform, BottomLeft, new Vector2(12f, 4f), new Vector2(size.x - 24f, 18f));
            }

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                state = !state;
                Paint();
                onChanged?.Invoke(state);
            });

            return button;
        }

        /// <summary>
        /// A labelled left/right chooser for a small set of options. Used for quality
        /// and frame-rate, where a slider would imply a continuum that is not there.
        /// </summary>
        public static void OptionRow(Transform parent, string optionName, string caption,
            Vector2 size, string[] options, int index, System.Action<int> onChanged)
        {
            int current = Mathf.Clamp(index, 0, Mathf.Max(0, options.Length - 1));

            var background = Panel(parent, optionName, new Color(0f, 0f, 0f, 0.35f));
            background.rectTransform.sizeDelta = size;

            var label = Label(background.transform, "Caption", caption, 20, TextAnchor.MiddleLeft, InkDim);
            Anchor(label.rectTransform, TopLeft, new Vector2(12f, 0f), new Vector2(size.x * 0.5f, size.y));

            var readout = Label(background.transform, "Value", "", 20, TextAnchor.MiddleCenter);
            Anchor(readout.rectTransform, TopRight, new Vector2(-54f, 0f), new Vector2(size.x * 0.34f, size.y));

            void Paint() => readout.text = options.Length > 0 ? options[current] : "-";
            Paint();

            void Step(int delta)
            {
                if (options.Length == 0) return;
                current = (current + delta + options.Length) % options.Length;
                Paint();
                onChanged?.Invoke(current);
            }

            var left = TextButton(background.transform, "Prev", "<", new Vector2(34f, size.y - 12f),
                () => Step(-1), 22);
            Anchor((RectTransform)left.transform, TopRight, new Vector2(-96f, -6f), new Vector2(34f, size.y - 12f));

            var right = TextButton(background.transform, "Next", ">", new Vector2(34f, size.y - 12f),
                () => Step(1), 22);
            Anchor((RectTransform)right.transform, TopRight, new Vector2(-12f, -6f), new Vector2(34f, size.y - 12f));
        }

        /// <summary>
        /// A small labelled bar, for comparing one site's water/air/fuel pressure
        /// against another's at a glance.
        /// </summary>
        public static void StatBar(Transform parent, string barName, string caption,
            float value01, Vector2 size, Color color, string readout = "")
        {
            var row = Group(parent, barName);
            row.sizeDelta = size;

            var label = Label(row, "Caption", caption, 15, TextAnchor.MiddleLeft, InkDim);
            Anchor(label.rectTransform, TopLeft, Vector2.zero, new Vector2(size.x * 0.34f, size.y));

            var track = Panel(row, "Track", new Color(0f, 0f, 0f, 0.55f));
            Anchor(track.rectTransform, TopLeft, new Vector2(size.x * 0.36f, -size.y * 0.5f + 3f),
                new Vector2(size.x * 0.44f, 6f));
            track.rectTransform.pivot = new Vector2(0f, 0.5f);

            var fill = Panel(track.transform, "Fill", color);
            var fillRect = Stretch(fill.rectTransform);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fill.sprite = BuiltinSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = Mathf.Clamp01(value01);

            if (string.IsNullOrEmpty(readout)) return;

            var value = Label(row, "Value", readout, 15, TextAnchor.MiddleRight, color);
            Anchor(value.rectTransform, TopRight, Vector2.zero, new Vector2(size.x * 0.16f, size.y));
        }

        private static Sprite _builtinSprite;

        /// <summary>Unity's built-in white UI sprite. Needed by filled images.</summary>
        public static Sprite BuiltinSprite
        {
            get
            {
                if (_builtinSprite != null) return _builtinSprite;

                _builtinSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
                if (_builtinSprite == null)
                {
                    // Synthesise a 4x4 white sprite so filled images still work.
                    var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var pixels = new Color32[16];
                    for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
                    texture.SetPixels32(pixels);
                    texture.Apply();

                    _builtinSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
                }

                return _builtinSprite;
            }
        }

        // ---------------------------------------------------------------------
        // Rect helpers
        // ---------------------------------------------------------------------

        public static RectTransform Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        /// <summary>Anchors a rect to a corner with a pixel offset and a fixed size.</summary>
        public static RectTransform Anchor(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
            return rect;
        }

        public static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        public static readonly Vector2 TopRight = new Vector2(1f, 1f);
        public static readonly Vector2 BottomLeft = new Vector2(0f, 0f);
        public static readonly Vector2 BottomRight = new Vector2(1f, 0f);
        public static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);
        public static readonly Vector2 BottomCentre = new Vector2(0.5f, 0f);
        public static readonly Vector2 TopCentre = new Vector2(0.5f, 1f);

        /// <summary>Colour for an alert severity, so every surface agrees.</summary>
        public static Color ColorFor(AlertSeverity severity) => severity switch
        {
            AlertSeverity.Critical => InkAlarm,
            AlertSeverity.Warning => InkWarn,
            _ => Ink
        };

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _font = null;
            _builtinSprite = null;
        }
#endif
    }
}
