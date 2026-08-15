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
  (SR latch, ch. 8) also need a real time step to resolve.

- **Order-independence is a hard invariant.** Nodes within a single tick
  must be evaluatable in any order with the same outcome. Do not add
  anything that breaks this.

- **SourceNode emits one bit per tick from tick 0**, then goes silent.
  It needs no special casing: a source has no inputs, so the
  "all inputs filled" rule is vacuously true and it fires every tick.
  If sparse streams are ever needed, make the sequence `Bit?[]` where
  null means emit nothing. Not needed yet — do not add it preemptively.

- **Collisions never throw.** A bit delivered to an occupied input port
  increments CorruptedCount and is dropped.

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
