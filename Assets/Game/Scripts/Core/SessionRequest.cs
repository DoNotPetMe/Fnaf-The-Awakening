using UnityEngine;
using UnityEngine.SceneManagement;

namespace Grotto.Core
{
    /// <summary>
    /// What the front end asked for, carried across a scene reload.
    ///
    /// The site is decided before anything loads: the cavern geometry, the control
    /// room fixtures, the camera rig and the cast are all built at Awake from the
    /// chosen layout, and rebuilding them live would mean tearing down half the scene
    /// while the other half holds references into it. Reloading the scene is both
    /// simpler and more honest — it is one line, it cannot leave a stale reference
    /// behind, and it takes about a second, which is exactly what a "loading" beat in
    /// this kind of game is for.
    ///
    /// So the menu writes the request here, reloads, and the freshly built scene reads
    /// it: <see cref="Core.NightController"/> starts the requested night instead of the
    /// editor auto-start, and the facility loads the requested site. An empty request
    /// means "show the title screen", which is what a cold boot gets.
    /// </summary>
    public static class SessionRequest
    {
        /// <summary>Night to start once the scene is up. 0 means stay on the title screen.</summary>
        public static int PendingNight { get; private set; }

        /// <summary>Site to load. Empty means whatever the profile has selected.</summary>
        public static string PendingSiteId { get; private set; } = "";

        /// <summary>Seed for the requested night, when the player or a test pinned one.</summary>
        public static int? PendingSeed { get; private set; }

        /// <summary>True when a night has been asked for and not yet consumed.</summary>
        public static bool HasNight => PendingNight > 0;

        /// <summary>
        /// Asks for a night at a site and reloads so the facility is rebuilt for it.
        /// </summary>
        public static void Begin(string siteId, int night, int? seed = null)
        {
            PendingSiteId = siteId ?? "";
            PendingNight = Mathf.Max(1, night);
            PendingSeed = seed;

            GLog.Info(LogChannel.Core, $"Session requested: night {PendingNight} at '{PendingSiteId}'.");
            Reload();
        }

        /// <summary>Drops the request and reloads to the title screen.</summary>
        public static void ReturnToTitle()
        {
            Clear();
            Reload();
        }

        /// <summary>
        /// Takes the pending night, leaving the request empty.
        ///
        /// Consuming rather than reading means a mid-session scene reload — a restart
        /// from the pause menu, say — does not silently replay the menu's choice.
        /// </summary>
        public static int ConsumeNight(out int? seed)
        {
            int night = PendingNight;
            seed = PendingSeed;

            PendingNight = 0;
            PendingSeed = null;
            return night;
        }

        public static void Clear()
        {
            PendingNight = 0;
            PendingSeed = null;
            PendingSiteId = "";
        }

        private static void Reload()
        {
            // Unpause first: a request made from the pause menu would otherwise load
            // the new scene into a frozen timescale.
            Time.timeScale = 1f;

            var active = SceneManager.GetActiveScene();
            SceneManager.LoadScene(active.buildIndex >= 0 ? active.buildIndex : 0);
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Clear();
#endif
    }
}
