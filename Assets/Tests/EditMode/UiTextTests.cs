using System.Collections.Generic;
using System.IO;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The game's keys stand aside while a text field is being typed in.
    /// </summary>
    /// <remarks>
    /// What typing does to the keys is tested where there is a field to type in: the naming
    /// panel's Play Mode tests type N and Q into one and check nothing mutes or changes level.
    /// </remarks>
    public class UiTextTests
    {
        /// <summary>
        /// Nothing under View reads the keyboard except through <see cref="UiText.Keyboard"/>.
        /// </summary>
        /// <remarks>
        /// A scan of the sources, as <c>UiEscapeTests</c> scans for Escape: the failure this guards
        /// against is a key read added next year by someone who never saw the naming panel, and a
        /// behaviour test would only catch the readers that exist today.
        /// </remarks>
        [Test]
        public void EveryKeyReader_TakesItsKeyboardFromUiText()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "View");
            var direct = new List<string>();
            int readers = 0;

            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                string source = File.ReadAllText(path);

                if (name == "UiText.cs")
                    continue;

                if (source.Contains("UiText.Keyboard"))
                    readers++;

                if (source.Contains("Keyboard.current"))
                    direct.Add(name);
            }

            Assert.Greater(readers, 10, "sanity: the scan found hardly any key readers, so it is looking in the wrong place");
            Assert.IsEmpty(direct,
                "these read Keyboard.current, so their keys fire while a name is being typed: " +
                string.Join(", ", direct));
        }
    }
}
