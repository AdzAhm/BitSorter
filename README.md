# BitSorter

Bits fall through logic. Sort them.

A puzzle game about building digital circuits. Sources emit a stream of 0s and
1s, bins want particular values, and you have a box of gates. Wire them together
so every bin gets what it asked for.

Nine levels, from routing a single bit to building a full adder. Built in Unity
as a way of working through a computer engineering digital systems course from
the inside.

---

## Playing

Unzip the build anywhere and run `BitSorter.exe`. Nothing is installed and
nothing is written outside your own user folder.

Windows will probably warn that it does not recognise the publisher — the build
is unsigned, which is all that warning means. "More info", then "Run anyway".

### Two rules explain almost everything

**A gate fires when all of its inputs are full, and consumes them.** It cannot
fire on one input and wait for the other. Whatever arrives first sits in the port
until its partner turns up.

**Every wire takes at least one tick.** A longer path costs more ticks, so two
paths into the same gate can arrive at different times.

Put those together and you get the mistake the middle levels are built around. If
one input to a gate arrives a tick before the other, the early bit waits in its
port — and the *next* bit down that wire arrives to find it still there. They
collide, and both are destroyed.

A red scorch mark appears on the port where that happened. It is the most useful
thing on the screen: it names the junction whose paths are unbalanced. Fix it by
making both paths take the same number of ticks, either by routing differently or
by scrolling a wire to lengthen it.

An unbalanced circuit does not run slower. It loses bits.

### Controls

| Action | Effect |
| --- | --- |
| Click a gate in the palette, then click the board | Place it |
| Drag from one port to another | Wire them |
| Right click | Delete a gate or a wire |
| Scroll on a wire | Change its delay |
| `Enter`, or the RUN button | Run |
| `R` | Reset the board back to editing |
| `Shift`+`R` | Clear everything you built |
| `Space` | Pause a run |
| `→` while paused | Step one tick |
| `H`, or the `!` button | This level's truth table and a hint |
| `Esc` | Level list |
| `M` | Main menu |
| `Q` / `E` | Previous / next level |
| `F3` | Diagnostics |

### Your progress

Solved levels, the circuits you built, and your best gate count and tick count
per level save automatically to:

```text
%USERPROFILE%\AppData\LocalLow\Ahmad\BitSorter\progress.json
```

Delete that file to start over. Nothing is uploaded, and nothing is compared
against anyone else — every number the game shows is about the circuit in front
of you, or the one you built last time.

---

## Developing

Unity 6.3 LTS (6000.3.11f1).

- **BitSorter → Build Windows Player** writes a player to `Build/Windows/`.
- **BitSorter → Build Play Scene** regenerates the play scene from code. The
  scene is generated rather than authored, so anything added by hand is discarded
  the next time that runs.
- Tests: Window → General → Test Runner → EditMode → Run All. 425 at present.

### Why it isn't a physics game

The obvious implementation is Rigidbody2D marbles bouncing off ramps. That fails:
floating-point variation means the same circuit can produce different results on
different runs, so a correct solution can fail and levels cannot be validated
automatically.

Instead the game is a deterministic tick-based simulator, and Unity is only a
view. Bits are tokens on a graph, not rigid bodies. The physical look — sparks,
bloom, debris — is animation driven by simulation state, and never feeds back
into it.

### Architecture

```text
Assets/Scripts/LogicCore/   pure C#, no UnityEngine reference
Assets/Scripts/View/        Unity rendering, input and interface
Assets/Resources/Levels/    one JSON file per level
Assets/Tests/EditMode/      NUnit tests
```

`LogicCore` is its own assembly with `noEngineReferences: true`, so a stray
`using UnityEngine;` fails to compile rather than silently coupling the simulator
to the engine.

State flows LogicCore → View only. The view never mutates the simulation.

The interface is a Canvas built in code, for the same reason the scene is: an
authored hierarchy would be dozens of RectTransforms for the builder to reproduce
and get subtly wrong. Sprites and sound are both generated at runtime, so there
are no art or audio files and no licences.

### Simulation model

Time is an integer tick. Each `Tick()` runs three phases in a fixed order:
advance bits in transit, deliver arrivals into input ports, evaluate nodes.

- An input port holds at most one bit (`Bit?`, where null is empty).
- A node evaluates only when **all** its input ports are filled, then consumes
  them. That is what makes two-input gates work with serially arriving bits, and
  it makes timing part of the puzzle.
- Every edge has an integer delay of at least 1. Zero-delay edges are rejected at
  construction because they would force node evaluation into topological order.
- **Node evaluation order within a tick cannot affect the result.** This is a
  tested invariant: the same graph built with nodes and edges registered in
  reverse order produces identical output.
- A bit delivered to an occupied port is a collision. Matching values destroy the
  arrival; differing values destroy both and poison the port for the rest of that
  delivery phase, so no ordering leaves a survivor. `CorruptedCount` counts
  destroyed bits, not events, and `CorruptionSites` says which ports they were
  destroyed at.

#### The consequence that shapes the game

Because delay is real and ports latch, a circuit with a correct truth table can
still fail if its paths are unbalanced. A second-stage gate receiving its inputs
on different ticks holds the first, waits, and corrupts when the next arrives.
The failure is corruption, not a wrong answer.

That is the same problem timing closure solves in real hardware, and it is the
intended difficulty of the adder chapters.

There is also no backpressure — a source has no inputs, so it emits every tick
regardless of what is stalled downstream. This is why throughput is not scored:
a balanced circuit sustains exactly one vector per tick whatever its depth, and
an unbalanced one fails outright, so there is nothing in between to rank.

### Not built

- `RegisterNode` for sequential logic. Memory cannot emerge from gate feedback
  under consume semantics — a cross-coupled NOR latch deadlocks at startup and
  stalls after one firing — so registers have to be a primitive rather than
  something the player builds. A deliberate decision, not an oversight.
- Anything that ranks a player against other people or an authored ideal. Star
  ratings, par scores and leaderboards are out by design.

`CLAUDE.md` carries the full set of decisions and the reasoning behind them.
