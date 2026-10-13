using System;
using System.Collections.Generic;
using System.Linq;
using BitSorter.Board;
using BitSorter.View;
using NUnit.Framework;
using UnityEngine;
using GateKind = BitSorter.View.GateKind;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Part 7b: every shipped level's own solution, made into a 5.0 document, gives every bin what the
    /// level expects of it -- read on 5.0's engine just before each vector's clock edge -- which is
    /// what migration promises (Docs/architecture-5.md, section 8).
    /// </summary>
    /// <remarks>
    /// The solutions are the ones each level's own tests solve it with, called from there, so a level
    /// retuned and its solution with it is checked here too without a second copy to keep in step.
    /// Each is held to passing in 4.x first, so a failure here is the migration's.
    /// </remarks>
    public class MigrationTests
    {
        private static IEnumerable<TestCaseData> Solutions()
        {
            yield return Case("route-the-bit", _ => ShippedLevelTests.RouteTheBit());
            yield return Case("the-long-way-round", _ => LongWayRoundLevelTests.AndThenNot());
            yield return Case("balance-the-paths", _ => BalanceLevelTests.Wiring(directDelay: 2));
            yield return Case("four-corners", _ => FourCornersLevelTests.FirstCover());
            yield return Case("dont-care", _ => DontCareLevelTests.SumOfProducts());
            yield return Case("nothing-but-nand", _ => NothingButNandLevelTests.CanonicalFour());
            yield return Case("the-slow-lane", _ => SlowLaneLevelTests.Wiring());
            yield return Case("out-of-step", OutOfStepLevelTests.Fixed);
            yield return Case("odd-one-out", _ => OddOneOutLevelTests.Tree());
            yield return Case("one-of-four", _ => OneOfFourLevelTests.Decoder());
            yield return Case("pick-a-lane", _ => PickALaneLevelTests.TextbookMux());
            yield return Case("four-lanes", _ => FourLanesLevelTests.Tree());
            yield return Case("highest-wins", _ => HighestWinsLevelTests.Encoder());
            yield return Case("which-is-bigger", _ => WhichIsBiggerLevelTests.Comparator());
            yield return Case("half-adder", _ => HalfAdderLevelTests.Solution());
            yield return Case("carry-the-one", _ => CarryTheOneLevelTests.FullAdder());
            yield return Case("wrong-part", level => WrongPartLevelTests.Replaced(level, GateKind.Or));
            yield return Case("pass-it-on", _ => PassItOnLevelTests.Adder());
            yield return Case("carry-it-further", _ => CarryItFurtherLevelTests.Adder());
            yield return Case("four-at-once", level => FourAtOnceLevelTests.Ripple(level));
            yield return Case("look-ahead", level => LookAheadLevelTests.Lookahead(level));
            yield return Case("one-clock-late", _ => OneClockLateLevelTests.Solution());
            yield return Case("rising-edge", _ => RisingEdgeLevelTests.Detector(directDelay: 2));
            yield return Case("flip-on-one", _ => FlipOnOneLevelTests.Toggle());
            yield return Case("hold-when-told", _ => HoldWhenToldLevelTests.Textbook());
            yield return Case("count-the-ones", _ => CountTheOnesLevelTests.Counter());
            yield return Case("miscount", MiscountLevelTests.Fixed);
            yield return Case("spot-the-pattern", _ => SpotThePatternLevelTests.Detector());
            yield return Case("one-clock-behind", _ => OneClockBehindLevelTests.Detector(reportThroughARegister: true));
            yield return Case("add-as-you-go", _ => AddAsYouGoLevelTests.Adder());
        }

        private static TestCaseData Case(string level, Func<LevelDefinition, CircuitBlueprint> solution) =>
            new TestCaseData(level, solution).SetName($"ALevelsOwnSolution_Migrates_AndStillSolvesIt({level})");

        private static LevelDefinition Level(string name)
        {
            LevelLoadResult loaded = LevelLoader.Load(name, LevelTestFixtures.Board);
            Assert.IsTrue(loaded.IsValid, loaded.Error);
            return loaded.Level;
        }

        [Test]
        public void EveryShippedLevel_HasASolutionHere()
        {
            var covered = new HashSet<string>(Solutions().Select(c => (string)c.Arguments[0]));
            foreach (TextAsset asset in Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath))
                Assert.IsTrue(covered.Contains(asset.name), $"{asset.name} has no solution in MigrationTests: add the one its own tests use");
        }

        [TestCaseSource(nameof(Solutions))]
        public void ALevelsOwnSolution_Migrates_AndStillSolvesIt(string name, Func<LevelDefinition, CircuitBlueprint> solution)
        {
            LevelDefinition level = Level(name);
            CircuitBlueprint board = solution(level);
            RunVerdict verdict = LevelTestFixtures.RunAndGrade(level, board);
            Assert.IsTrue(verdict.IsPass, $"sanity: the solution passes in 4.x: {verdict}");

            LegacyCircuit circuit = LegacyCircuits.From(name, level, board);
            MigrationResult migrated = Migrator.Migrate(circuit);

            Assert.IsEmpty(BoardRules.Check(migrated.Project).Where(d => d.IsError).Select(d => d.Message));
            List<string> wrong = migrated.Mismatches(circuit.expected);
            Assert.IsEmpty(wrong, $"{wrong.Count} bins disagree:\n  " + string.Join("\n  ", wrong.Take(12)));
        }

        [Test]
        public void ADocument_ThroughJsonUtility_ComesBackTheSame()
        {
            // Four at once has blocks, a hierarchy and buffers: the most of the format at once.
            LevelDefinition level = Level("four-at-once");
            Project project = LegacyCircuits.Migrate("four-at-once", level, FourAtOnceLevelTests.Ripple(level)).Project;

            string json = JsonUtility.ToJson(ProjectRecord.From(project));
            var problems = new List<string>();
            Project again = JsonUtility.FromJson<ProjectRecord>(json).ToProject(problems);

            Assert.IsEmpty(problems);
            Assert.AreEqual(ProjectRecord.Format, JsonUtility.FromJson<ProjectRecord>(json).format);
            CollectionAssert.AreEqual(
                BoardPrinter.Print(project).Select(p => p.Text).ToArray(),
                BoardPrinter.Print(again).Select(p => p.Text).ToArray());
        }
    }
}
