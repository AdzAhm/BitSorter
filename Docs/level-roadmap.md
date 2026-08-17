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
| Grading ignores arrival ticks. It checks: settled, `CorruptedCount == 0`, and each sink's value sequence | [LevelGrader.cs:82-98](../Assets/Scripts/View/Level/LevelGrader.cs#L82-L98) |
| Every sink must be named by an expectation, even an empty one | [LevelLoader.cs:218-231](../Assets/Scripts/View/Level/LevelLoader.cs#L218-L231) |

### The two derived facts that shape every level

**Throughput is fixed at one vector per tick, and the circuit has to keep up.**
Sources stream back to back with no gap. A path-balanced circuit sustains that
indefinitely; an unbalanced one does not. The early bit sits in its port, and the
*next* vector's bit collides with it.

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
| 2b | K-map minimisation with don't-cares | **(b)** | Needs an `x` expectation character — see [Q1](#q1-can-a-test-vector-express-a-dont-care-output) |
| 3 | Functional completeness (NAND-only / NOR-only) | **(a)** | Buildable today, no schema change — see [Q3](#q3-can-a-level-require-a-specific-gate-set-only) |
| 4 | Propagation delay | **(a)** | Shipped: `balance-the-paths` |
| 5 | Combinational components (mux, decoder, comparator) | **(a)** | Buildable today |
| 6 | Adders: half, full, ripple-carry | **(a)** | Half shipped; full verified by `FullAdderTests`. Ripple-carry is board-space limited |
| 6b | Carry-lookahead | **(c)**, practically | Gate count does not fit 9 × 5 — see below |
| 7 | Latches and flip-flops | **(b)** | `RegisterNode` + initial-state authoring + a palette slot |
| 7b | Level- vs edge-triggering, setup/hold, clock skew | **(c)** | No clock signal exists — see below |
| 8 | FSMs (Moore / Mealy), state minimisation | **(b)** | Same three blockers as 7 |
| 9 | Critical path | **(a)** to *feel*, **(b)** to *grade* | Needs `maxLatency` — see [Q2](#q2-can-a-level-score-on-critical-path-length) |
| 10 | Pipelining (latency vs throughput) | **(c)** | Every gate is already a register — see below |
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

### 2b. K-map minimisation with don't-cares

**Blocked on:** an `x` character in the expectation string, meaning "a bit
arrives here and either value passes".

Don't-cares are what make K-map minimisation a *choice*. Without them the minimal
cover is fixed and the level has one answer, which by your own standard is not
teaching anything. The `-` character already in the format is a different thing —
it means no bit arrives at all — and cannot be used for this. Full mechanism and
the minimal change in [Q1](#q1-can-a-test-vector-express-a-dont-care-output).
This is the cheapest unblock on the list and the one with the highest ratio of
lesson to work.

### 6b. Carry-lookahead

**Blocked on:** board space, not a primitive.

A 2-bit ripple-carry adder is two full adders: 10 gates, 4 sources, 3 sinks — 17
of 45 cells, which fits but is dense. The lookahead version of the same width
needs the generate and propagate terms plus a carry tree, and does not. Since the
lesson only exists in the *comparison* between the two, and the comparison needs
a width where the depths visibly diverge, this is out of reach at 9 × 5. Options
are a bigger board for one chapter, or accepting that the ripple-carry side alone
teaches the carry chain and leaving lookahead to the lecture. Worth deciding
before writing the adder chapters.

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
at gate granularity. Throughput is one vector per tick regardless of depth, and no
amount of added delay improves it because the source is what sets it. There is
nowhere for the trade-off to live.

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

## Format answers

### Q1: Can a test vector express a don't-care output?

**No — and the character that looks like it does means something else.**

`-` is already legal in an expectation and means *"this vector produces no bit at
all here"*. It is not a wildcard. The loader compacts the values string into a
dense `List<ExpectedBit>`, **omitting** every `-`
([LevelLoader.cs:378-391](../Assets/Scripts/View/Level/LevelLoader.cs#L378-L391)),
and the grader then compares that compacted list positionally against
`sink.Received`
([LevelGrader.cs:214-233](../Assets/Scripts/View/Level/LevelGrader.cs#L214-L233)).
So a `-` consumes no slot. If a bit *does* arrive on a `-` vector, every
subsequent comparison shifts by one and the run fails as `WrongOutput` or
`ExtraOutput`.

A K-map don't-care is the opposite shape: a bit **does** arrive, and either value
is acceptable. That needs a slot in the expected list, so `-` cannot be
repurposed. The two must coexist.

**Minimal change — three edits, no change to the JSON shape.** Only a new legal
character in a string that is already free-form.

1. **`ExpectedBit` gains a wildcard flag.** Add `public readonly bool IsAny;` and
   a static `ExpectedBit.Any(int vector)`. *Not* `Bit?` — CLAUDE.md has already
   spent nullable-`Bit` on "port is empty", and giving the same nullable a second
   meaning of "any value" in a neighbouring type is how that decision stops being
   readable.
2. **`LevelLoader.TryBuildExpectations`** gains one case in the switch at
   [LevelLoader.cs:382](../Assets/Scripts/View/Level/LevelLoader.cs#L382):
   `case 'x': expected.Add(ExpectedBit.Any(vector)); break;` — plus the error text
   two lines down becomes `expected 0, 1, x or -`.
3. **`LevelGrader.GradeSink`** guards its one comparison:
   `if (!want.IsAny && got != want.Value)`.

Then a test in `LevelGradingTests` proving `x` accepts both values and still
rejects a *missing* bit, which is the boundary that matters — `x` constrains the
value, never the arrival.

This works only because grading already ignores ticks, which it does deliberately
and documents at
[LevelGrader.cs:90-94](../Assets/Scripts/View/Level/LevelGrader.cs#L90-L94).

### Q2: Can a level score on critical path length?

**Not today, in two separate senses — but the measurement already exists and
LogicCore needs no change at all.**

What exists now: `budget` (per-kind gate counts), `delayBudget` (total added wire
ticks), `maxWireDelay`, and `tickLimit` — which is explicitly a safety net against
oscillators and documented as *not* a difficulty knob
([LevelLoader.cs:49-60](../Assets/Scripts/View/Level/LevelLoader.cs#L49-L60)).
And grading throws arrival ticks away on purpose, so that a correct-but-slow
circuit passes.

What already exists and is unused: **`SinkNode.Reception` records the tick each
bit was consumed** ([SinkNode.cs:16-31](../Assets/Scripts/LogicCore/SinkNode.cs#L16-L31)).
Since sources emit vector 0 at tick 0, `sink.Received[0].Tick` **is** the
source-to-sink latency in ticks. Measured, not modelled.

**Use the observed latency, not a static graph walk.** The alternative — summing
edge delays along the longest source→sink path in the blueprint — is undefined on
a cyclic circuit, and `WiringRules` deliberately permits cycles and self-loops
([WiringRules.cs:66-69](../Assets/Scripts/View/WiringRules.cs#L66-L69)). The
observed figure needs no cycle detection, no graph analysis, and measures the
thing the player actually watched happen.

**Minimal change:**

- `LevelFile`: add `public int maxLatency;` — 0 means unspecified, matching how
  `tickLimit` and `maxWireDelay` already handle JsonUtility's inability to
  distinguish a missing key from an explicit zero.
- `LevelDefinition`: carry it, plus `bool HasLatencyLimit => MaxLatency > 0`.
- `LevelLoader.Validate`: reject negative, as it already does for the other two.
- `LevelGrader.Grade`: after the sequence checks pass, take
  `max over graded sinks of Received[0].Tick` and fail with a new
  `RunOutcome.TooSlow` if it exceeds `MaxLatency`. **After**, not before — a
  wrong circuit should hear that it is wrong before it hears that it is slow.

**Two things to flag before you write levels against this.**

*It should be a constraint, not a score.* CLAUDE.md lists scoring under "Not
yet". A `maxLatency` pass/fail gate stays entirely inside the existing model — no
stars, no ranking, no persistence, one more `RunOutcome`. A graded
bronze/silver/gold on latency **is** the scoring item and should wait until you
ask for it by name.

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
