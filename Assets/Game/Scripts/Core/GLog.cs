using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Grotto.Core
{
    /// <summary>
    /// Log channels. Stored as flags so a developer can silence whole subsystems
    /// from the dev console (<c>log.channels ai facility</c>) without touching code.
    /// </summary>
    [System.Flags]
    public enum LogChannel
    {
        None      = 0,
        Core      = 1 << 0,
        Facility  = 1 << 1,
        AI        = 1 << 2,
        Audio     = 1 << 3,
        UI        = 1 << 4,
        Player    = 1 << 5,
        Rendering = 1 << 6,
        Procedural= 1 << 7,
        Dev       = 1 << 8,
        Save      = 1 << 9,
        All       = ~0
    }

    /// <summary>
    /// Channel-filtered logging façade.
    ///
    /// <see cref="Info"/> and <see cref="Verbose"/> are annotated
    /// <see cref="ConditionalAttribute"/>, so in a non-development player the calls
    /// (and the argument expressions that build their strings) are removed by the
    /// compiler entirely. <see cref="Warn"/> and <see cref="Error"/> always survive.
    /// </summary>
    public static class GLog
    {
        /// <summary>Channels that are currently allowed to print.</summary>
        public static LogChannel Enabled = LogChannel.All;

        /// <summary>Verbose channels. Off by default; spammy per-frame diagnostics.</summary>
        public static LogChannel VerboseEnabled = LogChannel.None;

        private static string Prefix(LogChannel c) => string.Concat("<b>[", c.ToString(), "]</b> ");

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(LogChannel channel, string message, Object context = null)
        {
            if ((Enabled & channel) == 0) return;
            Debug.Log(Prefix(channel) + message, context);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Verbose(LogChannel channel, string message, Object context = null)
        {
            if ((VerboseEnabled & channel) == 0) return;
            Debug.Log(Prefix(channel) + message, context);
        }

        public static void Warn(LogChannel channel, string message, Object context = null)
        {
            if ((Enabled & channel) == 0) return;
            Debug.LogWarning(Prefix(channel) + message, context);
        }

        public static void Error(LogChannel channel, string message, Object context = null)
        {
            // Errors ignore the channel filter: silencing them is never what you want.
            Debug.LogError(Prefix(channel) + message, context);
        }

        public static void Exception(System.Exception ex, Object context = null)
        {
            Debug.LogException(ex, context);
        }
    }
}
