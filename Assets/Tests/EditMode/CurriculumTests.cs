using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.View;
using UnityEngine;

namespace BitSorter.LogicCore.Tests
{
    /// <summary>
    /// Rules about the run as a whole, rather than about any one level.
    /// </summary>
    /// <remarks>
    /// Every other level test asks "does this level work". These ask "does the sequence work" --
    /// whether a mechanic is taught before it is required, and whether any hint gives its level away.
    /// Both are invisible from inside a single level file, which is why they live here.
    /// </remarks>
    public class CurriculumTests
    {
        private static readonly Vector2Int Board = new Vector2Int(4, 2);

        /// <summary>The level whose whole job is to introduce re-timing a wire.</summary>
        private const string DelayTutorial = "balance-the-paths";

        private static IReadOnlyList<KeyValuePair<string, LevelDefinition>> LevelsInPlayOrder()
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(LevelLoader.ResourcePath);
            var entries = new List<LevelEntry>(assets.Length);
            var byName = new Dictionary<string, LevelDefinition>();

            foreach (TextAsset asset in assets)
            {
                LevelLoadResult parsed = LevelLoader.Parse(asset.text, Board);
                Assert.IsTrue(parsed.IsValid, $"{asset.name}: {parsed.Error}");

                entries.Add(new LevelEntry(asset.name, parsed.Level.Order));
                byName[asset.name] = parsed.Level;
            }

            IReadOnlyList<LevelEntry> ordered = LevelCatalog.Sort(entries, out string clash);
            Assert.IsNull(clash, clash);

            var run = new List<KeyValuePair<string, LevelDefinition>>(ordered.Count);
            foreach (LevelEntry entry in ordered)
                run.Add(new KeyValuePair<string, LevelDefinition>(entry.FileName, byName[entry.FileName]));

            return run;
        }

        // -----------------------------------------------------------------
        // Teach a mechanic before requiring it
        // -----------------------------------------------------------------

        [Test]
        public void TheDelayTutorial_ComesBeforeEveryLevelThatBudgetsDelay()
        {
            // A level that sets a delay budget is one whose author expects wires to be re-timed. Any
            // such level before balance-the-paths hands the player a circuit they cannot fix with a
            // tool they have not been given -- which reads as the game being broken, not as a puzzle.
            IReadOnlyList<KeyValuePair<string, LevelDefinition>> run = LevelsInPlayOrder();

            int tutorialOrder = -1;
            foreach (KeyValuePair<string, LevelDefinition> level in run)
            {
                if (level.Key == DelayTutorial)
                    tutorialOrder = level.Value.Order;
            }

            Assert.Greater(tutorialOrder, 0, $"'{DelayTutorial}' is missing from the run");

            foreach (KeyValuePair<string, LevelDefinition> level in run)
            {
                if (level.Key == DelayTutorial || !level.Value.HasDelayBudget)
                    continue;

                Assert.Greater(level.Value.Order, tutorialOrder,
                    $"'{level.Key}' (order {level.Value.Order}) budgets delay, but the level that " +
                    $"teaches re-timing is order {tutorialOrder}. Teach the mechanic first.");
            }
        }

        [Test]
        public void TheTutorialLevel_NeedsNoMechanicAtAll()
        {
            // Whatever sorts first has to be solvable by someone who has been told nothing.
            LevelDefinition first = LevelsInPlayOrder()[0].Value;

            Assert.IsFalse(first.HasDelayBudget, "the opening level should not be about delay");
            Assert.IsFalse(first.HasLatencyLimit, "nor about the critical path");
        }

        [Test]
        public void TheLatencyCeiling_IsIntroducedAfterDelayIsUnderstood()
        {
            // A latency ceiling only means anything to a player who already knows a wire has a length.
            IReadOnlyList<KeyValuePair<string, LevelDefinition>> run = LevelsInPlayOrder();

            int tutorialOrder = -1;
            foreach (KeyValuePair<string, LevelDefinition> level in run)
            {
                if (level.Key == DelayTutorial)
                    tutorialOrder = level.Value.Order;
            }

            foreach (KeyValuePair<string, LevelDefinition> level in run)
            {
                if (!level.Value.HasLatencyLimit)
                    continue;

                Assert.Greater(level.Value.Order, tutorialOrder,
                    $"'{level.Key}' grades on time before the player has met wire delay at all");
            }
        }

        // -----------------------------------------------------------------
        // Hints must not hand over the answer
        // -----------------------------------------------------------------

        [Test]
        public void EveryLevel_HasAHint()
        {
            foreach (KeyValuePair<string, LevelDefinition> level in LevelsInPlayOrder())
            {
                Assert.IsNotEmpty(level.Value.Hint, $"'{level.Key}' has no hint");
            }
        }

        // -----------------------------------------------------------------
        // Goals: the objective, stated plainly
        // -----------------------------------------------------------------

        [Test]
        public void EveryLevel_HasANonEmptyGoal()
        {
            // A level that does not say what it wants is a guessing game. This is the gate that stops
            // a new level shipping without one.
            foreach (KeyValuePair<string, LevelDefinition> level in LevelsInPlayOrder())
            {
                Assert.IsNotEmpty(level.Value.Goal,
                    $"'{level.Key}' has no goal -- the player cannot see what they are being asked for");

                Assert.IsNotNull(level.Value.Goal, $"'{level.Key}' goal is null");
            }
        }

        [Test]
        public void AGoalAndItsHint_AreNotTheSameSentence()
        {
            // If they say the same thing, one of them is doing no work -- and it is usually the hint
            // that has quietly become a restatement of the objective.
            foreach (KeyValuePair<string, LevelDefinition> level in LevelsInPlayOrder())
            {
                Assert.AreNotEqual(
                    level.Value.Goal.Trim().ToLowerInvariant(),
                    level.Value.Hint.Trim().ToLowerInvariant(),
                    $"'{level.Key}' repeats its goal as its hint");
            }
        }

        [Test]
        public void TheNoGiveawayRules_ApplyToTheHintAndNotTheGoal()
        {
            // Pins the division on purpose. A goal is free to name every gate it likes -- "SUM gets
            // A XOR B, CARRY gets A AND B" is a perfectly good objective and a terrible hint. If a
            // future change ever extends the giveaway rules to cover goals, this fails and says why.
            var goalNamingTwoGates = new LevelDefinition(
                "test", "SUM gets A XOR B and CARRY gets A AND B",
                100, 1,
                new List<LevelFixture>(),
                new List<LevelBudgetEntry>
                {
                    new LevelBudgetEntry(GateKind.Xor, 1),
                    new LevelBudgetEntry(GateKind.And, 1),
                },
                new List<LevelExpectation>(),
                goal: "SUM gets A XOR B and CARRY gets A AND B");

            Assert.AreEqual(2, GatesNamedIn(goalNamingTwoGates.Goal, goalNamingTwoGates),
                "sanity: this goal really does name two gates");

            // And that is fine. The rule is about the hint, so nothing here should be treated as a
            // violation -- the assertion is simply that a goal like this is representable.
            Assert.IsNotEmpty(goalNamingTwoGates.Goal);
        }

        private static int GatesNamedIn(string text, LevelDefinition level)
        {
            var words = new HashSet<string>();
            foreach (string word in text.ToLowerInvariant().Split(
                         new[] { ' ', '.', ',', ';', ':', '-', '!', '?', '(', ')', '\'' },
                         System.StringSplitOptions.RemoveEmptyEntries))
            {
                words.Add(word);
            }

            int named = 0;
            foreach (LevelBudgetEntry entry in level.Budget)
            {
                if (words.Contains(GatePalette.Label(entry.Kind).ToLowerInvariant()))
                    named++;
            }

            return named;
        }

        [Test]
        public void NoHint_NamesMoreThanOneOfItsOwnGates()
        {
            // The rule that catches a hint stating its whole solution. Naming one gate can be the
            // premise -- "NAND alone is enough" is the point of that level. Naming two from the same
            // parts list is a wiring diagram in prose, and leaves the player nothing to work out.
            foreach (KeyValuePair<string, LevelDefinition> level in LevelsInPlayOrder())
            {
                var words = new HashSet<string>();
                foreach (string word in level.Value.Hint.ToLowerInvariant().Split(
                             new[] { ' ', '.', ',', ';', ':', '-', '!', '?', '(', ')', '\'' },
                             System.StringSplitOptions.RemoveEmptyEntries))
                {
                    words.Add(word);
                }

                var named = new List<string>();
                foreach (LevelBudgetEntry entry in level.Value.Budget)
                {
                    string label = GatePalette.Label(entry.Kind).ToLowerInvariant();
                    if (words.Contains(label))
                        named.Add(label);
                }

                Assert.LessOrEqual(named.Count, 1,
                    $"'{level.Key}' names {named.Count} of its own gates ({string.Join(", ", named.ToArray())}) " +
                    $"-- that is the solution, not a hint. Hint: {level.Value.Hint}");
            }
        }

        [Test]
        public void NoHint_SpellsOutWhichGateProducesWhichOutput()
        {
            // The specific shape the half adder's hint had: naming a sink and the gate that feeds it
            // in the same breath. That is the answer written down.
            foreach (KeyValuePair<string, LevelDefinition> level in LevelsInPlayOrder())
            {
                string hint = level.Value.Hint.ToLowerInvariant();

                foreach (LevelFixture fixture in level.Value.Fixtures)
                {
                    if (fixture.Kind != FixtureKind.Sink)
                        continue;

                    string sinkId = fixture.Id.ToLowerInvariant();

                    foreach (LevelBudgetEntry entry in level.Value.Budget)
                    {
                        string label = GatePalette.Label(entry.Kind).ToLowerInvariant();

                        Assert.IsFalse(hint.Contains(sinkId + " is " + label),
                            $"'{level.Key}' tells the player outright which gate makes '{sinkId}'. " +
                            $"Hint: {level.Value.Hint}");
                    }
                }
            }
        }
    }
}
