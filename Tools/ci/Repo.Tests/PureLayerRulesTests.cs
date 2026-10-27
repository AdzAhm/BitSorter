using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// The four pure layers of BitSorter 5.0 stay pure, and point one way: SystemVerilog, then Sim, then
    /// Board, then the view-model.
    /// </summary>
    /// <remarks>
    /// Held three ways, because each catches what the others cannot. The asmdef is what Unity builds
    /// from; the compiled assembly is what the CI projects actually produced; the source is where a
    /// fully-qualified call to the engine or to reflection hides from both. Docs/architecture-5.md
    /// gives the reasons for each rule: above all that these layers have to run unchanged under the
    /// plain .NET SDK, IL2CPP and a single-threaded browser.
    /// </remarks>
    public class PureLayerRulesTests
    {
        /// <summary>Each pure layer's folder under Assets/Scripts, and the only assemblies it may use.</summary>
        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            ["SystemVerilog"] = new string[0],
            ["Sim"] = new[] { "BitSorter.SystemVerilog" },
            ["Board"] = new[] { "BitSorter.SystemVerilog", "BitSorter.Sim" },
            ["ViewModel"] = new[] { "BitSorter.SystemVerilog", "BitSorter.Sim", "BitSorter.Board" },

            // The 4.x engine, frozen until it is deleted. Held to the asmdef rule only.
            ["LogicCore"] = new string[0],
        };

        private static readonly string[] NewLayers = { "SystemVerilog", "Sim", "Board", "ViewModel" };

        private static IEnumerable<string> AllLayers => Allowed.Keys;

        [TestCaseSource(nameof(AllLayers))]
        public void ItsAsmdef_KeepsTheEngineOut_AndUsesOnlyTheLayersBelow(string layer)
        {
            string path = RepoRoot.Combine($"Assets/Scripts/{layer}/BitSorter.{layer}.asmdef");
            using JsonDocument asmdef = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement json = asmdef.RootElement;

            Assert.AreEqual("BitSorter." + layer, json.GetProperty("name").GetString());
            Assert.IsTrue(json.GetProperty("noEngineReferences").GetBoolean(),
                "noEngineReferences must be true: the layer has to build without Unity");
            Assert.IsFalse(json.GetProperty("overrideReferences").GetBoolean(),
                "overrideReferences would let precompiled DLLs in by the back door");

            string[] references = json.GetProperty("references").EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.That(references, Is.EquivalentTo(Allowed[layer]),
                $"BitSorter.{layer} may use exactly the layers below it");
        }

        [Test]
        public void TheCompiledLayers_ReferenceNothingAboveThemselves()
        {
            var assemblies = new Dictionary<string, Assembly>
            {
                ["SystemVerilog"] = typeof(BitSorter.SystemVerilog.SystemVerilogLayer).Assembly,
                ["Sim"] = typeof(BitSorter.Sim.SimLayer).Assembly,
                ["Board"] = typeof(BitSorter.Board.BoardLayer).Assembly,
                ["ViewModel"] = typeof(BitSorter.ViewModel.ViewModelLayer).Assembly,
            };

            foreach (KeyValuePair<string, Assembly> layer in assemblies)
            {
                string[] ours = layer.Value.GetReferencedAssemblies()
                    .Select(name => name.Name)
                    .Where(name => name.StartsWith("BitSorter.", StringComparison.Ordinal) ||
                                   name.StartsWith("Unity", StringComparison.Ordinal))
                    .ToArray();

                string[] wrong = ours.Except(Allowed[layer.Key]).ToArray();

                Assert.IsEmpty(wrong, $"BitSorter.{layer.Key} was compiled against " + string.Join(", ", wrong));
            }
        }

        /// <summary>
        /// What a pure layer must not reach for, as it would be written in source. Comments and string
        /// literals are taken out first, so explaining a rule in a doc comment does not break it.
        /// </summary>
        private static readonly (string Token, string Why)[] Forbidden =
        {
            ("UnityEngine", "the engine belongs to the view"),
            ("UnityEditor", "the editor belongs to the view"),
            ("System.Reflection", "IL2CPP strips what reflection would look for"),
            ("System.Threading.Thread", "a browser build has one thread"),
            ("new Thread(", "a browser build has one thread"),
            ("ThreadPool", "a browser build has one thread"),
            ("Task.Run", "a browser build has one thread"),
            ("Parallel.", "a browser build has one thread"),
            ("File.", "file access goes through an interface the host provides"),
            ("Directory.", "file access goes through an interface the host provides"),
            ("FileStream", "file access goes through an interface the host provides"),
        };

        private static readonly Regex CommentsAndLiterals = new Regex(
            @"@""(?:[^""]|"""")*""|""(?:\\.|[^""\\\n])*""|'(?:\\.|[^'\\\n])+'|/\*.*?\*/|//[^\n]*",
            RegexOptions.Singleline | RegexOptions.Compiled);

        [TestCaseSource(nameof(NewLayers))]
        public void ItsSource_ReachesForNoEngineReflectionThreadsOrFiles(string layer)
        {
            string folder = RepoRoot.Combine($"Assets/Scripts/{layer}");
            var offences = new List<string>();

            foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string code = CommentsAndLiterals.Replace(File.ReadAllText(file), " ");

                foreach ((string token, string why) in Forbidden)
                {
                    if (code.Contains(token, StringComparison.Ordinal))
                        offences.Add($"{Path.GetFileName(file)} uses {token}: {why}");
                }
            }

            Assert.IsEmpty(offences, string.Join("\n", offences));
        }

        /// <summary>
        /// The workbench files in the view that need no Unity of their own, each with the reason it
        /// stays: what it reads is the view's.
        /// </summary>
        private static readonly Dictionary<string, string> UnityFreeButTheViews = new Dictionary<string, string>
        {
            ["CanvasMotion.cs"] = "it reads the player's look and reduced motion, which are the view's settings",
            ["WorkbenchPanel.Documents.cs"] = "a part of the workbench's MonoBehaviour that opens, names and deletes circuits through the save " +
                "and the shell's popups, all the view's; its rules are Documents', in the view-model (Part 15a)",
        };

        /// <summary>
        /// A file under <c>View/Workbench</c> that never mentions Unity belongs in the view-model, where
        /// <c>dotnet test</c> can reach it -- the view-model left Unity in Part 9 so that the
        /// workbench's logic would be tested in seconds, and a Unity-free file left behind in the view is
        /// logic that went back to needing the editor.
        /// </summary>
        [Test]
        public void AWorkbenchFileWithNoUnityInIt_BelongsInTheViewModel()
        {
            string folder = RepoRoot.Combine("Assets/Scripts/View/Workbench");
            var misplaced = new List<string>();

            foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            {
                string code = CommentsAndLiterals.Replace(File.ReadAllText(file), " ");
                if (!code.Contains("UnityEngine", StringComparison.Ordinal) && !UnityFreeButTheViews.ContainsKey(Path.GetFileName(file)))
                    misplaced.Add(Path.GetFileName(file));
            }

            Assert.IsEmpty(misplaced,
                "these use no Unity, so they belong in Assets/Scripts/ViewModel: " + string.Join(", ", misplaced) +
                ". If one must stay, add it to UnityFreeButTheViews with the reason.");

            foreach (string kept in UnityFreeButTheViews.Keys)
                Assert.IsTrue(File.Exists(Path.Combine(folder, kept)), $"{kept} is excused from this rule but no longer exists");
        }

        [Test]
        public void TheSourceCheck_SeesThroughCommentsButNotCode()
        {
            const string commented = "// UnityEngine is not used here\n/* nor File. */ var s = \"File.ReadAllText\";";
            const string used = "var x = UnityEngine.Mathf.Abs(1);";

            Assert.IsFalse(CommentsAndLiterals.Replace(commented, " ").Contains("UnityEngine"));
            Assert.IsFalse(CommentsAndLiterals.Replace(commented, " ").Contains("File."));
            Assert.IsTrue(CommentsAndLiterals.Replace(used, " ").Contains("UnityEngine"));
        }
    }
}
