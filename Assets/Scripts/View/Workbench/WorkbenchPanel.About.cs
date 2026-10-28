using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using ShellButton = UnityEngine.UIElements.Button;

namespace BitSorter.View
{
    /// <summary>
    /// ABOUT BITSORTER (Part 15h): a page of the workbench, from HELP, saying what the simulator is and what it does -- not
    /// too deeply and not too plainly, as Ahmad asked (2026-10-28). It opened the credits roll until then. What it can do is
    /// named in the workbench's own words, so the page is a map of it too; where it came from closes it, and the credits,
    /// the keyboard map and the project's page are at its foot.
    /// </summary>
    public sealed partial class WorkbenchPanel
    {
        public const string AboutPageName = "Workbench about page", AboutCreditsButton = "Workbench about credits",
            AboutKeysButton = "Workbench about keys", AboutGitHubButton = "Workbench about github", AboutBackButton = "Workbench about back";

        /// <summary>Where the project lives.</summary>
        public const string GitHubAddress = "https://github.com/AdzAhm/BitSorter";

        public const string AboutTagline = "A digital logic workbench and SystemVerilog simulator.";

        public const string AboutIntro = "Draw a circuit from gates, or write it in SystemVerilog, and watch it run: every value on "
            + "every wire, every gate's delay, every glitch, as it happens.";

        /// <summary>What it can do, each under the workbench's own name for it: nine to a grid of three.</summary>
        public static readonly IReadOnlyList<(string Heading, string Text)> AboutAbilities = new[]
        {
            ("DRAW", "Gates with their own rise and fall delays, muxes, registers, constants, splitters and joiners, tri-state "
                + "buffers and buses. Wire from any pin, turn and mirror, align and copy, and undo anything. Make a selection a "
                + "block, step inside it, and use it as an array of copies."),
            ("WRITE", "SystemVerilog modules with always_ff and always_comb, generate, functions and interfaces, checked as you "
                + "type with each error marked on its line. Drawn and written modules work together in one design."),
            ("RUN", "Watch each change travel the wires at the speed you choose. Play, pause, step from edge to edge or back, run "
                + "to a moment, or break when a signal takes a value."),
            ("READ THE WAVES", "Binary, hex, or signed and unsigned decimal; two cursors and the time between them; groups of "
                + "signals; export to VCD or PNG."),
            ("DRIVE IT", "Double-click a wave to set an input, or build a whole sequence in INPUTS: every combination, a count, "
                + "a clock."),
            ("ANALYSE IT", "TIMING finds the critical path, lights it on the circuit and works out the minimum clock period; STEP "
                + "TEST measures the slowest path by trying every input. LOGIC gives truth tables, minimal expressions and "
                + "K-maps, and STATES draws the state diagram."),
            ("BRING YOUR OWN PROJECT", "Import its files with their testbench and memory images, run it with every $display in "
                + "the TRANSCRIPT, and export it again as a project ModelSim can run."),
            ("PRACTISE", "Thirty-five exercises, from routing a single bit to pipelining, each with a goal, a hint if you want "
                + "one, and CHECK. Answer by drawing or in code; a guided tutorial starts you off."),
            ("HOW IT SIMULATES", "Four values: 0, 1, x for unknown and z for undriven. Gates switch after real delays, so a "
                + "glitch shows on screen as it would in hardware. Events run in the order the language defines, as ModelSim "
                + "runs them, and every construct is checked against Icarus Verilog, a second simulator."),
        };

        /// <summary>The page's foot: where it runs, and where it came from.</summary>
        public static readonly IReadOnlyList<(string Heading, string Text)> AboutStory = new[]
        {
            ("IN A BROWSER OR ON THE DESKTOP", "The same simulator in both; the desktop build is faster with big designs. Your "
                + "work is kept on the machine, or in the browser, you use it in."),
            ("WHERE IT CAME FROM", "BitSorter began as BitSorter 4.x, a puzzle game: bits fall through logic gates into bins, "
                + "over thirty levels from a single wire to a serial adder. Version 5.0 rebuilt it into a simulator for the "
                + "Digital Systems and Computer Structure course. The thirty levels live on as its exercises, and the original "
                + "game is still there, under Settings: THE 4.x GAME."),
        };

        public const string AboutMaker = "BitSorter 5.0   ·   made by Ahmad Zoabi   ·   ZADZ";

        private const float AboutCardWidth = 460f, AboutGap = 40f;

        private VisualElement _about;

        /// <summary>Whether ABOUT BITSORTER is up.</summary>
        public bool AboutShowing => _about != null && _about.style.display == DisplayStyle.Flex;

        /// <summary>ABOUT BITSORTER up, as HELP's item puts it: made the first time it is asked for.</summary>
        public void ShowAbout()
        {
            if (_about == null)
                BuildAbout(SchematicPalette.Current);
            ShowPage(_about);
        }

        private void HideAbout() => HidePage(_about);

        private void BuildAbout(SchematicPalette palette)
        {
            _about = AddPage(AboutPageName, HideAbout);
            if (_about == null)
                return;

            // A short window scrolls it, the viewport clipped by hand: the shell has no theme to clip it (Settings' lesson).
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "about scroll" };
            scroll.style.position = Position.Absolute;
            scroll.style.left = scroll.style.right = scroll.style.top = scroll.style.bottom = 0f;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.contentViewport.style.overflow = Overflow.Hidden;
            _about.Add(scroll);

            float width = 3 * AboutCardWidth + 2 * AboutGap;
            var column = new VisualElement { name = "about", pickingMode = PickingMode.Ignore };
            column.style.width = width;
            column.style.alignSelf = Align.Center;
            column.style.paddingTop = 28f;
            column.style.paddingBottom = 28f;
            column.style.flexShrink = 0f;
            scroll.Add(column);

            Label name = ShellTheme.Label("name", AppName, UiType.Title, palette.Accent, TextAnchor.MiddleCenter, bold: true);
            column.Add(name);
            Label tagline = ShellTheme.Label("tagline", AboutTagline, UiType.Lead, palette.Text, TextAnchor.MiddleCenter);
            tagline.style.marginTop = 4f;
            column.Add(tagline);
            Label intro = ShellTheme.Label("intro", AboutIntro, UiType.Body, palette.TextDim, TextAnchor.MiddleCenter);
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginTop = 10f;
            intro.style.marginBottom = 26f;
            column.Add(intro);

            // What it can do, three to a row; then where it runs and where it came from, two to a row under a rule.
            AboutCards(column, AboutAbilities, 3, palette);
            var rule = new VisualElement { name = "rule", pickingMode = PickingMode.Ignore };
            rule.style.height = 1f;
            rule.style.marginTop = 4f;
            rule.style.marginBottom = 18f;
            rule.style.backgroundColor = palette.Outline;
            column.Add(rule);
            AboutCards(column, AboutStory, 2, palette);

            // Its foot: who made it, and the credits, the keyboard map and the project's page.
            var foot = new VisualElement { name = "foot", pickingMode = PickingMode.Ignore };
            foot.style.flexDirection = FlexDirection.Row;
            foot.style.alignItems = Align.Center;
            foot.style.justifyContent = Justify.Center;
            foot.style.marginTop = 18f;
            column.Add(foot);
            Label maker = ShellTheme.Label("maker", AboutMaker, UiType.Body, palette.TextDim, TextAnchor.MiddleLeft);
            maker.style.marginRight = 28f;
            foot.Add(maker);
            AboutButton(foot, AboutCreditsButton, "CREDITS", () => _credits?.Open(ShowAbout), palette);
            AboutButton(foot, AboutKeysButton, "KEYBOARD MAP", () =>
            {
                HideAbout();
                if (!_keyMap.IsOpen)
                    _keyMap.Toggle();
            }, palette);
            AboutButton(foot, AboutGitHubButton, "GITHUB", () => Application.OpenURL(GitHubAddress), palette);

            Label help = ShellTheme.Label("help", "escape to go back", UiType.Caption, palette.Muted, TextAnchor.MiddleCenter);
            help.style.marginTop = 16f;
            column.Add(help);

            foreach (VisualElement child in column.Children())
                child.style.flexShrink = 0f;

            ShellButton back = ShellTheme.Button(AboutBackButton, "BACK", palette, HideAbout);
            back.style.position = Position.Absolute;
            back.style.top = 16f;
            back.style.right = 24f;
            back.style.width = 120f;
            back.style.height = 44f;
            _about.Add(back);
        }

        /// <summary>Cards of a heading and its paragraph, <paramref name="across"/> to a row, the card's width each.</summary>
        private static void AboutCards(VisualElement column, IReadOnlyList<(string Heading, string Text)> cards, int across,
            SchematicPalette palette)
        {
            float cardWidth = (3 * AboutCardWidth + 2 * AboutGap - (across - 1) * AboutGap) / across;
            VisualElement row = null;
            for (int i = 0; i < cards.Count; i++)
            {
                if (i % across == 0)
                {
                    row = new VisualElement { name = "row", pickingMode = PickingMode.Ignore };
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.FlexStart;
                    row.style.marginBottom = 18f;
                    row.style.flexShrink = 0f;
                    column.Add(row);
                }
                var card = new VisualElement { name = "card " + cards[i].Heading, pickingMode = PickingMode.Ignore };
                card.style.width = cardWidth;
                card.style.flexShrink = 0f;
                if (i % across != across - 1)
                    card.style.marginRight = AboutGap;
                Label heading = ShellTheme.Label("heading", cards[i].Heading, UiType.Label, palette.Accent, TextAnchor.LowerLeft, bold: true);
                heading.style.letterSpacing = 4f;
                card.Add(heading);
                Label text = ShellTheme.Label("text", cards[i].Text, UiType.Body, palette.Text, TextAnchor.UpperLeft);
                text.style.whiteSpace = WhiteSpace.Normal;
                text.style.marginTop = 4f;
                card.Add(text);
                row.Add(card);
            }
        }

        private static void AboutButton(VisualElement row, string name, string caption, System.Action clicked, SchematicPalette palette)
        {
            ShellButton button = ShellTheme.Button(name, caption, palette, clicked);
            button.style.width = 170f;
            button.style.height = 40f;
            button.style.marginLeft = 10f;
            row.Add(button);
        }
    }
}
