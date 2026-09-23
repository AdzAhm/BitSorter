using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// The page the browser build is served in keeps the whole game within reach at any window size.
    /// </summary>
    /// <remarks>
    /// Read from the template's own source, because nothing in Unity lays the page out: a layout
    /// fault here is only visible in a browser, in a window of the wrong size. One was found that
    /// way. The template centred a fixed 960x600 canvas with Unity's stock left: 50% and
    /// translate(-50%), and in a window narrower than the canvas the translate pushed its left edge
    /// off the page, where no scrollbar reaches -- the edge the parts list lives on, so a player in
    /// a narrow window could not pick up a gate.
    /// </remarks>
    public class WebTemplateTests
    {
        private const string Template = "WebGLTemplates/BitSorter";

        private static string Read(string file) =>
            File.ReadAllText(Path.Combine(Application.dataPath, Template, file));

        /// <summary>The declarations of one CSS rule, found by its selector.</summary>
        /// <remarks>
        /// To the last brace on the line, not the first: a template value such as
        /// <c>{{{ BACKGROUND_COLOR }}}</c> has braces of its own. Every rule in the file is one line.
        /// </remarks>
        private static string Rule(string css, string selector)
        {
            Match match = Regex.Match(css, Regex.Escape(selector) + @"\s*\{(.*)\}");
            Assert.IsTrue(match.Success, $"sanity: style.css has no rule for {selector}");
            return match.Groups[1].Value;
        }

        /// <summary>
        /// The game is never placed by a translate, which is how it was pushed off the page.
        /// </summary>
        /// <remarks>
        /// A negative offset is the one kind of overflow a page cannot scroll to. Auto margins
        /// centre a container just as well and fall to zero when it is wider than the window.
        /// </remarks>
        [Test]
        public void TheGame_IsNeverPushedOffThePage()
        {
            string desktop = Rule(Read("TemplateData/style.css"), "#unity-container.unity-desktop");

            StringAssert.DoesNotContain("translate", desktop,
                "the game is centred by a translate, which pushes its left edge off a narrow window");
        }

        /// <summary>
        /// The canvas is sized from the window, not fixed at the size the build was authored at.
        /// </summary>
        [Test]
        public void TheCanvas_IsNotAFixedSize()
        {
            string page = Read("index.html");

            StringAssert.DoesNotContain("canvas.style.width = \"{{{ WIDTH }}}px\"", page,
                "the canvas is fixed at its authored width, so a window narrower than that cannot hold it");
            StringAssert.Contains("addEventListener(\"resize\"", page,
                "the canvas is never sized again when the window changes");
        }

        /// <summary>
        /// The render is not allowed to scale with a high-density screen without limit.
        /// </summary>
        /// <remarks>
        /// The canvas grows with the window, and it renders at its size times the screen's pixel
        /// ratio: at 1920 CSS pixels and a ratio of 2 that is 3840x2400, with bloom, every frame.
        /// </remarks>
        [Test]
        public void TheRenderResolution_IsCappedOnHighDensityScreens()
        {
            StringAssert.Contains("config.devicePixelRatio = Math.min(", Read("index.html"),
                "the render follows the screen's pixel ratio without a limit");
        }

        /// <summary>
        /// The page around the game is the game's own dark, not the browser's white.
        /// </summary>
        /// <remarks>
        /// It was left to the browser, which paints white. That was barely seen while the canvas
        /// was a fixed size in a larger window; with the game centred in whatever room it has,
        /// the white became a frame round a near-black board.
        /// </remarks>
        [Test]
        public void ThePageAroundTheGame_IsDark()
        {
            string page = Rule(Read("TemplateData/style.css"), "html, body");

            StringAssert.Contains("background: {{{ BACKGROUND_COLOR }}}", page,
                "the page around the game is left to the browser, which paints it white");
        }
    }
}
