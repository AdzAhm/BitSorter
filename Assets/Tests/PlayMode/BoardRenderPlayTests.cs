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
        public void ClearTheSave()
        {
            SaveGuard.Clear();

            // A test that fixes the frame time puts it back itself, and this is for the one that
            // failed before it got there: a fixed frame time leaking into the next fixture would
            // change what every wait in it means.
            Time.captureDeltaTime = 0f;
        }

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

        /// <summary>
        /// The board tile's lines pass through the placement grid's cells.
        /// </summary>
        /// <remarks>
        /// A tiled sprite repeats from its renderer's bottom-left corner, not from its centre, and
        /// the backdrop is sized to the view -- so the pattern's phase was whatever the view's size
        /// made it. BoardBackground said the tile "stays centred on the board, so its pattern keeps
        /// lining up with the grid", and at 1920 by 1080 its lines ran 1.2 units off the cells
        /// vertically: hidden while they were half a unit apart, obvious once a look spaced them a
        /// cell apart and put a pad on every cell.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheBoardTile_LinesUpWithTheCells()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);

            // Long enough for the camera to frame the board and the backdrop to follow it.
            for (int frame = 0; frame < 5; frame++)
                yield return null;

            // By name: the backdrop shares its host with every other board renderer, so the first
            // SpriteRenderer under it is whichever happened to be built first.
            Transform host = Find<BoardBackground>().transform.Find("Board");
            SpriteRenderer board = host != null ? host.GetComponent<SpriteRenderer>() : null;
            Assert.IsNotNull(board, "sanity: the backdrop is in the scene");
            Assert.AreEqual(SpriteDrawMode.Tiled, board.drawMode, "sanity: this is the tiled backdrop");

            Vector2 tile = board.sprite.bounds.size;
            Vector2 corner = (Vector2)board.transform.position - board.size * 0.5f;

            Assert.Less(OffTheTile(corner.x, tile.x), 1e-3f,
                $"the backdrop starts tiling at x = {corner.x:F3}, which is not a whole number of " +
                $"{tile.x}-unit tiles from the board's centre, so its lines miss the cells");
            Assert.Less(OffTheTile(corner.y, tile.y), 1e-3f,
                $"the backdrop starts tiling at y = {corner.y:F3}, which is not a whole number of " +
                $"{tile.y}-unit tiles from the board's centre, so its lines miss the cells");
        }

        /// <summary>
        /// Under a look that draws bits as digits, every bit in flight wears one, upright.
        /// </summary>
        [UnityTest]
        public IEnumerator ABitInFlight_WearsItsDigit_Upright()
        {
            Assume.That(Look.Current.Bits, Is.EqualTo(BitStyle.Digit),
                "this needs a look that draws bits as digits");

            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel(Level), "the level did not load");

            for (int frame = 0; frame < 30 && !runner.FixtureNodeIds.ContainsKey("a"); frame++)
                yield return null;

            BuildTheHalfAdder(session, runner);
            session.Run();

            Transform container = Find<BitRenderer>().transform.Find("Bits");
            Sprite zero = ProceduralSprites.BitGlyph(Bit.Zero);
            Sprite one = ProceduralSprites.BitGlyph(Bit.One);
            int zeros = 0;
            int ones = 0;

            for (int frame = 0; frame < 240; frame++)
            {
                yield return null;

                foreach (Transform bit in container)
                {
                    if (!bit.gameObject.activeInHierarchy)
                        continue;

                    Sprite drawn = bit.GetComponent<SpriteRenderer>().sprite;

                    Assert.IsTrue(drawn == zero || drawn == one,
                        $"a bit in flight is drawn as {drawn.name}, not as a digit");
                    Assert.Less(Quaternion.Angle(Quaternion.identity, bit.rotation), 0.01f,
                        "a digit turned to face its wire, and a turned digit is not a digit");

                    if (drawn == zero)
                        zeros++;
                    else
                        ones++;
                }
            }

            // Half adder streams carry both values, so a renderer drawing one digit for every bit
            // would fail here rather than pass.
            Assert.Greater(zeros, 0, "no bit was ever drawn as a 0");
            Assert.Greater(ones, 0, "no bit was ever drawn as a 1");
        }

        /// <summary>
        /// A collision that will take the waiting bit too crosses that bit out; one that will not,
        /// does not.
        /// </summary>
        /// <remarks>
        /// The two warnings were amber and red and nothing else, which a red-green colour-blind
        /// player cannot tell apart. An AND fed only from A: its first 0 waits, the next 0 arrives
        /// as a matching collision that takes only the arrival, and the 1 after it arrives against
        /// the waiting 0 and takes both. Both cases have to be seen, or a mark that never showed --
        /// or always did -- would pass half of this.
        /// </remarks>
        [UnityTest]
        public IEnumerator AWaitingBitThatWillDie_IsCrossedOut_AndOnlyThen()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel(Level), "the level did not load");

            for (int frame = 0; frame < 30 && !runner.FixtureNodeIds.ContainsKey("a"); frame++)
                yield return null;

            var low = new Vector2Int(0, -1);
            Assert.IsTrue(session.TryPlaceGate(GateKind.And, low), "could not place the AND");
            int and = NodeOn(runner, low);
            Wire(session, runner.FixtureNodeIds["a"], 0, and, 0);
            Wire(session, and, 0, runner.FixtureNodeIds["carry"], 0);

            session.Run();

            // Paused, so the only ticks are the ones stepped below: a tick of the runner's own
            // landing between the renderer drawing and this checking would make the two disagree.
            runner.SetPaused(true);
            yield return null;
            and = NodeOn(runner, low);

            PortRenderer ports = Find<PortRenderer>();
            var port = new PortAddress(and, true, 0);
            bool sawOnlyTheArrival = false;
            bool sawBoth = false;

            // A port mid-flash is showing the collision that just happened, which outranks the
            // warning of the next one -- cross included. Ticks are half a second apart in play and
            // the flash is over well within one, so each step waits the flash out, on a fixed frame
            // time so the wait is the same however fast the editor is rendering.
            Time.captureDeltaTime = 1f / 60f;

            for (int tick = 0; tick < 12 && !(sawOnlyTheArrival && sawBoth); tick++)
            {
                runner.StepOneTick();
                yield return null;

                for (int frame = 0; frame < 240 && ports.IsFlashing(port); frame++)
                    yield return null;

                Assert.IsFalse(ports.IsFlashing(port), "a collision's flash never ended");
                yield return null;

                Edge edge = IncomingEdge(runner, and);
                if (edge == null || !PortState.WillCollide(edge, out bool heldBitDies))
                {
                    Assert.IsFalse(ports.IsMarkedDoomed(port), "a waiting bit is crossed out with nothing coming");
                    continue;
                }

                if (heldBitDies)
                {
                    sawBoth = true;
                    Assert.IsTrue(ports.IsMarkedDoomed(port),
                        "the waiting bit will die with the arrival and is not crossed out");
                }
                else
                {
                    sawOnlyTheArrival = true;
                    Assert.IsFalse(ports.IsMarkedDoomed(port),
                        "the waiting bit survives this collision and is crossed out anyway");
                }
            }

            Time.captureDeltaTime = 0f;

            Assert.IsTrue(sawOnlyTheArrival, "sanity: no collision that takes only the arrival was seen");
            Assert.IsTrue(sawBoth, "sanity: no collision that takes the waiting bit too was seen");
        }

        /// <summary>
        /// The bits-lost meter's pop stays inside its own row, clear of the help badge below it.
        /// </summary>
        /// <remarks>
        /// The pop scaled the whole meter from its top-right corner, so at its peak the backdrop
        /// reached sixteen pixels further down than its row -- over the top of the badge, whose row
        /// starts ten below. <see cref="UiRows"/> had it written down as a known overlap. Sampled on
        /// every frame of every pop, on a fixed frame time, and paired with proof that a pop was
        /// running while it was sampled, or a meter that never popped would pass.
        /// </remarks>
        [UnityTest]
        public IEnumerator TheBitsLostPop_StaysClearOfTheHelpBadge()
        {
            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel(Level), "the level did not load");

            for (int frame = 0; frame < 30 && !runner.FixtureNodeIds.ContainsKey("a"); frame++)
                yield return null;

            // An AND fed only from A: its first bit waits, and every one after it collides.
            var low = new Vector2Int(0, -1);
            Assert.IsTrue(session.TryPlaceGate(GateKind.And, low), "could not place the AND");
            int and = NodeOn(runner, low);
            Wire(session, runner.FixtureNodeIds["a"], 0, and, 0);
            Wire(session, and, 0, runner.FixtureNodeIds["carry"], 0);

            session.Run();
            runner.SetPaused(true);
            yield return null;

            GameObject badge = GameObject.Find("Help badge");
            Assert.IsNotNull(badge, "sanity: no help badge on screen");
            float badgeTop = TopOf(badge.GetComponent<RectTransform>());

            Time.captureDeltaTime = 1f / 60f;

            bool sawAPop = false;
            float lowest = float.PositiveInfinity;

            for (int tick = 0; tick < 8; tick++)
            {
                int before = runner.View.CorruptedCount;
                runner.StepOneTick();

                for (int frame = 0; frame < 30; frame++)
                {
                    yield return null;

                    GameObject meter = GameObject.Find("Bits lost");
                    if (meter == null)
                        continue;

                    var backdrop = meter.GetComponent<UnityEngine.UI.Image>();
                    if (runner.View.CorruptedCount > before && backdrop.color != Palette.Current.MeterBackdrop)
                        sawAPop = true;

                    lowest = Mathf.Min(lowest, BottomOf(meter.GetComponent<RectTransform>()));
                }
            }

            Time.captureDeltaTime = 0f;

            Assert.IsTrue(sawAPop, "sanity: the meter never popped, so there was nothing to measure");
            Assert.GreaterOrEqual(lowest, badgeTop,
                $"the bits-lost pop reached {badgeTop - lowest:F1}px over the top of the help badge");
        }

        private static float TopOf(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[1].y;
        }

        private static float BottomOf(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return corners[0].y;
        }

        private static Edge IncomingEdge(SimulationRunner runner, int node)
        {
            for (int id = 0; id < runner.View.EdgeCount; id++)
            {
                Edge edge = runner.View.GetEdge(id);

                if (edge != null && edge.Target.Owner.Id == node)
                    return edge;
            }

            return null;
        }

        /// <summary>
        /// Every sprite a bit can be drawn as is built with the board, not the first time one is
        /// drawn.
        /// </summary>
        /// <remarks>
        /// The first time is mid-run: the frame a run starts, the frame a gate first stalls, the
        /// frame a register first takes a bit. A held bit's disc is supersampled, and building one
        /// took 65-80 ms in the editor, so the game froze at exactly the moment the board was
        /// showing something happen.
        ///
        /// The cache outlives a test, so whatever an earlier test left is destroyed first -- the
        /// cache rebuilds a destroyed sprite rather than handing it back -- and the board's own
        /// load is what has to build them.
        /// </remarks>
        [UnityTest]
        public IEnumerator EverySpriteABitCanWear_IsBuiltWithTheBoard()
        {
            Assume.That(Look.Current.Bits, Is.EqualTo(BitStyle.Digit),
                "this needs a look that draws bits as digits");

            foreach (Sprite sprite in BitSprites())
            {
                Object.DestroyImmediate(sprite.texture);
                Object.DestroyImmediate(sprite);
            }

            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            int before = ProceduralSprites.BuiltCount;
            BitSprites();

            Assert.AreEqual(before, ProceduralSprites.BuiltCount,
                "a bit's sprite was left to be built the first time one is drawn, which is mid-run");
        }

        private static Sprite[] BitSprites() => new[]
        {
            ProceduralSprites.BitGlyph(Bit.Zero), ProceduralSprites.BitGlyph(Bit.One),
            ProceduralSprites.HeldBit(Bit.Zero), ProceduralSprites.HeldBit(Bit.One),
        };

        /// <summary>
        /// A register shows the bit it is keeping by its shape, and changes it when the bit does.
        /// </summary>
        /// <remarks>
        /// The register's bit is a machine's state -- the thing a sequential level is about -- and
        /// was a disc told apart by colour alone. The sprite is swapped where the state is noticed
        /// to change, so a register that never swapped it would hold its first digit forever: the
        /// run below watches it take a 1 after starting on a 0.
        /// </remarks>
        [UnityTest]
        public IEnumerator ARegister_ShowsTheBitItIsKeeping_ByItsShape()
        {
            Assume.That(Look.Current.Bits, Is.EqualTo(BitStyle.Digit),
                "this needs a look that draws bits as digits");

            yield return TestScene.Load();
            Find<MainMenu>().Show(false);
            yield return null;

            LevelSession session = Find<LevelSession>();
            SimulationRunner runner = Find<SimulationRunner>();

            Assert.IsTrue(session.LoadLevel("one-clock-late"), "the level did not load");

            for (int frame = 0; frame < 30 && !runner.FixtureNodeIds.ContainsKey("in"); frame++)
                yield return null;

            var middle = new Vector2Int(0, 0);
            Assert.IsTrue(session.TryPlaceGate(GateKind.Register, middle), "could not place the register");
            int register = NodeOn(runner, middle);
            Wire(session, runner.FixtureNodeIds["in"], 0, register, 0);
            Wire(session, register, 0, runner.FixtureNodeIds["out"], 0);

            yield return null;
            yield return null;

            Assert.AreSame(ProceduralSprites.HeldBit(Bit.Zero), HeldBitOf(register).sprite,
                "a register starts holding a 0, and does not show one");

            // Run rebuilds the board, so the register and its drawing are found again after it.
            session.Run();
            yield return null;
            register = NodeOn(runner, middle);

            bool HoldsOne() => runner.View.GetNode(register) is RegisterNode node && node.State == Bit.One;

            for (int frame = 0; frame < 2000 && !HoldsOne(); frame++)
                yield return null;

            Assert.IsTrue(HoldsOne(), "sanity: the register never took a 1");
            yield return null;

            Assert.AreSame(ProceduralSprites.HeldBit(Bit.One), HeldBitOf(register).sprite,
                "the register took a 1 and still shows the 0 it started with");
        }

        private static SpriteRenderer HeldBitOf(int register)
        {
            GameObject held = GameObject.Find($"Held {register}");
            Assert.IsNotNull(held, $"sanity: register {register} has no held bit drawn");
            return held.GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Two bits that meet are never drawn at the same depth, and never swap order mid-flight.
        /// </summary>
        /// <remarks>
        /// Every bit draws its dot, halo and trail at one sorting order each, shared with every
        /// other bit, and all at depth zero -- so where two bits overlapped, Unity was free to draw
        /// either one's pieces on top. It did not choose the same way every time. At the crossing in
        /// the middle of the half adder about one reference capture in three drew the two bits'
        /// trails the other way round, and forcing that one order reproduced the odd capture to the
        /// pixel. In play the same freedom is a flicker wherever bits cross.
        ///
        /// A sort key is the sorting layer, the order, then depth; the first two are the same for
        /// every bit by design, so depth is what has to differ. And it has to keep its sign for as
        /// long as both bits are in flight, or the flicker is back, one swap per crossing.
        /// </remarks>
        [UnityTest]
        public IEnumerator TwoBitsThatMeet_AreNeverDrawnAtTheSameDepth()
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
            session.Run();

            Transform container = Find<BitRenderer>().transform.Find("Bits");
            Assert.IsNotNull(container, "sanity: the bits have a container");

            var order = new Dictionary<long, bool>();
            var wasLive = new HashSet<int>();
            var isLive = new HashSet<int>();
            var bits = new List<Transform>();
            int meetings = 0;

            for (int frame = 0; frame < 240; frame++)
            {
                yield return null;

                bits.Clear();
                isLive.Clear();

                foreach (Transform child in container)
                {
                    if (!child.gameObject.activeInHierarchy)
                        continue;

                    bits.Add(child);
                    isLive.Add(child.GetInstanceID());
                }

                // A sprite that was not live last frame is carrying a new bit, so any order recorded
                // for it belonged to the bit before.
                foreach (Transform bit in bits)
                {
                    if (!wasLive.Contains(bit.GetInstanceID()))
                        Forget(order, bit.GetInstanceID());
                }

                for (int i = 0; i < bits.Count; i++)
                {
                    for (int j = i + 1; j < bits.Count; j++)
                    {
                        if (!Meet(bits[i], bits[j]))
                            continue;

                        meetings++;

                        foreach ((string piece, float a, float b) in Depths(bits[i], bits[j]))
                        {
                            Assert.AreNotEqual(a, b,
                                $"two bits meeting at {bits[i].position.x:F2}, {bits[i].position.y:F2} " +
                                $"draw their {piece}s at the same depth ({a}), so which is on top is " +
                                "Unity's choice, and it does not always make the same one");
                        }

                        // Which of the two is in front, stated for the pair's own order rather than
                        // for this frame's list, so the same answer means the same thing next frame.
                        int first = bits[i].GetInstanceID();
                        int second = bits[j].GetInstanceID();
                        long pair = Pair(first, second);
                        bool inFront = first < second
                            ? bits[i].position.z < bits[j].position.z
                            : bits[j].position.z < bits[i].position.z;

                        if (order.TryGetValue(pair, out bool was))
                            Assert.AreEqual(was, inFront, "two bits swapped order while both were in flight");
                        else
                            order[pair] = inFront;
                    }
                }

                (wasLive, isLive) = (isLive, wasLive);
            }

            Assert.Greater(meetings, 0,
                "sanity: no two bits overlapped, so nothing here was tested -- the half adder's " +
                "crossing is where they are expected to meet");
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        /// <summary>Whether two bits overlap anywhere they draw: dot, halo or trail.</summary>
        private static bool Meet(Transform a, Transform b)
        {
            Rect first = Footprint(a);
            Rect second = Footprint(b);
            return first.Overlaps(second);
        }

        /// <summary>Everything a bit draws, flattened onto the board.</summary>
        private static Rect Footprint(Transform bit)
        {
            Bounds bounds = bit.GetComponent<SpriteRenderer>().bounds;

            foreach (Renderer piece in bit.GetComponentsInChildren<Renderer>())
            {
                if (piece is TrailRenderer trail && trail.positionCount < 2)
                    continue;   // an empty trail draws nothing, wherever its bounds say it is

                bounds.Encapsulate(piece.bounds);
            }

            return Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
        }

        /// <summary>The depth each of two bits draws each piece at: dot, halo and trail.</summary>
        private static IEnumerable<(string, float, float)> Depths(Transform a, Transform b)
        {
            yield return ("dot", a.position.z, b.position.z);
            yield return ("halo", a.Find("Glow").position.z, b.Find("Glow").position.z);

            TrailRenderer first = a.GetComponentInChildren<TrailRenderer>();
            TrailRenderer second = b.GetComponentInChildren<TrailRenderer>();

            if (first.positionCount > 1 && second.positionCount > 1)
                yield return ("trail", first.GetPosition(0).z, second.GetPosition(0).z);
        }

        /// <summary>Two sprites, in either order.</summary>
        private static long Pair(int first, int second) =>
            first < second ? ((long)first << 32) | (uint)second : ((long)second << 32) | (uint)first;

        /// <summary>Drops every recorded order involving one sprite.</summary>
        private static void Forget(Dictionary<long, bool> order, int sprite)
        {
            var stale = new List<long>();

            foreach (long pair in order.Keys)
            {
                if ((int)(pair >> 32) == sprite || (int)pair == sprite)
                    stale.Add(pair);
            }

            foreach (long pair in stale)
                order.Remove(pair);
        }

        /// <summary>How far a position is from the nearest whole number of tiles.</summary>
        private static float OffTheTile(float position, float tile)
        {
            float into = Mathf.Repeat(position, tile);
            return Mathf.Min(into, tile - into);
        }

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
