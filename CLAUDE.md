# BitSorter

2D Unity puzzle game. Bits (0/1) fall through logic components into bins.
Teaching digital logic: gates, flip-flops, adders, FSMs.

## This document
This file is authoritative, and it drifts silently — nothing recompiles
when it goes stale, and no test turns red. When you hit a section that no
longer matches the code, say so and correct it in the same change. Do not
quietly work around a stale paragraph, and do not treat one as binding
just because it is written down here.

## Architecture rule (do not break this)
- Assets/Scripts/LogicCore/ = pure C#, NO UnityEngine imports.
  Deterministic tick-based simulator. Has its own asmdef.
- Assets/Scripts/View/ = Unity MonoBehaviours. Reads sim state, renders it.
- State flows LogicCore -> View only. The view never mutates the sim.
- No Rigidbody2D for bits. Physics is decoration only (sparks, debris).

## Conventions
- Every LogicCore component gets a unit test with its truth table.
- Levels are JSON in Assets/Resources/Levels/, and each one names its own
  `order` — play order is authored, not the ordinal sort of file names.
  `LevelCatalog` refuses two levels claiming the same place.
- **`goal` states the objective and may name gates outright. `hint` may
  not.** The no-giveaway rules in `CurriculumTests` apply to the hint
  alone. Before `goal` existed the hint had to carry both jobs, which is
  how the half adder ended up with a hint that named both its gates and
  said which output each produced.
- A mechanic is taught before it is required. `CurriculumTests` enforces
  that the delay tutorial precedes every level that budgets delay.

## My level
I'm a 3rd-semester CE student, new to Unity and git.
Explain things as you go and tell me when I'm about to do something dumb.

## Syllabus scope

What this game teaches, and what it deliberately leaves alone.

**In scope:** boolean algebra, K-map minimization, functional
completeness, propagation delay, combinational components, adders,
flip-flops, FSMs, critical path, pipelining.

**Out of scope:** assembly, the RISC-V datapath, memory addressing, and
number representation. Those are a different game — don't reach for them
even when a level looks like it could stretch that way.

**Hazards are not expressible.** Static and dynamic hazards need a
continuous signal model; bits here are discrete tokens, so a glitch has
nowhere to live. The related lesson this game *can* teach is
unbalanced-path corruption: two paths of different total delay into one
gate, where the early bit waits and the next arrival collides with it.
Reach for that whenever the subject would otherwise be hazards.

**Throughput is not a meaningful score.** A balanced circuit sustains
exactly one vector per tick whatever its depth. Every gate consumes its
inputs, emits, and hands the result to an edge of at least one tick, so
every circuit is already pipelined at gate granularity, and depth buys
latency rather than throughput. An unbalanced circuit does not run slower
— it loses bits: sources have no input ports, so they emit every tick
regardless of what is stalled downstream. There is no backpressure, so
the next bit arrives into a port that is still occupied and the collision
destroys it. Throughput therefore has two states, one vector per tick or
a failed run, with nothing in between to rank. **Score gate count and
latency only.** This also means the classical latency-versus-throughput
pipelining trade has nowhere to live here; the lesson that survives is
stage balancing, which is the same requirement arrived at from the
failure side.

## Core decisions

- **Bit** is `enum Bit { Zero, One }`, never a raw int. Port state is `Bit?`,
  where null means empty. One field expresses zero / one / empty.

- **Edge delay must be >= 1.** The constructor throws
  ArgumentOutOfRangeException below that. Zero-delay edges would force
  node evaluation into topological order, which breaks the rule that
  evaluation order within a tick cannot affect the result. Feedback loops
  also need a real time step to resolve — but note that latches come from
  RegisterNode, not from cross-coupled gates. See the sequential-logic
  decision below.

- **Order-independence is a hard invariant.** Nodes within a single tick
  must be evaluatable in any order with the same outcome. Do not add
  anything that breaks this.

- **SourceNode emits one bit per tick from tick 0**, then goes silent.
  It needs no special casing: a source has no inputs, so the
  "all inputs filled" rule is vacuously true and it fires every tick.
  If sparse streams are ever needed, make the sequence `Bit?[]` where
  null means emit nothing. Not needed yet — do not add it preemptively.

- **Collisions never throw.** A bit delivered to an occupied input port is
  destroyed, not thrown on. If the two values match, the port keeps its
  value and only the arrival is destroyed. If they differ the result is
  ambiguous, so neither bit survives: the port is cleared and stays
  poisoned for the rest of that tick's delivery phase, so a later arrival
  in the same tick cannot refill it. A mixed-value collision never leaves
  a survivor.

- **CorruptedCount counts destroyed bits, not collision events.** A
  matching-value collision adds 1. A mixed-value collision adds 2, since
  both bits are destroyed.

- **NodeCount and EdgeCount are id bounds, not populations.** Removing
  leaves a tombstone: the slot becomes null and the id is retired, never
  reissued, so every surviving id keeps meaning the same node. Use
  LiveNodeCount / LiveEdgeCount for the population, and null-check
  anything GetNode / GetEdge returns — the id range is not dense. A
  removed node or edge reports Id -1, so capture an id before removing
  rather than reading it back off the object. Bits lost to a removal are
  not corruption and must never touch CorruptedCount.

- **Sequential logic uses stateful RegisterNode primitives plus seedable
  edges, not gate-built latches.** Consume semantics destroys a value on
  use, so a cross-coupled NOR latch deadlocks at startup (each gate waits
  on the other's first output) and stalls after one firing (its external
  input port is never refilled). Memory cannot emerge from gate feedback
  here. A RegisterNode — emits the bit it holds, stores the one it just
  consumed — plus edges that start with bits already in transit gives full
  synchronous sequential power with no change to the tick loop.
  **Not yet implemented.**

## Working agreement
- Use Plan mode for anything touching more than one file.
- Every new LogicCore component ships with its Edit Mode tests in the
  same change. No component without a truth-table test.
- After editing scripts, remind me to focus the Unity window so it
  recompiles, then run EditMode tests before we commit.
- After any change that builds or modifies the demo scene, verify the
  saved scene file itself — serialized references can be `{fileID: 0}`
  even when the setup code looks correct. A scene that opens fine on
  this machine can still be broken for a fresh clone.
- Commit after each green test run, with a short descriptive message.
- Bug fixes land as two commits: a failing test that reproduces the bug,
  then the fix that makes it pass. The red commit must still compile —
  write the test against the existing API so it fails on an assertion and
  not on a missing symbol, otherwise the commit is useless to bisect and
  leaves the editor broken for anyone who lands on it.
- Unrelated changes go in their own commits, never swept in with a fix.
  A commit that fixes a bug and also re-saves the demo scene and retunes
  the HUD cannot be reviewed, reverted or bisected. `5a70be7` is the
  example not to follow: the level-switch tests, the fix they cover, and
  a scene re-serialisation all landed as one commit.

## The view layer
- **The interface is a Canvas, built in code.** `UiTheme` holds the shared
  colours and builders; each panel constructs its own hierarchy at runtime
  the way `PlacementGrid` builds its dots. The scene is generated, so an
  authored hierarchy would be dozens of RectTransforms for the builder to
  reproduce and get subtly wrong.
- **`HalfAdderDemoSceneBuilder` is the only authority on scene contents.**
  Anything added by hand is wiped by `BitSorter/Build Play Scene`.
- **`PointerGate` arbitrates the mouse.** Every component that reads a
  click asks it first. Ownership is *derived* from what is happening, never
  claimed and released — a claim that leaks disables input silently, with
  no error and no way for the player to recover. `WiringController` runs at
  `DefaultExecutionOrder(-100)` because a press that grabs a port and a
  press that places a gate are the same press.
- **Sound is procedural**, generated by `ProceduralAudio` exactly as
  `ProceduralSprites` generates sprites. No audio files, no licences.
- **Anything shown to the player is derived, never restated.** The truth
  table comes from the level's own streams and expectations; node labels
  come from `Node.Name`. A second copy of a fact is a second thing to
  drift.

## Not yet
Do not build ahead of me. The logic core, the view layer, the level
format, the nine levels, the canvas interface, sound, level select,
saved progress and analytics are all in.

**Analytics is the one thing that sends data anywhere.** `GameAnalytics`
reports exactly two events, `levelStarted` and `levelSolved`, each
carrying the level's file name, to answer one question: which level
people stop at. The README's "What it collects" section is the canonical
list — do not restate it elsewhere. Nothing about the player's circuit,
their bests or their progress file is ever sent, and a reporting failure
must never interrupt play. Adding a third event, or a new parameter, is a
change to what players were told is collected, so ask first.

**The line is ranking, not measurement.** A number describing the player's
own circuit is fine, including one kept between sessions: gate count and
latency on the win panel, and the personal best beside each level, are
facts about what they built measured against what they built last time.
Nobody else's number appears anywhere.

**Still off limits until I ask by name:** anything that ranks a player
against other people or against an authored ideal — a star rating, a par
score, a leaderboard, a percentile, a grade. Those turn "here is your
circuit" into "here is how you compare", which is a different game.

Whatever gets measured, **throughput must never be one of them** — see the
Syllabus scope note above for why it has only two states.

## Level ideas
Design notes only — not implementation work, and not a backlog. Nothing
here gets built, scaffolded or prepared for until I explicitly ask for it
by name. Treat this section as a place to park ideas, not as a to-do list.

- **NAND-only puzzle.** NAND and NOR are each functionally complete —
  every other gate, including NOT, can be built from either one alone.
  A level that hands the player nothing but NANDs and asks for XOR.

- **Unbalanced path delays.** Feeding a second-stage gate along paths of
  different total delay causes corruption, not wrong answers. The early
  bit latches in its port, and the next arrival collides with it. This is
  the core difficulty of the adder chapters, and the reason CorruptedCount
  exists as a game mechanic rather than just a diagnostic.

  **Shipped, and the timing-hazard chapter is unblocked.** Of the two
  candidate fixes once listed here — a locked `wires` array in the level
  JSON, or player-chosen wire delays — the second is the one that landed
  (2925472). Delay is a resource the player manages: wires carry a delay
  scrolled on the wire itself, `LevelRules.CanSetDelay` polices the floor
  of 1, and a level bounds it with `maxWireDelay` and `delayBudget`.
  `balance-the-paths.json` is the first level built on it.

  The road not taken is still not built: there is no fixture-wire array,
  so a level cannot author an unbalanced path of its own. Every wire on
  the board is the player's, which means every timing hazard is one they
  created and can therefore undo. Worth knowing when writing the adder
  chapters — a level can constrain the delay budget, but it cannot hand
  the player a pre-broken circuit to repair.

- **A waiting bit needs a stronger visual.** A bit held in an input port
  currently renders as a small square inside the node. That is readable
  but not legible: it does not communicate "waiting for a second input"
  to anyone who does not already know the rules. Needs a stronger
  treatment — pulse, glow, or a filled/empty port indicator — before
  playtesting with anyone unfamiliar with the game.
