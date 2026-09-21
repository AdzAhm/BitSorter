using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace BitSorter.TestTools
{
    /// <summary>
    /// Runs one mode of the test suite from a menu item and writes the result somewhere readable,
    /// so a run can be started and collected without anyone clicking through the Test Runner
    /// window.
    /// </summary>
    /// <remarks>
    /// The working agreement says the tests get run after every change and that nobody should be
    /// asked to click anything, but nothing in the project actually did the running -- it was done
    /// by hand each time against whatever editor state happened to exist. Three traps that have
    /// each cost real time are closed here in code rather than in somebody's memory.
    ///
    /// <see cref="TestRunnerApi.Execute"/> throws during play mode, and the exception surfaces as
    /// an unhandled log inside whichever test is running -- so it fails somebody else's test and
    /// looks like their bug. <see cref="Busy"/> refuses instead, loudly.
    ///
    /// Results cannot be collected from a callback registered at the moment a run starts: entering
    /// play mode reloads the domain and takes the callback with it. Registration happens in
    /// <see cref="InitializeOnLoadMethodAttribute"/> instead, which runs again after every reload.
    ///
    /// And one run covers one mode, deliberately. A single ExecutionSettings holding a filter for
    /// each mode reads as though it covers both and silently runs only Edit Mode: the first use of
    /// this menu item reported PASS over 779 tests, the Edit Mode tally exactly, for a suite whose
    /// other half never ran. Chaining the two inside the editor was tried and is worse -- the
    /// second run is started across the domain reload entering play mode causes, and when it fails
    /// to start there is nothing on screen to say so. Two runs, two summaries, each naming the
    /// mode it covered, is the version that cannot quietly cover less than it claims.
    ///
    /// Run Play Mode tests from here or from the PlayMode tab -- never the Player tab, which builds
    /// a player for the active target and fails in the launcher long before a test is reached.
    /// </remarks>
    public static class TestRun
    {
        /// <summary>Where the summary lands. Beside Unity's own TestResults.xml.</summary>
        public static string ResultPath =>
            Path.Combine(Application.persistentDataPath, "BitSorterTestRun.txt");

        /// <summary>
        /// The mode the run in flight was asked to cover, for the summary to name.
        /// </summary>
        /// <remarks>
        /// SessionState rather than a static field, because a Play Mode run reloads the domain and
        /// a field would be gone by the time that run reports. A tally is only evidence when you
        /// know what it was supposed to count.
        /// </remarks>
        private const string AskedKey = "BitSorter.TestRun.Asked";

        [MenuItem("BitSorter/Run Tests/Edit Mode")]
        public static void EditMode() => Run(TestMode.EditMode);

        [MenuItem("BitSorter/Run Tests/Play Mode")]
        public static void PlayMode() => Run(TestMode.PlayMode);

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

            SessionState.SetString(AskedKey, mode.ToString());

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
        /// Writes a summary a reader can act on: what ran, the tally, and every failure with its
        /// message.
        /// </summary>
        private sealed class Collector : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                string asked = SessionState.GetString(AskedKey, string.Empty);

                var report = new StringBuilder();

                report.AppendLine(result.FailCount == 0 ? "PASS" : "FAIL");
                report.AppendLine($"ran={(asked.Length == 0 ? "(started elsewhere)" : asked)}");
                report.AppendLine(
                    $"passed={result.PassCount} failed={result.FailCount} " +
                    $"skipped={result.SkipCount} inconclusive={result.InconclusiveCount} " +
                    $"duration={result.Duration:F1}s");
                report.AppendLine();

                var failures = new List<ITestResultAdaptor>();
                Collect(result, failures);

                foreach (ITestResultAdaptor failure in failures)
                {
                    report.AppendLine($"FAILED  {failure.FullName}");

                    if (!string.IsNullOrWhiteSpace(failure.Message))
                        report.AppendLine($"        {failure.Message.Trim().Replace("\n", "\n        ")}");

                    report.AppendLine();
                }

                File.WriteAllText(ResultPath, report.ToString());
                SessionState.SetString(AskedKey, string.Empty);

                Debug.Log($"[TestRun] finished: {result.PassCount} passed, {result.FailCount} failed.");
            }

            /// <summary>Walks the result tree and gathers the leaves that went red.</summary>
            private static void Collect(ITestResultAdaptor node, List<ITestResultAdaptor> into)
            {
                if (node.HasChildren)
                {
                    foreach (ITestResultAdaptor child in node.Children)
                        Collect(child, into);

                    return;
                }

                if (node.TestStatus == TestStatus.Failed)
                    into.Add(node);
            }
        }
    }
}
