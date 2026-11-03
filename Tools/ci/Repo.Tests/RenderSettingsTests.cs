using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// What URP is set to do for a camera that draws nothing but its ground colour under the workbench (Part 16b, and Part
    /// 16's URP pass). Each is a line in an asset, which an inspector's tick box changes and nothing recompiles for -- the
    /// box beside the 2D renderer's post-processing puts URP's post-processing shaders back in every build -- so they are
    /// held here, read from the files as Unity writes them.
    /// </summary>
    public class RenderSettingsTests
    {
        private static string Read(string relative) => File.ReadAllText(RepoRoot.Combine(relative));

        private static string GuidOf(string asset)
        {
            Match match = Regex.Match(Read(asset + ".meta"), @"^guid:\s*([0-9a-f]{32})\s*$", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, asset + ".meta has no guid");
            return match.Groups[1].Value;
        }

        /// <summary>The value written for <paramref name="field"/> in <paramref name="text"/>, which must have it once.</summary>
        private static string FieldIn(string text, string field, string where)
        {
            MatchCollection found = Regex.Matches(text, @"^\s*" + field + @":[ \t]*(.*?)\s*$", RegexOptions.Multiline);
            Assert.AreEqual(1, found.Count, where + " should write " + field + " once");
            return found[0].Groups[1].Value;
        }

        /// <summary>
        /// The files the tests below read are the ones a build uses: every quality level renders with the URP asset, and its
        /// one renderer is the 2D renderer -- so a setting held in them is a setting of the build, not of a file nothing reads.
        /// </summary>
        [Test]
        public void EveryQualityLevel_RendersWithTheUrpAsset_AndItsTwoDRenderer()
        {
            string pipeline = GuidOf("Assets/Settings/UniversalRP.asset");
            MatchCollection levels = Regex.Matches(Read("ProjectSettings/QualitySettings.asset"),
                @"^\s*customRenderPipeline:\s*\{fileID: \d+, guid: ([0-9a-f]{32})", RegexOptions.Multiline);

            Assert.Greater(levels.Count, 0, "QualitySettings.asset names no render pipeline");
            Assert.IsTrue(levels.Cast<Match>().All(m => m.Groups[1].Value == pipeline),
                "a quality level renders with something other than Assets/Settings/UniversalRP.asset");

            string renderers = Regex.Match(Read("Assets/Settings/UniversalRP.asset"),
                @"^\s*m_RendererDataList:\s*\n((?:\s*-\s*\{[^\n]*\}\s*\n)+)", RegexOptions.Multiline).Groups[1].Value;
            StringAssert.Contains("guid: " + GuidOf("Assets/Settings/Renderer2D.asset"), renderers,
                "the URP asset's renderer is not Assets/Settings/Renderer2D.asset");
            Assert.AreEqual(1, Regex.Matches(renderers, "guid:").Count, "the URP asset has a renderer besides the 2D one");
        }

        /// <summary>
        /// Nothing BitSorter draws is post-processed, and the 2D renderer's post-processing data is what put URP's sixteen
        /// post-processing shaders and their textures in every build. Without it the renderer makes no post-processing pass,
        /// whatever a camera asks. The inspector's Post-processing box, ticked, fills it with the package's copy -- the one
        /// with film-grain noise in it (<c>Docs/distribution.md</c>).
        /// </summary>
        [Test]
        public void TheTwoDRenderer_CarriesNoPostProcessingData()
        {
            Assert.AreEqual("{fileID: 0}", FieldIn(Read("Assets/Settings/Renderer2D.asset"), "m_PostProcessData", "Renderer2D.asset"),
                "the 2D renderer names post-processing data again, and its shaders go into every build");
        }

        /// <summary>
        /// URP 17.3 puts its three terrain-detail shaders in a build "even if not referenced" while this setting is on, and
        /// nothing in BitSorter is a terrain. Off, the URP asset hands a terrain no detail shader, which only a terrain asks for.
        /// </summary>
        [Test]
        public void TerrainShaders_AreLeftOutOfTheBuild()
        {
            Match setting = Regex.Match(Read("Assets/UniversalRenderPipelineGlobalSettings.asset"),
                @"type: \{class: URPTerrainShaderSetting,[^\n]*\n\s*data:\s*\n(?:[ \t]+m_\w+:[^\n]*\n)*?[ \t]+m_IncludeTerrainShaders:[ \t]*(\d)");

            Assert.IsTrue(setting.Success, "the URP global settings have no URPTerrainShaderSetting with m_IncludeTerrainShaders");
            Assert.AreEqual("0", setting.Groups[1].Value, "URP's terrain shaders go into every build again");
        }

        /// <summary>
        /// The main camera draws only its ground colour, under the workbench's opaque ground, and HDR was there for the
        /// bloom: with it URP's 2D renderer drew the camera into screen-sized float targets of its own and copied them to
        /// the screen every frame. The scene builder is the authority on the scene, so both say so -- the scene as built,
        /// and the builder that builds it again.
        /// </summary>
        [Test]
        public void TheMainCamera_DrawsWithoutHdr_InTheSceneAndItsBuilder()
        {
            string[] cameras = Regex.Split(Read("Assets/Scenes/HalfAdderDemo.unity"), @"^--- ", RegexOptions.Multiline)
                .Where(block => block.StartsWith("!u!20 ")).ToArray();

            Assert.AreEqual(1, cameras.Length, "the scene should hold one camera");
            Assert.AreEqual("0", FieldIn(cameras[0], "m_HDR", "the scene's camera"), "the scene's camera draws in HDR again");

            string builder = Regex.Replace(Read("Assets/Scripts/View/Editor/HalfAdderDemoSceneBuilder.cs"), "//[^\n]*", "");
            StringAssert.IsMatch(@"\bcamera\.allowHDR\s*=\s*false\s*;", builder, "the scene builder makes a camera that draws in HDR");
        }

        /// <summary>
        /// Paired with the test above, which would say nothing if HDR were off for every camera: the URP asset allows it, so
        /// the main camera's own switch is what turns it off. And it is kept on for <c>WaveformPng</c>'s camera, which draws
        /// the PNG through it -- off, the picture's pixels would change.
        /// </summary>
        [Test]
        public void TheUrpAsset_AllowsHdr_SoACameraChoosesIt()
        {
            Assert.AreEqual("1", FieldIn(Read("Assets/Settings/UniversalRP.asset"), "m_SupportsHDR", "UniversalRP.asset"));
        }
    }
}
