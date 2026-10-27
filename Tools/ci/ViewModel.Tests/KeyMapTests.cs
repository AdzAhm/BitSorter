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
