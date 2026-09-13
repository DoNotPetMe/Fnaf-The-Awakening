using UnityEngine;

namespace Grotto.Core
{
    /// <summary>
    /// Single authoritative tuning asset for the campaign. Lives at
    /// <c>Assets/Game/Settings/GameConfig.asset</c> and is loaded through
    /// <see cref="Resources"/> when nothing hands it in explicitly.
    /// </summary>
    [CreateAssetMenu(menuName = "Grotto/Game Config", fileName = "GameConfig")]
    public sealed class GameConfig : ScriptableObject
    {
        [Header("Campaign")]
        public NightDefinition[] nights = new NightDefinition[0];
        public NightDefinition customNightTemplate;

        [Header("Presentation")]
        [Tooltip("Seconds the 6 AM celebration holds before the summary screen.")]
        [Range(0f, 12f)] public float survivalHoldSeconds = 5f;

        [Tooltip("Seconds the jumpscare holds before the failure screen.")]
        [Range(0.5f, 6f)] public float jumpscareSeconds = 2.2f;

        [Header("Runtime")]
        [Tooltip("-1 leaves the platform default. 60 is plenty for a stationary horror game and keeps laptops quiet.")]
        public int targetFrameRate = -1;

        [Tooltip("Start this night automatically when the facility scene is opened directly. 0 disables.")]
        [Range(0, 7)] public int editorAutoStartNight = 1;

        public NightDefinition GetNight(int night)
        {
            for (int i = 0; i < nights.Length; i++)
                if (nights[i] != null && nights[i].night == night) return nights[i];

            return null;
        }

        public int HighestAuthoredNight
        {
            get
            {
                int highest = 0;
                for (int i = 0; i < nights.Length; i++)
                    if (nights[i] != null) highest = Mathf.Max(highest, nights[i].night);
                return highest;
            }
        }

        private static GameConfig _cached;

        /// <summary>Loads the shared config, caching it. Returns null if the asset is missing.</summary>
        public static GameConfig LoadDefault()
        {
            if (_cached != null) return _cached;
            _cached = Resources.Load<GameConfig>("GameConfig");
            if (_cached == null)
            {
                GLog.Warn(LogChannel.Core,
                    "GameConfig.asset was not found in a Resources folder. " +
                    "Run Tools > Grotto > Rebuild Settings Assets to create it.");
            }
            return _cached;
        }

#if UNITY_EDITOR
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache() => _cached = null;
#endif
    }
}
