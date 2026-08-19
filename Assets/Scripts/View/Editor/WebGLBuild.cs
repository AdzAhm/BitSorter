using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BitSorter.View.Editor
{
    /// <summary>
    /// Produces the browser build, with the settings both itch.io and Unity Play accept.
    /// </summary>
    /// <remarks>
    /// The compression choice is the part that matters and the part that goes wrong.
    ///
    /// Brotli gives much the smallest download, but a compressed Unity build only loads if the
    /// server answers with a matching Content-Encoding header. Unity Play sets those headers.
    /// A plain static host generally does not, and the failure is not subtle: the loader aborts
    /// with a console error about unexpected content and the player sees a blank page.
    ///
    /// Decompression fallback is the answer to that. It ships a decompressor inside the loader so
    /// the build works whether or not the host cooperates, at the cost of a slightly larger loader
    /// and a little CPU at startup. Enabled here, because one build that runs everywhere is worth
    /// more than two builds that each run in one place.
    ///
    /// Data caching keeps the downloaded data in IndexedDB, so a returning player does not fetch
    /// the whole thing again.
    /// </remarks>
    public static class WebGLBuild
    {
        private const string Scene = "Assets/Scenes/HalfAdderDemo.unity";
        private const string OutputFolder = "Build/WebGL";

        [MenuItem("BitSorter/Build WebGL Player")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Scene) == null)
            {
                Debug.LogError($"BitSorter: cannot build, {Scene} is missing.");
                return;
            }

            string root = Directory.GetParent(Application.dataPath).FullName;
            string target = Path.Combine(root, OutputFolder);

            Directory.CreateDirectory(target);

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;

            // The project's own template, which exists for one line: it sets
            // autoSyncPersistentDataPath. Unity's stock template leaves that commented out, and
            // without it every save the game makes is thrown away when the tab closes.
            PlayerSettings.WebGL.template = "PROJECT:BitSorter";

            // Thrown exceptions only. Full stack support costs size and speed, and nothing here
            // relies on catching engine-level faults.
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = target,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"BitSorter: WebGL build {summary.result}, {summary.totalErrors} errors.");
                return;
            }

            Debug.Log($"BitSorter: WebGL built to {target} " +
                      $"({summary.totalSize / (1024 * 1024)} MB before server compression)");
        }
    }
}
