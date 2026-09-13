using System;

namespace Grotto.Core
{
    /// <summary>
    /// Process-wide developer overrides.
    ///
    /// This lives in <c>Grotto.Core</c> — the assembly everything else depends on —
    /// specifically so that gameplay systems can *read* debug state without any of
    /// them taking a dependency on <c>Grotto.DevTools</c>. Dev tools write here;
    /// gameplay only reads. That keeps the dependency graph acyclic and lets the
    /// whole DevTools assembly be stripped from a shipping build.
    /// </summary>
    public static class DebugFlags
    {
        /// <summary>True in the editor and in development players.</summary>
        public const bool IsDevBuild =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            true;
#else
            false;
#endif

        // ---- Gameplay overrides -------------------------------------------------

        /// <summary>Animatronics stop taking movement opportunities and stop attacking.</summary>
        public static bool FreezeAI;

        /// <summary>Attacks resolve into a logged near-miss instead of a game over.</summary>
        public static bool GodMode;

        /// <summary>Generator fuel and battery charge never deplete.</summary>
        public static bool InfinitePower;

        /// <summary>Air quality never degrades and the flood never rises.</summary>
        public static bool FreezeEnvironment;

        /// <summary>Jumpscare presentation is skipped; the failure resolves silently.</summary>
        public static bool DisableJumpscares;

        /// <summary>Every camera node reports as healthy and powered.</summary>
        public static bool AllCamerasOnline;

        // ---- Visualisation ------------------------------------------------------

        public static bool ShowDebugOverlay;
        public static bool ShowNodeGraph;
        public static bool ShowNoiseField;
        public static bool ShowAIPaths;
        public static bool ShowAudioRanges;

        // ---- Time ---------------------------------------------------------------

        /// <summary>Multiplier applied on top of the night clock. 1 = normal.</summary>
        public static float ClockScale = 1f;

        /// <summary>When set, <see cref="RandomSource"/> seeds derive from this value so nights replay identically.</summary>
        public static int? ForcedSeed;

        /// <summary>Raised whenever any flag changes, so UI/overlays can refresh.</summary>
        public static event Action Changed;

        /// <summary>Call after mutating any field above.</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>Clears every override back to shipping defaults.</summary>
        public static void ResetAll()
        {
            FreezeAI = GodMode = InfinitePower = FreezeEnvironment = false;
            DisableJumpscares = AllCamerasOnline = false;
            ShowDebugOverlay = ShowNodeGraph = ShowNoiseField = false;
            ShowAIPaths = ShowAudioRanges = false;
            ClockScale = 1f;
            ForcedSeed = null;
            NotifyChanged();
        }

        /// <summary>
        /// Statics survive Play Mode exits when Domain Reload is disabled, which would
        /// otherwise leak "god mode is still on" across sessions. Reset on load.
        /// </summary>
#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnEnterPlayMode()
        {
            Changed = null;
            ResetAll();
        }
#endif
    }
}
