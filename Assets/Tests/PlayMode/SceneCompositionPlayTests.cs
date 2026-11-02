using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// The built scene itself: that nothing it produces is holding a null, and that what it cannot run without is there,
    /// once.
    /// </summary>
    /// <remarks>
    /// `HalfAdderDemoSceneBuilder` is the only authority on scene contents, and its output goes
    /// wrong in ways no single script can show: a collaborator missing from the scene, or one there twice. Until Part 16a
    /// this also held three pairs of the 4.x game's components to the order they updated in; the simulator's scene has no
    /// such pair.
    ///
    /// Loading the real scene is the point -- a hand-built subset could not catch either. The `{fileID: 0}` case
    /// is deliberately *not* here: it needs the scene read as a file, which `SceneWiringTests` does
    /// in Edit Mode, because by the time this fixture can look, Awake has already filled the gaps in.
    ///
    /// It also means the real analytics consent is in play, so it is turned off in
    /// <see cref="OneTimeSetup"/> before a single frame runs.
    /// </remarks>
    [TestFixture]
    public class SceneCompositionPlayTests
    {
        /// <summary>
        /// Stops the fixture touching anything the player owns.
        /// </summary>
        /// <remarks>
        /// Analytics is the one that would leave the building. `GameAnalytics.Boot` runs on
        /// AfterSceneLoad, and an exercise opened or solved is reported, so without this a test run
        /// could post real events to the dashboard and quietly corrupt the one measurement the game
        /// collects -- the answer to "which exercise do people stop at" would include a robot.
        ///
        /// The progress file is the other, and <see cref="SaveGuard"/> handles it by pointing the
        /// game at a scratch file rather than by moving the player's out of the way. Nothing here
        /// ever opens the real save.
        /// </remarks>
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            // Redirect first: SetReporting writes to the real machine otherwise. Mute and
            // reporting both go through Preferences, which SaveGuard redirects alongside the
            // save file, so nothing here has to be read and put back afterwards.
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        /// <summary>
        /// Each test starts on a fresh save, as every other fixture's does: without this, whatever
        /// one test wrote -- a milestone, a solve, a board -- was the next test's starting point,
        /// and the fixture passed or failed by the order NUnit ran it in.
        /// </summary>
        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        /// <summary>
        /// Takes the game back off the screen before the next fixture runs (<see cref="TestScene"/> says why that is not
        /// optional).
        /// </summary>
        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static IEnumerator LoadScene() => TestScene.Load();

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        // -----------------------------------------------------------------
        // Serialized references
        // -----------------------------------------------------------------

        /// <summary>
        /// Every component finishes Awake with all of its references resolved, from any source.
        /// </summary>
        /// <remarks>
        /// **This does not prove the scene builder wired anything**, and it was named as though it
        /// did. Every component in this project ends Awake with a fallback -- `if (_x == null) _x =
        /// FindFirstObjectByType&lt;X&gt;()` -- so a field the builder forgot is filled in before any
        /// test can look at it. Reading fields after Awake cannot tell "serialized correctly" from
        /// "found at runtime", and the old name claimed the first.
        ///
        /// What it does prove is still worth having, and is the thing that actually crashes: after
        /// Awake, nothing is holding a null it will dereference in Update. That catches a component
        /// whose collaborator is missing from the scene altogether, which no fallback can rescue.
        ///
        /// Testing serialization properly means reading `HalfAdderDemo.unity` as text and looking for
        /// `{fileID: 0}`, because that file is the only place the distinction exists. Worth doing; it
        /// is not this test.
        /// </remarks>
        [UnityTest]
        public IEnumerator EveryComponent_FinishesAwakeWithNothingNull()
        {
            yield return LoadScene();

            var missing = new List<string>();

            foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null)
                    continue;

                System.Type type = behaviour.GetType();

                if (type.Namespace == null || !type.Namespace.StartsWith("BitSorter"))
                    continue;

                foreach (FieldInfo field in Fields(type))
                {
                    if (!typeof(Object).IsAssignableFrom(field.FieldType))
                        continue;

                    var value = field.GetValue(behaviour) as Object;

                    if (value == null)
                        missing.Add(type.Name + "." + field.Name + " on '" + behaviour.name + "'");
                }
            }

            CollectionAssert.IsEmpty(missing,
                "references still null after Awake, fallback included -- so the collaborator is " +
                "missing from the scene entirely:\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// Serialized instance fields, including private ones, up the hierarchy -- as far as this
        /// project's own classes go.
        /// </summary>
        /// <remarks>
        /// Not into Unity's: the workbench's drawings are each a <see cref="UnityEngine.UI.MaskableGraphic"/>, whose
        /// serialized material is null on purpose, meaning the default UI material. A null there is
        /// Unity's convention, not a collaborator missing from the scene, which is what this test
        /// looks for.
        /// </remarks>
        private static IEnumerable<FieldInfo> Fields(System.Type type)
        {
            while (type != null && type != typeof(MonoBehaviour)
                   && type.Namespace != null && type.Namespace.StartsWith("BitSorter"))
            {
                foreach (FieldInfo field in type.GetFields(
                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                             BindingFlags.DeclaredOnly))
                {
                    bool serialized = field.IsPublic ||
                                      field.GetCustomAttribute<SerializeField>() != null;

                    if (serialized && field.GetCustomAttribute<System.NonSerializedAttribute>() == null)
                        yield return field;
                }

                type = type.BaseType;
            }
        }

        // -----------------------------------------------------------------
        // Things there must be exactly one of
        // -----------------------------------------------------------------

        /// <summary>
        /// Without an AudioListener the music plays to nobody, silently and with no warning.
        /// </summary>
        /// <remarks>
        /// `GameAudio` says so in its own remarks and then has no way to check. Two listeners is the
        /// other half: Unity warns once and then mixes audio through whichever it prefers, which
        /// makes a volume balance impossible to reason about.
        /// </remarks>
        [UnityTest]
        public IEnumerator ThereIsExactlyOneAudioListener()
        {
            yield return LoadScene();

            AudioListener[] listeners =
                Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);

            Assert.AreEqual(1, listeners.Length,
                "the music plays to nobody with no listener, and unpredictably with two");
        }

        [UnityTest]
        public IEnumerator ThereIsExactlyOneEventSystemAndOneCanvas()
        {
            yield return LoadScene();

            Assert.AreEqual(1,
                Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length,
                "two event systems and neither one reliably gets the click");

            Assert.AreEqual(1,
                Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                      .Count(c => c.transform.parent == null),
                "the interface is one root canvas built in code");
        }

        /// <summary>
        /// The components the game cannot run without are all present, and present once.
        /// </summary>
        /// <remarks>
        /// A rebuild that dropped one would leave a scene that opens and does nothing, which reads as a
        /// hang rather than as a missing component.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheScenesLoadBearingComponents_AreAllPresentExactlyOnce()
        {
            yield return LoadScene();

            foreach (System.Type type in new[]
                     {
                         typeof(ProgressTracker), typeof(WorkbenchPanel), typeof(SettingsPanel),
                         typeof(CreditsPanel), typeof(GameAudio), typeof(FrameRateCounter),
                     })
            {
                Object[] found = Object.FindObjectsByType(type, FindObjectsSortMode.None);

                Assert.AreEqual(1, found.Length, type.Name + " should be in the scene exactly once");
            }
        }
    }
}
