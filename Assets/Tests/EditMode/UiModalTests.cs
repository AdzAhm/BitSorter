using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Which full-screen panels are open, and the one-frame memory that stops a key doing two things.
    /// </summary>
    /// <remarks>
    /// The frame is passed in rather than read, so "the next frame" is reachable without a player
    /// loop. Within one test method the editor's frame count does not move.
    /// </remarks>
    public class UiModalTests
    {
        private ScriptableObject _panel;

        [SetUp]
        public void SetUp() => _panel = ScriptableObject.CreateInstance<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            UiModal.Closed(_panel);
            Object.DestroyImmediate(_panel);
        }

        [Test]
        public void AnOpenPanel_CoversTheBoard()
        {
            UiModal.Opened(_panel);

            Assert.IsTrue(UiModal.AnyOpen);
            Assert.IsTrue(UiModal.OpenOrClosedOn(Time.frameCount));
        }

        [Test]
        public void APanelClosedThisFrame_StillCountsForTheRestOfIt()
        {
            // The press that closed it is still this frame's press. Anything else deciding whether to
            // open on that key has to see the panel as if it were still there.
            UiModal.Opened(_panel);
            UiModal.Closed(_panel);

            Assert.IsFalse(UiModal.AnyOpen, "closed means closed");
            Assert.IsTrue(UiModal.OpenOrClosedOn(Time.frameCount),
                "a panel that closed this frame must still block a key from opening another");
        }

        [Test]
        public void ByTheNextFrame_ItIsGone()
        {
            UiModal.Opened(_panel);
            UiModal.Closed(_panel);

            Assert.IsFalse(UiModal.OpenOrClosedOn(Time.frameCount + 1),
                "the memory is one frame long, or the next press would be swallowed too");
        }
    }
}
