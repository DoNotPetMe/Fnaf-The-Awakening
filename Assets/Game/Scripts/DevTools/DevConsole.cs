using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Grotto.Core;

namespace Grotto.DevTools
{
    /// <summary>
    /// The in-game developer console.
    ///
    /// Drawn with IMGUI rather than uGUI on purpose. A debug tool must work when the
    /// thing it is debugging is broken — including when the Canvas, the event system
    /// or the whole UI layer has fallen over — and IMGUI needs none of them. It is the
    /// wrong choice for a shipping interface and the right one for this.
    ///
    /// Opens on backquote. In a development build or the editor it is always
    /// available; in a release player it appears only when the process was started
    /// with <c>-grotto-devtools</c>, so QA can reproduce a report on a real build
    /// without shipping the console to players.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class DevConsole : MonoBehaviour
    {
        private const string InputControlName = "GrottoConsoleInput";
        private const int MaxHistory = 64;

        [SerializeField] private int visibleLines = 18;
        [SerializeField] private float heightFraction = 0.45f;

        private bool _open;
        private string _input = "";
        private Vector2 _scroll;
        private bool _focusQueued;
        private bool _scrollQueued;

        private readonly List<string> _output = new List<string>(256);
        private readonly List<string> _history = new List<string>(MaxHistory);
        private readonly List<ConsoleLogCapture.Entry> _logScratch = new List<ConsoleLogCapture.Entry>(128);
        private int _historyIndex = -1;

        private GUIStyle _panelStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _inputStyle;
        private Texture2D _panelTexture;

        public bool IsOpen => _open;

        /// <summary>True when the console may be opened at all in this build.</summary>
        public static bool Available => DebugFlags.IsDevBuild || GameBootstrap.DevToolsRequestedByCommandLine;

        private void Awake()
        {
            if (!Available)
            {
                enabled = false;
                return;
            }

            ConsoleLogCapture.Hook();
            DevCommandRegistry.Initialise();
            ServiceLocator.Register(this);

            Print("<b>THE AWAKENING — developer console</b>");
            Print($"{DevCommandRegistry.Count} commands. Type 'help' for a list, TAB to complete.");
        }

        private void OnDestroy() => ServiceLocator.Unregister(this);

        // ---------------------------------------------------------------------
        // Output
        // ---------------------------------------------------------------------

        public void Print(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            foreach (var line in message.Split('\n'))
                _output.Add(line);

            while (_output.Count > 400) _output.RemoveAt(0);
            _scrollQueued = true;
        }

        public void Execute(string line)
        {
            Print($"<color=#7FB3C8>> {line}</color>");

            string result = DevCommandRegistry.Execute(line);
            if (!string.IsNullOrEmpty(result)) Print(result);

            if (_history.Count == 0 || _history[_history.Count - 1] != line)
            {
                _history.Add(line);
                while (_history.Count > MaxHistory) _history.RemoveAt(0);
            }
            _historyIndex = -1;
        }

        public void Open()
        {
            _open = true;
            _focusQueued = true;
            _scrollQueued = true;
        }

        public void Close()
        {
            _open = false;
            _input = "";
        }

        public void Toggle()
        {
            if (_open) Close();
            else Open();
        }

        // ---------------------------------------------------------------------
        // IMGUI
        // ---------------------------------------------------------------------

        private void OnGUI()
        {
            EnsureStyles();

            var e = Event.current;

            // The toggle is handled from the event stream rather than polled, so the
            // key never leaks into the text field as a stray backquote.
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.BackQuote)
            {
                Toggle();
                e.Use();
                return;
            }

            if (!_open) return;

            if (e.type == EventType.KeyDown)
            {
                switch (e.keyCode)
                {
                    case KeyCode.Escape:
                        Close();
                        e.Use();
                        return;

                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                        if (!string.IsNullOrWhiteSpace(_input))
                        {
                            Execute(_input.Trim());
                            _input = "";
                        }
                        _focusQueued = true;
                        e.Use();
                        break;

                    case KeyCode.Tab:
                        CompleteInput();
                        e.Use();
                        break;

                    case KeyCode.UpArrow:
                        StepHistory(1);
                        e.Use();
                        break;

                    case KeyCode.DownArrow:
                        StepHistory(-1);
                        e.Use();
                        break;
                }
            }

            DrawPanel();
        }

        private void DrawPanel()
        {
            float height = Screen.height * heightFraction;
            var panel = new Rect(0f, 0f, Screen.width, height);

            GUI.depth = -1000;
            GUI.Box(panel, GUIContent.none, _panelStyle);

            var contentRect = new Rect(8f, 6f, Screen.width - 16f, height - 42f);
            var viewRect = new Rect(0f, 0f, contentRect.width - 20f, _output.Count * _lineStyle.lineHeight + 8f);

            if (_scrollQueued && Event.current.type == EventType.Repaint)
            {
                _scroll.y = Mathf.Max(0f, viewRect.height - contentRect.height);
                _scrollQueued = false;
            }

            _scroll = GUI.BeginScrollView(contentRect, _scroll, viewRect);
            for (int i = 0; i < _output.Count; i++)
            {
                var lineRect = new Rect(0f, i * _lineStyle.lineHeight, viewRect.width, _lineStyle.lineHeight);
                GUI.Label(lineRect, _output[i], _lineStyle);
            }
            GUI.EndScrollView();

            var inputRect = new Rect(8f, height - 32f, Screen.width - 100f, 26f);
            GUI.SetNextControlName(InputControlName);
            _input = GUI.TextField(inputRect, _input, _inputStyle);

            var statsRect = new Rect(Screen.width - 88f, height - 32f, 80f, 26f);
            GUI.Label(statsRect,
                $"<color=#FF6B61>{ConsoleLogCapture.ErrorCount}e</color> " +
                $"<color=#FFD15A>{ConsoleLogCapture.WarningCount}w</color>",
                _lineStyle);

            if (_focusQueued)
            {
                GUI.FocusControl(InputControlName);
                _focusQueued = false;
            }
        }

        private void CompleteInput()
        {
            var tokens = DevCommandRegistry.Tokenise(_input);
            if (tokens.Count == 0) return;

            // Only complete the command name; arguments are too varied to guess.
            if (tokens.Count > 1 || _input.EndsWith(" ")) return;

            var matches = DevCommandRegistry.Complete(tokens[0]);
            if (matches.Count == 0) return;

            if (matches.Count == 1)
            {
                _input = matches[0] + " ";
                return;
            }

            // Several matches: fill in the longest shared prefix and list the options.
            string prefix = matches[0];
            for (int i = 1; i < matches.Count; i++)
            {
                int length = 0;
                while (length < prefix.Length && length < matches[i].Length &&
                       char.ToLowerInvariant(prefix[length]) == char.ToLowerInvariant(matches[i][length]))
                    length++;
                prefix = prefix.Substring(0, length);
            }

            _input = prefix;
            Print(string.Join("   ", matches));
        }

        private void StepHistory(int direction)
        {
            if (_history.Count == 0) return;

            _historyIndex = Mathf.Clamp(_historyIndex + direction, -1, _history.Count - 1);
            _input = _historyIndex < 0 ? "" : _history[_history.Count - 1 - _historyIndex];
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null && _panelTexture != null) return;

            _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _panelTexture.SetPixel(0, 0, new Color(0.03f, 0.035f, 0.045f, 0.94f));
            _panelTexture.Apply();
            _panelTexture.hideFlags = HideFlags.HideAndDontSave;

            _panelStyle = new GUIStyle(GUI.skin.box);
            _panelStyle.normal.background = _panelTexture;
            _panelStyle.border = new RectOffset(0, 0, 0, 0);

            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 13,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft
            };
            _lineStyle.normal.textColor = new Color(0.82f, 0.84f, 0.86f);

            _inputStyle = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
            _inputStyle.normal.textColor = new Color(0.95f, 0.80f, 0.45f);
        }

        // ---------------------------------------------------------------------
        // Commands that belong to the console itself
        // ---------------------------------------------------------------------

        [DevCommand("help", Category = "console",
            Help = "Lists commands. 'help <category>' narrows it.", Usage = "help [category]")]
        private static string Help(CommandArgs args)
            => DevCommandRegistry.Describe(args.Count > 0 ? args.String(0) : null);

        [DevCommand("clear", Category = "console", Help = "Clears the console output.")]
        private static string Clear(CommandArgs args)
        {
            if (ServiceLocator.TryGet(out DevConsole console)) console._output.Clear();
            return "";
        }

        [DevCommand("log.channels", Category = "console",
            Help = "Enables only the named log channels. No arguments enables all.",
            Usage = "log.channels [core|facility|ai|audio|ui|player|rendering|procedural|dev|save ...]")]
        private static string LogChannels(CommandArgs args)
        {
            if (args.Count == 0)
            {
                GLog.Enabled = LogChannel.All;
                return "All log channels enabled.";
            }

            LogChannel enabled = LogChannel.None;
            var unknown = new StringBuilder();

            for (int i = 0; i < args.Count; i++)
            {
                if (System.Enum.TryParse(args.String(i), ignoreCase: true, out LogChannel channel))
                    enabled |= channel;
                else
                    unknown.Append(' ').Append(args.String(i));
            }

            GLog.Enabled = enabled;
            return unknown.Length > 0
                ? $"Enabled {enabled}. Unknown channels:{unknown}"
                : $"Enabled {enabled}.";
        }

        [DevCommand("log.verbose", Category = "console",
            Help = "Enables verbose logging for the named channels.",
            Usage = "log.verbose [channel ...]")]
        private static string LogVerbose(CommandArgs args)
        {
            if (args.Count == 0)
            {
                GLog.VerboseEnabled = LogChannel.None;
                return "Verbose logging off.";
            }

            LogChannel enabled = LogChannel.None;
            for (int i = 0; i < args.Count; i++)
                if (System.Enum.TryParse(args.String(i), ignoreCase: true, out LogChannel channel))
                    enabled |= channel;

            GLog.VerboseEnabled = enabled;
            return $"Verbose: {enabled}.";
        }

        [DevCommand("log.tail", Category = "console",
            Help = "Prints the last N lines of Unity's log.", Usage = "log.tail [count]")]
        private static string LogTail(CommandArgs args)
        {
            int count = args.Int(0, 20);
            if (!ServiceLocator.TryGet(out DevConsole console)) return "No console.";

            ConsoleLogCapture.CopyTo(console._logScratch, count);

            var builder = new StringBuilder(1024);
            foreach (var entry in console._logScratch)
            {
                var colour = ConsoleLogCapture.ColorFor(entry.Type);
                builder.Append($"<color=#{ColorUtility.ToHtmlStringRGB(colour)}>")
                       .Append($"[{entry.Time:0.0}] ")
                       .Append(entry.Message.Replace("\n", " "))
                       .Append("</color>\n");
            }

            return builder.ToString();
        }

        [DevCommand("quit", Category = "console", Help = "Exits the game.")]
        private static string Quit(CommandArgs args)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            return "Quitting.";
        }
    }
}
