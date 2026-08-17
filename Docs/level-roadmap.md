# Level roadmap

Which syllabus topics this game can teach, which are waiting on a missing
primitive, and which the token-passing model cannot hold at all.

Design notes. Nothing here is a backlog and nothing here gets built until it is
asked for by name, in line with the "Not yet" and "Level ideas" rules in
CLAUDE.md.

> **Topic list is inferred.** The course syllabus was not available when this was
> written, so the topics below come from CLAUDE.md's own "In scope" line, which
> matches "Boolean algebra through pipelining". The grouping into lectures 2–7 is
> a guess; the topic set is not. Re-cut the groupings against the real syllabus.

---

## What the simulator can express

Every level below is designed against these. They are facts about the code as it
stands, not preferences.

| Constraint | Where it comes from |
|---|---|
| Board is 9 × 5 cells (halfExtents 4, 2), one node per cell | [PlacementGrid.cs:24-27](../Assets/Scripts/View/PlacementGrid.cs#L24-L27) |
| Sources emit **one bit per tick from tick 0, densely**. No gaps, ever | [SourceNode.cs:24-31](../Assets/Scripts/LogicCore/SourceNode.cs#L24-L31), and the loader refuses `-` in a stream: [LevelLoader.cs:425-428](../Assets/Scripts/View/Level/LevelLoader.cs#L425-L428) |
| Every source stream is the same length; that length is the vector count | [LevelLoader.cs:181-188](../Assets/Scripts/View/Level/LevelLoader.cs#L181-L188) |
| Gates: NOT (1 input), AND / OR / XOR / NAND / NOR (2 inputs). One output each | [GatePalette.cs:7-15](../Assets/Scripts/View/GatePalette.cs#L7-L15) |
| Sinks always have exactly one input port | [CircuitBuilder.cs:117-120](../Assets/Scripts/View/Level/CircuitBuilder.cs#L117-L120) |
| Edge delay ≥ 1. Player wires default to 1, adjustable up to `maxWireDelay` | [Edge.cs:67-70](../Assets/Scripts/LogicCore/Edge.cs#L67-L70), [LevelRules.cs:199-237](../Assets/Scripts/View/Level/LevelRules.cs#L199-L237) |
| Fan-out from one output is unlimited. Fan-in to one input is legal, and is how bits get destroyed | [WiringRules.cs:60-73](../Assets/Scripts/View/WiringRules.cs#L60-L73) |
| Cycles and self-loops are legal; the tick limit is what stops them | [WiringRules.cs:66-69](../Assets/Scripts/View/WiringRules.cs#L66-L69) |
| Grading checks: settled, `CorruptedCount == 0`, each sink's value sequence, then latency if the level sets `maxLatency` | [LevelGrader.cs](../Assets/Scripts/View/Level/LevelGrader.cs) |
| Arrival ticks are ignored unless the level sets `maxLatency`, so a correct-but-scenic route passes by default | [LevelGrader.cs](../Assets/Scripts/View/Level/LevelGrader.cs) |
| An expectation character is `0`, `1`, `x` (any value, bit still required) or `-` (no bit). A stream is `0`/`1` only | [LevelLoader.cs](../Assets/Scripts/View/Level/LevelLoader.cs) |
| Every sink must be named by an expectation, even an empty one | [LevelLoader.cs:218-231](../Assets/Scripts/View/Level/LevelLoader.cs#L218-L231) |

### The two derived facts that shape every level

**The source rate is one vector per tick, and the circuit has to keep up.**
Sources stream back to back with no gap, and nothing can slow them down — a source
has no input ports, so it fires every tick regardless of what is stalled
downstream. A path-balanced circuit sustains that indefinitely; an unbalanced one
does not fall behind, it loses bits. The early bit sits in its port, and the *next*
vector's bit collides with it.

**Therefore any level meant to teach balancing needs at least two vectors, and
really four or more.** With a single vector an unbalanced circuit merely fires
late and still passes — which is why `route-the-bit` (1 vector) cannot corrupt
and `balance-the-paths` (4 vectors) can. This is the single easiest way to
author a level that silently fails to teach its own lesson.

---

## Classification

| # | Topic | Class | Note |
|---|---|---|---|
| 1 | Boolean algebra, De Morgan | **(a)** | Buildable today |
| 2 | K-map minimisation, fully specified | **(a)** | Buildable today |
| 2b | K-map minimisation with don't-cares | **(a)** | **Shipped.** `x` in an expectation — see [Q1](#q1-can-a-test-vector-express-a-dont-care-output) |
| 3 | Functional completeness (NAND-only / NOR-only) | **(a)** | Buildable today, no schema change — see [Q3](#q3-can-a-level-require-a-specific-gate-set-only) |
| 4 | Propagation delay | **(a)** | Shipped: `balance-the-paths` |
| 5 | Combinational components (mux, decoder, comparator) | **(a)** | Buildable today |
| 6 | Adders: half, full, ripple-carry | **(a)** | Half shipped; full verified by `FullAdderTests`. Ripple-carry is board-space limited |
| 6b | Carry-lookahead | **(b)** | Per-level board size. Reclassified from (c) — the limit is the view layer, not the model |
| 7 | Latches and flip-flops | **(b)** | `RegisterNode` + initial-state authoring + a palette slot |
| 7b | Level- vs edge-triggering, setup/hold, clock skew | **(c)** | No clock signal exists — see below |
| 8 | FSMs (Moore / Mealy), state minimisation | **(b)** | Same three blockers as 7 |
| 9 | Critical path | **(a)** | **Shipped.** `maxLatency` — see [Q2](#q2-can-a-level-score-on-critical-path-length) |
| 10 | Pipelining (latency vs throughput) | **(c)** | Every gate is already a register, and throughput is binary rather than graded — see below |
| 10b | Pipeline stage balancing | **(a)** | The substitute lesson, and the game's core mechanic |

---

## (a) Buildable now

### 1. Boolean algebra — "The Long Way Round"

The player is asked for `NOT (A AND B)` on a board that stocks no NAND, so both
De Morgan forms are live: AND-then-NOT (two gates) or `NOT A OR NOT B` (three
gates), and the level should pass either. Test vectors are the full 2-input
table — sources `0011` and `0101`, sink `1110` — which is enough to catch a
player who wires OR where AND belongs, and just long enough (4 vectors) that a
timing mistake corrupts rather than merely arriving late. The budget stocks NOT
×2, AND ×1, OR ×1 with `maxWireDelay: 1`, so the wiring is fixed and the only
way out of a mistake is to change the topology, not to pad it. The mistake this
level exists to permit is the half-applied De Morgan: inverting one input, wiring
the other straight into the OR, and feeding a gate along paths of depth 2 and
depth 1. That circuit does not produce wrong answers — it destroys bits — which
is the first time the player meets the game's central claim that a mis-timed
circuit tells you so.

### 2. K-map minimisation — "Four Corners"

The player is given a 3-variable truth table and asked to realise it in sum-of-
products with a budget that fits a minimal cover and nothing more. Use a cyclic
map, `f = Σm(0,1,2,5,6,7)` — sources `00001111` / `00110011` / `01010101`, sink
`11100111` — because it has **two** distinct minimal covers of three terms each,
`A'B' + BC' + AC` and `A'C' + B'C + AB`, so the level cannot be solved one way.
Both need exactly three inverters, so a budget of NOT ×3, AND ×3, OR ×2 admits
both and admits nothing sloppier. The designed mistake is a timing one hiding
inside a logic exercise: product terms sit at different depths (a term like `AC`
fires a tick before a term like `A'B'`, which waits on an inverter), so the OR
tree collecting them is unbalanced by construction and the player must spend
`delayBudget` padding the shallow terms. Set `maxWireDelay: 3`, `delayBudget: 4`.
Worth watching on board space: 3 sources + 1 sink + 8 gates is 12 of 45 cells,
which fits, but the wiring is dense on a 9-wide board.

### 3. Functional completeness — "Nothing but NAND"

The player is handed NANDs and nothing else and asked for XOR. Test vectors are
the full 2-input table (`0011`, `0101` → `0110`), and the budget is NAND ×5 —
one above the minimal four, so there is room for both the tight canonical
construction and a clumsier route the player finds first. The first discovery is
that a NAND becomes an inverter when one output fans out to both of its inputs,
which the wiring rules already permit and which nothing in the game currently
teaches. The designed mistake is the one that makes this level worth building:
**the 4-NAND XOR is unbalanced by construction.** The second-stage NANDs take one
input straight from a source and the other from the first NAND, so the source
side arrives a tick early every single time, and the player has to pad two wires
before a correct-looking circuit stops eating bits. Set `maxWireDelay: 3`,
`delayBudget: 4`. A NOR-only variant is the same level with the budget swapped
and is worth having as a later reprise rather than a twin.

### 4. Propagation delay — shipped, plus "The Slow Lane"

`balance-the-paths` already covers the introduction. The follow-up gives a
circuit with three convergence points and a `delayBudget` set to exactly the cost
of the one correct balancing, so guessing is punished by running out rather than
by being told. The mistake it is built around is the intuitive-but-wrong one:
padding the *long* path instead of the short one. That makes the imbalance worse
and burns budget, and the player has to work out that you only ever lengthen the
early side up to the late side. This is also the level that establishes the fact
the critical-path chapter later depends on — correct balancing never lengthens
the critical path, because you only pad paths that were already shorter than it.

### 5. Combinational components — "Pick a Lane"

A 2:1 multiplexer: three sources (A, B, S), one sink, eight vectors covering
every combination. The budget stocks NOT ×1, AND ×2, OR ×1 **and** XOR ×2, which
puts two genuinely different circuits on the table: the textbook
`(A AND NOT S) OR (B AND S)`, and the XOR-trick mux `A XOR (S AND (A XOR B))`,
which uses one fewer gate at the same depth. Neither is hinted at; the parts list
is the hint. The designed mistake is the fan-out timing error that a select line
makes almost inevitable — `S` feeds both the inverter and the second AND, so the
two ANDs fire on different ticks unless the direct path is padded. Players
reliably build the logic correctly and then watch it destroy bits, which is the
right order to learn this in. This is also the natural home for the decoder and
comparator variants later, all of which have the same shape.

### 6. Adders — "Carry the One"

The full adder, following the shipped half adder: three sources, two sinks, eight
vectors (`00001111` / `00110011` / `01010101` → sum `01101001`, carry
`00010111`). `FullAdderTests` already proves the circuit is buildable with five
gates and delays of 1 and 2, so this level is authoring, not engineering. Budget
XOR ×3, AND ×2, OR ×1 — the third XOR is deliberate slack, because carry-out can
be built with either OR or XOR (the two carry terms are never simultaneously 1),
and "why does XOR work there?" is worth a player discovering rather than being
told. The designed mistake is the one `FullAdderTests` documents at
[FullAdderTests.cs:76-103](../Assets/Tests/EditMode/FullAdderTests.cs#L76-L103):
running Cin straight into the second stage on a delay of 1, forgetting it must
wait a tick for the first half adder. Set `delayBudget: 5` — the balanced build
costs 4, so there is exactly one mistake's worth of slack.

### 10b. Pipeline stage balancing

Not a new level so much as the name for what levels 1–6 have been teaching. Worth
one explicit level late in the sequence that says so: a deliberately deep circuit
where the player has to reason about stage latency as a quantity rather than
fixing imbalances reactively, and where the parts list is generous but the
`delayBudget` is not. See the pipelining note under (c) for why this is the
lesson that survives and the latency/throughput trade-off is not.

---

## (b) Blocked on a missing primitive

### 2b. K-map minimisation with don't-cares — shipped

Was blocked on an `x` expectation character. Landed in `34722dd`, so this is now
(a) and the K-map level below can use it.

Don't-cares are what make K-map minimisation a *choice*. Without them the minimal
cover is fixed and the level has one answer, which by your own standard is not
teaching anything.

### 6b. Carry-lookahead

**Blocked on:** a per-level board size. Reclassified from (c) — see below.

A 2-bit ripple-carry adder is two full adders: 10 gates, 4 sources, 3 sinks — 17
of 45 cells, which fits but is dense. The lookahead version of the same width
needs the generate and propagate terms plus a carry tree, and does not fit. Since
the lesson exists only in the *comparison* between the two, this needs a bigger
board than 9 × 5.

**This was previously filed under (c), and that was wrong.** It framed a tooling
gap as a property of the model. LogicCore has no opinion about board size — it has
no geometry at all. Only the view does. The correction matters because (c) means
"stop looking", and this is ordinary work with a known shape.

Board size today is neither a level field nor a compile-time constant: it is
`[SerializeField] private int _halfColumns = 4` / `_halfRows = 2` on
[PlacementGrid.cs:23-27](../Assets/Scripts/View/PlacementGrid.cs#L23-L27) — scene
data, set by the scene builder. The **read path is already fully threaded**:
`PlacementGrid.HalfExtents` → `SimulationRunner.HalfExtents` → `LevelSession` →
`LevelLoader.Validate` (bounds-checks fixtures) and `LevelRules.CanPlace`
(bounds-checks placements). Every consumer already takes extents as a *parameter*
rather than reaching for a global, which is the expensive half of this job and it
is already done.

What per-level board size would still cost, in rough order of difficulty:

1. **Two ints on `LevelFile` and `LevelDefinition`**, 0 meaning unspecified, the
   same convention `tickLimit` and `maxWireDelay` already use. Cheap.
2. **An authority inversion in the validator.** `Validate(file, halfExtents)`
   currently treats the passed-in grid size as the authority when bounds-checking
   fixtures. If a level declares its own board, the level must win and the
   parameter becomes a fallback. Few lines, but it changes the meaning of a pure
   function that has tests, so it needs its own.
3. **`PlacementGrid` cannot resize.** `Start()` builds the dot markers once into a
   `GameObject("Grid dots")` container, and the extents have no setter
   ([PlacementGrid.cs:52-75](../Assets/Scripts/View/PlacementGrid.cs#L52-L75)). Per
   level means a `Resize` that tears down and rebuilds that container off the
   `LevelLoaded` event.
4. **The camera does not follow.** `orthographicSize = 5.5f` is hardcoded in the
   scene builder, and `BoardBackground` sizes itself off the camera. A bigger board
   needs both to react.

Items 1–2 are an hour with tests. Items 3–4 are the real cost — runtime-resizable
view geometry — and CLAUDE.md's warning about verifying the saved scene file
applies to both. Call it a focused half-day, entirely in the view layer.

**One honest caveat.** Even with a bigger board, a 4-bit lookahead is ~25–30 gates
plus 8 sources, and legibility of hand-drawn wires on a 2D grid becomes the binding
constraint well before the format does. The realistic target is a 2-bit ripple
versus 2-bit lookahead comparison at something like 13 × 7, not a textbook 4-bit
CLA.

### 7. Latches and flip-flops

**Blocked on three things**, and the roadmap should not pretend it is one:

1. **`RegisterNode`** — the primitive CLAUDE.md already specifies but has not
   built. Gate-built latches genuinely cannot work here: consume semantics means
   a cross-coupled NOR pair deadlocks at startup and stalls after one firing.
2. **A way to author initial state.** CLAUDE.md's design calls for "edges that
   start with bits already in transit", and the level format has no way to say
   that — there is no fixture-wire array, and `LevelFixtureFile` has no notion of
   a seeded value. A register with no initial content never fires.
3. **A palette slot.** `GateKind` has six members and the loader parses exactly
   those six names ([LevelLoader.cs:461-470](../Assets/Scripts/View/Level/LevelLoader.cs#L461-L470)).
   A register the player can place needs a seventh, plus a shape in `NodeShapes`.

The good news, worth recording so it does not have to be rediscovered: the
grader's settle rule already tolerates what a register does. `IsSettled` requires
nothing in transit but deliberately permits bits stranded in input ports
([LevelGrader.cs:106-113](../Assets/Scripts/View/Level/LevelGrader.cs#L106-L113)),
so a feedback loop drains and settles once the sources exhaust rather than
hanging the run. Sequential levels will not need a new pass rule.

### 8. FSMs and state minimisation

**Blocked on:** exactly the three above. Nothing additional.

Once registers exist, FSMs need no further mechanism — cycles are already legal
wiring, and the settle behaviour above already handles the drain. Moore and Mealy
machines differ only in whether the output logic reads the register or the
register's input, both of which are ordinary wiring here. State minimisation
becomes a budget puzzle in the most direct possible way: the same behaviour with
fewer state bits means fewer registers on the parts list.

---

## (c) Not expressible in a token-passing model

Written in the style of the static/dynamic hazards note in CLAUDE.md, and
intended to sit alongside it.

### Latency-versus-throughput pipelining is not expressible.

Classical pipelining is a trade: cut a combinational block with registers, and
throughput rises while latency gets slightly worse. That trade needs an
*un*-pipelined circuit to trade away from, and this model has none. Every gate
consumes its inputs, emits, and hands the result to an edge that takes at least a
tick — so every gate is already a register, and every circuit is already pipelined
at gate granularity.

A **balanced** circuit therefore sustains exactly one vector per tick whatever its
depth: adding stages buys latency, never throughput. An **unbalanced** one does not
degrade gracefully to something slower, and this is the part worth being precise
about — **there is no backpressure**. A `SourceNode` has no input ports, so
`IsReadyToEvaluate` is vacuously true and phase 3 fires it every tick no matter what
is stalled downstream
([Simulation.cs:209-214](../Assets/Scripts/LogicCore/Simulation.cs#L209-L214)). A
stalled gate cannot throttle its upstream. The next bit arrives into a port that is
still occupied, and phase 2 destroys it
([Simulation.cs:202-206](../Assets/Scripts/LogicCore/Simulation.cs#L202-L206)).

Concretely, with paths of delay 1 and 2 converging on one gate and a dense source:
tick 1 delivers vector 0's fast bit and the gate cannot fire; tick 2 delivers vector
1's fast bit into that same still-occupied port, and both are lost. Corruption on
the second vector, every time.

So throughput is not a spectrum that degrades — it is binary. Either exactly one
vector per tick, or the run fails as `Corrupted`. That is precisely why it cannot be
a score axis: a score needs a range of outcomes to rank, and across *correct*
solutions throughput does not vary at all. Gate count and latency both do.

The related lesson this game *can* teach is **stage balancing**: a pipeline only
works if every path into a stage has equal latency, and here a stage fed by
unequal paths does not produce a stale answer, it destroys bits. That is the same
requirement real pipelines have, arrived at from the failure side. Reach for that
whenever the subject would otherwise be pipeline throughput.

### Clock-edge phenomena are not expressible.

Setup and hold time, clock skew, and the level-triggered/edge-triggered
distinction all need a clock as a *signal* — something with its own edges, its own
arrival time, and a relationship to the data that can be violated. Here the tick
is the clock, it is global, it is exact, and it is not a wire. Nothing can arrive
late relative to it, so nothing can violate it. A latch and a flip-flop, once
`RegisterNode` exists, will be the same object.

The related lesson this game can teach is **latency budgeting**: the reason setup
time matters is that a stage's logic must finish before the next capture, and here
that becomes the concrete and gradeable question of whether a path fits within an
allowed number of ticks. Reach for that whenever the subject would otherwise be
clock timing.

---

## Level designs — concrete, not yet built

Six levels, fully specified. Nothing here is written to
`Assets/Resources/Levels/` yet; this is the version to argue with first.

### The arithmetic every design below uses

Define a node's **level** as the tick it evaluates vector *v*, minus *v*.

- A source is level 0: it emits vector *v* on tick *v*.
- A node fed from a level-*L* node down a wire of delay *d* receives at level
  *L + d*, and evaluates there.
- **Every input of a gate must arrive at the same level.** Otherwise the early bit
  waits in its port and the next vector's bit collides with it.
- A sink's level is its latency, which is what `maxLatency` grades.

So a plain wire costs 1 level, and "extra delay" below means ticks above the
default of 1 — exactly what `delayBudget` counts.

### 1. The Long Way Round — De Morgan

```json
{
  "name": "The long way round",
  "hint": "this level has no NAND. Build one.",
  "tickLimit": 40,
  "maxWireDelay": 1,
  "fixtures": [
    { "id": "a",   "kind": "Source", "cell": { "x": -3, "y":  1 }, "stream": "0011" },
    { "id": "b",   "kind": "Source", "cell": { "x": -3, "y": -1 }, "stream": "0101" },
    { "id": "out", "kind": "Sink",   "cell": { "x":  3, "y":  0 } }
  ],
  "budget": [
    { "kind": "Not", "count": 2 },
    { "kind": "And", "count": 1 },
    { "kind": "Or",  "count": 1 }
  ],
  "expected": [ { "sink": "out", "values": "1110" } ]
}
```

**Two intended solutions, both latency 3, both balanced with no padding at all:**

- `NOT (A AND B)` — AND at level 1, NOT at level 2, sink at 3. Two gates.
- `NOT A OR NOT B` — both NOTs at level 1, OR at level 2, sink at 3. Three gates.

`maxWireDelay: 1` fixes the wiring deliberately. Nothing on this board can be
re-timed, so a mistake has to be fixed by changing the shape of the circuit rather
than by padding around it. That is the right lesson this early, and it is why there
is no `delayBudget`.

**Designed mistake.** The half-applied De Morgan: invert one input, wire the other
straight into the OR. `A` reaches the OR at level 1 and `NOT B` at level 2, so on
vector 1 the second `A` bit lands on a port still holding the first. The circuit
does not produce wrong answers — it destroys bits, which is most players' first
encounter with the game's central claim.

### 2. Four Corners — K-map minimisation

```json
{
  "name": "Four corners",
  "hint": "six of the eight rows want a 1. Three gates' worth of grouping will do it.",
  "tickLimit": 60,
  "maxWireDelay": 3,
  "delayBudget": 5,
  "fixtures": [
    { "id": "a",   "kind": "Source", "cell": { "x": -4, "y":  2 }, "stream": "00001111" },
    { "id": "b",   "kind": "Source", "cell": { "x": -4, "y":  0 }, "stream": "00110011" },
    { "id": "c",   "kind": "Source", "cell": { "x": -4, "y": -2 }, "stream": "01010101" },
    { "id": "out", "kind": "Sink",   "cell": { "x":  4, "y":  0 } }
  ],
  "budget": [
    { "kind": "Not", "count": 3 },
    { "kind": "And", "count": 3 },
    { "kind": "Or",  "count": 2 }
  ],
  "expected": [ { "sink": "out", "values": "11100111" } ]
}
```

`f = Σm(0,1,2,5,6,7)` — the cyclic map, chosen because **it has exactly two minimal
covers of three terms each and no essential prime implicants**:

- `A'B' + BC' + AC`
- `A'C' + B'C + AB`

Both need exactly three inverters, three ANDs and two ORs, so the budget admits
both and nothing sloppier. Both come to eight gates, latency 5, and **+3 extra
delay** at best:

| Wire | Delay | Why |
|---|---|---|
| sources → the three NOTs | 1 | level 1 |
| NOT → AND, for the two-inverter term | 1 | level 2 |
| the bare literal into a one-inverter term | 2 | **+1**, to meet its NOT at level 2 |
| the two-literal term's AND | 1, 1 | sits at level 1 |
| first OR → second OR | 1 | level 4 |
| level-1 AND → second OR | 3 | **+2**, to reach level 4 |

`delayBudget: 5` leaves room for the alternative routing that lifts the shallow AND
to level 2 instead (+4 total). Tighten to 4 if you want that route squeezed out.

**Designed mistake.** Product terms sit at different depths by construction — a
term like `AC` fires a level before a term like `A'B'`, which waits on an inverter.
The OR tree collecting them is unbalanced before the player does anything wrong, so
correct K-map work still eats bits until the shallow terms are padded.

**No `maxLatency` here on purpose.** The lesson is gate count, and the budget
already enforces it.

### 3. Nothing but NAND — functional completeness

```json
{
  "name": "Nothing but NAND",
  "hint": "NAND is enough for everything. Including NOT.",
  "tickLimit": 40,
  "maxWireDelay": 3,
  "delayBudget": 3,
  "fixtures": [
    { "id": "a",   "kind": "Source", "cell": { "x": -3, "y":  1 }, "stream": "0011" },
    { "id": "b",   "kind": "Source", "cell": { "x": -3, "y": -1 }, "stream": "0101" },
    { "id": "out", "kind": "Sink",   "cell": { "x":  3, "y":  0 } }
  ],
  "budget": [ { "kind": "Nand", "count": 6 } ],
  "expected": [ { "sink": "out", "values": "0110" } ]
}
```

**The first discovery** is that a NAND becomes an inverter when one output fans out
to *both* of its inputs. The wiring rules already permit this — the duplicate check
rejects only the same source-and-target pair, and `In(0)` and `In(1)` are different
targets — and nothing in the game currently teaches it.

**Two intended solutions with a real trade-off**, which is why the budget is 6 and
not the minimal 4:

- **Canonical, 4 NANDs, +2 delay, latency 4.** `N1 = A NAND B` at level 1;
  `N2 = A NAND N1` and `N3 = B NAND N1` at level 2, which needs the two *source*
  wires padded to delay 2; `N4 = N2 NAND N3` at level 3.
- **Compositional, 6 NANDs, +1 delay, latency 5.** `A OR B` built from two NAND
  inverters plus a NAND (level 2), `A NAND B` at level 1, then AND them with a NAND
  pair — the level-1 term needs delay 2 to meet the OR at level 3.

Fewer gates costs more delay budget and less latency. That is a genuine engineering
choice, and it is the reason not to budget exactly 4.

**Designed mistake.** The canonical 4-NAND XOR is unbalanced *by construction*: its
second-stage gates take one input straight from a source and the other from `N1`, a
level behind. Players build a textbook-correct XOR and watch it destroy bits.

No `maxLatency`: the two routes differ (4 versus 5), and grading on time would kill
one of them.

### 4. The Slow Lane — propagation delay

```json
{
  "name": "The slow lane",
  "hint": "every gate you pass through costs a tick. The bits that skip it do not wait.",
  "tickLimit": 60,
  "maxWireDelay": 4,
  "delayBudget": 6,
  "fixtures": [
    { "id": "a",   "kind": "Source", "cell": { "x": -4, "y":  2 }, "stream": "00001111" },
    { "id": "b",   "kind": "Source", "cell": { "x": -4, "y":  0 }, "stream": "00110011" },
    { "id": "c",   "kind": "Source", "cell": { "x": -4, "y": -2 }, "stream": "01010101" },
    { "id": "out", "kind": "Sink",   "cell": { "x":  4, "y":  0 } }
  ],
  "budget": [
    { "kind": "Xor", "count": 2 },
    { "kind": "And", "count": 1 },
    { "kind": "Or",  "count": 1 }
  ],
  "expected": [ { "sink": "out", "values": "00111011" } ]
}
```

`f = (((A XOR B) XOR C) AND A) OR B` — a deliberate staircase. Each stage adds one
level, and each stage takes a fresh source straight off the left edge, so the
padding required grows by one every time:

| Gate | Level | Padding needed |
|---|---|---|
| `X1 = A XOR B` | 1 | none |
| `X2 = X1 XOR C` | 2 | C's wire to delay 2 (**+1**) |
| `G1 = X2 AND A` | 3 | A's wire to delay 3 (**+2**) |
| `G2 = G1 OR B` | 4 | B's wire to delay 4 (**+3**) |

Latency 5. **`delayBudget: 6` is exactly the required total**, so there is no slack:
a wrong guess has to be taken back rather than absorbed. That is the point of the
level.

**Designed mistake.** Padding the *long* path instead of the short one — adding
delay to `X1 → X2` rather than to `C → X2`. It makes the imbalance worse and burns
budget, and with zero slack the player must work out that you only ever lengthen the
side that arrives early.

**Open question for you.** This is the one level of the six with a single solution:
the topology is given by the expression and the delay assignment is forced. The
skill being taught is arithmetic rather than synthesis, so I think that is
defensible — but it does break the rule the rest of the set follows. Worth deciding.

### 5. Pick a Lane — 2:1 multiplexer

```json
{
  "name": "Pick a lane",
  "hint": "s chooses which input reaches the bin. s has to reach two places at once.",
  "tickLimit": 60,
  "maxWireDelay": 3,
  "delayBudget": 4,
  "maxLatency": 4,
  "fixtures": [
    { "id": "a",   "kind": "Source", "cell": { "x": -4, "y":  2 }, "stream": "00001111" },
    { "id": "b",   "kind": "Source", "cell": { "x": -4, "y":  0 }, "stream": "00110011" },
    { "id": "s",   "kind": "Source", "cell": { "x": -4, "y": -2 }, "stream": "01010101" },
    { "id": "out", "kind": "Sink",   "cell": { "x":  4, "y":  0 } }
  ],
  "budget": [
    { "kind": "Not", "count": 1 },
    { "kind": "And", "count": 2 },
    { "kind": "Or",  "count": 1 },
    { "kind": "Xor", "count": 2 }
  ],
  "expected": [ { "sink": "out", "values": "00011011" } ]
}
```

`out = s ? b : a`. The parts list is the only hint, and it stocks two circuits:

- **Textbook, 4 gates, +2 delay.** `(A AND NOT S) OR (B AND S)`. `NOT S` at level 1;
  the first AND at level 2 needs `A` padded to delay 2; the second AND sits at level
  1 and reaches the OR on a delay-2 wire.
- **XOR trick, 3 gates, +3 delay.** `A XOR (S AND (A XOR B))`. `A XOR B` at level 1,
  the AND at level 2 with `S` padded to 2, the second XOR at level 3 with `A` padded
  to 3.

Both land at latency 4, which is why **this is the first level to set
`maxLatency`** — it is satisfiable by every intended route, so it costs nothing but
introduces the idea before the adder needs it.

**Designed mistake.** `s` has to feed both the inverter and the second AND, so the
two ANDs end up a level apart unless the direct path is padded. Players reliably get
the logic right and then watch it eat bits, which is the correct order to learn
this in.

### 6. Carry the One — the full adder

```json
{
  "name": "Carry the one",
  "hint": "two half adders and something to join the carries.",
  "tickLimit": 60,
  "maxWireDelay": 3,
  "delayBudget": 5,
  "maxLatency": 4,
  "fixtures": [
    { "id": "a",    "kind": "Source", "cell": { "x": -4, "y":  2 }, "stream": "00001111" },
    { "id": "b",    "kind": "Source", "cell": { "x": -4, "y":  0 }, "stream": "00110011" },
    { "id": "cin",  "kind": "Source", "cell": { "x": -4, "y": -2 }, "stream": "01010101" },
    { "id": "sum",  "kind": "Sink",   "cell": { "x":  4, "y":  1 } },
    { "id": "cout", "kind": "Sink",   "cell": { "x":  4, "y": -1 } }
  ],
  "budget": [
    { "kind": "Xor", "count": 3 },
    { "kind": "And", "count": 2 },
    { "kind": "Or",  "count": 1 }
  ],
  "expected": [
    { "sink": "sum",  "values": "01101001" },
    { "sink": "cout", "values": "00010111" }
  ]
}
```

Already proven buildable — `FullAdderTests` constructs exactly this circuit. Five
gates, **+3 extra delay** minimum:

| Gate | Level | Padding |
|---|---|---|
| `sum1 = A XOR B`, `carry1 = A AND B` | 1 | none |
| `sum2 = sum1 XOR Cin` | 2 | Cin's wire to delay 2 (**+1**) |
| `carry2 = sum1 AND Cin` | 2 | Cin's wire to delay 2 (**+1**) |
| `cout = carry1 OR carry2` | 3 | carry1's wire to delay 2 (**+1**) |

`sum` lands at latency 3, `cout` at 4, so `maxLatency: 4` is exactly the minimum and
is satisfiable. `delayBudget: 5` gives two ticks of slack — and there is a subtle
true lesson in the pair: slack spent *off* the critical path is free, slack spent on
it fails the level.

**The third XOR is deliberate.** Carry-out can be built with `OR` or with `XOR`,
because `carry1` and `carry2` are never both 1 — if `A AND B` is 1 then `A XOR B` is
0. Two valid answers, and "why does XOR work there?" is worth discovering rather
than being told.

**Designed mistake.** Running `Cin` straight into the second stage on delay 1,
forgetting it must wait a tick for the first half adder. Documented and proven to
corrupt rather than mis-compute at
[FullAdderTests.cs:76-103](../Assets/Tests/EditMode/FullAdderTests.cs#L76-L103).

### On `x`, which none of these use

Worth recording, because it corrects an assumption in [Q1](#q1-can-a-test-vector-express-a-dont-care-output).

A K-map don't-care means "this input combination cannot occur". In this format the
natural way to say that is **to leave the combination out of the source streams** —
the vector simply does not exist, and any circuit agreeing on the vectors that do
exist passes. No `x` required.

`x` earns its place in a narrower case: **vectors are global across every sink**, so
a level cannot drop a vector for one sink alone. When sink A's output is meaningful
on a vector and sink B's is not, sink B needs `x` for that vector. None of the six
above is that shape — the multi-output ones, half and full adder, are fully
specified on every row.

So `x` is built, tested and correct, but its first real user will be a multi-output
level with asymmetric don't-cares. Worth knowing before designing one that needs it.

### How these get built

One level per red-then-green pair, per the working agreement:

- **Red** — a test that builds each intended solution and asserts it passes, plus a
  test that the designed mistake fails *the specific way the design claims* —
  `Corrupted`, not `WrongOutput`. The level file does not exist yet, so these fail
  on the fixture load.
- **Green** — the JSON, tuned until every intended solution passes and the mistake
  still fails.

The multi-solution levels get one test per solution. That is what stops a level
shipping that is accidentally single-solution, or whose designed mistake turns out
not to be reachable.

## Format answers

### Q1: Can a test vector express a don't-care output?

**Yes, since `34722dd`. Write `x`.** The original answer was no, and the reason is
worth keeping because it explains why `x` had to be a new character rather than a
reuse of the one that looked right.

`-` was already legal and means *"this vector produces no bit at all here"*. It is
not a wildcard. The loader compacts the values string into a dense
`List<ExpectedBit>`, **omitting** every `-`, and the grader compares that compacted
list positionally against `sink.Received`. So a `-` consumes no slot. A K-map
don't-care is the opposite shape: a bit **does** arrive and either value is
acceptable, which needs a slot. The two cannot be the same character.

What shipped:

- `ExpectedBit` carries `IsAny` plus a static `ExpectedBit.Any(int vector)`. An
  explicit flag, not `Bit?` — CLAUDE.md has already spent nullable-`Bit` on "port
  is empty", and a second meaning one type away would make that unreadable.
- `LevelLoader` accepts `'x'`; the refusal text is now `expected 0, 1, x or -`.
- `LevelGrader` guards one comparison with `!want.IsAny`, and `Describe()` stops a
  missing don't-care reporting "expected 0" for a value nobody asked for.

**`x` constrains the value and nothing else.** The arrival and the count are
untouched, so `"xxxx"` against an unwired board still fails as `MissingOutput`.
That is the boundary a test pins, and it is what keeps an empty-bin level
enforceable.

`x` remains refused in a *source* stream — a source must emit something definite.

**A related defect surfaced and was fixed in `0c494d6`.** Because `-` vectors are
dropped, the value loop ran positionally over a list with holes, so a sink that
emitted where the level asked for silence was reported against the *wrong* vector
(`"0-11"` blamed vector 2 for a vector 1 fault) or against none at all (`"0-01"`
degraded to a count mismatch with `Vector = -1`). Both now name vector 1. Latency
is inferred from the first expected bit to map each reception back to its vector,
and that inference words the failure only — pass and fail are still decided by
value and count.

### Q2: Can a level score on critical path length?

**Yes, since `1a01f45`. Set `maxLatency`.** LogicCore needed no change at all —
the measurement was already being recorded and thrown away.

`SinkNode.Reception` records the tick each bit was consumed. Sources emit vector
*v* on tick *v*, so a bit's latency is its consumption tick minus its vector.
Measured, not modelled.

**Observed latency, not a static graph walk.** The alternative — summing edge
delays along the longest source→sink path — is undefined on a cyclic circuit, and
`WiringRules` permits cycles and self-loops on purpose
([WiringRules.cs:66-69](../Assets/Scripts/View/WiringRules.cs#L66-L69)). The
observed figure needs no cycle detection and measures the circuit the player
actually watched.

What shipped:

- `LevelFile.maxLatency` — 0 or absent means no ceiling, matching how `tickLimit`
  and `maxWireDelay` handle JsonUtility's inability to tell a missing key from an
  explicit zero. Negative is refused.
- `LevelDefinition.MaxLatency` + `HasLatencyLimit`.
- `RunOutcome.TooSlow` — the only outcome that fails a circuit computing every
  answer correctly.
- `LevelGrader.GradeLatency`, run **last**, after every value and count check: a
  circuit that is both wrong and slow hears that it is wrong first, because
  shortening a path that computes the wrong answer is wasted work.

**The maximum across every graded bit, not vector 0's.** In a circuit that reaches
this check the two agree — a sink fed at uneven latency reorders or destroys bits
and has already failed. The maximum costs one comparison per bit and means that if
that structural assumption ever stops holding, the level fails loudly instead of
grading the circuit on its best vector. Worth being straight about: no legal
passing circuit currently has non-uniform latency, so this is a guard rather than
a behaviour any test can distinguish.

*It is a constraint, not a score.* CLAUDE.md lists scoring under "Not yet". A
`maxLatency` pass/fail gate stays inside the existing model — no stars, no ranking,
no persistence, one more `RunOutcome`. A graded bronze/silver/gold on latency
**is** the scoring item and waits until asked for by name.

*`maxLatency` and `delayBudget` do not fight, and it is worth knowing why.*
Balancing means padding the *short* paths up to the long one, and padding a short
path never makes it longer than the critical path. So correct balancing costs
zero latency, and a `maxLatency` set to the intended solution's critical path is
exactly satisfiable. That property is what makes ripple-carry versus lookahead
gradeable here at all — the ripple version fails a latency gate because its carry
chain is genuinely deep, not because it was balanced badly. Whether the two fit on
a 9 × 5 board is the separate problem noted under 6b.

### Q3: Can a level require a specific gate set only?

**Yes. Today. No schema change.**

`budget` is an allow-list, not a per-type cap on an otherwise-open palette. A kind
absent from the budget cannot be placed at all: `LevelRules.CanPlace` returns
`NotInBudget` with "this level has no X gates" for any kind whose budget is ≤ 0
([LevelRules.cs:130-131](../Assets/Scripts/View/Level/LevelRules.cs#L130-L131)),
and the loader actively refuses `count: 0` with the message *"omit the kind
entirely to forbid it"*
([LevelLoader.cs:296-305](../Assets/Scripts/View/Level/LevelLoader.cs#L296-L305)).
The shipped `route-the-bit.json` is already a NOT-only level by exactly this
mechanism.

So a NAND-only level is:

```json
"budget": [ { "kind": "Nand", "count": 5 } ]
```

**Verified alongside this:** the NAND-as-inverter trick works. Wiring one output
to both inputs of the same NAND is legal — the duplicate check rejects only the
same *source-and-target* pair, and `In(0)` and `In(1)` are different targets
([WiringRules.cs:109-113](../Assets/Scripts/View/WiringRules.cs#L109-L113)) — and
both edges default to delay 1, so the gate fires every tick on `(x, x)` and emits
`NOT x`. Functional completeness is reachable with the code as it stands.

**The one gap:** you cannot say "NAND-only, unlimited". Every budget entry needs a
positive integer, so gate count is always *also* constrained. For a puzzle that is
usually the better level anyway — the count is what makes minimality matter. If
you do want an uncapped set, the minimal change is a sentinel: accept `count: -1`
as unlimited in `TryBuildBudget`, have `BudgetFor` return `int.MaxValue` for it,
and give the HUD's parts row a `∞` instead of a remaining count.

**A design warning about the NAND-only XOR specifically.** The 4-NAND XOR has
essentially one topology, so budgeting exactly 4 produces a level with exactly one
solution. Budget 5 or 6 — the slack is what lets a player find a clumsy route
first and then discover the tight one, which is the actual lesson of functional
completeness.

---

## Note on CLAUDE.md

The "Unbalanced path delays" entry under **Level ideas** is now out of date. It
says every player wire is hardcoded to delay 1 and calls the timing-hazard chapter
"currently unreachable", but `WireDelayController`, `LevelRules.CanSetDelay`, the
`maxWireDelay` / `delayBudget` fields and the shipped `balance-the-paths.json`
resolved that — you took the second of the two candidate fixes, player-chosen wire
delays. Every level sketched above depends on that being true. Worth updating that
paragraph so the next design pass does not re-decide a settled question.
