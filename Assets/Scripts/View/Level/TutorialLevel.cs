using System.Collections.Generic;
using BitSorter.LogicCore;
using UnityEngine;

namespace BitSorter.View
{
    /// <summary>
    /// The board the guided tutorial runs on: one source, one bin, and room for one NOT gate
    /// between them.
    /// </summary>
    /// <remarks>
    /// Built in code rather than authored as JSON, for the reasons <see cref="SandboxLevel"/> sets
    /// out and which apply here unchanged: CurriculumTests parses every file in Resources/Levels and
    /// demands a goal and hint from each, and a level file cannot author wires. Building here leaves
    /// those rules exactly as strict as they were.
    ///
    /// It is deliberately **not** in <see cref="LevelCatalog"/>. Two consequences, both wanted: it
    /// cannot disturb the nine-level run that CurriculumTests pins, and it never appears in
    /// AvailableLevels, so Q and E do not cycle into it and the banner still counts to nine. It is
    /// reached by the row at the head of the level list, and once by itself on a fresh save.
    ///
    /// Unlike the sandbox it *is* graded. The last step is a bit landing in the bin, and letting the
    /// run settle Passed means the player meets the ordinary win panel at the end of the tutorial
    /// rather than something built only for this -- which is the point at which "what winning looks
    /// like" is worth teaching.
    /// </remarks>
    public static class TutorialLevel
    {
        /// <summary>
        /// The key boards are saved under. Not a file name; nothing by this name exists in
        /// Resources/Levels.
        /// </summary>
        public const string Key = "tutorial";

        /// <summary>The part the tutorial asks the player to place.</summary>
        public const GateKind Part = GateKind.Not;

        /// <summary>
        /// A second part, listed first, that the tutorial never asks for.
        /// </summary>
        /// <remarks>
        /// Here so the first step is a real action. PlacementController.SelectFirstOffered puts the
        /// selection on a level's first budget row every time a level loads, so a palette holding
        /// only the part we ask for would arrive already selected -- the step would complete before
        /// the player touched anything, and they would never learn the palette is clickable.
        ///
        /// It also stops a one-row "parts list" teaching that a parts list has one row. Placing it
        /// by mistake is recoverable, and recovering is what the next step's right click teaches.
        /// </remarks>
        public const GateKind Decoy = GateKind.And;

        public const string SourceId = "a";
        public const string SinkId = "bin";

        /// <summary>Where the player is asked to put the gate. The one highlighted cell.</summary>
        public static readonly Vector2Int GateCell = new Vector2Int(0, 0);

        public static Vector2Int SourceCell(Vector2Int halfExtents) =>
            new Vector2Int(-halfExtents.x + 1, 0);

        public static Vector2Int SinkCell(Vector2Int halfExtents) =>
            new Vector2Int(halfExtents.x - 1, 0);

        /// <summary>
        /// One source, one bin, one NOT, one test vector.
        /// </summary>
        /// <remarks>
        /// A single vector on purpose. The last step is watching one bit cross the board, and a
        /// second bit arriving behind it would start teaching timing -- which is a mechanic, and
        /// belongs to the first-time hints on the level where it first matters.
        ///
        /// The source emits a 1 and the bin expects a 0, so the gate is doing something visible. A
        /// pass-through board would end with the player unsure whether the part mattered.
        /// </remarks>
        public static LevelDefinition Build(Vector2Int halfExtents)
        {
            var fixtures = new List<LevelFixture>(2)
            {
                new LevelFixture(SourceId, FixtureKind.Source, SourceCell(halfExtents),
                    new[] { Bit.One }),
                new LevelFixture(SinkId, FixtureKind.Sink, SinkCell(halfExtents),
                    System.Array.Empty<Bit>()),
            };

            // Decoy first, deliberately: see its remarks.
            var budget = new List<LevelBudgetEntry>(2)
            {
                new LevelBudgetEntry(Decoy, 1),
                new LevelBudgetEntry(Part, 1),
            };

            var expected = new List<ExpectedBit>(1) { new ExpectedBit(Bit.Zero, 0) };

            var expectations = new List<LevelExpectation>(1)
            {
                new LevelExpectation(SinkId, "0", expected),
            };

            return new LevelDefinition(
                name: "First steps",
                hint: "Follow the highlights. Anything you do here can be undone.",
                tickLimit: LevelLoader.DefaultTickLimit,
                vectorCount: 1,
                fixtures: fixtures,
                budget: budget,
                expectations: expectations,
                maxWireDelay: 1,
                delayBudget: 0,
                maxLatency: 0,
                order: 0,
                goal: "Send A's bit through a NOT gate and into the bin.");
        }
    }
}
