# BitSorter

2D Unity puzzle game. Bits (0/1) fall through logic components into bins.
Teaching digital logic: gates, flip-flops, adders, FSMs.

## Architecture rule (do not break this)
- Assets/Scripts/LogicCore/ = pure C#, NO UnityEngine imports.
  Deterministic tick-based simulator. Has its own asmdef.
- Assets/Scripts/View/ = Unity MonoBehaviours. Reads sim state, renders it.
- State flows LogicCore -> View only. The view never mutates the sim.
- No Rigidbody2D for bits. Physics is decoration only (sparks, debris).

## Conventions
- Every LogicCore component gets a unit test with its truth table.
- Levels are JSON in Assets/Resources/Levels/.

## My level
I'm a 3rd-semester CE student, new to Unity and git.
Explain things as you go and tell me when I'm about to do something dumb.

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
- Commit after each green test run, with a short descriptive message.

## Not yet
Do not build ahead of me. Current scope is the logic core only.
No MonoBehaviours, no scenes, no sprites, no level JSON until I say so.

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

- **A waiting bit needs a stronger visual.** A bit held in an input port
  currently renders as a small square inside the node. That is readable
  but not legible: it does not communicate "waiting for a second input"
  to anyone who does not already know the rules. Needs a stronger
  treatment — pulse, glow, or a filled/empty port indicator — before
  playtesting with anyone unfamiliar with the game.
