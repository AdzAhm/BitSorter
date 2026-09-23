using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The interface's type scale: what each kind of text is drawn at, and that nothing goes round it.
    /// </summary>
    public class UiTypeTests
    {
        /// <summary>Each step of the scale is larger than the one before it.</summary>
        /// <remarks>
        /// The order of <see cref="UiType"/> is the order of importance, so a step drawn smaller than
        /// a lesser one would put a caption over a label in the hierarchy the eye reads.
        /// </remarks>
        [Test]
        public void TheScale_RisesWithImportance()
        {
            var types = (UiType[])Enum.GetValues(typeof(UiType));

            for (int i = 1; i < types.Length; i++)
            {
                Assert.Greater(UiTheme.SizeOf(types[i]), UiTheme.SizeOf(types[i - 1]),
                    $"{types[i]} is drawn no larger than {types[i - 1]}");
            }
        }

        /// <summary>Nothing is smaller than twelve.</summary>
        /// <remarks>
        /// The music credit was eleven, and it is the one line a licence requires be shown -- a
        /// credit too small to read is barely a credit.
        /// </remarks>
        [Test]
        public void NothingIsDrawnSmallerThanTwelve()
        {
            foreach (UiType type in Enum.GetValues(typeof(UiType)))
                Assert.GreaterOrEqual(UiTheme.SizeOf(type), 12f, $"{type} is too small to read");
        }

        /// <summary>No interface file sets a font size of its own.</summary>
        /// <remarks>
        /// <see cref="UiTheme.Label"/> takes a <see cref="UiType"/>, so a size cannot be passed to it;
        /// what is left is setting one afterwards, which is how the scale drifted into seventeen
        /// sizes in the first place. Read from the source, since an assignment is not something
        /// reflection can see.
        /// </remarks>
        [Test]
        public void NoInterfaceFile_SetsAFontSizeOfItsOwn()
        {
            string root = Path.Combine(Application.dataPath, "Scripts", "View", "Ui");
            var literal = new Regex(@"fontSize\s*=\s*[0-9]");

            string[] offenders = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(path => literal.IsMatch(File.ReadAllText(path)))
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();

            Assert.IsEmpty(offenders,
                "these set a font size by number rather than through UiType: " + string.Join(", ", offenders));
        }
    }
}
