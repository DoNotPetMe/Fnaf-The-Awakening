using UnityEngine;

namespace Grotto.Core
{
    /// <summary>
    /// First thing to run in any scene. Applies process-level settings and puts the
    /// profile into the service locator before anything asks for it.
    ///
    /// Execution order -1000 guarantees it beats <see cref="NightController"/> (-900)
    /// and every default-ordered system.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] private GameConfig config;
        [SerializeField] private bool dontDestroyOnLoad = true;

        private SaveSystem _save;

        private void Awake()
        {
            if (config == null) config = GameConfig.LoadDefault();

            if (dontDestroyOnLoad && transform.parent == null)
                DontDestroyOnLoad(gameObject);

            _save = new SaveSystem();
            _save.Load();
            ServiceLocator.Register(_save);

            ApplySettings(_save.Data.settings);

            ParseCommandLine();

            GLog.Info(LogChannel.Core,
                $"The Awakening booted. Dev build: {DebugFlags.IsDevBuild}. Profile: {_save.FilePath}");
        }

        private void OnApplicationQuit()
        {
            _save?.Save();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) _save?.Save();
        }

        public void ApplySettings(SettingsData settings)
        {
            if (settings == null) return;

            AudioListener.volume = Mathf.Clamp01(settings.masterVolume);

            int target = settings.targetFrameRate > 0
                ? settings.targetFrameRate
                : (config != null ? config.targetFrameRate : -1);

            if (target > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = target;
            }

            if (settings.qualityLevel >= 0 &&
                settings.qualityLevel < QualitySettings.names.Length)
            {
                QualitySettings.SetQualityLevel(settings.qualityLevel, applyExpensiveChanges: true);
            }
        }

        /// <summary>
        /// Command line switches, so QA can get the dev tools in a non-development
        /// build and reproduce a report:
        ///   <c>TheAwakening.exe -grotto-devtools -grotto-seed 12345 -grotto-night 4</c>
        /// </summary>
        private static void ParseCommandLine()
        {
            string[] args;
            try { args = System.Environment.GetCommandLineArgs(); }
            catch { return; }

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "-grotto-devtools":
                        DevToolsRequestedByCommandLine = true;
                        break;

                    case "-grotto-seed":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int seed))
                        {
                            DebugFlags.ForcedSeed = seed;
                            GLog.Info(LogChannel.Dev, $"Seed forced to {seed} from the command line.");
                        }
                        break;

                    case "-grotto-night":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int night))
                            CommandLineNight = night;
                        break;
                }
            }
        }

        /// <summary>Set by <c>-grotto-devtools</c>. Lets the console open in a release player.</summary>
        public static bool DevToolsRequestedByCommandLine { get; private set; }

        /// <summary>Set by <c>-grotto-night N</c>. 0 when absent.</summary>
        public static int CommandLineNight { get; private set; }
    }
}
