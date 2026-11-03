using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace BitSorter.Ci.Tests
{
    /// <summary>
    /// A static TextMesh Pro font atlas costs no more memory than its glyphs need. Its glyphs are baked in once, so its
    /// texture is never written again: the space past the last glyph is texels the GPU holds, and a browser build loads
    /// through its heap, for nothing. Each atlas is the smallest power of two its glyphs fit in, in each direction, and
    /// its texture, its material and the font asset agree on that size.
    /// </summary>
    /// <remarks>
    /// Read from the assets' YAML, so it needs no editor. A power of two because a glyph's UVs are its rect over the
    /// atlas's size, and over a power of two that division is exact: a cropped atlas samples every texel as the larger
    /// one did. IBM Plex Sans was baked into 2048 by 1024 with its glyphs ending 603 texels across, and its bold into
    /// 1024 square with its glyphs ending at 329: more than half of each atlas held nothing. Both were cut down to the
    /// columns their glyphs use, 1024 by 1024 and 512 by 1024, every glyph's texels where they were. A dynamic atlas
    /// grows as glyphs are added, and is held by nothing here.
    /// </remarks>
    public class FontAtlasTests
    {
        /// <summary>The script every TextMesh Pro font asset is an instance of, <c>TMPro.TMP_FontAsset</c>.</summary>
        private const string FontAssetScript = "guid: 71c1514a6bd24e1e882cebbe1904ce04";

        private static IEnumerable<string> StaticFontAssets() =>
            Directory.EnumerateFiles(RepoRoot.Combine("Assets"), "*.asset", SearchOption.AllDirectories)
                .Where(file => IsStaticFontAsset(File.ReadAllText(file)))
                .Select(file => Path.GetRelativePath(RepoRoot.Path, file).Replace('\\', '/'))
                .OrderBy(file => file, StringComparer.Ordinal);

        [Test]
        public void TheGamesOwnFaces_AreAmongTheStaticFontAssets()
        {
            string[] found = StaticFontAssets().ToArray();

            CollectionAssert.Contains(found, "Assets/Fonts/IBMPlexSans/IBMPlexSans SDF.asset");
            CollectionAssert.Contains(found, "Assets/Fonts/IBMPlexSans/IBMPlexSans Bold SDF.asset");
        }

        [TestCaseSource(nameof(StaticFontAssets))]
        public void AStaticAtlas_IsTheSizeItsGlyphsNeed(string file)
        {
            List<string> problems = Problems(File.ReadAllText(RepoRoot.Combine(file)));

            Assert.IsEmpty(problems, file + ":\n" + string.Join("\n", problems));
        }

        /// <summary>Paired with the test above, so its passing says something: the check does see an atlas too big.</summary>
        [Test]
        public void TheCheck_SeesAnAtlasWiderThanItsGlyphs()
        {
            List<string> problems = Problems(Made(atlasWidth: 2048, atlasHeight: 1024, textureWidth: 2048, materialWidth: 2048, glyphEnd: 597));

            Assert.That(problems, Has.Some.Contains("2048 by 1024").And.Contains("1024 by 1024"));
        }

        [Test]
        public void TheCheck_SeesATextureOrMaterialOfAnotherSize()
        {
            Assert.That(Problems(Made(1024, 1024, textureWidth: 2048, materialWidth: 1024, glyphEnd: 597)),
                Has.Some.Contains("texture"));
            Assert.That(Problems(Made(1024, 1024, textureWidth: 1024, materialWidth: 2048, glyphEnd: 597)),
                Has.Some.Contains("material"));
        }

        [Test]
        public void TheCheck_PassesAnAtlasTheSizeItsGlyphsNeed()
        {
            Assert.IsEmpty(Problems(Made(1024, 1024, textureWidth: 1024, materialWidth: 1024, glyphEnd: 597)));
        }

        [TestCase(1, 1)]
        [TestCase(329, 512)]
        [TestCase(512, 512)]
        [TestCase(513, 1024)]
        [TestCase(603, 1024)]
        [TestCase(1023, 1024)]
        public void TheSizeNeeded_IsTheSmallestPowerOfTwoHoldingTheGlyphs(int end, int size)
        {
            Assert.AreEqual(size, Needed(end));
        }

        /// <summary>A TextMesh Pro font asset whose atlas is baked once (population mode 0, static).</summary>
        private static bool IsStaticFontAsset(string text) =>
            text.Contains(FontAssetScript, StringComparison.Ordinal)
            && Regex.IsMatch(text, @"^  m_AtlasPopulationMode: 0\s*$", RegexOptions.Multiline);

        /// <summary>What is wrong with the static font asset <paramref name="text"/>'s atlas, if anything.</summary>
        private static List<string> Problems(string text)
        {
            var problems = new List<string>();
            string asset = Block(text, "114");
            int width = Int(asset, "m_AtlasWidth"), height = Int(asset, "m_AtlasHeight"), padding = Int(asset, "m_AtlasPadding");

            // How far the glyphs reach, their padding with them: TextMesh Pro draws each glyph's rect and its padding.
            int endX = 0, endY = 0;
            string table = Between(asset, "  m_GlyphTable:", "  m_CharacterTable:");
            foreach (Match rect in Regex.Matches(table, @"m_GlyphRect:\s*m_X: (\d+)\s*m_Y: (\d+)\s*m_Width: (\d+)\s*m_Height: (\d+)"))
            {
                int x = int.Parse(rect.Groups[1].Value), y = int.Parse(rect.Groups[2].Value);
                int w = int.Parse(rect.Groups[3].Value), h = int.Parse(rect.Groups[4].Value);
                if (w == 0 || h == 0)
                    continue;
                endX = Math.Max(endX, x + w + padding);
                endY = Math.Max(endY, y + h + padding);
            }

            if (endX > width || endY > height)
                problems.Add($"its glyphs reach {endX} by {endY}, past its {width} by {height} atlas");
            else if (Needed(endX) != width || Needed(endY) != height)
                problems.Add($"its atlas is {width} by {height}, and its glyphs, which reach {endX} by {endY}, need {Needed(endX)} by {Needed(endY)}");

            string texture = Anchored(text, "28", FileId(asset, "m_AtlasTextures"));
            if (Int(texture, "m_Width") != width || Int(texture, "m_Height") != height)
                problems.Add($"its texture is {Int(texture, "m_Width")} by {Int(texture, "m_Height")}, not the atlas's {width} by {height}");
            if (Int(texture, "m_IsReadable") != 0)
                problems.Add("its texture is kept readable: a copy in memory that a static font never writes or reads");

            // An asset saved by an older TextMesh Pro, as its own Liberation Sans was, names its material "material".
            string material = Anchored(text, "21", FileId(asset, "(?:m_Material|material)"));
            if (Float(material, "_TextureWidth") != width || Float(material, "_TextureHeight") != height)
                problems.Add($"its material says {Float(material, "_TextureWidth")} by {Float(material, "_TextureHeight")}, not the atlas's {width} by {height}");

            return problems;
        }

        /// <summary>The smallest power of two at least <paramref name="end"/>.</summary>
        private static int Needed(int end)
        {
            int size = 1;
            while (size < end)
                size *= 2;
            return size;
        }

        /// <summary>The first object of Unity class <paramref name="classId"/> in a YAML asset.</summary>
        private static string Block(string text, string classId)
        {
            Match header = Regex.Match(text, @"^--- !u!" + classId + @" &-?\d+", RegexOptions.Multiline);
            Assert.IsTrue(header.Success, "no object of class " + classId);
            return Rest(text, header.Index);
        }

        /// <summary>The object of class <paramref name="classId"/> whose file id is <paramref name="fileId"/>.</summary>
        private static string Anchored(string text, string classId, string fileId)
        {
            int at = text.IndexOf("--- !u!" + classId + " &" + fileId + "\n", StringComparison.Ordinal);
            Assert.GreaterOrEqual(at, 0, $"no object of class {classId} with file id {fileId}");
            return Rest(text, at);
        }

        private static string Rest(string text, int start)
        {
            int next = text.IndexOf("\n--- ", start + 1, StringComparison.Ordinal);
            return next < 0 ? text.Substring(start) : text.Substring(start, next - start);
        }

        private static string Between(string text, string from, string to)
        {
            int start = text.IndexOf(from, StringComparison.Ordinal), end = text.IndexOf(to, StringComparison.Ordinal);
            Assert.IsTrue(start >= 0 && end > start, $"no {from.Trim()} before {to.Trim()}");
            return text.Substring(start, end - start);
        }

        /// <summary>The file id a field refers to: <c>m_Material: {fileID: 21}</c>, or a list's first, <c>- {fileID: 28}</c>.</summary>
        private static string FileId(string block, string field)
        {
            Match match = Regex.Match(block, "^  " + field + @":\s*(?:- )?\{fileID: (-?\d+)\}", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "no " + field);
            return match.Groups[1].Value;
        }

        private static int Int(string block, string field)
        {
            Match match = Regex.Match(block, "^  " + field + @": (-?\d+)\s*$", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "no " + field);
            return int.Parse(match.Groups[1].Value);
        }

        private static float Float(string block, string property)
        {
            Match match = Regex.Match(block, @"^    - " + property + @": (-?[\d.]+)\s*$", RegexOptions.Multiline);
            Assert.IsTrue(match.Success, "no material property " + property);
            return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>A static font asset's YAML cut down to what the check reads: one glyph, its texture and its material.</summary>
        private static string Made(int atlasWidth, int atlasHeight, int textureWidth, int materialWidth, int glyphEnd) =>
            "%YAML 1.1\n" +
            "--- !u!21 &2100000\nMaterial:\n  m_SavedProperties:\n    m_Floats:\n" +
            $"    - _TextureHeight: {atlasHeight}\n    - _TextureWidth: {materialWidth}\n" +
            "--- !u!114 &11400000\nMonoBehaviour:\n  m_Script: {fileID: 11500000, " + FontAssetScript + ", type: 3}\n" +
            "  m_Material: {fileID: 2100000}\n  m_AtlasPopulationMode: 0\n" +
            "  m_GlyphTable:\n  - m_Index: 1\n    m_GlyphRect:\n" +
            $"      m_X: {glyphEnd - 40}\n      m_Y: {atlasHeight - 60}\n      m_Width: 40\n      m_Height: 54\n" +
            "  m_CharacterTable: []\n" +
            "  m_AtlasTextures:\n  - {fileID: 2800000}\n" +
            $"  m_AtlasWidth: {atlasWidth}\n  m_AtlasHeight: {atlasHeight}\n  m_AtlasPadding: 6\n" +
            "--- !u!28 &2800000\nTexture2D:\n" +
            $"  m_Width: {textureWidth}\n  m_Height: {atlasHeight}\n  m_IsReadable: 0\n";
    }
}
