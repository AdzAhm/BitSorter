using System.Collections.Generic;
using System.Text;

namespace BitSorter.View
{
    /// <summary>
    /// Who made the game and what it borrows: the one place those facts are written down.
    /// </summary>
    /// <remarks>
    /// The main menu's music line and the credits roll both read from here, so a track's author,
    /// its licence and the change made to it cannot say one thing on the menu and another in the
    /// credits. The CC BY licence asks for all four wherever the music is used, and the licence's
    /// address with every copy -- a browser build ships without the README, so the game itself is
    /// where it has to be.
    /// </remarks>
    public static class Credits
    {
        /// <summary>The person who made the game, in every role.</summary>
        public const string Maker = "Ahmad Zoabi";

        /// <summary>The company it is made under, as the licence names it.</summary>
        public const string Company = "ZADZ";

        /// <summary>The line the copyright stands in, as the LICENSE file words it.</summary>
        public const string Copyright = "© 2026 Ahmad Zoabi (ZADZ)";

        /// <summary>
        /// The roles on the roll. Every one is <see cref="Maker"/>'s.
        /// </summary>
        /// <remarks>
        /// Four were added on 2026-09-27, as asked, each for work the game shows: what it teaches and
        /// in what order, the simulator every level runs on, the goals, hints and tutorial, and how
        /// its screens read and respond.
        /// </remarks>
        public static readonly string[] Roles =
        {
            "GAME DESIGN",
            "CURRICULUM DESIGN",
            "PROGRAMMING",
            "SIMULATION ENGINEERING",
            "LEVEL DESIGN",
            "WRITING",
            "ART AND INTERFACE",
            "USER EXPERIENCE",
            "SOUND DESIGN",
            "LEVEL MUSIC",
            "PRODUCTION",
            "TESTING",
        };

        /// <summary>A track the game plays but did not write, and the terms it is used under.</summary>
        public sealed class Track
        {
            public readonly string Title;
            public readonly string Author;
            public readonly string Site;
            public readonly string Licence;
            public readonly string LicenceAddress;
            public readonly string Change;

            public Track(string title, string author, string site, string licence, string licenceAddress, string change)
            {
                Title = title;
                Author = author;
                Site = site;
                Licence = licence;
                LicenceAddress = licenceAddress;
                Change = change;
            }

            /// <summary>"by" the author, and where to find them if they say.</summary>
            public string ByLine => Site == null ? $"by {Author}" : $"by {Author}, {Site}";

            /// <summary>The licence, where to read it, and what was changed, in that order.</summary>
            public string Terms
            {
                get
                {
                    var terms = new StringBuilder(Licence);

                    if (LicenceAddress != null)
                        terms.Append(", ").Append(LicenceAddress);

                    if (Change != null)
                        terms.Append("; ").Append(Change);

                    return terms.ToString();
                }
            }
        }

        /// <summary>
        /// The main menu's two recorded tracks. Everything else the game plays is generated in code.
        /// </summary>
        /// <remarks>
        /// Woodland Fantasy is CC BY 3.0, which requires the author, the title, the licence and a
        /// note of any change -- it is converted to mono -- wherever the work is used, and the
        /// licence's address with every copy. The README links it too, but a browser build is a copy
        /// that ships without the README. Dream is CC0 and owed nothing, and is credited anyway.
        /// Both came from OpenGameArt, where each licence was read before either was downloaded; the
        /// README's credits say where.
        ///
        /// The credits roll is where the game credits them, under a heading of their own and as
        /// prominent as any other credit on it -- which is the licence's own rule for a credit in a
        /// list of credits. They were also on a line along the foot of the main menu until a
        /// playtest asked for it gone (2026-09-27).
        /// </remarks>
        public static readonly Track[] MenuMusic =
        {
            new Track("Dream", "jkjkke", null, "CC0", null, null),
            new Track("Woodland Fantasy", "Matthew Pablo", "matthewpablo.com",
                "CC BY 3.0", "creativecommons.org/licenses/by/3.0", "converted to mono"),
        };

        /// <summary>The thanks to the people who played it early. By role, not by name, as asked.</summary>
        public const string PlaytesterThanks = "Every playtester who found a problem before anyone else could";

        /// <summary>The last word.</summary>
        public const string Farewell = "THANK YOU FOR PLAYING";

        /// <summary>What a line of the roll is, which decides how it is drawn.</summary>
        public enum Kind
        {
            /// <summary>The game's name, at the top.</summary>
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

                new Line(Kind.Role, "A GAME BY"),
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

            roll.Add(new Line(Kind.Role, "MAIN MENU MUSIC"));

            foreach (Track track in MenuMusic)
            {
                roll.Add(new Line(Kind.Name, $"\"{track.Title}\""));
                roll.Add(new Line(Kind.Detail, track.ByLine));
                roll.Add(new Line(Kind.Detail, track.Terms));
                roll.Add(new Line(Kind.Gap));
            }

            roll.Add(new Line(Kind.Role, "SPECIAL THANKS"));
            roll.Add(new Line(Kind.Detail, PlaytesterThanks));
            roll.Add(new Line(Kind.Gap));

            roll.Add(new Line(Kind.Role, "BUILT WITH"));
            roll.Add(new Line(Kind.Name, "Unity"));
            roll.Add(new Line(Kind.Detail, "with TextMesh Pro and Liberation Sans"));
            roll.Add(new Line(Kind.Gap));
            roll.Add(new Line(Kind.Gap));

            roll.Add(new Line(Kind.Farewell, Farewell));
            roll.Add(new Line(Kind.Detail, $"{Copyright}  ·  {version}"));

            return roll;
        }
    }
}
