using System.Collections.Generic;
using System.IO;
using BitSorter.View.Workbench;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BitSorter.View.Editor
{
    /// <summary>
    /// Builds a browser player that does nothing but time the engine on the corpus processor
    /// (<see cref="SimBenchmark"/>), with the browser build's own settings (<see cref="WebGLBuild.Configure"/>),
    /// so the engine is measured as it ships: Part 6's performance gate.
    /// </summary>
    /// <remarks>
    /// Its scene is made for the build, holds the processor's files copied out of the oracle corpus,
    /// and is deleted after; nothing of it goes into the game. From the menu it asks to save the open
    /// scene first and opens it again after. In batch mode:
    /// <c>-executeMethod BitSorter.View.Editor.BenchmarkBuild.BuildFromCommandLine -benchmarkOut &lt;folder&gt;</c>.
    /// </remarks>
    public static class BenchmarkBuild
    {
        private const string TempFolder = "Assets/BenchmarkTemp";
        private const string Processor = "Tools/ci/Oracle/corpus/processor";
        private const string Design = "processor/mcpu.sv";

        /// <summary>The cycles mcpu's program takes, which mcpu_tb prints and the oracle holds.</summary>
        private const int Cycles = 2716;

        [MenuItem("BitSorter/Benchmark/Build WebGL Benchmark")]
        public static void BuildFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            string previous = SceneManager.GetActiveScene().path;
            Build(Path.Combine(Root, "Build/Benchmark"));
            if (!string.IsNullOrEmpty(previous))
                EditorSceneManager.OpenScene(previous);
        }

        public static void BuildFromCommandLine()
        {
            string target = Path.Combine(Root, "Build/Benchmark");
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-benchmarkOut")
                    target = args[i + 1];

            if (!Build(target))
                EditorApplication.Exit(1);
        }

        private static string Root => Directory.GetParent(Application.dataPath).FullName;

        private static bool Build(string target)
        {
            string folder = Path.Combine(Root, Processor);
            var sources = new List<(string, string)>();
            var images = new List<(string, string)>();

            foreach (string path in Directory.GetFiles(folder))
            {
                string name = Path.GetFileName(path);
                if (name.EndsWith(".sv") || name.EndsWith(".svh"))
                    sources.Add(("processor/" + name, File.ReadAllText(path)));
                else if (name.EndsWith(".hex"))
                    images.Add((name, File.ReadAllText(path)));
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Camera", typeof(Camera));
            new GameObject("Benchmark").AddComponent<SimBenchmark>().Load(Design, Cycles, sources, images);

            Directory.CreateDirectory(TempFolder);
            string scenePath = TempFolder + "/Benchmark.unity";
            EditorSceneManager.SaveScene(scene, scenePath);

            try
            {
                Directory.CreateDirectory(target);
                WebGLBuild.Configure();

                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { scenePath },
                    locationPathName = target,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                });

                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"BitSorter: benchmark build {report.summary.result}, {report.summary.totalErrors} errors.");
                    return false;
                }

                Debug.Log($"BitSorter: benchmark built to {target}");
                return true;
            }
            finally
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }
    }
}
