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
    /// The built scene itself: that nothing it produces is holding a null, and that the components
    /// which depend on running in a particular order do.
    /// </summary>
    /// <remarks>
    /// `HalfAdderDemoSceneBuilder` is the only authority on scene contents, and its output goes
    /// wrong in ways no single script can show: a collaborator missing from the scene, or two
    /// components whose Update order decides whether one sees the other's state.
    ///
    /// Loading the real scene is the point -- a hand-built subset like
    /// <see cref="PointerArbitrationPlayTests"/> uses could not catch either. The `{fileID: 0}` case
    /// is deliberately *not* here: it needs the scene read as a file, which `SceneWiringTests` does
    /// in Edit Mode, because by the time this fixture can look, Awake has already filled the gaps in.
    ///
    /// It also means the real analytics consent is in play, so it is turned off in
    /// <see cref="OneTimeSetup"/> before a single frame runs.
    /// </remarks>
    [TestFixture]
    public class SceneCompositionPlayTests
    {
        private bool _reportingWas;

        /// <summary>
        /// Stops the fixture touching anything the player owns.
        /// </summary>
        /// <remarks>
        /// Analytics is the one that would leave the building. `GameAnalytics.Boot` runs on
        /// AfterSceneLoad and reports `levelStarted` for whatever loads, so without this a test run
        /// would post real events to the dashboard and quietly corrupt the one measurement the game
        /// collects -- the answer to "which level do people stop at" would include a robot.
        ///
        /// The progress file is the other, and <see cref="SaveGuard"/> handles it by pointing the
        /// game at a scratch file rather than by moving the player's out of the way. Nothing here
        /// ever opens the real save.
        /// </remarks>
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            _reportingWas = GameAnalytics.Reporting;
            GameAnalytics.SetReporting(false);

            SaveGuard.Redirect();
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
            GameAnalytics.SetReporting(_reportingWas);
        }

        /// <summary>
        /// Takes the game back off the screen before the next fixture runs.
        /// </summary>
        /// <remarks>
        /// Not optional. Leaving the scene loaded turned two of `PointerArbitrationPlayTests`'
        /// assertions red -- it builds its own canvas and assumes nothing else is on screen, and
        /// the main menu is a full-screen panel, so the pointer was over UI everywhere.
        /// </remarks>
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

        /// <summary>Serialized instance fields, including private ones, up the hierarchy.</summary>
        private static IEnumerable<FieldInfo> Fields(System.Type type)
        {
            while (type != null && type != typeof(MonoBehaviour))
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
        // Component order
        // -----------------------------------------------------------------

        /// <summary>
        /// Component order within one GameObject decides Update order, and three pairs depend on it.
        /// </summary>
        /// <remarks>
        /// The trap CLAUDE.md records: `WinPanel` never registers with `UiModal`, so
        /// `TutorialDirector` watches its `IsShowing` instead -- which only works because the builder
        /// adds the director after it, so on the frame a run passes the win panel has already
        /// presented. Add the director first and the tutorial's card appears on top of the win panel,
        /// which is precisely the bug the ordering was introduced to fix.
        ///
        /// Asserted as indices on the shared GameObject rather than by observing behaviour, because
        /// the behaviour only diverges on one frame in a whole playthrough.
        /// </remarks>
        [UnityTest]
        public IEnumerator ComponentsThatDependOnUpdateOrder_AreInTheRightOrder()
        {
            yield return LoadScene();

            AssertOrder<WinPanel, TutorialDirector>(
                "the director must see a win panel that has already presented, or the tutorial's " +
                "card and the win panel share the screen");

            AssertOrder<ProgressTracker, FirstTimeHints>(
                "hints ask the store whether they have been seen; the store must have loaded first");

            AssertOrder<LevelSelectPanel, SandboxPanel>(
                "the sandbox row is drawn into the level list, which has to exist by then");
        }

        private static void AssertOrder<TFirst, TSecond>(string because)
            where TFirst : MonoBehaviour
            where TSecond : MonoBehaviour
        {
            TFirst first = Find<TFirst>();
            TSecond second = Find<TSecond>();

            Assert.IsNotNull(first, typeof(TFirst).Name + " is missing from the scene");
            Assert.IsNotNull(second, typeof(TSecond).Name + " is missing from the scene");

            // Only meaningful on one GameObject: across objects Unity gives no ordering guarantee at
            // all, so if the builder ever splits these the assertion below is the thing that says so.
            Assert.AreSame(first.gameObject, second.gameObject,
                typeof(TFirst).Name + " and " + typeof(TSecond).Name +
                " must share a GameObject for their order to mean anything");

            MonoBehaviour[] all = first.GetComponents<MonoBehaviour>();

            int firstAt = System.Array.IndexOf(all, (MonoBehaviour)first);
            int secondAt = System.Array.IndexOf(all, (MonoBehaviour)second);

            Assert.Less(firstAt, secondAt,
                typeof(TFirst).Name + " must come before " + typeof(TSecond).Name + ": " + because);
        }

        // -----------------------------------------------------------------
        // Things there must be exactly one of
        // -----------------------------------------------------------------

        /// <summary>
        /// Without an AudioListener every cue plays to nobody, silently and with no warning.
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
                "cues play to nobody with no listener, and unpredictably with two");
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
        /// A rebuild that dropped one would leave a scene that opens, draws a grid and does nothing,
        /// which reads as a hang rather than as a missing component.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheScenesLoadBearingComponents_AreAllPresentExactlyOnce()
        {
            yield return LoadScene();

            foreach (System.Type type in new[]
                     {
                         typeof(SimulationRunner), typeof(LevelSession), typeof(ProgressTracker),
                         typeof(PointerGate), typeof(WiringController), typeof(GameAudio),
                         typeof(TutorialDirector), typeof(WinPanel), typeof(FirstTimeHints),
                     })
            {
                Object[] found = Object.FindObjectsByType(type, FindObjectsSortMode.None);

                Assert.AreEqual(1, found.Length, type.Name + " should be in the scene exactly once");
            }
        }
    }
}
