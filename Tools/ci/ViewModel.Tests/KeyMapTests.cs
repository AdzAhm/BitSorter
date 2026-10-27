using System.Linq;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The keyboard map (Part 11d): every key the workbench answers is on it, under one heading, from the key table alone;
    /// two keys doing one thing in one cap are one line; ? opens it; and the status line names the gestures where the
    /// pointer is, in one line.
    /// </summary>
    public class KeyMapTests
    {
        [Test]
        public void EveryKey_IsOnTheMap_UnderOneHeading_AsItsCapSays()
        {
            string[] keyGroups = { "RUN", "CIRCUIT", "WAVES", "VIEW" };
            CollectionAssert.AreEqual(keyGroups, KeyMap.Groups.Take(4).Select(g => g.Title));

            foreach (KeyBinding binding in WorkbenchKeys.All)
            {
                var entry = new KeyMapEntry(binding.Cap, binding.Says);
                int found = KeyMap.Groups.Take(4).Count(g => g.Entries.Contains(entry));
                Assert.AreEqual(1, found, $"{binding} is on the map under one heading");
            }

            foreach (KeyMapGroup group in KeyMap.Groups)
                Assert.AreEqual(group.Entries.Count, group.Entries.Distinct().Count(), $"{group.Title} lists a line twice");
        }

        /// <summary>Every heading is drawn in exactly one of the map's three columns, and no column names one that is not there.</summary>
        [Test]
        public void EveryHeading_IsInExactlyOneColumn()
        {
            Assert.AreEqual(3, KeyMap.Columns.Count);
            foreach (KeyMapGroup group in KeyMap.Groups)
                Assert.AreEqual(1, KeyMap.Columns.Count(c => c.Contains(group.Title)), $"{group.Title} is in one column");
            foreach (string title in KeyMap.Columns.SelectMany(c => c))
                Assert.IsTrue(KeyMap.Groups.Any(g => g.Title == title), $"{title} is a heading of the map");
        }

        /// <summary>
        /// The code editor's keys (Part 14g) are listed once, under IN THE CODE: the ones a writer reaches for -- applying,
        /// undo, indenting, commenting -- and none of them a key the circuit's gestures list as well.
        /// </summary>
        [Test]
        public void TheCodesKeys_AreListedUnderInTheCode_AndCtrlEnterOnlyThere()
        {
            KeyMapGroup code = KeyMap.Groups.Single(g => g.Title == "IN THE CODE");
            foreach (string cap in new[] { "CTRL ENTER", "CTRL S", "CTRL Z", "TAB", "CTRL /", "CTRL F", "ESC" })
                Assert.IsTrue(code.Entries.Any(e => e.Cap == cap), $"{cap} is listed");
            Assert.IsFalse(KeyMap.CircuitGestures.Any(e => e.Cap.Contains("ENTER")), "Ctrl+Enter is the code's, listed with it");
        }

        [Test]
        public void TheMapItself_IsOnAKey_TheGameAndTheBrowserLeaveFree()
        {
            KeyBinding keys = WorkbenchKeys.For(WorkbenchCommand.Keys);
            Assert.AreEqual("?", keys.Cap);
            Assert.IsNull(WorkbenchKeys.WhyRefused(keys));
            Assert.AreEqual("VIEW", KeyMap.GroupOf(WorkbenchCommand.Keys));
        }

        [Test]
        public void TheGestures_AreListed_AndTheStatusLineNamesThoseWhereThePointerIs_InOneLine()
        {
            Assert.IsTrue(KeyMap.Groups.Any(g => g.Title == "ON THE CIRCUIT" && g.Entries.Count > 0));
            Assert.IsTrue(KeyMap.Groups.Any(g => g.Title == "ON THE WAVES" && g.Entries.Count > 0));
            Assert.IsTrue(KeyMap.Groups.Any(g => g.Title == "IN PARTS" && g.Entries.Count > 0), "how a module is deleted (Part 14g)");
            Assert.IsTrue(KeyMap.Groups.Any(g => g.Title == "IN THE CODE" && g.Entries.Count > 0), "the code editor's keys (Part 14g)");

            foreach (PointerArea area in new[] { PointerArea.Elsewhere, PointerArea.Circuit, PointerArea.Waves, PointerArea.Blocks })
            {
                string line = KeyMap.StatusFor(area);
                StringAssert.Contains("?", line, "every status says how to see every key");
                Assert.IsFalse(line.Contains("\n"), "one line");
            }

            StringAssert.StartsWith("circuit:", KeyMap.StatusFor(PointerArea.Circuit));
            StringAssert.StartsWith("waves:", KeyMap.StatusFor(PointerArea.Waves));
            StringAssert.StartsWith("blocks:", KeyMap.StatusFor(PointerArea.Blocks));
            StringAssert.Contains("right-click to delete its module", KeyMap.StatusFor(PointerArea.Blocks));
        }
    }
}
