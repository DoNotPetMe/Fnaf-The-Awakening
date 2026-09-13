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
