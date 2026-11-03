using System.Collections.Generic;

namespace BitSorter.View
{
    /// <summary>
    /// Who made BitSorter and what it borrows: the one place those facts are written down.
    /// </summary>
    /// <remarks>
    /// The credits roll reads its facts from here, so each is written once. It credited two recorded tracks too, under a
    /// heading of their own as their licence asked, until Part 16b took the sound away (Ahmad, 2026-11-03).
    /// </remarks>
    public static class Credits
    {
        /// <summary>
        /// The line at the top of the credits, under the title. It was the 4.x game's, "bits fall through logic. sort them.", until
        /// Part 16e; this says what BitSorter is, in the About page's words.
        /// </summary>
        public const string Tagline = "a digital logic workbench and SystemVerilog simulator.";

        /// <summary>The version the credits show: the build's own (<c>bundleVersion</c>), as "v5.0.0".</summary>
        public static string VersionText => "v" + UnityEngine.Application.version;

        /// <summary>The person who made BitSorter, in every role.</summary>
        public const string Maker = "Ahmad Zoabi";

        /// <summary>The company it is made under, as the licence names it.</summary>
        public const string Company = "ZADZ";

        /// <summary>The line the copyright stands in, as the LICENSE file words it.</summary>
        public const string Copyright = "© 2026 Ahmad Zoabi (ZADZ)";

        /// <summary>
        /// The roles on the roll. Every one is <see cref="Maker"/>'s.
        /// </summary>
        /// <remarks>
        /// Rewritten for the simulator (Ahmad's choice, 2026-11-03): the 4.x game's GAME DESIGN, LEVEL DESIGN, ART AND
        /// INTERFACE, WRITING and TESTING became PRODUCT DESIGN, the exercises, INTERFACE AND VISUAL DESIGN, DOCUMENTATION and
        /// VERIFICATION AND TESTING, and a role came in for each thing the simulator does: its engine, its SystemVerilog,
        /// its timing, its logic analysis, its drawing of code, its waves and its three processors.
        /// </remarks>
        public static readonly string[] Roles =
        {
            "PRODUCT DESIGN",
            "CURRICULUM DESIGN",
            "EXERCISE DESIGN",
            "SIMULATION ENGINE",
            "SYSTEMVERILOG FRONT END",
            "STATIC TIMING ANALYSIS",
            "LOGIC ANALYSIS",
            "SCHEMATIC DRAWING",
            "WAVEFORM VIEWER",
            "RISC-V PROCESSORS",
            "WORKBENCH DESIGN",
            "INTERFACE AND VISUAL DESIGN",
            "USER EXPERIENCE",
            "PROGRAMMING",
            "VERIFICATION AND TESTING",
            "DOCUMENTATION",
            "PRODUCTION",
        };

        /// <summary>
        /// The thanks to the people who tried it early. By role, not by name, as asked; "tester" since 2026-11-03, where it said
        /// "playtester", a game's word.
        /// </summary>
        public const string TesterThanks = "Every tester who found a problem before anyone else could";

        /// <summary>
        /// Who it is for (5.0, Ahmad's choice, 2026-10-28): every student of the course, its name in full (2026-10-29: it said
        /// "digital systems"), and no university named.
        /// </summary>
        public const string Dedication = "every student of Digital Systems and Computer Structure";

        /// <summary>
        /// The last word (Ahmad's choice, 2026-11-03: it said THANK YOU FOR PLAYING, which BitSorter stopped being a game
        /// to say). The copyright and the version are under it.
        /// </summary>
        public const string Farewell = "HAPPY SIMULATING";

        /// <summary>What a line of the roll is, which decides how it is drawn.</summary>
        public enum Kind
        {
            /// <summary>BitSorter's name, at the top.</summary>
            Title,

            /// <summary>A small line under the title.</summary>
            Subtitle,

            /// <summary>What someone did: small, spaced, dim.</summary>
            Role,

            /// <summary>Who did it: large and bright.</summary>
            Name,

            /// <summary>Supporting detail: an author, a licence, a note.</summary>
            Detail,

            /// <summary>Empty space between groups.</summary>
            Gap,

            /// <summary>The last line, which the roll comes to rest on.</summary>
            Farewell,
        }

        /// <summary>One line of the roll.</summary>
        public readonly struct Line
        {
            public readonly Kind Kind;
            public readonly string Text;

            public Line(Kind kind, string text = "")
            {
                Kind = kind;
                Text = text;
            }
        }

        /// <summary>
        /// The whole roll, top to bottom, built from the facts above.
        /// </summary>
        /// <param name="tagline">The line under the title -- the main menu's, passed in so it has one home.</param>
        /// <param name="version">The build's version, as the main menu's corner shows it.</param>
        public static IReadOnlyList<Line> Roll(string tagline, string version)
        {
            var roll = new List<Line>
            {
                new Line(Kind.Title, "BITSORTER"),
                new Line(Kind.Subtitle, tagline),
                new Line(Kind.Gap),

                new Line(Kind.Role, "MADE BY"),
                new Line(Kind.Name, Maker),
                new Line(Kind.Gap),

                new Line(Kind.Role, "COMPANY"),
                new Line(Kind.Name, Company),
                new Line(Kind.Gap),
            };

            foreach (string role in Roles)
            {
                roll.Add(new Line(Kind.Role, role));
                roll.Add(new Line(Kind.Name, Maker));
                roll.Add(new Line(Kind.Gap));
            }

            roll.Add(new Line(Kind.Role, "SPECIAL THANKS"));
            roll.Add(new Line(Kind.Detail, TesterThanks));
            roll.Add(new Line(Kind.Gap));

            roll.Add(new Line(Kind.Role, "BUILT WITH"));
            roll.Add(new Line(Kind.Name, "Unity"));
            roll.Add(new Line(Kind.Detail, "with TextMesh Pro and Liberation Sans"));
            roll.Add(new Line(Kind.Gap));
            roll.Add(new Line(Kind.Name, "CodeMirror"));
            roll.Add(new Line(Kind.Detail, "the code editor in a browser, by Marijn Haverbeke and others (MIT)"));
            roll.Add(new Line(Kind.Gap));
            roll.Add(new Line(Kind.Name, "IBM Plex Sans and IBM Plex Mono"));
            roll.Add(new Line(Kind.Detail, "the typefaces, by Mike Abbink and Bold Monday for IBM (SIL Open Font License)"));
            roll.Add(new Line(Kind.Gap));

            roll.Add(new Line(Kind.Role, "CHECKED AGAINST"));
            roll.Add(new Line(Kind.Name, "Icarus Verilog"));
            roll.Add(new Line(Kind.Detail, "the simulator BitSorter's engine is checked against, by Stephen Williams and others"));
            roll.Add(new Line(Kind.Gap));
            roll.Add(new Line(Kind.Name, "ModelSim"));
            roll.Add(new Line(Kind.Detail, "the course's simulator, the second check (Intel FPGA Starter Edition, by Siemens)"));
            roll.Add(new Line(Kind.Gap));

            roll.Add(new Line(Kind.Role, "DEDICATED TO"));
            roll.Add(new Line(Kind.Name, Dedication));
            roll.Add(new Line(Kind.Gap));
            roll.Add(new Line(Kind.Gap));

            roll.Add(new Line(Kind.Farewell, Farewell));
            roll.Add(new Line(Kind.Detail, $"{Copyright}  ·  {version}"));

            return roll;
        }
    }
}
