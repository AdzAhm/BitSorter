# BitSorter

Bits fall through logic. Sort them.

![A half adder running: bits leave the two sources, cross into an XOR and an AND,
and land in the SUM and CARRY bins](Docs/half-adder.gif)

A puzzle game about building digital circuits. Sources emit a stream of 0s and
1s, bins want particular values, and you have a box of gates. Wire them together
so every bin gets what it asked for.

Above is level 8, the half adder: `A` and `B` each feed both gates, XOR produces
the sum and AND produces the carry. Yellow bits are 1, grey are 0.

Nine levels, from routing a single bit to building a full adder. Built in Unity
as a way of working through a computer engineering digital systems course from
the inside.

## Play it

| Where | |
| --- | --- |
| **In a browser** | [Unity Play](https://play.unity.com/en/games/c22f4580-98a3-4fcd-a844-e9d731257c83/bitsorter), or [GitHub Pages](https://adzahm.github.io/BitSorter/) |
| **Windows** | [Download the latest release](https://github.com/AdzAhm/BitSorter/releases/latest) — 37 MB zip |

The two browser links are the same build, hosted twice so neither one going down
takes the game with it. Nothing to install, and no account needed for either.

---

## Playing

**In a browser**, use either link above. Progress is saved by the browser itself,
so it survives a reload but is per-browser and per-device — and a private window
or blocked third-party storage will make the game forget between sessions.

**On Windows**, unzip the release anywhere and run `BitSorter.exe`. Nothing is
installed and nothing is written outside your own user folder.

Windows will probably warn that it does not recognise the publisher — the build
is unsigned, which is all that warning means. "More info", then "Run anyway".

Nine levels teach the ideas in order, and [a sandbox](#the-sandbox) is there for
when you would rather build something without being marked on it.

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
| `1` – `6` | Pick a gate: NOT, AND, OR, XOR, NAND, NOR |
| Drag from one port to another | Wire them |
| Right click | Delete a gate or a wire |
| Scroll on a wire, or `[` / `]` | Change its delay |
| `Enter`, or the RUN button | Run |
| `R` | Reset the board back to editing |
| `Shift`+`R` | Clear everything you built |
| `Space` | Pause a run |
| `→` while paused | Step one tick |
| `H`, or the `!` button | This level's truth table and a hint |
| `Esc` | Level list, and the way into the sandbox |
| `M` | Main menu |
| `N` | Mute the music |
| `Q` / `E` | Previous / next level |
| `F3` | Diagnostics |

### What it teaches

Nine levels, in this order. Each one is a topic from a digital systems course,
arranged so that a mechanic is always taught before it is required.

| | Level | The idea |
|---|---|---|
| 1 | Route the bit | Sources, bins, wires. A bin that must stay empty |
| 2 | The long way round | Boolean algebra and De Morgan — build a NAND without one |
| 3 | Balance the paths | Propagation delay, and the collision an unbalanced path causes |
| 4 | Four corners | K-map minimisation. Two different minimal covers both pass |
| 5 | Nothing but NAND | Functional completeness — XOR out of NANDs alone |
| 6 | The slow lane | Delay arithmetic across a deep circuit |
| 7 | Pick a lane | A multiplexer, and fan-out to two places at once |
| 8 | Half adder | Two outputs from one circuit: sum and carry |
| 9 | Carry the one | A full adder, joining two half adders and their carries |

Deliberately out of scope: assembly, datapaths, memory addressing and number
representation. Static and dynamic hazards are out too, and cannot be expressed —
a hazard needs a continuous signal model, and bits here are discrete tokens with
nowhere for a glitch to live. Unbalanced-path corruption is the lesson that
replaces it.

### The sandbox

Free play, from the main menu or the foot of the level list. Every gate, as many
as you like, no delay budget, and nothing to pass or fail.

You set up the inputs yourself: how many sources, what each one emits, how many
sinks, and how many test vectors they all run for. Click a bit in the panel to
flip it between 0 and 1.

**A readout in the corner shows what each sink actually caught**, in order, while
the run happens and after it stops. That is the point of the mode. In a level you
already know what you wanted and the verdict tells you whether you got it; here
there is no intended answer, so what came out is the only result there is.

Everything else behaves normally. Bits still collide on unbalanced paths, the
bits-lost meter still fires, and the scorch mark still names the junction — you
just do not get marked on any of it. The board is saved like any other, so a
sandbox circuit and its setup are still there next time.

Changing the number of sources or sinks rebuilds the board, and anything that no
longer has somewhere to connect is dropped. Changing a stream leaves your circuit
alone.

### Your progress

Solved levels, the circuits you built, and your best gate count and tick count
save automatically, per level. On Windows they go to a file:

```text
%USERPROFILE%\AppData\LocalLow\ZADZ\BitSorter\progress.json
```

Delete that file to start over. In a browser the same data lives in the browser's
own storage for the address you played at, so clearing site data is the
equivalent. Progress does not travel between the desktop build and a browser, and
because it is keyed to the address, the two browser links above each keep their
own.

Your progress file itself is never uploaded. Nothing is compared against anyone
else either — every number the game shows is about the circuit in front of you, or
the one you built last time.

### What it collects

The game uses Unity Analytics, which sends anonymous usage data to Unity.

Two events come from the game itself, and nothing else does:

| Event | Sent when | Data |
| --- | --- | --- |
| `levelStarted` | A level is opened | `levelName`, the level's file name |
| `levelSolved` | A level is solved | `levelName`, the level's file name |

`levelName` is the identifier, like `half-adder` — not a display title and not
anything you typed. The pair exists to answer one question: which level people
stop at.

Alongside those, Unity's SDK collects its own standard session data: a random
installation identifier, session start and end, app version, platform and
operating system, device model, language, and an approximate region derived from
your IP address. That set is Unity's, not mine, and
[Unity documents it](https://docs.unity.com/analytics/en/manual/UnityAnalyticsData).

**What is never sent:** your progress file, the circuits you build, your gate or
tick counts, your personal bests, or any account, name or email. There is no
login, and the game asks for nothing.

**To turn it off:** main menu → **Data**. It toggles between `DATA ON` and
`DATA OFF`, takes effect immediately, and is remembered on that machine. Turning
it off stops collection rather than merely hiding it — the game tells Unity's
consent framework that consent is denied, and nothing is queued or sent while it
is off.

Reporting is on unless you turn it off, so if you would rather it never ran at
all, that is the first thing to change.

---

## Developing

Unity 6.3 LTS (6000.3.11f1).

- **BitSorter → Build Windows Player** writes a player to `Build/Windows/`.
- **BitSorter → Build WebGL Player** writes a browser build to `Build/WebGL/`.
  See [Docs/distribution.md](Docs/distribution.md) for what differs in a browser,
  and what Pages, itch.io and Unity Play each need.
- **BitSorter → Publish WebGL to GitHub Pages** force-pushes `Build/WebGL/` to the
  `gh-pages` branch, via [Tools/publish-pages.ps1](Tools/publish-pages.ps1). The
  script runs standalone too, and warns if the build is older than the scripts.
- **BitSorter → Generate App Icon** redraws `Assets/Icon/BitSorterIcon.png` and
  assigns it to every standalone icon size.
- **BitSorter → Build Play Scene** regenerates the play scene from code. The
  scene is generated rather than authored, so anything added by hand is discarded
  the next time that runs.
- Tests: Window → General → Test Runner. 443 EditMode and 4 PlayMode at present.
  The PlayMode four cover pointer arbitration, which needs a live scene; if you
  ever script that run, read the results from `TestResults.xml` in the save
  directory rather than from a `TestRunnerApi` callback, which does not survive
  the domain reload that entering play mode causes.

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
