using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace BitSorter.TestTools
{
    /// <summary>
    /// Runs the test suite from a menu item and writes the result somewhere readable, so a run can
    /// be started and collected without anyone clicking through the Test Runner window.
    /// </summary>
    /// <remarks>
    /// The working agreement says the tests get run after every change and that nobody should be
    /// asked to click anything, but nothing in the project actually did the running -- it was done
    /// by hand each time against whatever editor state happened to exist. Two of the traps that
    /// cost real time are closed here in code rather than in somebody's memory:
    ///
    /// <see cref="TestRunnerApi.Execute"/> throws during play mode, and the exception surfaces as
    /// an unhandled log inside whichever test is running -- so it fails somebody else's test and
    /// looks like their bug. <see cref="Busy"/> refuses instead, loudly.
    ///
    /// And results cannot be collected from a callback registered at the moment a run starts:
    /// entering play mode reloads the domain and takes the callback with it. Registration happens
    /// in <see cref="InitializeOnLoadMethodAttribute"/> instead, which runs again after every
    /// reload, so the callback is there to receive the end of a run that outlived its own start.
    ///
    /// Run Play Mode tests from here or from the PlayMode tab -- never the Player tab, which builds
    /// a player for the active target and fails in the launcher long before a test is reached.
    /// </remarks>
    public static class TestRun
    {
        /// <summary>Where the summary lands. Beside Unity's own TestResults.xml.</summary>
        public static string ResultPath =>
            Path.Combine(Application.persistentDataPath, "BitSorterTestRun.txt");

        [MenuItem("BitSorter/Run Tests/Edit Mode")]
        public static void EditMode() => Run(TestMode.EditMode);

        [MenuItem("BitSorter/Run Tests/Play Mode")]
        public static void PlayMode() => Run(TestMode.PlayMode);

        [MenuItem("BitSorter/Run Tests/Both")]
        public static void Both() => Run(TestMode.EditMode | TestMode.PlayMode);

        /// <summary>
        /// Whether the editor is in a state where starting a run would throw or compile underneath
        /// itself.
        /// </summary>
        private static bool Busy(out string why)
        {
            if (EditorApplication.isPlaying)
            {
                why = "the editor is in play mode -- TestRunnerApi.Execute throws, and the " +
                      "exception lands inside whichever test is running";
                return true;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                why = "the editor is still compiling or importing -- a run started now would " +
                      "test the previous assemblies";
                return true;
            }

            why = null;
            return false;
        }

        private static void Run(TestMode mode)
        {
            if (Busy(out string why))
            {
                Debug.LogError($"[TestRun] refused: {why}.");
                return;
            }

            // Written before the run, so a result file left over from last time cannot be mistaken
            // for this run's if the editor dies partway through.
            File.WriteAllText(ResultPath, $"RUNNING {mode}\n");

            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.Execute(new ExecutionSettings(new Filter { testMode = mode }));

            Debug.Log($"[TestRun] started {mode}. Result will be written to {ResultPath}");
        }

        /// <summary>
        /// Registered on every domain load, including the one entering play mode causes, so a Play
        /// Mode run's end is still heard by something.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void Subscribe()
        {
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Collector());
        }

        /// <summary>
        /// Writes a summary a reader can act on: the tally, and every failure with its message.
        /// </summary>
        private sealed class Collector : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var report = new StringBuilder();

                report.AppendLine(result.FailCount == 0 ? "PASS" : "FAIL");
                report.AppendLine(
                    $"passed={result.PassCount} failed={result.FailCount} " +
                    $"skipped={result.SkipCount} inconclusive={result.InconclusiveCount} " +
                    $"duration={result.Duration:F1}s");
                report.AppendLine();

                Failures(result, report);

                File.WriteAllText(ResultPath, report.ToString());
                Debug.Log($"[TestRun] finished: {result.PassCount} passed, {result.FailCount} failed.");
            }

            /// <summary>Walks the result tree and prints the leaves that went red.</summary>
            private static void Failures(ITestResultAdaptor node, StringBuilder into)
            {
                if (node.HasChildren)
                {
                    foreach (ITestResultAdaptor child in node.Children)
                        Failures(child, into);

                    return;
                }

                if (node.TestStatus != TestStatus.Failed)
                    return;

                into.AppendLine($"FAILED  {node.FullName}");

                if (!string.IsNullOrWhiteSpace(node.Message))
                    into.AppendLine($"        {node.Message.Trim().Replace("\n", "\n        ")}");

                into.AppendLine();
            }
        }
    }
}
