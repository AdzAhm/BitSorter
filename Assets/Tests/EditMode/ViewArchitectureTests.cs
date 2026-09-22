using System.Linq;
using System.Reflection;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Rules about how the view is built, pinned where a single file's tests cannot see them.
    /// </summary>
    /// <remarks>
    /// Each rule here is one that has already been broken, found by someone looking at the screen
    /// rather than by any test, and would be broken again by the next component written without
    /// knowing about it.
    /// </remarks>
    public class ViewArchitectureTests
    {
        /// <summary>
        /// Nothing in the view draws with IMGUI.
        /// </summary>
        /// <remarks>
        /// IMGUI paints after the canvas, so nothing on the canvas can ever cover it -- not a
        /// full-screen scrim, not a card, not a panel. The interface is a canvas built in code
        /// precisely so that every piece of it sits in one stack with one order.
        ///
        /// The wire delay numbers were the one thing drawn with it, and they broke through twice:
        /// scattered across the rows of the level list, which was fixed by hiding them by hand
        /// behind full-screen panels; and printed on top of the solved card, which is not a
        /// full-screen panel and so was never covered by that fix. A rule that has to be obeyed
        /// by hand for each panel is a rule the next panel forgets. Drawn on the board instead,
        /// the numbers sit under the canvas like everything else on the board.
        /// </remarks>
        [Test]
        public void NothingInTheView_DrawsWithImgui()
        {
            const BindingFlags Declared =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            string[] offenders = typeof(BitRenderer).Assembly.GetTypes()
                .Where(type => typeof(MonoBehaviour).IsAssignableFrom(type))
                .Where(type => type.GetMethod("OnGUI", Declared) != null)
                .Select(type => type.Name)
                .OrderBy(name => name)
                .ToArray();

            Assert.IsEmpty(offenders,
                "these draw with OnGUI, which paints over every panel on the canvas and cannot be " +
                "covered by any of them: " + string.Join(", ", offenders));
        }
    }
}
