using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;

namespace BitSorter.EditMode.Tests
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

        /// <summary>The page with its comments taken out, so that a line commented out never counts as one that runs.</summary>
        private static string Code(string page) =>
            Regex.Replace(page, @"/\*.*?\*/|<!--.*?-->|^\s*//[^\n]*", "", RegexOptions.Singleline | RegexOptions.Multiline);

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
        /// ratio: at 1920 CSS pixels and a ratio of 2 that is 3840x2400, every frame.
        /// </remarks>
        [Test]
        public void TheRenderResolution_IsCappedOnHighDensityScreens()
        {
            string page = Read("index.html");

            StringAssert.Contains("Math.min(window.devicePixelRatio || 1, 1.5)", page,
                "the render follows the screen's pixel ratio without a limit");
            StringAssert.Contains("config.devicePixelRatio = cappedPixelRatio()", page,
                "the capped ratio is not what the game starts with");
        }

        /// <summary>
        /// The capped ratio follows the screen after the game has loaded, as Unity's own did.
        /// </summary>
        /// <remarks>
        /// The cap was read once, into the config, when the page loaded. Unity's default reads the
        /// window's ratio every time it sizes the canvas; the config's value replaces it for good, so
        /// zooming the page or moving the window to a sharper screen left the game rendering at the
        /// ratio it happened to load at. Seen in a smoke build: the window at 1.25, the canvas's
        /// backing store at 1. Unity reads <c>Module.devicePixelRatio</c> live -- setting it on the
        /// loaded instance resized the render within a frame -- so the page has to keep it current.
        /// </remarks>
        [Test]
        public void TheCappedRatio_FollowsTheScreenAfterLoading()
        {
            StringAssert.Contains("unityInstance.Module.devicePixelRatio =", Read("index.html"),
                "the capped ratio is read once at load, and a zoom or a move to another screen is never seen");
        }

        /// <summary>
        /// Following the pixel ratio never throws on a browser without media-query listeners.
        /// </summary>
        /// <remarks>
        /// It runs inside the loader's success handler, and a throw there reaches the handler's
        /// .catch, which puts up an alert over a game that loaded perfectly well. Safari before 14 has
        /// no addEventListener on a media query.
        /// </remarks>
        [Test]
        public void FollowingThePixelRatio_AsksBeforeListening()
        {
            StringAssert.Contains("if (query.addEventListener)", Read("index.html"),
                "the page calls a media query's addEventListener without asking whether it has one");
        }

        /// <summary>
        /// The page writes text into itself as text, never as markup.
        /// </summary>
        /// <remarks>
        /// Unity's stock template put its warning messages into the page with innerHTML. The
        /// messages come from Unity itself, so nothing hostile was known to reach it -- but markup
        /// built from a string is how a page ends up running something it did not mean to, and
        /// textContent costs nothing.
        /// </remarks>
        [Test]
        public void ThePage_WritesTextAsText_NeverAsMarkup()
        {
            StringAssert.DoesNotContain("innerHTML", Read("index.html"),
                "the page builds markup from a string");
        }

        /// <summary>
        /// The page's fullscreen button takes the page itself fullscreen, and the canvas fills it: a written block's code is a
        /// box the page lays over the canvas (CodeMirror, Part 12f), and with the canvas alone the fullscreen element nothing
        /// else on the page was drawn -- the code could be neither read nor typed until fullscreen was left (browser-2).
        /// </summary>
        [Test]
        public void TheFullscreenButton_TakesThePageFullscreen_SoTheCodeShowsOverTheCanvas()
        {
            string page = Read("index.html");
            int button = page.IndexOf("#unity-fullscreen-button\").onclick", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(button, 0, "sanity: the page has its fullscreen button");
            string handler = page.Substring(button, System.Math.Min(600, page.Length - button));

            int whole = handler.IndexOf("document.documentElement", System.StringComparison.Ordinal);
            int canvasAlone = handler.IndexOf("SetFullscreen", System.StringComparison.Ordinal);
            Assert.GreaterOrEqual(whole, 0, "the button takes the canvas alone fullscreen, over the code's box");
            Assert.IsTrue(canvasAlone < 0 || canvasAlone > whole, "the canvas alone only where the page cannot go fullscreen");
            StringAssert.Contains("fullscreenElement", page, "and the canvas fills the screen while the page does");
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

        /// <summary>
        /// The page shows BitSorter while it loads, not Unity: no Unity logo, no stock progress bar, and none of their
        /// images left behind.
        /// </summary>
        /// <remarks>
        /// Unity's template drew its logo and its loading bar over the canvas, and its logo again in the footer; the
        /// loading screen took the place of all three (Part 16). An image left in the template's folder is copied into
        /// every build whether the page draws it or not.
        /// </remarks>
        [Test]
        public void ThePage_CarriesNoUnityBranding()
        {
            string page = Read("index.html") + Read("TemplateData/style.css");

            StringAssert.Contains("id=\"bs-loading\"", page, "sanity: the page has no loading screen of its own");
            StringAssert.DoesNotContain("unity-logo", page, "the page still draws Unity's logo");
            StringAssert.DoesNotContain("progress-bar-", page, "the page still draws Unity's stock progress bar");

            string[] files = Directory.GetFiles(Path.Combine(Application.dataPath, Template, "TemplateData"))
                .Select(file => Path.GetFileName(file)).ToArray();
            CollectionAssert.Contains(files, "favicon.ico", "sanity: the template's own files are not where they are looked for");
            CollectionAssert.IsEmpty(files.Where(file => file.StartsWith("unity-logo", StringComparison.Ordinal)
                                                         || file.StartsWith("progress-bar-", StringComparison.Ordinal)),
                "Unity's stock images are still in the template, and so in every build");
        }

        /// <summary>
        /// The loading screen keeps still for a reader who has asked for reduced motion.
        /// </summary>
        /// <remarks>
        /// It takes both halves. The style sheet's media query stops the CSS animations -- the drift, the waves, the
        /// halo -- but every front along a wire is a Web Animation made by the script, which no style reaches, so the
        /// script asks the same question and pauses each one at a single moment.
        /// </remarks>
        [Test]
        public void TheLoadingScreen_HonoursReducedMotion()
        {
            string page = Code(Read("index.html"));

            StringAssert.IsMatch(@"@media\s*\(\s*prefers-reduced-motion\s*:\s*reduce\s*\)\s*\{[^\n]*#bs-loading \*\s*\{\s*animation\s*:\s*none", page,
                "the loading screen's CSS animations run whatever the reader asked for");
            StringAssert.IsMatch(@"still = matchMedia\('\(prefers-reduced-motion: reduce\)'\)\.matches", page,
                "the loading screen's script never asks whether the reader wants reduced motion");
            StringAssert.IsMatch(@"if \(still\) \{ a\.pause\(\);", page,
                "the loading screen's Web Animations run whatever the reader asked for");
        }

        /// <summary>
        /// The loading screen's colours are the default look's -- <see cref="SchematicPalette.Dark"/>'s, to the digit.
        /// </summary>
        /// <remarks>
        /// The page is drawn before BitSorter exists, so it cannot ask the palette and writes its colours out: a second
        /// copy of each, and a second copy is how two things drift. Each colour the page declares is held to the
        /// palette's field it was taken from, and a colour added to the page that the palette does not have fails too.
        /// </remarks>
        [Test]
        public void TheLoadingScreensColours_AreThePalettes()
        {
            SchematicPalette palette = SchematicPalette.Dark;
            var expected = new Dictionary<string, Color>
            {
                ["--ground"] = palette.Ground, ["--grid"] = palette.Grid, ["--chip"] = palette.Chip,
                ["--edge"] = palette.CardEdge, ["--outline"] = palette.Outline, ["--text"] = palette.Text,
                ["--dim"] = palette.TextDim, ["--muted"] = palette.Muted, ["--accent"] = palette.Accent,
                ["--v0"] = palette.Zero, ["--v1"] = palette.One, ["--vx"] = palette.Unknown, ["--vz"] = palette.HighZ,
                ["--bus"] = palette.Bus, ["--playhead"] = palette.Playhead, ["--cursor"] = palette.Cursor,
                ["--sel"] = palette.Selection,
            };

            Match root = Regex.Match(Read("index.html"), @":root\s*\{([^}]*)\}");
            Assert.IsTrue(root.Success, "sanity: the page declares no colours of its own");

            var declared = new Dictionary<string, string>();
            foreach (Match colour in Regex.Matches(root.Groups[1].Value, @"(--[\w-]+)\s*:\s*(#[0-9A-Fa-f]+)"))
                declared[colour.Groups[1].Value] = colour.Groups[2].Value.ToUpperInvariant();

            CollectionAssert.AreEquivalent(expected.Keys, declared.Keys,
                "the page declares a colour the palette is not asked for, or leaves one out");
            foreach (KeyValuePair<string, Color> colour in expected)
                Assert.AreEqual("#" + ColorUtility.ToHtmlStringRGB(colour.Value), declared[colour.Key],
                    $"the page's {colour.Key} is not the palette's");
        }

        /// <summary>
        /// The loading screen leaves the page once BitSorter is running, and leaves nothing running behind it.
        /// </summary>
        /// <remarks>
        /// It is about a thousand elements and two hundred animations over the canvas. Hidden and left in place, they
        /// would go on being laid out and animated under every frame of the simulator. So once the loader's promise
        /// resolves the page calls finishLoading, which cancels the animations, clears the timers and takes the layer
        /// out of the page.
        /// </remarks>
        [Test]
        public void TheLoadingScreen_IsRemovedOnceBitSorterIsRunning()
        {
            string page = Code(Read("index.html"));

            int ready = page.IndexOf(".then((unityInstance) =>", StringComparison.Ordinal);
            int failed = ready < 0 ? -1 : page.IndexOf(".catch(", ready, StringComparison.Ordinal);
            Assert.IsTrue(failed > ready && ready >= 0, "sanity: the page has no loader promise to wait on");
            StringAssert.Contains("finishLoading();", page.Substring(ready, failed - ready),
                "the page never says BitSorter is running, so the loading screen stays over it");

            int finish = page.IndexOf("function finishLoading()", StringComparison.Ordinal);
            Assert.IsTrue(finish >= 0, "sanity: the page has no finishLoading");
            string body = page.Substring(finish, page.IndexOf("\n}", finish, StringComparison.Ordinal) - finish);
            StringAssert.Contains("L.parentNode.removeChild(L)", body, "the loading screen is never taken out of the page");
            StringAssert.Contains("a.cancel()", body, "the loading screen's animations outlive it");
            StringAssert.Contains("timers.forEach(clearTimeout)", body, "the loading screen's timers outlive it");
        }

        /// <summary>
        /// A load that fails stops the loading screen where it stands, and still says why.
        /// </summary>
        /// <remarks>
        /// The loader's failure was put up in an alert, and still is; the loading screen stops first, so a page that will
        /// not load does not go on looking as though it were loading.
        /// </remarks>
        [Test]
        public void AFailedLoad_StopsTheLoadingScreen_AndStillSaysWhy()
        {
            string page = Code(Read("index.html"));

            int failed = page.IndexOf(".catch((message) =>", StringComparison.Ordinal);
            Assert.IsTrue(failed >= 0, "sanity: the page has no failure handler");
            string handler = page.Substring(failed, page.IndexOf("});", failed, StringComparison.Ordinal) - failed);
            StringAssert.Contains("stopLoading();", handler, "a failed load leaves the loading screen moving as if loading");
            StringAssert.Contains("alert(message);", handler, "a failed load no longer says why");
        }

        /// <summary>
        /// The page's dark is the workbench's ground, which the build takes from the palette.
        /// </summary>
        /// <remarks>
        /// The template's BACKGROUND_COLOR -- the page round the canvas, and the canvas until BitSorter draws -- is the
        /// splash screen's background colour, and it was Unity's warm #231F20 beside the loading screen's #151515.
        /// WebGLBuild sets it from the palette before every browser build, and the project's settings hold the same
        /// value, so a build made before Configure has ever run looks no different.
        /// </remarks>
        [Test]
        public void ThePagesDark_IsTakenFromThePalette()
        {
            string build = Regex.Replace(
                File.ReadAllText(Path.Combine(Application.dataPath, "Scripts", "View", "Editor", "WebGLBuild.cs")), "//[^\n]*", "");
            int configure = build.IndexOf("void Configure()", StringComparison.Ordinal);
            Assert.IsTrue(configure >= 0, "sanity: WebGLBuild has no Configure");
            StringAssert.Contains("PlayerSettings.SplashScreen.backgroundColor = SchematicPalette.Dark.Ground;",
                build.Substring(configure, build.IndexOf("\n        }", configure, StringComparison.Ordinal) - configure),
                "the browser build does not take the page's dark from the palette");

            string path = Path.Combine(Application.dataPath, "..", "ProjectSettings", "ProjectSettings.asset");
            Match saved = Regex.Match(File.ReadAllText(path),
                @"^\s*m_SplashScreenBackgroundColor:\s*\{r: ([\d.]+), g: ([\d.]+), b: ([\d.]+)", RegexOptions.Multiline);
            Assert.IsTrue(saved.Success, "sanity: ProjectSettings.asset has no splash screen colour");
            var colour = new Color(float.Parse(saved.Groups[1].Value, CultureInfo.InvariantCulture),
                float.Parse(saved.Groups[2].Value, CultureInfo.InvariantCulture),
                float.Parse(saved.Groups[3].Value, CultureInfo.InvariantCulture));
            Assert.AreEqual(ColorUtility.ToHtmlStringRGB(SchematicPalette.Dark.Ground), ColorUtility.ToHtmlStringRGB(colour),
                "the project's settings keep a page colour that is not the palette's ground");
        }
    }
}
