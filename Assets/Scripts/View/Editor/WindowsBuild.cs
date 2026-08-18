using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BitSorter.View.Editor
{
    /// <summary>
    /// Produces the Windows player, from a menu item or from a command line.
    /// </summary>
    /// <remarks>
    /// A script rather than the Build Settings dialog, so a build is one action that always
    /// includes the same scene and always lands in the same place. The scene list is asserted here
    /// rather than trusted: the project shipped for weeks with only SampleScene in the build list,
    /// which would have produced a player that opens on an empty grey screen and looks like a
    /// crash. Nothing in the editor complains about that, because in the editor the right scene is
    /// simply the one that happens to be open.
    ///
    /// The output folder is gitignored. Builds are artefacts, not sources.
    /// </remarks>
    public static class WindowsBuild
    {
        private const string Scene = "Assets/Scenes/HalfAdderDemo.unity";
        private const string OutputFolder = "Build/Windows";
        private const string Executable = "BitSorter.exe";

        [MenuItem("BitSorter/Build Windows Player")]
        public static void Build()
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string target = Path.Combine(root, OutputFolder, Executable);

            Directory.CreateDirectory(Path.GetDirectoryName(target));

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene) == null)
            {
                Debug.LogError($"BitSorter: cannot build, {Scene} is missing.");
                return;
            }

            var options = new BuildPlayerOptions
            {
                // Named explicitly rather than read from EditorBuildSettings, so a build cannot
                // inherit whatever someone last left ticked in the dialog.
                scenes = new[] { Scene },
                locationPathName = target,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                TidyUp(Path.GetDirectoryName(target));

                Debug.Log($"BitSorter: built {summary.totalSize / (1024 * 1024)} MB to {target}");
                return;
            }

            Debug.LogError(
                $"BitSorter: build {summary.result} with {summary.totalErrors} errors.");
        }

        /// <summary>
        /// Removes the Burst debug folder the build leaves beside the player.
        /// </summary>
        /// <remarks>
        /// Unity names it "DoNotShip" and then puts it in the output folder anyway. Deleting it
        /// here means the folder can be zipped and sent as it stands, rather than depending on
        /// whoever does the zipping noticing the name.
        /// </remarks>
        private static void TidyUp(string folder)
        {
            foreach (string path in Directory.GetDirectories(folder, "*_DoNotShip"))
                Directory.Delete(path, true);
        }
    }
}
