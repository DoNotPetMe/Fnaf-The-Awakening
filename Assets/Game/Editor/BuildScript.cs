using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Grotto.Editor
{
    /// <summary>
    /// Command-line and menu builds.
    ///
    /// Validates the project first and refuses to build on a blocking problem. A build
    /// that succeeds and then renders magenta because the render pipeline was not
    /// assigned costs far more than a build that fails in ten seconds with the reason.
    ///
    /// Invoked in CI as:
    ///   Unity -quit -batchmode -projectPath . -executeMethod Grotto.Editor.BuildScript.BuildWindows
    /// </summary>
    public static class BuildScript
    {
        private const string OutputRoot = "Builds";

        [MenuItem("Tools/Grotto/Build/Windows (development)", priority = 80)]
        public static void BuildWindowsDevelopment() => Run(BuildTarget.StandaloneWindows64, development: true);

        [MenuItem("Tools/Grotto/Build/Windows (release)", priority = 81)]
        public static void BuildWindows() => Run(BuildTarget.StandaloneWindows64, development: false);

        [MenuItem("Tools/Grotto/Build/Linux (development)", priority = 82)]
        public static void BuildLinuxDevelopment() => Run(BuildTarget.StandaloneLinux64, development: true);

        [MenuItem("Tools/Grotto/Build/macOS (development)", priority = 83)]
        public static void BuildMacDevelopment() => Run(BuildTarget.StandaloneOSX, development: true);

        private static void Run(BuildTarget target, bool development)
        {
            var problems = new List<string>();
            var warnings = new List<string>();

            if (!ProjectValidator.Validate(problems, warnings))
            {
                string message = "Build refused — the project has blocking problems:\n  " +
                                 string.Join("\n  ", problems);

                Debug.LogError("[Grotto] " + message);

                if (Application.isBatchMode) EditorApplication.Exit(2);
                else EditorUtility.DisplayDialog("Build refused", message, "OK");

                return;
            }

            foreach (var warning in warnings) Debug.LogWarning("[Grotto] " + warning);

            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[Grotto] No scenes are enabled in Build Settings.");
                if (Application.isBatchMode) EditorApplication.Exit(3);
                return;
            }

            string folder = Path.Combine(OutputRoot, $"{target}{(development ? "-Dev" : "")}");
            Directory.CreateDirectory(folder);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, ExecutableName(target)),
                target = target,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None
            };

            Debug.Log($"[Grotto] Building {target} ({(development ? "development" : "release")}) " +
                      $"with {scenes.Length} scene(s) to {folder}.");

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Grotto] Build succeeded: {summary.totalSize / (1024 * 1024)} MB " +
                          $"in {summary.totalTime.TotalSeconds:0} s.");

                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError($"[Grotto] Build {summary.result} with {summary.totalErrors} error(s).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static string ExecutableName(BuildTarget target) => target switch
        {
            BuildTarget.StandaloneWindows64 => "TheAwakening.exe",
            BuildTarget.StandaloneOSX => "TheAwakening.app",
            _ => "TheAwakening"
        };

        /// <summary>Entry point for CI. Reads -buildTarget from the command line.</summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var target = BuildTarget.StandaloneLinux64;
            bool development = false;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-grottoTarget" && i + 1 < args.Length &&
                    Enum.TryParse(args[i + 1], out BuildTarget parsed))
                    target = parsed;

                if (args[i] == "-grottoDevelopment") development = true;
            }

            Run(target, development);
        }
    }
}
