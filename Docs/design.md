# BitSorter — design notes

How this game is built and why, for someone technical who wants the reasoning
rather than the API.

BitSorter is a 2D puzzle game about digital logic. Bits fall through gates into
bins; the player wires the circuit. It teaches boolean algebra, propagation
delay, combinational components and adders, and it is honest about the parts of
a digital-logic syllabus it cannot reach.

This document is drawn from `CLAUDE.md`, [`level-roadmap.md`](level-roadmap.md)
and the commit history. Where a decision was later reversed, it says so — those
are the more interesting entries, and pretending the first answer was right
would hide the reason the second one is better.

---

## 1. The model: tokens, not levels

A real circuit carries voltages. Every wire holds a level at every instant, and
the whole network settles continuously toward a consistent state.

BitSorter does not model that. A bit here is a **token**: a discrete object that
exists at one place at one time, travels along exactly one wire, and is consumed
when it arrives. `Bit` is `enum Bit { Zero, One }`, never a raw int, and a port's
state is `Bit?` — one field expressing zero, one, or empty.

That single choice determines almost everything downstream, including several
things the game cannot do.

**Consume semantics is the load-bearing part.** A gate whose input ports are all
filled evaluates, emits one output token, and *empties its inputs*. The values
are gone. Nothing can read them again.

### Why memory cannot emerge from gates

The classic construction for memory is a cross-coupled NOR latch: two NOR gates,
each taking the other's output. In a voltage model it works because both gates
hold their outputs continuously and each can read the other whenever it likes.

Under consume semantics it fails twice, and neither failure is a bug to fix:

1. **It deadlocks at startup.** Each NOR needs both inputs filled before it may
   fire. Each is waiting on the other's first output. Neither ever produces one.
2. **It stalls after one firing.** Suppose you seed it. Each gate consumes its
   inputs and emits once — and the external input port, having been consumed, is
   never refilled. The loop runs dry.

So memory is not something a clever player can discover here. It has to be a
primitive: `RegisterNode`, which emits the bit it holds and stores the one it
just consumed, plus edges that begin with bits already in transit to break the
startup deadlock. Together those give full synchronous sequential power with no
change to the tick loop at all.

**`RegisterNode` is specified and not built.** Flip-flops and FSMs are blocked on
it, plus a way to author a seeded edge and a seventh `GateKind` for the palette.
The roadmap classifies them as *blocked on a missing primitive*, not as
impossible — the distinction matters, because one means "ordinary work" and the
other means "stop looking".

---

## 2. Time: the tick

Time is an integer. There is no continuous clock anywhere in the model.

Each tick runs in phases: advance everything in transit, deliver whatever has
arrived, then evaluate every node whose input ports are all filled.

### Edge delay must be ≥ 1

`Edge`'s constructor throws `ArgumentOutOfRangeException` for anything less. This
is not defensive coding; it is the invariant the whole simulator rests on.

A zero-delay edge would let one node observe another node's output *within the
same tick*. The moment that is possible, "evaluate every ready node" stops being
well defined — you would have to evaluate in topological order, and a cyclic
graph has none. Wiring rules deliberately permit cycles and self-loops, so
topological order is not available even in principle.

The floor of 1 also gives feedback loops a real time step to resolve in. Note
that this is *not* what makes latches work — see above; latches need
`RegisterNode`, not a delay trick.

### Order-independence, and what it buys

**Nodes within a single tick must be evaluatable in any order with the same
outcome.** This is a hard invariant, and it is worth being explicit about what it
purchases, because it costs real expressiveness.

It buys three things:

- **Determinism that does not depend on construction order.** Node ids come from
  `Add` call order. Without order-independence, a level that built its gates in a
  different sequence would simulate differently, and a player's circuit would
  behave differently depending on the order they happened to draw it in.
- **A grader that can use the real simulation.** Grading runs the same
  `Simulation` the player just watched. The alternative — a separate oracle —
  would have to reimplement consume semantics, collisions and delay arithmetic,
  and any divergence between the two is a bug the player experiences as *the game
  lying to them*.
- **Freedom to iterate however is convenient.** The tick loop walks nodes by
  index. It never has to sort, and adding a node type cannot break the ordering
  because there is no ordering to break.

The collision rules are written to preserve it. When two bits arrive at one
occupied port:

- **Matching values** — the port keeps its value, the arrival is destroyed.
  `CorruptedCount` += 1.
- **Differing values** — the result is ambiguous, so *neither* survives. The port
  is cleared and stays **poisoned for the rest of that tick's delivery phase**, so
  a later arrival in the same tick cannot refill it. `CorruptedCount` += 2.

That poisoning is the order-independence rule doing visible work. Without it, a
third bit arriving in the same tick would land in a port that had just been
cleared, and the outcome would depend on delivery order. With it, a mixed-value
collision never leaves a survivor, whatever order the deliveries happened in.

**Collisions never throw.** They are a game mechanic, not an error — the central
one, in fact. `CorruptedCount` counts *destroyed bits, not collision events*,
which is why the counter climbs 2, 4, 6, 8 on a mis-timed circuit rather than
1, 2, 3, 4. That cadence is itself the diagnostic: it tells the player the two
arrivals disagreed.

### Sources have no backpressure

A `SourceNode` has no input ports. The rule "fire when all inputs are filled" is
therefore vacuously true, so it fires **every tick from tick 0** with no special
casing anywhere in the loop.

This has a consequence worth stating plainly, because it drives the game's core
difficulty: **a stalled gate cannot throttle its upstream.** Sources keep emitting
into a circuit that is not keeping up. The next bit arrives at a port still
holding the last one, and the collision destroys it.

So an unbalanced circuit does not run *slower*. It loses bits.

---

## 3. Two assemblies, one direction

- `Assets/Scripts/LogicCore/` — pure C#, its own asmdef, **`noEngineReferences`**.
  No `UnityEngine` import anywhere.
- `Assets/Scripts/View/` — MonoBehaviours. Reads simulation state, renders it.

State flows `LogicCore → View` only. The view never mutates the simulation.

### Why LogicCore has no engine reference

Four reasons, in roughly descending order of how much they actually mattered:

1. **Tests run without a scene.** Every gate's truth table, every collision case,
   every delay calculation is an Edit Mode test against plain objects. No
   GameObject, no play mode, no domain reload. This is why the suite is large and
   fast enough to run before every commit.
2. **The compiler enforces the architecture.** The rule "the view never mutates
   the sim" would be a convention that erodes. `noEngineReferences` makes the
   reverse dependency *fail to compile*, which is a much stronger guarantee than
   a paragraph in a document.
3. **Geometry stays out of the model.** LogicCore has no opinion about board size
   because it has no geometry at all. Layout is a `Dictionary` keyed by
   `Node.Id`, held in the view. The node id is the only thing the two sides share.
4. **It keeps the simulator honest about what it is.** A tick-based token
   simulator does not need `Vector2`, and having it available invites decisions
   that quietly assume a renderer.

Point 3 paid off in an unexpected place. Carry-lookahead was originally filed as
*not expressible in this model*. That was wrong — the constraint is board size,
which is a `[SerializeField]` on `PlacementGrid`, entirely in the view. The
model never had an opinion. Reclassifying it moved a topic from "stop looking" to
"a focused half-day", which is the whole reason the boundary is worth keeping
sharp.

---

## 4. The view interpolates a discrete simulation

The simulator moves bits only on whole ticks. A bit on a delay-1 edge is at its
source, and then it is at its target; there is no in-between state in the model.

Rendered literally, that looks broken. Bits would teleport, or worse — a bit on a
delay-1 edge would report the same progress for its entire life and sit
motionless on its source.

So `SimulationRunner` accumulates `Time.deltaTime`, ticks on an interval, and
exposes **`TickProgress`**: how far into the current tick the wall clock has got.
Renderers lerp along the wire using it. The simulation stays discrete and
deterministic; only the presentation is continuous.

Two details in `BitRenderer` are worth recording because they are not obvious:

- **Sprites are keyed by `(edge id, ticks remaining)`**, which is constant within
  a tick. On a miss, the lookup also tries `ticks remaining + 1` — the same bit
  one tick earlier. Without that fallback a bit is handed a fresh sprite every
  tick and cannot be animated across its journey.
- **Nothing allocates per frame.** The sim is polled by index, the two key
  dictionaries are swapped rather than rebuilt, and sprites come from a pool.

**Physics is decoration only.** There is no `Rigidbody2D` on a bit, ever. Sparks
and debris are physics; anything that affects a result is the simulation.

This split is also why the game can be paused, single-stepped, and rewound to
tick 0 on every edit without any of the renderers needing to know.

---

## 5. The level format

Levels are JSON in `Assets/Resources/Levels/`. A level pins its sources and sinks
to cells, budgets the gates the player may place, and states what each sink must
receive.

The format is deliberately small. What it can say:

| Field | Meaning |
|---|---|
| `fixtures` | Sources and sinks the player can neither move nor delete |
| `budget` | An **allow-list** of gates, with counts |
| `expected` | Per sink, the value sequence it must receive |
| `tickLimit` | When to give up on a circuit that never settles |
| `maxWireDelay` | Ceiling on a single wire's delay |
| `delayBudget` | Total ticks above the default, across all wires |
| `maxLatency` | Critical-path ceiling, graded last |
| `order` | Place in the run |
| `goal` / `hint` | What to do, and a nudge |

### Two decisions inside that table

**`budget` is an allow-list, not a cap on an open palette.** A gate kind absent
from the budget cannot be placed at all. The loader actively refuses `count: 0`
with *"omit the kind entirely to forbid it"* — because two ways to say the same
thing is one way to be inconsistent. This is what makes a NAND-only level a
one-line change rather than a feature.

**`goal` may name gates outright; `hint` may not.** These were one field
originally, and it had two incompatible jobs: state the objective plainly, and
nudge without giving the answer. The half adder is the evidence — its hint ended
up naming both its gates *and* saying which output each produced, which left
nothing to work out. Splitting them let the no-giveaway rules in `CurriculumTests`
apply to the hint alone.

### What the format deliberately cannot express

- **No authored wires.** There is no fixture-wire array. Every wire on the board
  is the player's, which means every timing hazard is one they created and can
  therefore undo. A level can constrain the delay budget; it cannot hand the
  player a pre-broken circuit to repair.
- **No seeded edges**, which is one of the three things blocking flip-flops.
- **Sinks have exactly one input port.**
- **Sources are dense.** One bit per tick, no gaps. The loader refuses `-` in a
  source stream, because `SourceNode` has no way to skip a tick.
- **Vectors are global.** Every source stream is the same length, and that length
  is the vector count. A level cannot drop a vector for one sink alone — which is
  precisely the narrow case the `x` expectation character exists for.

### The arithmetic every level design uses

Define a node's **level** as the tick it evaluates vector *v*, minus *v*.

- A source is level 0.
- A node fed from a level-*L* node down a wire of delay *d* evaluates at *L + d*.
- **Every input of a gate must arrive at the same level.** Otherwise the early bit
  waits in its port and the next vector's bit collides with it.
- A sink's level is its latency, which is what `maxLatency` grades.

A plain wire costs one level. `delayBudget` counts only ticks *above* that
default, so drawing a wire never quietly costs budget and lowering one refunds
immediately.

### Rules versus grading

Two components, deliberately not one:

- **`LevelRules`** answers *"may this edit happen"* and simulates nothing.
- **`LevelGrader`** answers *"did it work"* and simulates everything.

Grading is: the run settled within the tick limit, `CorruptedCount == 0`, every
sink's value sequence matches, and *then* latency if the level sets a ceiling.
Latency is checked **last** on purpose — a circuit that is both wrong and slow
should hear that it is wrong first, because shortening a path that computes the
wrong answer is wasted work.

Latency is **observed, not derived from a graph walk.** `SinkNode.Reception`
records the tick each bit was consumed; sources emit vector *v* on tick *v*, so
latency is consumption tick minus vector. Measured, not modelled — which matters
because summing edge delays along the longest path is undefined on a cyclic
circuit, and cycles are legal here.

### The blueprint is the authority

`CircuitBlueprint` is the single authority on the circuit, addressed **by cell
rather than by node id**. The `Simulation` is a derived artifact, rebuilt from the
blueprint on Run, on Reset, and after every single edit.

So Reset captures nothing. It is the same rebuild call that Run makes, differing
only in the state it lands in.

The alternative — snapshotting the `Simulation` — was rejected early and has
stayed rejected, because it would mean every future node type, `RegisterNode`
above all, had to implement deep-copy correctly forever.

That decision paid a dividend years earlier than expected when undo was added.
A blueprint is two lists of readonly structs, so a snapshot is a pair of list
copies with nothing shared and nothing to alias — correct by construction rather
than by argument. Undo is therefore a stack of whole-board snapshots, not a stack
of inverse operations, and undoing "remove a gate" restores the wires that came
with it without anything having to know that removal takes wires.

---

## 6. The honest limits

Three things this game cannot teach. Each has a substitute lesson that is
genuinely close, and reaching for the substitute is the rule rather than a
consolation.

### Hazards are not expressible

Static and dynamic hazards are glitches: a signal briefly taking a value it
should not while a network settles. That needs a continuous signal model. Bits
here are discrete tokens, so a glitch has nowhere to live — there is no instant
between two states for it to occupy.

**The substitute is unbalanced-path corruption.** Two paths of different total
delay into one gate: the early bit waits in its port, and the next arrival
collides with it. It is the same *shape* of fault — a timing mismatch a purely
logical reading of the circuit will not reveal — arrived at from the failure side.

### Clock-edge phenomena are not expressible

Setup time, hold time, clock skew, and the level-triggered versus edge-triggered
distinction all need the clock to be a **signal**: something with its own edges,
its own arrival time, and a relationship to the data that can be violated.

Here the tick *is* the clock. It is global, it is exact, and it is not a wire.
Nothing can arrive late relative to it, so nothing can violate it. Once
`RegisterNode` exists, a latch and a flip-flop will be the same object.

**The substitute is latency budgeting.** The reason setup time matters is that a
stage's logic must finish before the next capture; here that becomes the concrete
and gradeable question of whether a path fits within an allowed number of ticks,
which is exactly `maxLatency`.

### Throughput is not a meaningful score

This one was **reasoned wrong the first time**, and the correction is the
interesting part.

The original claim was that a circuit sustains one vector per tick whatever its
depth, so depth buys latency rather than throughput. True — but only for
*balanced* circuits, and the missing half changes the conclusion.

An unbalanced circuit does not sustain a lower rate. There is no backpressure, so
it does not degrade gracefully at all: it loses bits and fails as `Corrupted`.

So throughput has exactly two states — one vector per tick, or a failed run — with
nothing in between to rank. A score axis needs a range of outcomes, and across
*correct* solutions throughput does not vary at all. **Gate count and latency
both do, so those are what get scored.**

Getting this right strengthened the conclusion rather than weakening it, which is
usually the sign that the correction was worth making.

The same reasoning kills classical pipelining as a topic. Every gate consumes its
inputs, emits, and hands the result to an edge of at least one tick — so every
gate is already a register and every circuit is already pipelined at gate
granularity. There is no un-pipelined circuit to trade away from.

**The substitute is stage balancing**, which is the game's core mechanic: a
pipeline only works if every path into a stage has equal latency. Here a stage fed
by unequal paths does not produce a stale answer, it destroys bits. Same
requirement, approached from the failure side.

### The line on scoring

A number describing the player's **own** circuit is fine, including one kept
between sessions — gate count and latency on the win panel, personal bests beside
each level. Those are facts about what they built, measured against what they
built last time.

Anything ranking a player against **other people or an authored ideal** — stars,
par scores, leaderboards, percentiles, grades — is deliberately absent. That turns
"here is your circuit" into "here is how you compare", which is a different game.

---

## 7. Decisions that were reversed

The entries above that changed are collected here with the reason, because the
reason is the part worth keeping.

**Play order was the ordinal sort of file names.** That put `balance-the-paths`
ahead of the tutorial and would have scattered the planned levels across the
syllabus. Each file now names its own `order` and `LevelCatalog` refuses two
levels claiming the same seat. Authored in tens, so a level can be inserted
without renumbering. *(`582c83f`)*

**Then the order was wrong anyway.** `balance-the-paths` moved from 50 to 25,
because `four-corners` and `nothing-but-nand` both budget delay and both sat
*ahead* of the level that teaches re-timing — handing the player a circuit that
cannot work without a tool the game had not introduced. `CurriculumTests` now
enforces the general rule: a mechanic is taught before it is required.
*(`1855476`)*

**The hint field was doing two jobs**, so `goal` was added and the half adder's
hint was rewritten to stop naming both its gates. *(`cf92777`)*

**Don't-care outputs were first answered "not expressible".** The tempting fix was
to reuse `-`, which was already legal — but `-` means *no bit arrives at all*, and
the loader **omits** it from the compacted expectation list, so it consumes no
slot. A K-map don't-care is the opposite shape: a bit *does* arrive and either
value is acceptable, which needs a slot. They could not be the same character, so
`x` was added. *(`34722dd`)*

**Unbalanced path delays had two candidate fixes**: a locked `wires` array in the
level JSON, or player-chosen wire delays. The second landed. Delay became a
resource the player manages — scrolled on the wire itself, floored at 1 by
`LevelRules.CanSetDelay`, bounded per-level by `maxWireDelay` and `delayBudget`.
The road not taken is still not built, which is why no level can author an
unbalanced path of its own. *(`2925472`)*

**`PointerOverUi` used `EventSystem.IsPointerOverGameObject()`.** The no-argument
overload reports whichever pointer id the input module touched most recently, from
the module's own update rather than from now — so the answer depends on component
execution order and can lag a frame or describe a different pointer entirely. For
a method deciding whether a click reaches the board, being wrong for one frame
means a click doing two things at once, which is the exact bug the component
exists to prevent. It now raycasts explicitly. *(`eb1a43d`)*

**Pointer arbitration was nearly a claim/release protocol**, and that was rejected
before it was written. A claim protocol has one catastrophic failure — a claim
never released disables input silently, with no error and no way for the player to
recover. Deriving the owner from facts that are already true each frame makes that
state *unrepresentable*: when a drag ends for any reason, including ones nobody
anticipated, the fact goes false and the owner is `None` next frame. Unknown owners
**fail open**, because a wrong extra click is a bug you can see and report and an
unresponsive board is not. *(`966517e`)*

**The IMGUI hud was deleted.** Its own doc comment had said that when it stopped
being a debug readout it should become a canvas, and it did. Buttons were
impossible before that: `GUI.Button` does not consume Input System mouse events,
so a Run button would fire *and* let the same click reach the board. `PointerGate`
is what made real buttons possible. *(`996f32c`)*

**A robot cursor shipped and was removed entirely** — component, generation code
and scene wiring — after playtesting. *(`3e7f714` → `cd62ab7`)*

**`IsIdle` required every input port to be empty.** With unequal source rates the
faster stream runs out first and strands the slower stream's last bit in a port
whose partner never arrives, so the check would never fire again and the demo
froze. It now tests only that nothing is in flight and no source has anything
left — a node with every port filled would already have fired, so if nothing is
moving, nothing can become ready. *(`f8bd10c`)*

**Carry-lookahead was filed as not expressible** and was reclassified as blocked
on board size. It had framed a tooling gap as a property of the model.
*(`becb5d4`)*

---

## 8. How it is tested

- **Every LogicCore component ships with a unit test carrying its truth table.**
  No component without one.
- **Bug fixes land as two commits**: a failing test that reproduces the bug, then
  the fix. The red commit must still *compile* — written against the existing API
  so it fails on an assertion rather than a missing symbol, otherwise it is
  useless to bisect and leaves the editor broken for anyone who lands on it.
- **Levels are tested as designs, not just as files.** Each shipped level has a
  test that builds every intended solution and asserts it passes, *plus* a test
  that the designed mistake fails the specific way the design claims —
  `Corrupted`, not `WrongOutput`. That is what stops a level shipping that is
  accidentally single-solution, or whose intended mistake turns out to be
  unreachable.
- **`CurriculumTests` checks the run as a whole**: that hints give nothing away,
  that every level states a goal, and that a mechanic is taught before it is
  required.
- **Edit Mode cannot see the input layer at all**, because it never calls `Awake`
  and never runs `Update`. A pointer-arbitration bug once passed 353 Edit Mode
  tests while being plainly broken in play, which is why a PlayMode assembly
  exists for exactly the interactions that need a real mouse, a real canvas and a
  running frame loop.

**Unrelated changes go in their own commits.** `5a70be7` is the local example of
what not to do: it landed level-switch tests, the fix they covered, and a scene
re-serialisation together, and the result cannot be reviewed, reverted or
bisected.

---

## 9. Things worth knowing before changing something

- **`NodeCount` and `EdgeCount` are id bounds, not populations.** Removal leaves a
  tombstone: the slot becomes null and the id is retired, never reissued, so every
  surviving id keeps meaning the same node. Use `LiveNodeCount` / `LiveEdgeCount`
  for the population, and null-check anything `GetNode` / `GetEdge` returns. A
  removed node reports `Id -1`, so capture an id *before* removing rather than
  reading it back off the object.
- **Bits lost to a removal are not corruption** and must never touch
  `CorruptedCount`. An edit is not a collision.
- **Zero and unlimited are opposites that both look falsy.** Unlimited is spelled
  `-1`, the way `RemainingDelay` spells an absent budget. Test `== 0` for "not
  stocked", never `<= 0`.
- **`HalfAdderDemoSceneBuilder` is the only authority on scene contents.**
  Anything added by hand is wiped by `BitSorter/Build Play Scene`. After any change
  that touches the scene, verify the saved scene *file* — serialized references can
  be `{fileID: 0}` even when the setup code looks correct, and a scene that opens
  fine on one machine can be broken for a fresh clone.
- **`Editor.log` accumulates across sessions.** A warning found in it may describe
  code that has since changed. Reading history as present tense once produced a
  finding that two `GridPulse` fields were dead when a later commit had started
  using them; deleting them would have broken the grid pulse.
- **Anything shown to the player is derived, never restated.** The truth table
  comes from the level's own streams and expectations; node labels come from
  `Node.Name`. A second copy of a fact is a second thing to drift.
