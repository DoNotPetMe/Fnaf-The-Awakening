using System.Collections.Generic;
using UnityEngine;

namespace Grotto.DevTools
{
    /// <summary>
    /// Mirrors Unity's log into a ring buffer the in-game console can display.
    ///
    /// A ring buffer rather than a growing list: a night that runs for ten minutes
    /// with verbose AI logging on produces tens of thousands of lines, and a console
    /// that slowly eats memory is a debugging tool that causes the bug it is meant to
    /// find.
    /// </summary>
    public static class ConsoleLogCapture
    {
        public readonly struct Entry
        {
            public readonly string Message;
            public readonly LogType Type;
            public readonly float Time;

            public Entry(string message, LogType type, float time)
            {
                Message = message; Type = type; Time = time;
            }
        }

        private const int Capacity = 512;

        private static readonly Entry[] Buffer = new Entry[Capacity];
        private static int _head;
        private static int _count;
        private static bool _hooked;

        /// <summary>Number of errors and exceptions seen this session.</summary>
        public static int ErrorCount { get; private set; }

        public static int WarningCount { get; private set; }

        public static int Count => _count;

        public static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
            Application.logMessageReceived += OnLog;
        }

        public static void Unhook()
        {
            if (!_hooked) return;
            _hooked = false;
            Application.logMessageReceived -= OnLog;
        }

        private static void OnLog(string message, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) ErrorCount++;
            else if (type == LogType.Warning) WarningCount++;

            // Exceptions are useless without at least the top of the stack.
            if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace))
            {
                int newline = stackTrace.IndexOf('\n');
                message += "\n  " + (newline > 0 ? stackTrace.Substring(0, newline) : stackTrace);
            }

            Buffer[_head] = new Entry(message, type, Time.realtimeSinceStartup);
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
        }

        /// <summary>Writes the buffered entries, oldest first, into <paramref name="results"/>.</summary>
        public static void CopyTo(List<Entry> results, int maximum = Capacity)
        {
            results.Clear();

            int take = Mathf.Min(maximum, _count);
            int start = (_head - take + Capacity * 2) % Capacity;

            for (int i = 0; i < take; i++)
                results.Add(Buffer[(start + i) % Capacity]);
        }

        public static void Clear()
        {
            _head = 0;
            _count = 0;
            ErrorCount = 0;
            WarningCount = 0;
        }

        public static Color ColorFor(LogType type) => type switch
        {
            LogType.Error or LogType.Exception or LogType.Assert => new Color(1f, 0.42f, 0.38f),
            LogType.Warning => new Color(1f, 0.82f, 0.35f),
            _ => new Color(0.78f, 0.80f, 0.82f)
        };

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            Unhook();
            Clear();
        }
#endif
    }
}
