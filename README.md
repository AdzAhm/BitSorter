# BitSorter

A puzzle game about building digital circuits, where bits are physical
objects that fall through gates. Built in Unity as a way of working
through a computer engineering digital systems course from the inside.

**Status:** simulation core complete and tested (38 tests). Unity view
layer not yet started — there is nothing on screen yet.

## The idea

Bits (0 and 1) drop from spouts at the top of the board. The player
places gates, wires and delay elements to route and transform them so
they land in the correct bins. Each level is specified by a truth table
or timing requirement.

Levels follow a digital systems syllabus: routing and propagation
delay, single gates, NAND-only construction, half and full adders,
multiplexers, registers, counters, and finally a small datapath.

## Why it's not a physics game

The obvious implementation is Rigidbody2D marbles bouncing off ramps.
That fails: floating-point variation means the same circuit can produce
different results on different runs, so a correct solution can fail and
levels cannot be validated automatically.

Instead the game is a deterministic tick-based simulator, and Unity is
only a view. Bits are tokens on a graph, not rigid bodies. The physics
look — bouncing, sparks, squash and stretch — is animation driven by
simulation state, and never feeds back into it.

## Architecture

    Assets/Scripts/LogicCore/   pure C#, no UnityEngine reference
    Assets/Tests/EditMode/      NUnit tests against LogicCore
    Assets/Scripts/View/        Unity rendering (not yet built)

`LogicCore` is compiled as its own assembly with
`noEngineReferences: true`, so a stray `using UnityEngine;` fails to
compile rather than silently coupling the simulator to the engine. It
also builds and passes its full test suite in a plain NUnit project
with no Unity assemblies present.

State flows LogicCore → View only.

## Simulation model

Time is an integer tick. Each `Tick()` runs three phases in a fixed
order: advance bits in transit, deliver arrivals into input ports,
evaluate nodes.

- An input port holds at most one bit (`Bit?`, where null is empty).
- A node evaluates only when **all** its input ports are filled, then
  consumes them. This is what makes two-input gates work with serially
  arriving bits, and it makes timing part of the puzzle.
- Every edge has an integer delay of at least 1. Zero-delay edges are
  rejected at construction because they would force node evaluation
  into topological order.
- **Node evaluation order within a tick cannot affect the result.**
  This is a tested invariant: the same graph built with nodes and edges
  registered in reverse order produces identical output.
- A bit delivered to an occupied port is a collision. Matching values
  destroy the arrival; differing values destroy both and poison the
  port for the rest of that delivery phase, so no ordering leaves a
  survivor. `CorruptedCount` counts destroyed bits, not events.

### The consequence that shapes the game

Because delay is real and ports latch, a circuit with a correct truth
table can still fail if its paths are unbalanced. A second-stage gate
receiving its two inputs on different ticks holds the first, waits, and
corrupts when the next bit arrives. The failure is corruption, not a
wrong answer.

That is the same problem timing closure solves in real hardware, and it
is the intended difficulty of the adder chapters.

## Running the tests

Open the project in Unity 6, then Window → General → Test Runner →
EditMode → Run All.

## Built so far

- Simulation, nodes, ports, edges, tick loop
- Source, sink and pass-through nodes
- NOT, AND, OR, XOR, NAND, NOR
- Half adder, built by wiring rather than as a component
- 38 Edit Mode tests

## Not yet built

- Unity view layer, art, and input
- `RegisterNode` for sequential logic. Memory cannot emerge from gate
  feedback under consume semantics — a cross-coupled NOR latch fires
  once and stalls — so registers are a primitive rather than something
  the player builds. Recorded as a deliberate decision, not an
  oversight.
- Level format, scoring, campaign
