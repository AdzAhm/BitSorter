using System.Collections;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// Free play, through the real scene: what changing the setup does to the circuit around it.
    /// </summary>
    [TestFixture]
    public class SandboxPlayTests
    {
        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        private static IEnumerator OpenFreePlay()
        {
            Find<MainMenu>().Show(false);
            yield return null;

            Find<SandboxPanel>().Open();
            yield return null;
            yield return null;

            Assert.AreEqual(SandboxLevel.Key, Find<LevelSession>().LevelName, "sanity: free play did not load");
        }

        /// <summary>
        /// Flipping one input bit leaves the circuit's undo history and the part in hand alone.
        /// </summary>
        /// <remarks>
        /// Every setup edit used to reload free play from scratch. That is how a level switch is
        /// done, so it did what a level switch does: emptied the undo history, put the selection
        /// back on the first part, cancelled any run and wrote the save file -- for a single click
        /// on a bit.
        /// </remarks>
        [UnityTest]
        public IEnumerator ChangingTheSetup_KeepsTheUndoHistoryAndThePartInHand()
        {
            yield return TestScene.Load();
            yield return OpenFreePlay();

            LevelSession session = Find<LevelSession>();
            PlacementController placement = Find<PlacementController>();

            // Not the first part on the list, so a reset selection would show.
            Assert.IsTrue(placement.TrySelect(GateKind.Xor), "could not pick up an XOR");
            Assert.IsTrue(session.TryPlaceGate(GateKind.Xor, new Vector2Int(0, 0)), "could not place it");
            Assert.IsTrue(session.CanUndo, "sanity: placing a gate is an undoable step");

            Bit before = FirstBitOf(session, "A");

            Button bit = FindButton("bit 0 0");
            Assert.IsNotNull(bit, "no button for source A's first bit");

            bit.onClick.Invoke();
            yield return null;

            Assert.AreNotEqual(before, FirstBitOf(session, "A"),
                "sanity: the click should have flipped A's first bit");

            Assert.IsTrue(session.CanUndo, "flipping a bit threw away the circuit's undo history");
            Assert.AreEqual(GateKind.Xor, placement.Selected, "flipping a bit changed the part in hand");
            Assert.AreEqual(1, session.Blueprint.Placements.Count, "flipping a bit changed the circuit");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private static Bit FirstBitOf(LevelSession session, string sourceId)
        {
            foreach (LevelFixture fixture in session.Level.Fixtures)
            {
                if (fixture.Id == sourceId)
                    return fixture.Stream[0];
            }

            Assert.Fail($"no source {sourceId} in free play");
            return Bit.Zero;
        }

        private static Button FindButton(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null ? found.GetComponent<Button>() : null;
        }
    }
}
