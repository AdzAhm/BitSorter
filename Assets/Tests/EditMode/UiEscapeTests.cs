using System.Collections.Generic;
using System.IO;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// <see cref="UiEscape"/>: the list of things over the board that take Escape before the main
    /// menu, and the rule that nothing reading Escape is left off it.
    /// </summary>
    public class UiEscapeTests
    {
        private sealed class Holder : IHoldsEscape
        {
            public bool Holds;
            public bool HoldsEscapeNow => Holds;
        }

        [Test]
        public void AHolderThatJoins_HoldsEscape_UntilItLeaves()
        {
            var holder = new Holder();
            Assert.IsFalse(UiEscape.AnyHolds, "sanity: nothing should hold Escape before the test");

            UiEscape.Join(holder);

            try
            {
                Assert.IsFalse(UiEscape.AnyHolds, "a holder that is not holding held Escape anyway");

                holder.Holds = true;
                Assert.IsTrue(UiEscape.AnyHolds, "a holder that is holding was not heard");

                UiEscape.Join(holder);
                UiEscape.Leave(holder);
                Assert.IsFalse(UiEscape.AnyHolds, "joining twice left it on the list after one leave");
            }
            finally
            {
                UiEscape.Leave(holder);
            }
        }

        /// <summary>
        /// A holder destroyed without leaving -- a scene unloaded around it -- is not asked.
        /// </summary>
        [Test]
        public void ADestroyedHolder_IsSkipped()
        {
            var host = new GameObject("escape holder");
            StandInHolder holder = host.AddComponent<StandInHolder>();
            holder.Holds = true;

            UiEscape.Join(holder);

            try
            {
                Assert.IsTrue(UiEscape.AnyHolds, "sanity: the stand-in should hold Escape");

                Object.DestroyImmediate(host);
                Assert.IsFalse(UiEscape.AnyHolds, "a destroyed holder still held Escape");
            }
            finally
            {
                UiEscape.Leave(holder);

                if (host != null)
                    Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// Every script that reads Escape is a full-screen panel, the main menu that opens on it, or
        /// a member of <see cref="UiEscape"/> that joins it.
        /// </summary>
        /// <remarks>
        /// The solved card, a hint and the help panel each sat over the board, closed on Escape and
        /// were not modals -- and each was found the same way, by one press closing it and opening the
        /// main menu too. The list makes a fourth one easy to get right; this makes it impossible to
        /// get wrong quietly.
        /// </remarks>
        [Test]
        public void EverythingThatReadsEscape_IsAPanel_OrJoinsTheList()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "View");
            var loose = new List<string>();
            int readers = 0;

            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(path);

                if (!source.Contains("escapeKey"))
                    continue;

                readers++;
                string name = Path.GetFileName(path);

                bool panel = source.Contains(": FullScreenPanel");
                bool opener = name == "MainMenu.cs";
                bool member = source.Contains("IHoldsEscape") && source.Contains("UiEscape.Join(this)");

                if (!panel && !opener && !member)
                    loose.Add(name);
            }

            Assert.Greater(readers, 3, "sanity: the search found almost nothing that reads Escape");
            CollectionAssert.IsEmpty(loose,
                "these read Escape without being a full-screen panel or joining UiEscape, so one press " +
                "can close them and open the main menu as well: " + string.Join(", ", loose));
        }
    }

    /// <summary>A Unity object that holds Escape when told to, for the destroyed-holder test.</summary>
    internal sealed class StandInHolder : MonoBehaviour, IHoldsEscape
    {
        public bool Holds;
        public bool HoldsEscapeNow => Holds;
    }
}
