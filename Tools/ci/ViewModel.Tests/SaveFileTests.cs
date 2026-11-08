using System;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace BitSorter.ViewModel.Tests
{
    /// <summary>
    /// The save's own text (<see cref="SaveFile"/>, Ahmad's decisions, 2026-11-03): its stamp, found at its head, which says
    /// whether another window wrote it; and what of it a newer BitSorter wrote that this one does not know -- every key
    /// read that the save written back has no place for, at any depth, the 4.x game's aside.
    /// </summary>
    public class SaveFileTests
    {
        [Test]
        public void TheStamp_IsReadFromTheTop_AtItsHead_OrSaidUnsureWhereTheHeadEndsFirst()
        {
            const string Save = "{\n    \"stamp\": \"one\",\n    \"completed\": [\"half-adder\"]\n}";
            Assert.AreEqual("one", SaveFile.StampOf(Save, out bool sure));
            Assert.IsTrue(sure);

            Assert.AreEqual("one", SaveFile.StampOf(Save.Substring(0, 24), out sure), "the head alone, as far as the stamp");
            Assert.IsTrue(sure);
            Assert.IsNull(SaveFile.StampOf(Save.Substring(0, 18), out sure), "cut inside the stamp");
            Assert.IsFalse(sure, "so the whole file is read");

            Assert.AreEqual("two", SaveFile.StampOf("{\"completed\":[\"a\",{\"stamp\":\"inner\"}],\"stamp\":\"two\"}", out sure),
                "the top's, not one inside a value, wherever it is");
            Assert.IsNull(SaveFile.StampOf("{\"completed\":[\"a\"],\"format\":5}", out sure), "a save from before stamps");
            Assert.IsTrue(sure);
            Assert.IsNull(SaveFile.StampOf("{\"completed\":[\"a\"", out sure));
            Assert.IsFalse(sure);
            Assert.IsNull(SaveFile.StampOf("this is not json at all", out sure), "nonsense has no stamp, and throws nothing");
            Assert.IsTrue(sure);
            Assert.IsNull(SaveFile.StampOf("", out sure));
            Assert.AreEqual("a\"b", SaveFile.StampOf("{\"stamp\":\"a\\\"b\"}", out sure), "its escapes undone");
        }

        [Test]
        public void WhatIsNotKept_IsEveryKeyReadThatTheSaveWrittenBackHasNoPlaceFor_AtAnyDepth()
        {
            const string Written = "{\"stamp\":\"\",\"format\":5,\"projects\":[{\"key\":\"workbench\",\"project\":{\"name\":\"a\"," +
                                   "\"modules\":[{\"name\":\"m\"}]}}]}";
            const string Read = "{\"format\":5,\"glow\":1,\"projects\":[{\"key\":\"workbench\",\"project\":{\"name\":\"a\"," +
                                "\"modules\":[{\"name\":\"m\",\"shade\":\"blue\"}],\"theme\":{\"dark\":true}}}]}";

            CollectionAssert.AreEqual(new[] { "glow", "projects[].project.modules[].shade", "projects[].project.theme", "projects[].project.theme.dark" },
                SaveFile.NotKept(Read, Written));
            CollectionAssert.IsEmpty(SaveFile.NotKept(Written, Written), "a save this BitSorter wrote keeps everything");
            CollectionAssert.IsEmpty(SaveFile.NotKept("{\"format\":5}", Written), "and one with less in it drops nothing");
        }

        [Test]
        public void The4xGamesKeys_AreAnOldSavesNotANewerOnes()
        {
            const string Written = "{\"completed\":[],\"format\":5}";
            const string Read = "{\"completed\":[\"half-adder\"],\"boards\":[{\"level\":\"half-adder\",\"gates\":[{\"kind\":\"And\"}]}]," +
                                "\"freePlay\":{\"active\":\"My board\"},\"blocks\":[{\"name\":\"FA\"}]}";

            CollectionAssert.IsEmpty(SaveFile.NotKept(Read, Written));
            Assert.IsNull(SaveFile.Newer(0, 5, Read, Written));
        }

        [Test]
        public void ASaveIsANewerBitSortersByItsFormat_OrByAKeyThisOneDoesNotKnow()
        {
            const string Written = "{\"format\":5,\"recent\":[]}";

            StringAssert.Contains("format is 6", SaveFile.Newer(6, 5, Written, Written));
            StringAssert.Contains("glow", SaveFile.Newer(5, 5, "{\"format\":5,\"recent\":[],\"glow\":true}", Written));
            Assert.IsNull(SaveFile.Newer(5, 5, Written, Written));
            Assert.IsNull(SaveFile.Newer(5, 5, "{\"format\":5,\"recent\":[\"a\",\"b\"]}", Written), "values do not count, keys do");
        }

        /// <summary>No text stops the scanner: cut short, nonsense, or nested deeper than any stack.</summary>
        [Test]
        public void NoTextThrows_NotEvenOneNestedTooDeepForAnyStack()
        {
            var deep = new StringBuilder("{\"glow\":");
            deep.Append('[', 200_000);
            deep.Append(']', 200_000);
            deep.Append('}');

            Assert.DoesNotThrow(() => SaveFile.NotKept(deep.ToString(), "{}"));
            CollectionAssert.AreEqual(new[] { "glow" }, SaveFile.NotKept(deep.ToString(), "{}"));
            Assert.DoesNotThrow(() => SaveFile.StampOf(deep.ToString(), out _));
            foreach (string broken in new[] { "{", "{\"a\"", "{\"a\":", "{\"a\":[1,", "{\"a\":\"\\u12", "[[[", "\"", "}" })
            {
                Assert.DoesNotThrow(() => SaveFile.NotKept(broken, "{}"), broken);
                Assert.DoesNotThrow(() => SaveFile.StampOf(broken, out _), broken);
            }
        }

        [Test]
        public void ASaveFromANewerBitSorter_IsCopiedBesideItself_Dated()
        {
            Assert.AreEqual("C:/save/progress.json.newer-version-20261109-143005",
                SaveFile.NewerCopyOf("C:/save/progress.json", new DateTime(2026, 11, 9, 14, 30, 5)));
        }
    }
}
