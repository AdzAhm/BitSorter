using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using BitSorter.LogicCore;
using BitSorter.View;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BitSorter.PlayMode.Tests
{
    /// <summary>
    /// What the board draws while a run is moving.
    /// </summary>
    /// <remarks>
    /// The renderers pool their objects, and a pooled object carries whatever state the last user
    /// left on it. A sprite forgives that -- it is repositioned and redrawn. A TrailRenderer does
    /// not: it keeps its points, and it emits along whatever path its transform takes, including
    /// the one-frame jump from where the last bit died to where this one was born.
    /// </remarks>
    [TestFixture]
    public class BoardRenderPlayTests
    {
        private const string Level = "half-adder";

        [OneTimeSetUp]
        public void OneTimeSetup()
        {
            SaveGuard.Redirect();
            GameAnalytics.SetReporting(false);
        }

        [OneTimeTearDown]
        public void OneTimeCleanup()
        {
            SaveGuard.Release();
        }

        [TearDown]
        public void ClearTheSave() => SaveGuard.Clear();

        [UnityTearDown]
        public IEnumerator ClearTheScene()
        {
            yield return TestScene.Clear();
        }

        private static T Find<T>() where T : Object => Object.FindFirstObjectByType<T>();

        /// <summary>
        /// No bit leaves a trail that jumps across the board.
        /// </summary>
        /// <remarks>
        /// A trail is sampled as its bit moves, so consecutive points are a fraction of a cell
        /// apart. Two adjacent points further apart than a single wire mean the object was
        /// teleported while it was emitting -- which is what a recycled bit did: BitRenderer.Rent
        /// activated the pooled object and cleared its trail while it still stood where the
        /// previous bit died, and only the caller moved it afterwards. Clearing before the move
        /// does nothing about the move.
        ///
        /// Seen in play as thin diagonals across the board whenever bits reached a gate, in the
        /// colour of whichever bit had just been recycled.
        /// </remarks>
        [UnityTest]
        public IEnumerator NoBitsTrail_JumpsAcrossTheBoard()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel(Level), "the level did not load");

            for (int frame = 0; frame < 30 && !runner.FixtureNodeIds.ContainsKey("a"); frame++)
                yield return null;

            BuildTheHalfAdder(session, runner);

            // The longest a trail point may legitimately be from the one before it. A bit crosses
            // one wire per tick and a frame is a fraction of a tick, so this is generous by a wide
            // margin and still far under the width of the board.
            float longestWire = LongestWire(runner);

            session.Run();

            float worst = 0f;
            string where = null;

            // Long enough for bits to be born, consumed, and their sprites handed to new ones --
            // which is the moment the streak appears.
            for (int frame = 0; frame < 240; frame++)
            {
                yield return null;

                foreach (TrailRenderer trail in Object.FindObjectsByType<TrailRenderer>(
                             FindObjectsSortMode.None))
                {
                    float gap = LongestGap(trail);

                    if (gap > worst)
                    {
                        worst = gap;
                        where = trail.transform.parent != null ? trail.transform.parent.name : trail.name;
                    }
                }
            }

            Assert.Less(worst, longestWire,
                $"a bit's trail jumped {worst:F2} units in one step on '{where}', and the longest " +
                $"wire on this board is {longestWire:F2} -- a recycled bit is drawing a streak from " +
                "wherever the last one died");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>The biggest step between two consecutive points of a trail.</summary>
        private static float LongestGap(TrailRenderer trail)
        {
            int count = trail.positionCount;

            if (count < 2)
                return 0f;

            var points = new Vector3[count];
            trail.GetPositions(points);

            float worst = 0f;

            for (int i = 1; i < count; i++)
                worst = Mathf.Max(worst, Vector3.Distance(points[i - 1], points[i]));

            return worst;
        }

        /// <summary>How far apart the two ends of the longest wire on the board are.</summary>
        private static float LongestWire(SimulationRunner runner)
        {
            float longest = 0f;

            for (int id = 0; id < runner.View.EdgeCount; id++)
            {
                Edge edge = runner.View.GetEdge(id);

                if (edge == null)
                    continue;

                Vector2 from = PortGeometry.EndpointOf(edge.Source, runner.PositionOf(edge.Source.Owner.Id));
                Vector2 to = PortGeometry.EndpointOf(edge.Target, runner.PositionOf(edge.Target.Owner.Id));

                longest = Mathf.Max(longest, Vector2.Distance(from, to));
            }

            Assert.Greater(longest, 0f, "sanity: the board should have wires on it");
            return longest;
        }

        /// <summary>a,b into an XOR for the sum and an AND for the carry.</summary>
        private static void BuildTheHalfAdder(LevelSession session, SimulationRunner runner)
        {
            var high = new Vector2Int(0, 1);
            var low = new Vector2Int(0, -1);

            Assert.IsTrue(session.TryPlaceGate(GateKind.Xor, high), "could not place the XOR");
            Assert.IsTrue(session.TryPlaceGate(GateKind.And, low), "could not place the AND");

            int xor = NodeOn(runner, high);
            int and = NodeOn(runner, low);

            int a = runner.FixtureNodeIds["a"];
            int b = runner.FixtureNodeIds["b"];

            Wire(session, a, 0, xor, 0);
            Wire(session, b, 0, xor, 1);
            Wire(session, xor, 0, runner.FixtureNodeIds["sum"], 0);
            Wire(session, a, 0, and, 0);
            Wire(session, b, 0, and, 1);
            Wire(session, and, 0, runner.FixtureNodeIds["carry"], 0);
        }

        private static int NodeOn(SimulationRunner runner, Vector2Int cell)
        {
            for (int id = 0; id < runner.View.NodeCount; id++)
            {
                if (runner.TryCellOf(id, out Vector2Int at) && at == cell)
                    return id;
            }

            Assert.Fail($"nothing on {cell}");
            return -1;
        }

        private static void Wire(LevelSession session, int from, int fromPort, int to, int toPort)
        {
            Assert.IsTrue(
                session.TryConnect(
                    new PortAddress(from, false, fromPort),
                    new PortAddress(to, true, toPort)),
                $"could not wire {from} to {to}");
        }
    }
}
