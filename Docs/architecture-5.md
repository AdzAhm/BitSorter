# BitSorter 5.0 — architecture and data model

**Status:** proposed for review in Part 0, before any 5.0 code is written. The plan it comes from was
approved on 2026-10-12.
**Decided by:** Ahmad Zoabi, from the 5.0 brief (2026-10-11); this document turns those decisions into
an architecture.
**Scope:** what 5.0 is built from and why. It is written before the code on purpose, and it changes
when a part proves it wrong — in the same change, never quietly.

---

## 1. What 5.0 is

5.0 turns BitSorter from a puzzle game into a **simulator for a university digital-systems course**
that teaches SystemVerilog with ModelSim. The game's thirty levels survive as a side section,
*Exercises*. It is a re-foundation rather than a feature release: a new simulation engine, a new board
model (free coordinates, nets, buses, modules), a new interface, a SystemVerilog front end, and the
levels demoted.

The guiding principle: good for SystemVerilog for the course, and in general, but mainly for the
course. ModelSim supplies the vocabulary and the workflow; it does not have to be matched exactly, and
where simple and friendly conflicts with exact, simple and friendly wins.

### Decided before this document (the brief, D1–D11)

| # | Decision |
| --- | --- |
| D1 | 4-state values (0, 1, x, z), event-driven, as in SystemVerilog, replacing tokens. Changes travel visibly along wires. Two drivers on a net read x. Unbalanced paths make glitches; hazards are shown, not taught. |
| D2 | Delay lives on gates, as in the course: rise and fall (`Tpdlh`, `Tpdhl`), wires ideal. A path is balanced with buffers. |
| D3 | Placement on a fine snap grid, with free placement on a key, alignment guides, pan and zoom, fit, a minimap, box select, copy and paste, rotate, flip and align. |
| D4 | Wires smooth by default, with bend points, junction dots, and the whole net lit on hover. |
| D5 | Testbenches in the course's style by default, with a switch that adds self-checks. |
| D6 | The thirty levels kept as a side section, *Exercises*, reworked for the new model and ordered by the course. |
| D7 | One time unit per tick. The display unit is a setting (ps by default, as the course's ModelSim counts -- ns until the course audit, 2026-10-19). Export adds no `` `timescale `` unless asked -- and choosing a unit other than the default is the asking (Part 9b-3). A file read with a `` `timescale `` finer in precision than its unit counts in that precision, so a delay may be a fraction of a unit (Part L7); every project's own files name their unit as their precision, so a project still counts in whole units |
| D8 | 5.0.0 on a `v5` branch, in parts, a PR per part fast-forwarded, docs fixed in the same change, every saved board migrated. |
| D9 | Blocks are either drawn (gates and wires) or written (SystemVerilog). Written blocks run, show their signals, and sit on the board like any block. |
| D10 | Reach up to the course's multicycle RISC-V, memories and `$readmemh` included. The simulator runs it; the Exercises need not teach it. |
| D11 | Radix per signal: binary, hex, unsigned decimal (and signed decimal). Display, not a lesson in number representation. |

---

## 2. The ideas, in plain words

- **4-state value.** `0`, `1`, `x` (unknown) and `z` (nothing driving). Everything starts as `x`
  until something drives it, and two drivers that disagree give `x`. That is what ModelSim shows, and
  it is what the old "collision" becomes.
- **Event-driven simulation.** Instead of advancing a clock one tick at a time, the engine keeps a list
  of "this signal changes at time T" and jumps to the next entry. A quiet circuit costs nothing. Time is
  an integer.
- **Delta cycle.** Changes with zero delay still happen in rounds at the same instant, and the rounds
  repeat until nothing changes; then time moves on. This replaces 4.x's "an edge takes at least one
  tick" as the thing that makes evaluation order irrelevant.
- **Inertial versus transport delay.** With inertial delay a gate swallows an input pulse shorter than
  its delay; with transport delay every pulse passes. SystemVerilog's gates and `assign #d` are
  inertial, and that decides which glitches are seen.
- **Non-blocking assignment, `<=`.** The right side is read now and the left side written at the end
  of the time step, which is why flip-flops do not race.
- **Elaboration.** After parsing: fix the parameters, unroll `generate`, flatten the hierarchy into the
  netlist the engine runs.
- **The oracle.** A second, trusted simulator — Icarus Verilog — runs the same design, so a
  disagreement points at a bug in ours.
- **VCD.** The plain-text format simulators dump value changes in; GTKWave opens it.

---

## 3. The layers

```
 BitSorter.View    Unity: everything on screen. The only code that touches UnityEngine.
      |  reads; turns values into colours and positions into meshes
 BitSorter.ViewModel   the workbench's view-model (Part 9): a run read at a moment, the schematic's
      |                layout and routes, pan and zoom, the waveform's arithmetic
      |  reads, and edits through commands
 BitSorter.Board   the document: modules, parts, wires, layout, stimulus, editing commands, undo,
      |            import and export, migration of older saves, the course's analyses
 BitSorter.Sim     the engine: elaboration, scheduler, processes, system tasks, history, VCD
      |
 BitSorter.SystemVerilog   the language itself: its 4-state values, lexer, preprocessor, parser,
                           syntax tree and printer

 BitSorter.LogicCore   the 4.x token engine: frozen, deleted when 5.0 is finished
```

- **`SystemVerilog`, `Sim`, `Board` and `ViewModel` are pure C# 9** — no engine references, no reflection, no
  threads, no file access of their own (files go through interfaces the host provides). They have to run
  unchanged under the plain .NET SDK, Unity's IL2CPP and a single-threaded browser.
- **The view-model's points are its own `Vec2`**, which is Unity's `Vector2` arithmetic operation for
  operation, so a position worked out under `dotnet test` is the float the editor and the browser draw.
  The plan named `System.Numerics.Vector2`; it is implemented twice, in the .NET CI runs and in the older
  class library Unity ships, and the two have not always rounded `Lerp` or a division alike. An Edit Mode
  test compares every member with Unity's, bit for bit, over a million random cases; it found `Distance`
  rounding its sum where Unity's does not, and `Distance` is now the difference's `magnitude`.
- **The layers point one way**: `SystemVerilog ← Sim ← Board ← ViewModel ← View`. The view never changes the engine or the
  document except through the document's editing commands and the engine session's controls (run,
  pause, step, seek, restart).
- **Held by tests, not by good intentions.** `Tools/ci/Repo.Tests` checks each layer's asmdef, the
  assemblies the CI projects actually compiled, and the source itself. `Tools/ci` builds each layer
  against netstandard2.1, the profile Unity compiles against, so an API Unity lacks fails in CI first.

---

## 4. Decisions

Each in the shape of an architecture decision record: what was chosen, what was not, why, and what it
costs.

### ADR-1 — New layers beside the old engine, not a rewrite of it

- **Decision.** New assemblies `SystemVerilog`, `Sim` and `Board` are built alongside `LogicCore`, which is frozen
  and deleted once the Exercises replace the old levels.
- **Rejected.** Rewriting `LogicCore` in place would break more than a hundred test files and the game
  for the whole branch. A new repository would lose the history, CI and the interface machinery that
  carries over.
- **Why.** Every part ships green, and the 4.x game stays playable until the new one replaces it.
- **Cost.** Two engines coexist in the middle of the branch.

### ADR-2 — SystemVerilog is the one representation

- **Decision.** What the code panel shows is what runs. A drawn board compiles to the same syntax tree
  its exported SystemVerilog is printed from, and that tree is what the engine elaborates. Import
  parses into it.
- **Rejected.** A separate simulation graph for drawn boards — two semantics to keep in step, the
  exact drift CLAUDE.md warns about. Interpreting SystemVerilog text on every edit — slow, and it loses
  the layout.
- **Why.** Export cannot disagree with what was simulated, and the oracle can run the printed text.
- **Cost.** The tree has to carry what the editor needs: buses, bit and part selects.

### ADR-3 — Two-plane 4-state vectors of any width

- **Decision.** A value is two bit-planes, as the simulators' own interface (VPI) stores them, of any
  width, with a fast path for 64 bits and under.
- **Rejected.** An array of single 4-state bits — 10 to 30 times slower, and heavy for a memory of a
  thousand words. `BigInteger` — no `x` or `z`. A single 64-bit word — the course's own ripple carry
  is 65 bits wide.
- **Cost.** Operators need care; a differential test holds them to a slow, bit-by-bit reference.

### ADR-4 — The kernel

- **Decision.** Events are ordered by time, then region, then a stable id. Regions run Active, then
  Inactive (`#0`), then NBA, repeated until all are empty, then time advances. Gate and `assign #d`
  delays are inertial. A delta limit turns a zero-delay loop into a diagnostic, never a hang.
- **Rejected.** A tick loop — it cannot express rise and fall delays or a `#5` in a testbench.
  Transport-only delay — it would break the glitch lesson and disagree with ModelSim.
- **Cost.** Order inside a region is stable and the same on every run, but on code with a race in it
  (a testbench reading a signal in the same instant another process writes it) it may differ from
  another simulator's. That is documented, and flagged where lint can see it.

### ADR-5 — A process is a flat instruction array and a program counter

- **Decision.** Procedural code is lowered to jumps: `if`, `case`, `for` and `forever` become branches.
  `#d` and `@(...)` save the counter and return to the scheduler.
- **Rejected.** C# iterators — an allocation per statement, and hard to snapshot. Threads — a browser
  build has one. Walking the syntax tree recursively — it cannot stop in the middle of a statement.
- **Cost.** A small compiler. In return: no allocation per step, easy time-slicing, and a process that
  can be snapshotted if it is ever needed.

### ADR-6 — Run ahead, record, replay

- **Decision.** The engine runs in time-boxed slices and records a history: every value change, and
  every **transition in flight** — when a change was scheduled, when it is due, and whether it arrived
  or was swallowed. The view reads only the history, so rewind, scrubbing and stepping back are
  lookups. An edit to an input or to the board is a deterministic replay from time 0.
- **Rejected.** Periodic snapshots of the engine — complicated by process counters, and not yet
  needed. The view driving the engine a tick at a time — it ties the frame rate to the simulation, as
  4.x does.
- **Why.** 4.x already treats a simulation as disposable, rebuilt from the board after every edit;
  replay is the same idea. And the look needs transitions: a change has to be drawn *leaving* the gate
  that caused it and travelling for that gate's delay — and a swallowed glitch dying on the wire — which
  a log of value changes alone cannot draw.
- **Cost.** History takes memory: it is capped, and recorded per signal for large designs. Snapshots
  only if measurements demand them.

### ADR-7 — The board is a document; nets and code are derived

- **Decision.** The board stores modules, parts and connections, with the layout beside them. Nets are
  derived from connections; SystemVerilog is derived from the document. Saved as a versioned record
  (`format: 5`).
- **Rejected.** Storing SystemVerilog text with a layout file beside it — names and positions are
  fragile across a round trip, and migration is harder. Keeping 4.x's cell blueprint — it rules out free
  placement.
- **Cost.** A printer and an importer to keep in step with the model, held by round-trip tests.

### ADR-8 — Render from a view-model; a look switch reloads the scene

- **Decision.** The renderer reads a view-model built from the document, the layout and the history.
  Choosing a look in Settings stores it and reloads the scene behind a fade. Since Part 9 the view-model is
  a layer of its own, `BitSorter.ViewModel`, so it is tested by `dotnet test` in seconds; the view keeps
  only what needs Unity -- the meshes, the colours, the labels, and the player's look and reduced motion
  (`CanvasMotion`). `Tools/ci/Repo.Tests` refuses a file under `View/Workbench` that uses no Unity at all,
  unless it is listed there with its reason.
- **Rejected.** Rebuilding every renderer in place: each caches its colours as it builds, and the rule
  that a palette is never changed in place stays.
- **Cost.** A reload, with the session restored after it.

---

## 5. The data model

### SystemVerilog (`SystemVerilog`)

```
Logic4        0 | 1 | x | z
LogicVec      width, two planes, signedness; every operator per the standard, x-propagating
Literal       8'hA5, 4'b1x0z, 'd7, underscores; printed in binary, hex, unsigned or signed decimal
Syntax tree   modules, ports, parameters, declarations (widths and array dimensions), instances,
              gate primitives with delays, continuous assigns, always / always_ff / always_comb /
              initial, generate-for, enum typedefs, includes. Every node knows its file, line and column.
Printer       syntax tree -> SystemVerilog in the course's style (section 7)
Diagnostic    severity, file, line, column, code, message
```

**Values, as built in Part 1.** `Logic4`, `LogicVec`, `Literal`, `Radix` in `Assets/Scripts/SystemVerilog`.

- **Storage.** Up to 64 bits a value is two words inside the struct and no operator allocates — a
  guardrail test measures every one at zero bytes. A wider value keeps both planes in one array that is
  never written after it is made, so copies share it.
- **Width.** An operator works at its operands' width: a binary one extends both to the wider,
  sign-extending only when both are signed (IEEE 1800, 11.8.1). Sizing an expression by its context —
  `r = a + b` adds at the widest of r, a and b — is the elaborator's job, done by extending each operand
  before the operator runs (`ExpressionBuilder`, Part 4b; section 7).
- **x and z.** A z operand bit reads as x. Bitwise operators follow the standard's truth tables, so a
  known 0 still wins an AND; arithmetic with any unknown bit, or division by zero, is all x; `==` is 0
  as soon as a known bit differs and x only when unknown bits could decide it; `===` is never x; a
  condition with a known 1 is true; an unknown condition merges the two choices bit by bit; an unknown
  shift amount or index gives x; `>>>` fills with the sign only on a signed value.
- **Literals.** IEEE 1800, 5.7.1: plain decimals are signed and 32 bits, a short literal pads with x or
  z only when its leftmost digit is x or z, and truncation warns when it loses a bit. `'0 '1 'x 'z`
  are reported as filling their context.
- **Checked three ways.** Worked examples of each rule; a slow second implementation written from the
  standard's tables and `BigInteger`, held equal on thousands of random values of widths 1 to 130
  (`Tools/ci/SystemVerilog.Tests`); and about 1,600 generated cases run under Icarus and compared bit
  for bit (`Tools/ci/Oracle/OperatorOracleTests`). Icarus agreed on every one.

### The engine (`Sim`)

```
Time          an integer number of time units; the display unit only labels it
Signal        id, hierarchical name, width, net or variable, its drivers, its value
Driver        one source of a net's value; a gate output is a driver with a pending event
Process       gate | assign | always_comb | always_ff | always | initial;
              a program (instruction array), a counter, and a state: runnable, waiting on a delay,
              waiting on an event, or done
Event         (time, region, id) -> apply a driver's value | wake a process | commit a non-blocking write
Elaborator    syntax tree -> design: parameters resolved, generate unrolled, hierarchy flattened,
              widths typed
Session       run to a time within a budget | step | step back | seek | restart | replay with new stimulus
History       per signal, every value change; every transition in flight (scheduled at, due at, value,
              applied or swallowed); every message ($info, $error, lint) with its time and place
Files         an interface for $readmemh and $writememh: in memory in tests and in the browser
VCD           written for export, read for the oracle comparison
```

**The structural engine, as built in Part 2.** `Simulator`, `NetlistBuilder`, `Design`, `Stimulus`,
`History`, `SimSession`, `VcdWriter`/`VcdReader` in `Assets/Scripts/Sim`.

- **Two phases a delta.** Within a time step every delta first applies everything due — outputs whose
  delay is up, testbench writes — then evaluates every driver that reads a signal that changed, and
  each driver schedules only its own next output. Nothing sees another's half-done work, so the order
  drivers are visited in cannot change the result: invariant 1, held by building random circuits in
  shuffled orders. It also gives Icarus's answer where a pulse exactly as long as a delay meets it.
- **Inertial delay** as section 6 says, each driver with at most one change on its way; a cancelled one
  is recorded as swallowed where it was cancelled.
- **Drivers, nets and variables.** A gate or continuous assignment drives one signal, or a slice of one
  (Part 4b): `assign y[3] = a`, a gate whose output is a bit of a vector, a module output connected to
  part of one. A gate's inputs are one-bit expressions, so it reads a bit of a vector directly. A net
  resolves its drivers (`LogicVec.ResolveWire`), each contributing z outside its slice, and reads z with
  none; a variable's bits take at most one continuous driver each, and `NetlistBuilder` refuses a second
  with the way out. Every 4-state signal starts at x, an undriven net at z -- and so does a top-level input,
  declared a variable for a stimulus to write, until one does (an input port is a net, IEEE 1800,
  23.2.2.3) -- and a net driven in slices at x
  where a driver reaches and z where none does -- as Icarus shows it. A 2-state one (`bit`, `byte`,
  `shortint`, `int`, `longint`, and an enum that names no base type, which is an `int`) starts at 0 and
  holds an x or a z written to it as 0 (`SignalInfo.TwoState`, applied where every value a signal takes is
  shaped; found by the course audit, 2026-10-19). Every driver evaluates once at time 0.
- **Running.** `RunTo` runs whole time steps until a time or an event budget, so the browser can run in
  slices; a delta limit stops a zero-delay loop with a message naming what was still changing. A
  `Stimulus` writes inputs from outside, as the oracle's structural runs do; from Part 5a a testbench's
  own `initial` blocks do it instead.
- **Recorded.** Every value change with its delta, every transition with its outcome; rewind and step
  back read them. Once its arrays have grown, a run with recording off allocates nothing (a test holds
  it), and recording grows its buffers by doubling. On the development machine a 64-bit ripple adder of
  320 gates ran at about 1.4 million events a second.
- **Checked.** The probe circuits' waveforms by hand and against Icarus's VCD, signal by signal
  (`Docs/oracle-notes.md`); wire resolution and every primitive by table; replay, slicing and VCD
  round trips; the zero-delay loop; allocation and throughput floors (`Tools/ci/Sim.Tests`).
- **What the waveform needs to know about a signal** (Part 9b-2a), kept by the elaborator, which until
  then kept only the flattened, dotted names. `Design.Scopes` lists every scope it made, in the order it
  made them, each after its parent: the top, each instance, each generate block (`slice[0]`, `genblk2`)
  and each named block in a process -- a fourth kind beyond the plan's three, since what such a block
  declares has no other scope whose path is its prefix. A `ScopeInfo` has its `Path`, exactly its
  signals' prefix, its `Name` in its parent, its `Module` (an instance's or the top's) and its `Parent`.
  A `SignalInfo` gains its range as written (`Msb`, `Lsb`: `[0:7]` is 0 and 7), its `Port` direction or
  none, its `Scope`, and its `Enum` (`EnumInfo`: the type's members in order, `TryName` finding the one a
  value holds bit for bit), through a typedef or in place, made per instance since a parameter can change
  the members' values. A circuit built in code has none of them: `[Width-1:0]`, no scope, no enum. None
  of it touches the run, the history or the VCD (`SignalFactsTests`).

**Processes, as built in Part 5a** (`Process.cs`, `Simulator`, `Elaboration/Elaborator.Processes.cs`).

- **A process is instructions and a counter** (ADR-5): a blocking write, a nonblocking write, a jump
  (`if`, `case`, the loops), a delay, an event control, `wait`, `$finish`, the end. Expressions are the
  same `Expr` trees continuous assignments evaluate, so a process allocates nothing either once warm (a
  test holds it). A select worked out as the process runs (`y[i] = a`) writes where it lands, and nothing
  if that is unknown or outside the variable.
- **A third phase each delta.** Drivers are evaluated first, then every process woken runs, in a fixed
  order, until it waits or ends. A blocking write is seen at once by the process and the processes after
  it, and by drivers in the next delta; so the drivers keep invariant 1, and processes -- which the
  language lets race -- run in an order that is the same on every run: **an instance's processes before
  its parent's own, and a module's own as written**, as Icarus starts them. Among a scope's instances its
  interfaces' are made first (Part L8), since a module given one through a port needs it made, so their
  processes come before those of the modules beside them. A testbench that raises reset
  at time 0 therefore reaches the flip-flops of the design it instantiates. A `fork`'s branches (Part L5)
  are processes of their own, each written after the process that forks it, and start straight after it
  in the order written, once it waits -- Icarus 12 starts them last first; the standard leaves it open. At time 0 the processes start
  once what has no delay has settled from the starting values (from Part 6a, as Icarus starts them), so
  an `initial` that reads a wire at time 0 sees it worked out, not x.
- **Regions.** A nonblocking write is worked out when it runs and applied once nothing active is left in
  the time step; `#0` waits for that moment too, and runs before the nonblocking writes (IEEE 1800, 4.4).
  `q <= #3 d` takes `d` now and writes it three units on; `a = #3 b` takes `b` now, waits, and writes.
- **Waiting.** Every wait's signals are known when the design is built, so a change wakes exactly the
  processes waiting on it, with no list that grows. An edge is the standard's on the lowest bit, x and z
  included; an event on an expression of several signals is judged once every change of the delta is in.
  `always_comb` runs once at time 0 and then on any change of what it reads but does not write; `@*` waits
  on everything its statement reads.
- **Never hangs.** A process that runs a million steps without waiting stops the run with its name, and
  so does a zero-delay loop of processes at the delta limit. `$finish` and `$fatal` end the run
  (`RunStatus.Finished`) with a message saying which and where, once the time step they ran in has
  settled -- every region, as Icarus finishes it -- so the last values agree with each other.
- **`$stop` suspends the run** (`RunStatus.Suspended`, Part 9b-3; Ahmad's choice, 2026-10-16, as ModelSim
  does it): the rest of its time step happens as it would for `$finish`, the process that called it waits
  at the next statement, and the next `RunTo` carries on from there in the same time step, its deltas
  numbered on from the suspended step's so the last change of the time is still the one it settled on.
  `History.Suspensions` keeps every time it happened. Icarus under `vvp -n` ends the run at a `$stop`, so
  the oracle, which compares up to it, sees no difference.
- **Variables** may be declared with a value (`logic a = 0;`), held from before time 0. A process's own
  working variables -- a `for` loop's `int i`, a `repeat`'s count, a value held across an intra-assignment
  delay -- are `Local`: not recorded, not in a VCD. A variable takes its value from processes or from one
  continuous driver, never both, and only a variable can be written by a process.
- **Checked.** 30 tests (`ProcessTests`, and a guardrail): each statement, the regions, the edges, the
  start order, intra-assignment delays, the stops, and every refusal. Against Icarus
  (`BehaviouralOracleTests`): the scheduling probes (`corpus/behavioural/scheduling.sv`), the traffic
  light's own testbench, and the delay probes read from their SystemVerilog, every signal agreeing.

**The multiplier and the first lint check, as built in Part 5b.**

- **Ports keep a process's passing values to themselves.** An `always_comb` that gives `busy` a default
  of 0 and then sets it to 1 has it at 0 for an instant each time it runs. A port is a continuous
  assignment, so a testbench waiting on `@(negedge busy)` across it never sees that instant -- which is
  what the course's own exercise needs, and what ModelSim must do for it to run. Icarus joins ports into
  one net and lets it through; the standard and the course win (`Docs/oracle-notes.md`).
- **The corpus multiplier** (`corpus/behavioural/multiplier.sv`, written from scratch in the shape of the
  course's second exercise): a Mealy controller and a shift-and-add datapath in a top module, and a
  testbench with a clock generator, reset held for four cycles, inputs on the falling edge, and a wait on
  `@(negedge busy)` after each of four products. It agrees with Icarus on every one of its 36 signals.
- **Lint: a latch where logic was meant to be combinational** (`Elaborator.Lint.cs`, `SV0401`, a
  warning). An `always_comb` or `always @*` that assigns a variable on some paths and not on others makes
  it hold its value. The check follows `if` and `case`, names each such variable, and gives the way out:
  a value at the top of the block. It errs towards silence -- a `case` is complete with a `default`, as
  `unique` or `priority`, or when its labels name every value of its subject (an enum missing a state is
  a latch); a `for` loop that runs at least once counts; `always_latch` is left alone.
- **A cast to a typedef** (`state_t'(n)`) sizes and signs the value as the type says.
- **Checked.** 11 more tests: the lint's cases, the course's `busy` pattern (released when `busy` really
  falls -- Icarus, given the same file, releases at the first dip), the same instant seen by a wait in the
  one module, as both simulators see it, and `$finish` settling its step.

**Memories, files and the transcript, as built in Part 6a** (`Formatting.cs`, `MemoryImage.cs`,
`SimFiles.cs`, and the elaborator's names).

- **A memory is its words.** `logic [31:0] mem [0:1023];` (or `[1024]`, or `[1023:0]`) is a `MemoryInfo`
  and one variable a word, side by side in `Design.Signals`, named `mem[0]` on. A write is a write to a
  word, recorded and waking readers as any variable's is; `mem[i]` with `i` known only as the design runs
  reads the word it picks, or x outside the memory, and is woken by a write to any word. A process may
  write `mem[addr]` and a part of a word; an assign needs a constant word. A VCD leaves the words out, as
  Icarus does.
- **Files are the run's** (`ISimFiles`, in `SimOptions.Files`; `MemorySimFiles` in tests and the
  browser). `$readmemh` and `$readmemb` load from the lowest word up (or a range given, either way),
  with `@` addresses, `x`/`z`/`?` digits, underscores and both comments; `$writememh` and `$writememb`
  write a word a line. A missing or malformed file is an error in the transcript and the run goes on, as
  a testbench's file is the testbench's business; a short one, or one too long, is a warning.
- **The transcript.** `$display`, `$write`, `$strobe` and `$monitor` (and their `b`, `h`, `o` forms),
  `$info`, `$warning`, `$error` and `$fatal` write lines into the history's messages, marked by
  severity. Formats are read when the design is built: `%d %b %o %h %x %c %s %t %m %%`, widths, `-` and
  `0`, and every string argument a format for the ones after it. The widths and x/z digits are Icarus's,
  and the oracle compares every line printed. `$strobe` and `$monitor` print once their step has
  settled, the monitor first; `$monitor` only when a value other than the time changed.
- **`$time`** (and `$stime`, `$realtime`) reads a hidden signal the engine sets as time moves, with no
  event, so an assign of it is not woken by it -- as the standard has it. Since Part L7 each reads it in
  its own module's units.
- **Names through the hierarchy**: `dut.ctl.state`, `slice[3].s.sum`, `top.x`, read and written, in
  processes and assigns. Processes, and assigns that use such a name, are compiled once their module's
  instances are in, so a testbench may name `dut.x` above the line that makes `dut`. Since Part L8 a task or
  function of an instance is called the same way, `dut.reset()` and `u.f(x)`.
- **A typed parameter is its value's context** (a Part 4b fault found here): `localparam [5:0] ONES = '1`
  is six ones.
- **Checked.** 30 tests (`SystemTaskTests`, two more elsewhere); and against Icarus,
  `corpus/system/printing.sv` and `corpus/system/memories.sv` with their images, every signal and every
  line printed agreeing.

**The processor and the browser's budget, as built in Part 6b** (`corpus/processor/`, `SimBenchmark`,
`BenchmarkBuild`).

- **An original multicycle processor** (`corpus/processor/mcpu.sv`, written from the published RISC-V
  specification in the shape of the course's third exercise, nothing taken from it): a controller that is
  an eleven-state machine, a datapath with its instruction, A, B, ALU-out and data registers between the
  steps, a register file, and instruction and data memories of 1,024 words each, loaded with `$readmemh`
  from images an instance names by parameter. Its opcodes and ALU operations are `localparam`s in an
  included file (`mcpu_defs.svh`). It runs the RV32I a program needs -- `lui`, `auipc`, `jal`, `jalr`,
  every branch, `lw`, `sw`, and every arithmetic, logic and shift operation -- and its program sorts eight
  signed words, sums them, multiplies by shift and add, counts Fibonacci, and takes every kind of branch.
- **It says it is done by a write**, as the course's processors do: the program stores to `0x7fc`, and the
  testbench, watching the data memory on the falling edge, prints the cycles taken, checks the 23 results
  against `mcpu_expected.hex` through the hierarchy (`dut.dmem.mem[128+i]`), prints PASS or FAIL and
  finishes. A timeout ends a program that never gets there.
- **A string is a value** (IEEE 1800, 5.9): eight bits a character, the first in the top byte, padded and
  cut on the left as any unsigned value is. That is what lets `parameter IMAGE = "mcpu_program.hex"` carry
  a file name to `$readmemh(IMAGE, mem)`; a string had been refused until now.
- **Agreement.** Icarus and BitSorter both run it 2,716 cycles, to its finish at 27,160, and agree on
  every one of its 98 signals' 124,783 settled changes and on the three lines it prints.
- **The performance gate.** Read, elaborated and run to its end with everything recorded, as the workbench
  will run it -- 111,141 events. Measured on 2026-10-13 on a laptop (an i9-13900H) with other heavy work
  running, so each figure is a range: most of the spread is which core a single thread lands on, a
  performance core or an efficiency core.

  | Where | Read and elaborate | Simulate | Cycles a second | Events a second |
  | --- | --- | --- | --- | --- |
  | .NET 10, Release (CI's test, best of five) | 8--9 ms | 188--206 ms | 13,000--14,500 | 540,000--590,000 |
  | Browser: WebGL, one thread, the game's own build settings (best of five, four loads) | 9--21 ms | 77--194 ms | 14,000--35,000 | 570,000--1,440,000 |

  A page's first run is slower while its code warms: 36--77 ms to elaborate, 109--236 ms to simulate.
  Elaborating is one piece of work on a compile, not sliced; at these sizes it fits in a frame or two.

  **Sliced, as the workbench must run it.** Run again in slices of what the load's own best rate does in
  8 ms (4,583 to 11,547 events), the processor took 10 to 25 frames, and the longest slice 10 to 16 ms:
  inside a 60 Hz frame, so the page kept drawing, but up to twice the aim. An event count is a poor
  clock -- the first slice also reads the memory images, and a slice that lands on a slower core takes
  longer. **So the workbench slices by its own clock** (Part 9): `RunTo(time, maxEvents)` in chunks of
  about a thousand events, as many as fit in 8 ms of the frame, which can overshoot by one chunk -- a
  millisecond or two -- and never by a slow machine's whole frame.

  **Built in Part 9b-1** (`ViewModel/RunSlicer`, `RunSession`), with three rules found on the way:
  - **The clock is handed in**, so a test drives it; the view passes real time.
  - **The horizon is always finite.** `RunTo(long.MaxValue, budget)` never returns on a design whose events
    run out without a `$finish`: with nothing queued the next event is at `long.MaxValue`, never past the
    time asked for, and each step runs nothing, so no budget is spent. The workbench runs to where the
    testbench stops, `Stimulus.StopTime`: the end, or a step after it.
  - **The model follows the run as it is made** (`WorkbenchModel.Grow`, `SignalTrack`): a slice ends only
    between time steps, so each signal's settled changes can be read a piece at a time and come out as
    `History.SettledChanges` gives them. A moment past where the run has got waits for it, and a model
    grown at slices of one event answers every moment as one made in one go (`Tools/ci/ViewModel.Tests`).
  - **The reference circuit is made in its first slice**, inside the call that opens it, so it opens
    exactly as before.
  - **The waveform's rows are drawn again only when the moment passes where they were drawn to**: nothing a
    run adds is earlier than where it had got, so redrawing them each slice cost 3 ms a frame for nothing.
    A frame while the run is being made is 3.9 ms on the 300-wire board, inside the canvas's 6.

  **Memory.** The history keeps about 80 bytes an entry -- a value change, a transition or a line printed
  -- and this run keeps about 230,000, 19 MB; the page's whole managed heap after six runs was 46 MB. A
  browser build may have 2 GB, and a testbench with no `$finish` run to a far-off time would fill it and
  take the page down. **`SimOptions.MaxRecorded`** stops a run that has kept more than 4 million entries,
  about 320 MB, with a message in the transcript saying so and the way out -- run to an earlier time, or
  end the testbench with `$finish` -- which is invariant 2 extended to memory.

  **The budget, set from this:** a course processor's program of a few thousand cycles finishes in the
  browser in under half a second at the slowest figure measured, drawing every frame as it runs.
  `TheCorpusProcessor_RunsFastEnough` holds .NET above 2,000 cycles a second, a seventh of the slowest
  measured, generous for a shared CI machine. The browser's figure is measured, not tested: build
  `BitSorter/Benchmark/Build WebGL Benchmark` (or `BenchmarkBuild.BuildFromCommandLine` in batch mode),
  serve the folder, open it with the page in front, and read the console's `BitSorter benchmark:` line.
- **Checked.** The processor against Icarus (`BehaviouralOracleTests`), the latch check over it
  (`LintTests`), its rate and the history's cap (`GuardrailTests`), and a string as a value naming a
  memory's file through a parameter, in a width that is not whole bytes too (`SystemTaskTests`).

### The document (`Board`)

```
Project     name, modules, the top, the testbench, settings (time unit; default gate delay 1)
Module      name, drawn or written, ports
              drawn:   parts, connections, layout
              written: SystemVerilog source, parsed by the SystemVerilog layer
Port        name, direction, width, comment
Part        id, name (g1, g2 ...), kind, parameters, terminals
              gates (and, or, nand, nor, xor, xnor, not, buf, bufif0/1, notif0/1), constant, clock, reset,
              lamp, splitter, joiner, select, mux, register, memory, a block, a written block
              a gate's delay is rise and fall: 0 prints as a primitive, more as a library cell
Connection  from a terminal to a terminal, or to a tap on another wire (a junction dot);
              a net is everything connected, with an optional name
Layout      beside the module, never in it: each part's position (cell units, on a quarter-cell snap
              or free), rotation and flip; each port's place on the frame; each wire's bend points and
              whether it is drawn smooth; the view's pan and zoom
Testbench   generated from a stimulus, or a written module
Stimulus    clocks, resets, and per input a list of (time, value) steps; an end time; whether to add
              self-checks. Printed as a course-style `initial` block, compiled as the same tree.
Exercise    a stable id (analytics uses it), title, goal, hint, order, chapter; the module and ports to
              build; the parts allowed and how many; an optional broken circuit to start from; the
              streams it is tested with and what is expected (x where anything goes); the clock;
              scoring by gate count and settle time, never a ranking
```

**The document, as built in Part 7a** (`Assets/Scripts/Board`).

- **Immutable, all the way down.** `Project`, `Module`, `Part`, `Wire`, `Layout` and `Stimulus` are C# 9
  records (`IsExternalInit.cs` gives netstandard2.1 what they need); an edit makes a new document that
  shares everything it did not touch. A module gives each part and wire an id once, from `NextId`, and
  never again.
- **The parts built:** a port (`PortPart` -- a port is a pin on the board), a gate (the eight primitives,
  one to eight inputs, any width, rise and fall), a constant, a register (`d`, `clk`, an optional
  asynchronous active-high `rst` with its value, a starting value, and since Part 14a a clock to Q and a
  setup), a mux of two, four or eight, a
  splitter and a joiner bit by bit, a lamp, and a block of another module. Memory, select and written
  blocks come with the parts that need them (Part 12). The four tri-state gates came in Part 8, so the
  canvas could show a z: a data input `a` and a control `en`, one bit wide, printed as the primitive
  with its delay (`bufif1 #(1, 2) g1 (q, d, en);`) because no cell of the library is one. A net with
  more than one driver is a bus since Part 14f: allowed when every driver can let go -- a tri-state gate, or
  since Part 14g a block's inout pin -- and printed as a `tri`, as any wire an inout pin is on is.
- **A clock is not a part.** The model above lists clock and reset parts; as built, a clock is an input
  port that the stimulus drives as a clock (`ClockTrack`), and a reset is an input it gives a pulse. That is
  how the course's testbenches make them, and it keeps a drawn module synthesizable: nothing in it
  generates its own time. The canvas (Part 8) may draw such a port as a clock.
- **Nets are worked out** (`Netlist`), by union-find over pins and wires, a tap joining the wire it is on.
  They are named in three passes, so a made name never takes a chosen one: a port's name; then a name
  the player gave a wire; then the pin that drives it, `g1_y`, which stays put as other parts come and go,
  so a signal picked out in the waveform keeps its name across edits.
- **One rule for every edit** (`BoardEditor`, `BoardRules`): the editor builds the document an edit would
  make and checks it; an error it did not have before refuses the edit, with that error as the reason.
  Widths that disagree, two drivers on a net, a bad or taken name, a module that contains itself, a
  stimulus for a port that is not there. An input left open, or a vector gate with different rise and
  fall (where simulators part, section 6), is a warning. Renaming a port renames it on every block of its
  module and in the stimulus; renaming a module renames its blocks and the top.
- **Undo keeps whole documents**: a reference each, a drag one step, a refused edit none, two hundred
  deep.
- **Compiled to one syntax tree** (`BoardCompiler`), which `Printer` prints and `Elaborator` runs: in the
  course's style, an ideal one-bit gate as a primitive, one with delay as a cell of BitSorter's library
  (`CellLibrary`, inputs A to D, out Z, `Tpdlh` and `Tpdhl`), anything wider as an `assign`, a register as
  `always_ff`, a block as an instance by name, an output sharing an input's net fed by an `assign`, and the
  stimulus as a testbench (`<top>_tb`, `dut`, `always #half clk = ~clk;`, steps in an `initial`, `$stop`).
  An open pin reads a net of its own, x; a block's open pin is left open, `.a()`. A register with a
  starting value whose q is a port gets a variable of its own, because a port cannot be given one.
- **Printed a module a file** (`BoardPrinter`), the cells in `bitsorter_cells.sv`, in the order a
  simulator reads them; `SyntaxReader.ReadAll` reads such a list back as one design.
- **The first importer** (`BoardImporter`): structural SystemVerilog drawn as a board -- ports, nets,
  primitives, the course's cells with their delays, instances of other modules, and the shapes of
  `assign` and `always_ff` the printer writes. A net keeps the name it was written with. Anything else is
  refused with what and where (a module with behaviour comes in as written code, Part 12).
- **Checked.** 55 tests (`Tools/ci/Board.Tests`): nets and names, every refusal, undo, the printed text,
  and the sample boards -- a half adder ideal and with delays, a full adder of two half-adder blocks, a
  Johnson counter on a clock with a reset, buses through a mux, splitter and joiner, a four-way mux. Each
  is printed, read back and run with **the same history, change for change**, as compiled and run
  (invariant 4); imported from its own print and printed again to **the same text**; and run under
  **Icarus** from its printed files, every signal of design and testbench agreeing (`BoardOracleTests`).
  The corpus's hand-written `mux2.sv` imports as its gates and runs as the file does, glitch included.
- **Found on the way:** the printer put an `if` with an `else` on its head's line, the `else` adrift; the
  reader dropped the comment on a port-less module's header; and Icarus's rule for a vector's delay is the
  longest delay of the bits that change, not the lowest bit (`Docs/oracle-notes.md`).

---

## 6. Time and delay

The contract the engine is tested against, by hand-computed waveforms first and Icarus second.

| Rule | Behaviour |
| --- | --- |
| `#(rise, fall)` on a gate | Chosen by the output's **new** value: to 1 takes rise, to 0 takes fall, to `x` the smallest, to `z` the turn-off delay (the smaller of rise and fall when none is given). A single `#d` applies to every transition; none means 0. |
| A vector's delay | By the whole value (IEEE 1800, 28.16): from nonzero to all zeros takes fall, to all z turn-off, anything else rise. Icarus decides by something like the lowest bit instead; the standard wins (`Docs/oracle-notes.md`). The course never delays a vector. |
| Inertial | A new evaluation cancels the gate's pending output event, and schedules a new one only if the new value differs from the output's current value. A pulse shorter than the delay is swallowed, and the history records it as swallowed. |
| `assign #d` | The same rule as a gate. With no delay, the update is in the Active region. |
| `#d` in procedural code | A plain wait. |
| Two drivers on one net | The standard resolution: equal values stay, `0` against `1` is `x`, `z` gives way to anything, `x` wins. Only a `wire` may have two: a `logic` is a variable, and a second continuous driver on one is an error (IEEE 1800, 6.5) that Icarus refuses to compile. |
| Zero delay | Delta cycles; past a delta limit in one time step the run stops with a diagnostic naming the loop. |
| Library cells | Rise and fall default to 1, as the course's cells do. A freshly placed gate is a cell with delay 1; "ideal" (0) is one switch. |
| Order inside a region | Stable, by creation, the same on every run. Where Icarus and the standard disagree, the standard wins, and the difference is written down in `Docs/oracle-notes.md`. |
| A delay in a fraction of a unit, `#2.5` (Part L7) | A tick is the finest unit or precision any module's `` `timescale `` names. A module's delay is its value in its own units scaled to ticks and rounded to its own precision, half away from zero (IEEE 1800, 22.7); with no `` `timescale `` a tick is a unit, so a fraction rounds to a whole one. `$time` gives a module's own units, rounded; `$realtime` the fraction too; `%t` writes a time in ticks. Icarus agrees on all of it (`corpus/behavioural/reals.sv`). |

**Confirmed against Icarus** (`Docs/oracle-notes.md`): eleven probe circuits, from a pulse swallowed to
the reference visual's glitch, give the same waveform in both, signal by signal; and from Part 4b the
structural corpus, read from its SystemVerilog by both, under random 4-state stimulus with gaps shorter
than its slowest path (section 7). The first fact pinned,
in Part 0: an XOR with rise 2 and fall 3, inputs at 0 from time 0, reads `x` until time 3 (a fall),
rises at 12 when an input rises at 10, falls at 23 when it falls at 20.

---

## 7. The SystemVerilog it reads and writes

The subset is bounded by what the course's own code uses: a scan of its 53 sources (counts only) found
vectors up to 65 bits wide, delays given as parameters (`#PERIOD`, `#(Tpdlh, Tpdhl)`), `>>>` applied
to operands never declared `signed` (which the standard makes a *logical* shift), `x` only in `casex`
patterns, and no tri-state, `function`, `task`, `$display`, `` `timescale ``, `defparam` or `===`.

| Tier | What | Part |
| --- | --- | --- |
| A — structural | modules and ANSI ports; `logic` and vectors; `parameter` and `localparam`, in `#(parameter ...)` headers too; gate primitives with `#(rise, fall)`; `assign`; instances, by name and by position, with `#(.P(v))`; `generate for` with `genvar` and a named block; `` `include ``; concatenation and `{n{x}}`; bit and part selects; sized and unsized literals | 4 |
| B — behavioural | `always_ff @(posedge clk, posedge rst)`; `always_comb`; `initial`; plain `always`; `begin`/`end`; `if`/`else`; `case` with `default`; `for`, `repeat`, `forever`; `=` and `<=`; `#delay`; `@(posedge` and `negedge)`; `typedef enum`; `?:`; arithmetic, shifts, comparisons, bitwise and reduction operators | 5 |
| C — the processor | memories; `$readmemh` and `$writememh`; `casex`; `signed` and `>>>`; `$info`, `$error`, `$stop`, `$finish` with `%X %d %b %h`; hierarchical references; timeouts | 6 |
| Syllabus extras | tri-state (`bufif`, `notif`, `tri`), `integer`, `$time`, `` `timescale `` read as a display hint -- and, from L7, a finer precision as the tick a file counts in -- `**` | a `bufif` or `notif` alone on its net is drawn, printed and imported from 8; several on one net, a bus, and `inout` in written code from 14f |
| Functions and tasks | `function` and `task`, static and automatic, ANSI and old-style arguments, defaults, `return`, `void` functions, recursion inside an expression | L2 |
| Expressions | `inside` and `case inside`, `==?` and `!=?`, casts to the built-in types and to typedefs, an enum's methods, `$countones`, `$onehot`, `$onehot0`, `$isunknown`, `$random`, `$urandom`, `$urandom_range` | L3 |
| Data types | packed `struct` and `union`, their members read and written (`pkt.kind`, `prog[i].op`); packed arrays of any number of dimensions, of structs and of typedefs; memories of more than one dimension; `foreach`; assignment patterns, `'{...}`, in order, by member, by index and with `default:`; `$bits` of a type or a memory | L4 |
| Testbench control | `fork` with `join`, `join_any` and `join_none`; `wait fork`, `disable fork`; `disable` of a named block, a named fork or a task; named events with `->`, `->>`, `@(e)` and `e.triggered`; `force` and `release`; immediate `assert`, `assume` and `cover` | L5 |
| Reals and time | `real`, `realtime` and `shortreal` (held as a real), real parameters typed or by their value, real ports and functions; arithmetic and comparison with whole numbers made reals; conversion both ways, rounding half away from zero; `$itor`, `$rtoi`, `$realtobits`, `$bitstoreal` and the maths functions; `%e`, `%f`, `%g` with a precision; delays in fractions of a unit, `$realtime` | L7 |
| Interfaces | `interface` with parameters, ports, signals, assigns, processes, functions and tasks; its instances, in generate loops too, reached by name; modports of `input`, `output`, `inout`, `ref` (read as `inout`) and `import`; interface ports with a modport or without, `interface` for any, a modport chosen at the connection, a port passed on; a task or function of an instance called through it, `bus.send(v)`, `dut.f(x)` | L8 |
| Packages, macros, strings, files | `package` of parameters, typedefs, functions and tasks; `import p::*`, `import p::name`, in a module's header too, and `p::name`; macros with arguments and defaults, ``` `` ``` and `` `"...`" ``; `string`, joined with `{}`, compared as text, with its methods; `$sformatf`, `$sformat`, `$swrite`; `$fopen`, `$fdisplay`, `$fwrite`, `$fstrobe`, `$fclose`, `$fflush` | L6 |
| Not supported | interface classes, virtual interfaces, unpacked structs, classes, concurrent assertions, `defparam`, DPI — each refused with file, line and a plain message, until the language part that brings it (section 11) |

**Written in the course's style:** an ANSI header with inputs first and outputs last, one commented
port per line; `logic` everywhere, except a net with more than one driver, which must be a net to
compile at all (section 6) -- a bus is printed `tri logic` (Part 14f), and so is a wire a block's inout pin is
on (Part 14g); vectors MSB first; an ideal gate as a primitive (`and(z, a, b);`), a
gate with delay as a library cell (`NAND2 #(.Tpdlh(5), .Tpdhl(5)) g1 (.A(a), .B(b), .Z(n0));`) with
BitSorter's **own** cell files beside it; named association; one module per file; instances `g1, g2 …`;
`always_comb` with defaults first; `always_ff` with an asynchronous reset; enums for states; testbenches
with `initial` steps or `forever #half clk = ~clk;`.

**Reading, as built in Part 4a** (`Assets/Scripts/SystemVerilog/Syntax`). The tiers above say what the
simulator *runs*, part by part; the reader reads the whole of tiers A to C at once, because the course
writes `always_comb` from its second workshop and a parser split by tier would be rewritten twice. Ahmad
asked for it to read the course and general SystemVerilog alike (2026-10-12).

- **Lexer** (`Lexer`): a sized literal is one token however it is spaced (`8 'h FF`); a `//` comment
  later on a line is kept on the token before it, so a port's comment survives; characters copied out of
  a slide -- `’` for an apostrophe, `–` for a minus -- are read as what they look like, with a warning.
- **Preprocessor** (`Preprocessor`): `` `include `` through `ISourceFiles` (never the disk), looked for
  beside the including file and then as written, with cycles and depth caught; `` `define ``, with
  arguments and their defaults from Part L6 -- a use's arguments may run over lines, ``` `` ``` joins two
  tokens and `` `"...`" `` makes a string with the arguments put in -- `` `ifdef `` / `` `ifndef `` /
  `` `elsif `` / `` `else `` / `` `endif `` (so include guards work), `` `undef ``, and `` `timescale ``,
  noted for the tree and, from Part L7, given to every module after it.
- **Syntax tree** (`SyntaxTree.cs`): modules with ANSI or non-ANSI ports, header and body parameters,
  declarations of any keyword type, net type or typedef name, enums, generate regions, for- and
  if-generate, primitives with delays, module instances by name or position, continuous assigns,
  `initial` / `always` / `always_comb` / `always_ff` / `always_latch`, functions and tasks, and every
  statement the workshops use and the common general ones (`for`, `while`, `do`, `forever`, `repeat`,
  `wait`, `case` / `casez` / `casex` with `unique` and `priority`, delay and event controls, system task
  calls). Every node knows its file, line and column.
- **Parser** (`Parser`, recursive descent): the course's operator table, plus `**` and the wildcard
  equalities -- and `~&` and `~|` between two operands, which the standard has only as reductions but the
  course's first workshop writes (`assign Z = X ~& Y;`): they bind as `&` and `|`, run as NAND and NOR as
  Icarus runs them, import as those gates, print as written, and draw warning SV0207 naming `~(X & Y)`
  (Ahmad's choice, 2026-10-19). A name followed by a name is a declaration of a typedef's type or an instance, told apart by
  what follows. It never throws and never hangs: after an error it skips to a `;` or a keyword that starts
  something; every loop makes progress; nesting deeper than 200 stops the read with a message rather than
  the browser's stack. A missing `;` is reported on the line it is missing from. Classes, an interface
  declared inside a module, `export`, concurrent and deferred assertions, `defparam`, `deassign`, drive strengths and the switch
  primitives are refused where they stand, their text kept.
- **Printer** (`Printer`): the course's style -- four-space indents, one port per line with its comment,
  connections one per line beyond three, `[N-1:0]` tight. Printing is a fixed point: print, read, print
  gives the same text, and a tree built in code gets the parentheses its meaning needs.
- **Checked.** 111 tests (`Tools/ci/SystemVerilog.Tests/Syntax`): each construct; the precedence table;
  refusals and recovery; the fixed point over the whole oracle corpus; every corpus file cut off at three
  hundred points, random tokens and characters, and nesting a hundred thousand deep, none of which may
  throw or take more than a few seconds. Outside the repository, every one of the course's 53 source files
  -- SIM1 to the multicycle processor of SIM3 -- read with no diagnostic and printed to a fixed point.

**Elaborating, as built in Part 4b** (`Assets/Scripts/Sim/Elaboration`). `Elaborator.Elaborate(tree, top)`
turns a syntax tree into a flattened `Design` and the top's ports, or into diagnostics and no design. It
lives in the engine's layer, which already sees the syntax tree, so the layers still point one way.

- **The top** is the module named, or the one nothing instantiates. A file that includes a cell library
  leaves its unused cells uninstantiated too, so among several such modules the one that instantiates
  something wins, as a testbench does; if that still leaves more than one, it names them and asks.
- **One instance at a time, in the standard's order**: header parameters (each overridden by the
  instance's `#( )`, worked out in the parent), the ANSI ports, the body's parameters and typedefs, every
  other declaration, and then everything that drives something -- so a use before a declaration in the
  body still finds it. A body `parameter` is settable only when the header has no `#( )` list, as the
  course's cells are written; with one, it is a localparam (IEEE 1800, 6.20.1). Parameters with no type
  keep their value's own; typed ones are converted to it.
- **Names are places in the hierarchy**, as Icarus writes them in a VCD: `alu64.slice[3].s.fa.sum`. An
  unnamed generate block is `genblk<n>`, *n* counting every generate construct in its scope.
- **Ports are signals of their own, joined by zero-delay assignments.** An input is a wire in the child,
  driven from the parent's expression; an output drives the parent's signal, or the bits of it a select or
  a concatenation names, from the child's port. Icarus joins the two into one net instead, which takes no
  delta cycle -- the values every time step settles on are the same, and those are what the oracle
  compares. The top's inputs are variables nothing drives, so a `Stimulus` (and later the workbench's
  switches) can write them; every other input is a wire, as the standard makes an `input logic` port. An
  output is a variable when it has a data type (`output logic`) and a wire with a net type or none.
- **Expressions are sized as the standard sizes them** (`ExpressionBuilder`; IEEE 1800, 11.6 and 11.8),
  in two passes: each node's own width and signedness bottom up, then the context's width pushed into
  every operand the operator's width depends on -- arithmetic, bitwise, `?:`'s two choices, a shift's
  left side -- each extended before the operator runs, with its sign only when the whole expression is
  signed (a signed operand of an unsigned expression is read unsigned, so `(s1 / s2) < u` divides without
  the sign). A comparison's operands are sized against each other, never the context; a shift amount,
  a condition, a reduction's operand and a concatenation's items are self-determined. That is what keeps
  the carry in `{cout, sum} = a + b`. A select counts against the declared range, either way round; a
  variable index or `+:` base becomes the engine's bit-select or a shift, and reads x past the vector.
  An unsized number inside `{ }` is an error, as the standard makes it.
- **Constants are the same expressions, evaluated** with no signal values: parameters, ranges, delays,
  replication counts, generate bounds. `$clog2`, `$bits` and `**` between constants are worked out here.
- **Generate** loops are unrolled with the genvar a constant in each block, capped at 65,536 rounds and
  refusing a loop that comes back to a value; an `if` picks its block, and an `else if` chain is one
  construct. Instances may nest 64 deep, and a module that instantiates itself is refused.
- **Drivers**: an assign or a primitive whose target is a concatenation or a slice drives each piece
  from its bits of the value. `buf` and `not` may have several outputs; a gate's input wider than a bit
  uses its lowest, with a warning. An undeclared name where a net may be implied -- a port connection,
  the left of an assign, a gate terminal -- is a one-bit wire, with a warning.
- **Refused, with the part it arrives in** (`SV0306`): `inout` (14), arrays of wires, unpacked arrays as
  ports, unpacked structs and unions, net types other than `wire`, `tri`, `uwire` and the two supplies. Simulating
  a design without them would be simulating something else, so they are errors, never warnings. Processes
  and declared starting values, refused in Part 4b, run from Part 5a; memories and hierarchical names from
  Part 6a (section 5).
- **Diagnostics** `SV0301` to `SV0323`, each at the item's file, line and column: no top, unknown module
  or name, not a constant, a width mismatch at a port (a warning), a bad port, parameter, select, delay or
  left side, two drivers on a variable's bit (with the way out: a wire), recursion, an implicit net and an
  unconnected input (warnings), a duplicate, an unsized number in `{ }`, and a design too large. A read
  that failed is not elaborated, so the read's own errors stand alone. Part 5a adds three: a task
  skipped (a warning, once a task -- since Part 6a only `$timeformat`, `$printtimescale` and a `final`
  block), a process that would never wait, and a `break` or `continue` outside a loop; Part 6a one
  more, a format with too few values (a warning); L2 one more, a function or task called in a way it
  cannot be (`SV0323`).
- **Checked.** 44 tests (`Tools/ci/Sim.Tests/ElaboratorTests`): the sizing rules each by a design that
  shows the difference, parameters by name and position, body parameters, enums, `$clog2` and `**`,
  generate naming, every way of connecting a port, non-ANSI ports, every error code, and the hand-worked
  delays of the corpus's mux (y falls 5 after s rises: the inverter's 3 and two NANDs) and 64-bit ALU
  (the carry ripples two units a slice, out at 328 for a change at 200). Every corpus file, and 400
  mangled copies of the structural ones, elaborate or say why without throwing. Against Icarus
  (`Tools/ci/Oracle/StructuralOracleTests`): the mux, full adder, ALU slice and 64-bit ALU, read from the
  corpus by both and driven by the same random 4-state writes -- all x, all z, stray unknown bits, gaps
  shorter than the slowest path -- agree on every one of 2,062 signals, every port of every instance
  down to the cells, over more than 77,000 settled changes.

---

## 8. Saves and migration

- The save file gains `format: 5` — 4.x's had no version field. 5.0 reads every 4.x key and writes the
  new ones; `WholeSaveMigrationTests` is extended to start from a 4.0.0 file.
- **Geometry moves one to one.** A cell becomes a position in cell units; a source becomes an input pin
  with a stimulus track; a bin becomes an output pin.
- **Delay moves by rule.** In 4.x delay lived on wires and gates took no time; in 5.0 it lives on gates
  and wires are ideal. Each migrated gate gets the cell default of 1; a wire of delay d above 1 gets a
  buffer of delay d − 1 on its route. Registers become flip-flops on one clock port; blocks become
  modules.
- **What migration promises is every output's settled value after each vector, not the exact tick.**
  A board that only worked by collision or stalling has no meaning in the new model; it still loads,
  and shows what it now does. Every shipped level's own circuit is migrated and checked.

**As built in Part 7b** (`Assets/Scripts/Board/Migration`, `Save`; `View/Level/LegacyCircuits`,
`SaveMigration`).

- **The seam is `LegacyCircuit`**: a 4.x board in its level as plain fields -- fixtures with their
  streams, gates by kind name, blocks with their contents, wires by cell and port index, expectations as
  the level writes them. The view fills one from a `LevelDefinition` and a `CircuitBlueprint`
  (`LegacyCircuits`); `Migrator` makes it a `Project` without Unity, and is tested without it.
- **One cycle a vector, long enough for anything to settle**: twice the sum of every delay on the board
  (each block counted where it is placed), at least ten. A vector's inputs change at its start; the clock
  -- an input port the stimulus drives, `clk` -- rises half-way; outputs are read just before that edge
  (`MigrationResult.SampleTime`). So a register, a flip-flop starting at 0 as every 4.x register did,
  hands on during a vector the bit it was given in the one before -- 4.x's "its k-th output is the bit it
  was given on cycle k-1". **The plan said the period would be the old `clockPeriod`;** that was the
  spacing of tokens, and means nothing once gates take time, so it is kept on the circuit and not used.
- **Names come across as SystemVerilog takes them** (`Migrator.NameFrom`): free play's bin `OUT 1` is
  `OUT_1`, a fixture called `and` is `and_`, a level's file name `half-adder` is the module `half_adder`,
  and a block called `OR2` steps aside for the cell of that name.
- **Two wires into one input are two drivers**, an error the document explains when it loads; the
  migration does not guess which to keep. Nothing else is refused: a wire to nothing is left out with a
  note.
- **The save** gains `format` (5 when 5.0 writes; absent, so 0, in every 4.x file) and `projects`, each
  document under its board's key -- a level's name, or `free-play/` and the board's name -- as
  `ProjectRecord`: plain fields in the shape `JsonUtility` needs, a part one record with a `kind`, values as
  literals. The 4.x keys stay as they were, because the 4.x game reads them until Part 16.
  `SaveMigration.MigrateAll` migrates every board with no document yet, rebuilding each exactly as the
  game would, so a document edited in 5.0 is never replaced by its old board. **Nothing calls it on the
  player's save yet**: Part 15d wires it in with the Exercises, and must then leave alone a board whose
  circuit the student has deleted, or DELETE would be undone at the next launch.
- **Part 15a adds** `recent`, the circuits the latest opened first, and two kinds of key under `projects`:
  `workbench`, MY CIRCUIT's, and `doc/` with the name of a circuit made with NEW or imported.
- **Checked.** In `Board.Tests`, a board each way a 4.x board could be -- gates, a slow wire, a register
  in a loop, a block placed twice, names, two drivers -- run on the engine and read at each vector; and
  every sample board through the save format and back to the same text. In Edit Mode,
  **every one of the thirty shipped levels' own solutions** -- the one its tests solve it with -- first
  passing in 4.x, then migrated and reading what the level expects at every vector (`MigrationTests`); a
  document through `JsonUtility` and back; the 3.0.2 save and a 4.0.0 one (named free-play boards, a block
  from the library, four at once's blocks) migrated whole, written as format 5 and read back, the 4.x
  keys intact (`WholeSaveMigrationTests`, `Release400MigrationTests`).

---

## 9. Invariants

These take the place of 4.x's core decisions as each part lands.

1. **Determinism.** The same design and stimulus give the same history — value changes and transitions
   — whatever order its drivers were built in. The engine gets it from its phases a delta (section 5),
   and a test holds it by building random circuits in shuffled orders. Processes, which the language
   lets race, run in one fixed order (section 5), so a design with a race in it still gives the same
   history on every run.
2. **The engine never throws on a design.** A broken design is a diagnostic; a runaway one ends with a
   message (delta limit, a process that never waits, a history grown past its cap) or pauses (event
   budget, end time). Nothing hangs a browser tab or fills its memory.
3. **The view does not change** the engine or the document except through their commands.
4. **Printing loses nothing.** Print, parse and print again gives the same text, and running the
   printed text gives the same history as running the board.
5. **The pure layers stay pure** (section 3).
6. **Nothing private comes in.** No course file, no course code and no ID number is ever committed.
   `Tools/ci/Repo.Tests` scans every hand-written file in CI, and on the developer's machine compares
   the repository and all of its history with the course folder itself.
7. **Time is an integer.** The display unit is presentation only.

---

## 10. The look

The new look, *Schematic*, is modelled on a reference visual: a flat dark ground with one thin-bordered
card, thin-outline textbook gates, rounded-box ports, quiet grey labels, every wire in its value's
colour, each change travelling down its wire as a ring holding the new digit, the waveform drawing
itself live underneath, and a pink playhead labelled in ns over a slider that rewinds when dragged.

**It must be better than the reference, in the same style** (2026-10-12): all four states (`x` red and
hatched, `z` dashed at mid level); changes leaving the gate that caused them, and a swallowed glitch
visibly dying on its wire; buses with their width and their value in the chosen radix; smooth wires with
junction dots and the whole net lit on hover; nothing moving once the circuit has settled; honest rise
and fall labels; a waveform with two cursors, radix, enum names and glitch markers; a light variant for
report screenshots; and a 0 and a 1 that differ by more than colour. Variants are mocked up and chosen
before any is built, and the result is reviewed side by side with the reference. It sits in Settings
beside Neon Board and Classic, which also draw the new board.

**Chosen, 2026-10-12: Quiet with a touch of Lively.** Three variants were drawn side by side in an HTML
mock on the real engine's history (Quiet, Lively, Precise). Ahmad chose Quiet as the base, with Lively's
trail and halo on a change in flight and its tint on a gate as it flips, kept faint (35% of Lively's,
which he set on the mock's slider), and the waveform's lines in their values' colours. Nothing from
Precise, and no casing under the wires.

**As built in Part 3** (`Assets/Scripts/View/Workbench`), as a preview off the main menu:

- **The view reads a view-model, never the engine.** `WorkbenchModel` holds one run of the circuit,
  made once to its end, and answers for any moment: each net's value, the changes on their way and how
  far down their wires they have got, a gate's flash, the waveform so far. Playing, replaying and
  dragging the slider back are `Seek`; nothing re-runs (ADR-6). It formats every value once, so a frame
  of playback makes no garbage. The circuit and where it is drawn were `ReferenceCircuit`, written in
  code, the stand-in Part 8 replaced with a board document and its layout (below).
- **Drawn as anti-aliased meshes.** `VectorPainter` writes strokes, discs, rings, glows and filled
  shapes into UI meshes, each edge carrying a strip about a pixel wide that fades out — smooth at any
  size, and in a browser build, which has no MSAA. The picture is four layers (card, wires, parts,
  waveform), so a ring's digit, a TextMeshPro label, sits over its ring and under the gate it reaches.
- **A legend of all four values** along the card's foot, where the reference has one for 0 and 1:
  each value drawn as a wire draws it, and what a ring and a cross mean.
- **The glow is drawn, not bloomed.** The plan named URP bloom; the mock's glow is a soft trail and halo
  at a chosen strength, and drawing it reproduces that exactly, costs no full-screen pass in the browser,
  and keeps bloom for the 4.x looks. Colours are `SchematicPalette`; a faint colour's alpha is corrected
  (`Over`) because the project blends in linear space, where 35% over a dark ground looks far stronger
  than in the browser the mock was chosen in.
- **Checked.** `WorkbenchTests` holds the view-model to the recorded run (the glitch passing, the short
  one swallowed and fading, a ring at its change's distance), the routes' geometry and the palette's
  contrast; `WorkbenchPlayTests` opens it from the menu and reads back the colours the wires were
  actually drawn in after the slider is dragged; reference shot 27 is the Schematic look's first.

**As built in Part 8a**: the canvas on the real document.

- **Drawn from a document.** `ReferenceCircuit` is now a `Project` put together through `BoardEditor`
  like any board, compiled, elaborated and run; `DocumentSchematic` turns its top module into what the
  drawing reads (`SchematicLayout`): every part as its symbol, every net as the branches a change takes
  from its driver to each load, the ports as boxes and again as the waveform's rows. Nothing about the
  circuit is written in the view.
- **Where things go is the document's, in `Board/Geometry`**, pure like the rest of the layer, so the
  editor of Part 10 and any other view share it. `PartGeometry`: cells with y up, a part centred on its
  placement facing right, mirrored then turned a quarter at a time; every pin on the quarter-cell grid,
  inputs half a cell apart; a tri-state's control and a mux's select from underneath, a register's reset
  too. `WireGeometry`: a wire is its bend points, or an automatic route at right angles with half-cell
  stubs out of its pins -- round the parts when it runs backwards, and into a pin facing up or down from
  the side that pin faces, never through the body or past the pin and back. A net is traced from its
  driver's pin to each load through the wires, cut where a tap joins one, and a tap is a junction dot.
- **The names are the engine's.** A net's signal is the compiled design's own, under
  `CompiledProject.DesignPath` (`half_adder_tb.dut` under a testbench). A gate with delay prints as a
  library cell, whose output changes before the cell's port hands the change on, so that output is one of
  the net's aliases and the ring travels the drawn wire all the same.
- **Every symbol, as the textbook draws it**: the AND family's D and the OR family's shield, with the
  XOR's second back and the bubble on the inverting kinds; NOT and BUF; the tri-state's triangle with
  its control coming up into it; the mux's trapezoid; the register's box with its clock wedge; a
  constant's box holding its value; a lamp; a splitter's and a joiner's bar; a block's box naming its
  ports. Turned and mirrored by the same arithmetic as their pins.
- **A wire runs on into the body.** A pin's short lead is the end of its wire, so it is in the wire's
  value and a change leaves the gate that made it; only a pin with nothing on it keeps a grey stub. A
  first build drew every lead grey, and every wire looked as if it stopped short of its gate.
- **Labels where the reference puts them**: a gate's kind inside the D and the shield, over a triangle
  or a block; the delay just past the output, over the wire leaving it -- under the part it had landed on
  the tri-state's enable wire. A row per port in the order they stand on the board, top to bottom, so
  each output reads beside its input; the waveform as wide as the stage.
- **Pan, zoom and fit** (`SchematicView`). The wheel over the circuit zooms in about the cursor, a
  notch a quarter, to four times; a drag moves it; FIT, up only while the circuit is not whole, puts it back.
  Since Part 10a the wheel goes on out past the whole circuit, to a quarter of its size, and a drag -- the
  middle button's, or Space's -- reaches six cells past its edge, or half the window when that is more, for
  room to place a part beside what is there (see "As built in Part 10a").
  The circuit's window clips it; the waveform has a zoom of its own, in time, since Part 9b-2a. **The view's
  zoom lives in the view, not in the document's layout** as section 5 planned, and Part 10a kept it there:
  MY CIRCUIT is saved without it, and opens fitted.
- **Checked.** `GeometryTests` (pins, turns and mirrors, every sample board's pins on the grid, the
  routes, a branch per load, taps), the tri-state sample against Icarus and the design path in
  `BoardCompilerTests`, the view's rules in `WorkbenchTests`, the wheel, a real drag and FIT in
  `WorkbenchPlayTests`, and reference shots 27 and 28 -- the second zoomed in on the AND that swallows
  the short glitch.
- **Found on the way:** an automatic route into an enable pin from less than a stub below went past it
  and came back up, and from above ran through the part.

**As built in Part 8b**: the looks, and the choice of one.

- **Every look draws the canvas** (`Look.Canvas`, `Look.CanvasGlow`). Four, in the order Settings
  offers them: **Schematic**, the reference made better as Ahmad chose it (Quiet with a touch of
  Lively, glow 35%); **Schematic Light**, the same on paper for a report's screenshots, its value
  colours taken dark enough for 3:1 on a light ground and a bus drawn dark; **Neon Board**, made from
  its board's colours -- the black-violet ground, cyan outlines and buses, a magenta 1, a lighter
  indigo 0 than the board's bloom-lit bit, and twice Schematic's glow, since glow is what the look is;
  and **Classic**, 2.0's navy ground, yellow 1, slate 0 and teal.
- **The Schematic looks are a canvas, not a board.** They keep Neon Board's board, interface and bloom,
  because the 4.x board is retired in Part 15 when the levels move onto the canvas, and drawing it a
  third way for one part's life would be thrown away. So **Schematic became `Look.Default`** -- the
  plan's proposal, approved with it and confirmed at this part's review -- and moved no pixel of the
  game: every board shot is the same in Schematic and in Neon Board.
- **The workbench's own controls are the canvas's** -- buttons outlined as the reference outlines
  REPLAY, chips with a hairline edge, the slider in the playhead's colour with a thumb in the text's.
  They were the game's, and Neon Board's bordered panels darken a body whatever colour it is given:
  on paper every button and chip came out a dark slab, and the chips' grey words could not be read.
- **STYLE in Settings switches the look by loading the game again** (`LookSwitch`, ADR-8): under a
  cover the open board is written, the look put on and the scene loaded, then Settings comes back up
  and the cover fades. The choice is kept as the look's own number (`Look.Code`), never its place in
  the list, and put on before the first scene in a build, never in the editor, as the frame cap is.
- **Reduced motion** (`ReducedMotion`, a switch under the looks) leaves out the canvas's glow and its
  gates' flashes; a change still travels its wire, since where it has got to is what the canvas shows.
  Read where it is drawn, so it needs no reload. The 4.x board keeps its motion until Part 15.
- **A wire pointed at lights its whole net** -- a soft band of its own colour under every branch, driver
  to every load -- and a chip by the pointer says what drives it and what reads it (`na   from g3.y
  (NOT) to g4.b (AND)`), written once per net so hovering formats nothing. The last of the canvas items
  Part 8a listed.
- **Checked.** `LookBriefTests` holds every look's canvas, Classic's included, to the brief of section
  5: every value and outline 3:1 on its ground, every word 4.5:1, a 0 and a 1 apart by weight as well
  as colour, z apart from both, and reduced motion leaving out glow and flash. `LookTests` holds the
  stored numbers, the chosen look and the default; `SettingsPlayTests` chooses Schematic Light through
  the real reload and finds Settings back up and the workbench drawn in it; `WorkbenchPlayTests` points
  the fixture's mouse at a wire. Reference shot 29 is a wire pointed at, and every shot is captured in
  all four looks (`BitSorter.Capture.Look`).
- **Reviewed and signed off, 2026-10-14.** The reference beside the four looks, with a design critique
  and an accessibility review of the shots; Ahmad confirmed **Schematic as the default** and asked for
  every finding fixed before the part merged:
  - a triangle's name (BUF, BUFIF1) is placed over the drawn triangle -- placed from its outline, half a
    cell tall to the triangle's third, it floated as near the row above as its own part;
  - the bus is quieter, 5 units rather than 6.5 and a sand, a warm grey, a mid cyan or a mid teal by
    look -- it had been the loudest stroke on the screen;
  - the stage never draws under its own size for the height (`WorkbenchPanel.MinStageScale`): a 1920 by
    800 browser tab, fitted, took every delay and row name under ten units, so a short window scrolls
    the page instead, by the wheel off the circuit or up and down;
  - every control has a key, drawn over its button as the 4.x HUD draws RUN's: Space plays or pauses,
    R replays, the arrows step a nanosecond (a tenth with Shift), plus and minus zoom, F fits.
  Left as they are, and why: a chip's hairline edge and the slider's fill are below 3:1, but neither is
  how anything is told apart -- the words in a chip are 4.8:1 and over, and the slider's thumb stands on
  the ground at 15:1. uGUI shows nothing to a screen reader, which no part of 5.0 can change.
- **Fast: the plan's 60 frames a second with 300 wires, met** (`WorkbenchPerformanceTests`, 2026-10-14).
  The test plays thirty chains of nine NOTs -- 300 wires, about 200 changes in flight, twice the plan's
  hundred, and every wire routed backwards round four bends because its gates are two cells apart --
  and holds a frame of playback to 6 ms, the canvas's share of a 60 Hz frame, by the best of three
  runs' medians, with motion as it ships whatever the machine's own setting. A frame was 61 ms when
  every layer was one mesh redrawn whole; it is 4.5 to 5.5 ms in the editor from run to run, at a
  canvas scale of 1 (3.3, and 5.5 in a batch run, before the browser's fix below). How:
  - **Still and moving, drawn apart** (`WorkbenchLayers`). Every layer is cut into chunks a mesh each,
    under uGUI's 65,000 vertices -- one mesh a layer went over that on this board, and threw. The wires
    and the parts at rest, and the waveform's rows for the window of time it shows (the whole run until
    Part 9b-2a gave it a zoom), are drawn once and redrawn only when what they show changes; the changes in flight, the gates flashing and the playhead are redrawn
    with the moment, a lamp when its value changes colour, and a row's glitch marks as each appears. The
    waveform is revealed up to the playhead by a `RectMask2D`, as a pen draws; its glitch marks are
    not, since a cross reaches either side of its moment and would show early. A pan of the circuit
    redraws nothing and a zoom of it the schematic alone: the edges' smoothing is a pixel wide, and only
    a zoom changes what a pixel is. A zoom or a pan of the waveform redraws the waveform alone.
  - **Cut by estimates, held by a guard** (`VectorPainter.Fits`). How many changes a wire carries at
    once has no bound -- a fast input into a slow gate leaves one swallowed after another fading -- so
    no estimate can promise a chunk fits a mesh. A chunk that outgrows one draws as many whole items
    as fit, a wire, a change, a part or a row at a time, and says so once; it used to draw nothing.
  - **Meshes written directly** (`MeshBuffer`): an array of positions and one of colours, in place of
    uGUI's `VertexHelper`, which keeps eight channels a vertex and copies them twice; and every shape
    written straight into the arrays after one reservation. The same vertices in the same order --
    the shots of every board are identical to the pixel. **Handed over through `SetVertices`,
    `SetColors` and `SetIndices`, never one interleaved stream**: a mesh filled with
    `SetVertexBufferData` and 16-bit `SetIndexBufferData` drew in the editor and drew nothing at all
    in the browser build, every chunk of the workbench blank with no error, from this part until it
    was found on 2026-10-15 (`5701303`). The editor's tests and shots cannot see that; a browser build
    can, so a change to how a mesh is handed over is smoke-tested in one. The classic calls cost
    about twice as much to hand a mesh over, and the frame the gate measures went from 3.3 ms to
    4.5--5.5 in the editor: inside the budget, with less room than it had.
  - **Less work per shape.** A wire run straight through a pin or a stub is no longer bent at it (it
    was nine points on a line, and a straight wire 38 points); a ring is drawn round its radius from a
    table of unit circles rather than through a mitred stroke; a route's points keep their normals; a
    level or upright cap keeps its directions; each chunk finds its own changes from a list of their
    nets; and a swallowed change's mark is indexed by signal once rather than found by walking the run.
  - **Reduced motion is read once** (`ReducedMotion`): it was read from PlayerPrefs for every part on
    every frame, some five hundred reads, and a frame went from 22 ms to 13 in the editor, where
    PlayerPrefs is the registry, once it was kept.
  - **One thing a reference shot shows.** Revealed, a bus's box runs on under the playhead rather than
    closing in a point there, which it did when each row was drawn up to the moment: the value has not
    ended, so the open box is the truer picture. Shots 27 to 29 differ there and nowhere else but for
    float rounding -- 35 pixels at most by 2 levels, from the straightened corners and the ring.
  - **Reviewed before it merged**: three reviews of the change, by geometry, by what is redrawn and
    by runtime, found the glitch marks shown early, every chunk redrawn on every frame of a drag, the
    estimates called bounds, a lamp's outline drawn twice and the test reading the machine's own
    settings, all fixed here; and one bug from Part 8a, fixed with its own test: no glitch was ever
    marked on a row of a board document, since a gate with delay prints as a library cell and its
    changes go to the cell's own output, one of the net's names rather than the port's. A row now
    marks a change swallowed on its way to any name of its net.
  - **What the test does not time**: the ring digits and the waveform's words (TextMeshPro labels the
    panel moves), uGUI's batching and the GPU. Nor is it the browser: the canvas has not been timed in
    a browser build yet, and Part 16's performance check is where it will be. (At Part 6 the browser
    build ran the engine as fast as .NET did, or faster.)

**As built in Part 9b-2a**: the waveform's signals and drawing.

- **The waveform zooms and moves in time** (`ViewModel/WaveView`, under dotnet; `View/Workbench/WaveformView`).
  The 4.x timing diagram's gestures: the wheel over the time axis zooms about the cursor, a notch 1.25
  times, in to 640 pixels a unit and back out past the whole run, as far as the run across a quarter of the strip --
  the fit a stop on the way, so a notch that would pass it lands on it (Part 15h: it stopped at the run's end, and
  Ahmad asked for a limit "but not at the limit of the line", 2026-10-28); with Shift, or sideways, it moves an
  eighth of the window a notch. While the run plays, a zoomed view pages on once the playhead passes 90%
  of it and lands it at 10%, and stops following once it is moved away; a replay follows it again. The
  axis is ticked on the 1-2-5 ladder at least 40 pixels apart, in the step's own decimals (`TimeText`,
  which writes into a buffer and reads no culture), so the reference's run is ticked every 2 ns fitted as
  it was before and every 0.1 ns zoomed in to the full.
- **Only the window is drawn.** Zoomed in, a run can be millions of pixels long, so the rows are drawn
  for the time in view and drawn again when it moves, and nothing of the schematic is. Changes closer than
  a pixel and a half are one busy block, hatched if any of them is x, so what is drawn is bounded by the
  window's pixels rather than the run's changes: a clock with 20,000 edges across 860 pixels is 1,184
  vertices fitted, and no zoom between that and its edges one by one outgrows a mesh. A box the window cuts
  is square at the cut. The words over a bus's boxes come from the same stretches, pooled per row.
- **Values in the course's radixes** (`ValueText`, `TextCache`): binary, octal, hex, unsigned, signed and
  an enum's names, every bus the same length whatever its value, as `MaxChars` says. The waveform writes
  a bus in hex as before, with one deliberate change: a digit partly unknown is `X`, as `$display` writes
  it, where the whole value used to read `x` -- `8'b0000_1x01` is `0X`. The radix chosen per row, and the
  rings' radix with it, are Part 9b-2b's, with the list they are chosen from.
- **The samples** (`ViewModel/WorkbenchSamples`): the reference, a full adder of two half-adder blocks for
  hierarchy, and a three-bit counter with an asynchronous reset for a clock, each a board document of its
  own, laid out with no wire through a part or a label and each crossing counted
  (`Tools/ci/ViewModel.Tests/SampleTests`). The SAMPLE chip, and S, steps through them; a step builds
  the stage again round the new document and plays its run from the start.
- **The controls are two rows** -- REPLAY, PLAY and SAMPLE over the slider and the time -- so Part 9b-3's
  run controls have a row to go on and the reference shots moved once. FIT's caption went to Body: the
  caption check in `UiTypeTests` had a backspace character where `\b` was meant, matched nothing, and
  never looked at the workbench's own buttons.
- **Moved to Part 9b-2b**, where the add-signal list, groups and the row menu use them: the signal index
  and its search, the rows' list (`WaveList`), and the aliases that carry a glitch mark through a block's
  ports. The header did not become a toolbar of its own; Part 11's shell is where it will.
- **Checked.** `WaveViewTests` (17 and a replay's), `TextTests` (35), `SampleTests` (33) and
  `SignalFactsTests` (12) under dotnet; in Edit Mode, a move of the window redrawing the waveform and none
  of the schematic, and the 20,000-edge clock; in Play Mode, the wheel over the waveform, a zoomed view
  following the playhead and a replay, and the chip stepping through the samples; reference shots 27 to 29,
  moved by the second row of controls and nothing else.

**As built in Part 9b-2b, its first half**: cursors, rows of any signal, and their radix. The second
half adds the add-signal list, groups, full names and Alt+click on a wire.

- **Rows of any signal** (`WorkbenchModel.SetRows`). The model's rows were the layout's ports, fixed when
  it was made; now they are any signals of the design -- a block's inside, the testbench's, -1 for a
  group's header -- each in a radix of its own, set at any moment, the run still being made included.
  A signal not read before gets its track read as far as the run has got, and every row its glitch
  marks so far: a model whose rows were set mid-run marks them as one set after it.
- **One node, every name** (`SignalAliases`). A port connected to a net is a zero-delay assignment that
  reads the whole net (`Expr.TryRead`, new in Sim), so following those joins gives each electrical node
  all its names, through any depth of blocks: h1's XOR output, h1.s, the parent's h1_s and h2's input are
  one. A swallowed change is marked on every row showing its node -- the rule `DocumentSchematic` wrote for
  the top module's nets and the cells driving them, now down through blocks, so a row inside the counter
  shows the two changes that die on d2's wire.
- **Radix on every word.** A row's radix writes its readout (`3h`, `1010b`, `10`, `-6`), its boxes, and the
  rings on its wire: a net's rings take the radix of the first row showing its node, so out read as signed
  reads -6 on its wire where it read A. Texts in another radix are made once, through a `TextCache`.
- **Arranged as a list** (`WaveList`, with `SignalIndex` for the keys): each row kept by where its signal is
  below the design (`h1.s`), so a design built again, under a renamed testbench too, finds every row again.
  In this half a click on a row's name selects it, a right-click opens its menu -- a radix, or REMOVE
  (`WaveRowMenu`) -- X steps the selected rows through the radixes and Delete takes them out. A change of
  rows builds the stage again round them, the circuit's zoom and the waveform's kept.
- **A row and its wire light each other.** Selecting a row lights its net on the circuit, and pointing at a
  wire puts a faint band behind every row showing it (`SchematicPalette.Selection`, opaque, with every
  word 4.5:1 and every value 3:1 on it in all four looks).
- **The playhead is cursor 1** (Ahmad's choice). Shift and a click on the waves seeks there and pauses,
  snapped to an edge of the row under it within 8 px or else to a whole unit (`WaveSnap`); a drag scrubs, as
  a press within 6 px of the playhead's own line does. A click places C2, snapped alike, a drag moves it
  through the waves, and C takes it away; the time from the playhead to it is written over a bracket under
  the rows. **The two clicks were the other way round until Part 15h**: the waves are drawn only up to the
  playhead, so a click back in a finished run hid everything after it, and Ahmad asked for the click to be
  the cursor that moves through the waves and Shift's the one that cuts them (2026-10-28). C2 is in its own colour (`SchematicPalette.Cursor`:
  amber, olive on paper, Neon's gold, and Classic's text white, its 1 being yellow), 3:1 on the ground and
  apart from every value and the playhead. The font has no Δ, so the bracket says only the time.
- **The mouse as gestures** (`WavePointer`, tested from scripted events under dotnet), and **a press on a
  control is never one**: `WorkbenchControlSample` samples, before anything reacts to the frame, whether
  the topmost thing under the pointer is a control, as `PointerGate` samples the interface for the board,
  so the click that closes the row menu never seeks. The menu holds Escape (`IHoldsEscape`), and the
  workbench closes on Escape only when nothing does.
- **Every key from one table** (`WorkbenchKeys`), which refuses N, M, H, F2, F8 and Escape (the rest of the
  game's), Ctrl with R, W, S, T, N or L (a browser's), and every function key. The radix key is X, since
  H is help. A, G and L are bound now and answered in the second half.
- **Checked.** `RowsTests` (9), `SignalListTests` (16), `CursorTests` (12) and Sim's `APortJoin` under dotnet;
  the cursor and selection colours in `LookBriefTests`; in Play Mode a click seeking onto an edge or a
  whole unit, Shift and a click placing C2 with its words, the row menu's radix and X and REMOVE, Escape
  closing the menu first and a click off it not seeking, a row and its wire lighting each other, Delete,
  and every key in the table being one the Input System has. Reference shot 30, new: the reference with s
  unsigned and out signed, the playhead at 11 and C2 at 20, out's change on its wire as -6; shots 27 to 29
  moved only along the help line.

**As built in Part 9b-2b, its second half**: any signal on the waveform, groups and names.

- **+ SIGNAL, or A**, opens a search over every signal the design has (`WaveSignalPicker` over
  `SignalIndex.Search`): its best ten hits as buttons, a click adding one at the end of the rows -- a
  block's inside as readily as a port -- selected and read at once. It stays up to add another; Enter adds
  the best hit; CELLS shows a library cell's pins, which are hidden; a click off it or Escape closes it.
- **Alt and a click on a wire** adds the wire's signal, or selects its row if it has one: a net no port
  shows, such as NOT A's output, becomes a row.
- **GROUP, or G**, puts the selected rows under a header where the first of them was -- a row of its own,
  bold, marked - while open and + while shut, with no wave -- and a click on it opens and shuts it. The
  row menu groups, ungroups a selected group, and renames a row or a group (`WaveRenameField`; an empty
  name gives a row its own back).
- **NAMES, or L**, names every row by its whole path, as ModelSim does (`/full_adder_tb/dut/h1/s`), and back.
  **The names column is as wide as its longest name**, from the reference's 58 units to 260, a longer name
  cut from its start behind "..."; the waves start further right by as much, so the time view is made
  again at the new width.
- **The waveform's toolbar** holds + SIGNAL, GROUP and NAMES, each with its key beside it, over the rows
  (`WaveformView.ToolbarHeight`), so the stage grew by it and shots 27 to 30 moved once more.
- **The popups are built side by side with their blockers**, not inside them, so a click on a popup's own
  ground stays the popup's; and each counts as a control for `WorkbenchControlSample`, so a click on it never
  seeks or selects what is under it. While a field holds the keyboard the game's keys are off (`UiText`) and
  the field answers Enter and Escape itself.
- **Checked.** In Play Mode: the list searching, adding h1's sum with its value, staying up, closing on a
  click off it without seeking, its toolbar button, and CELLS; Alt and a click adding NOT A's output; G, a
  header opened and shut by a click, and UNGROUP; L and NAMES widening and narrowing the names column; and
  RENAME and an empty name. Reference shots 31 (the full adder's carries grouped under full names) and 32
  (the add list searching h1), new in all four looks; 27 to 30 moved by the toolbar; 1 to 26 the same.

**As built in Part 9b-3, its first half (9b-3a)**: running the run on, playing it, and breakpoints. The
second half (9b-3b) is the timescale's units and editing an input.

- **A run suspended by `$stop` can be run on** (`RunSession.RunOn`, over `RunSlicer.RunOn`), a slice a
  frame like the first run, and **the timeline grows with it**: a signal's track grows in place
  (`SignalTrack.Grow`), the model's end moves (`WorkbenchModel.Extend`, `RunVersion` counting each
  growth), the time view keeps its zoom over the longer run (`WaveView.Extend`), and the panel makes its
  texts, the slider and the rows again for the new end (`WorkbenchLayers.Extended`).
- **Playing is `Playback`'s** (ViewModel, 28 tests under dotnet): the playhead moving at a speed and
  pausing by itself at a `$stop`, a breakpoint, the RUN TO moment and the end, each looked for strictly
  after the playhead in what is already recorded, so PLAY from where it paused goes on past it. **PLAY at
  a `$stop` runs the run on**, as ModelSim's RUN does (Ahmad's choice, 2026-10-16): to the timeline's end,
  or at the end, where a board's own testbench calls `$stop`, as long again as the first run, or to RUN
  TO if that is later. PLAY at a `$finish`, or at an end the run had to stop at, plays it again from 0.
- **Speed is a ladder that doubles**, 0.1 to 1638.4 ns a second, then INSTANT, which goes straight to
  wherever playing would pause. The default is the reference's 1.6, and a speed chosen is kept from one
  sample to the next. The model is told the speed (`WorkbenchModel.UnitsPerSecond`), so a gate's flash
  lasts the same seconds at every speed.
- **Breakpoints** (`Breakpoints`): at a moment; on a signal changing into a value, compared as `===`
  compares, x and z included; and on a signal going x after time 0, when every 4-state variable starts at x. A
  value is typed as a SystemVerilog literal fitted to the signal (`Breakpoints.TryParseValue`), and one
  that does not read is refused with its reason on the chip. A breakpoint on a signal no row shows is
  dropped when the rows change (`Breakpoints.KeepOnly`): it would pause the run with nothing on screen
  saying why.
- **The controls** are RESTART (R), PLAY (Space), STEP BACK (comma) and STEP (period) over the slider,
  SLOWER and FASTER (the brackets) round the speed, a RUN TO field and the time; SAMPLE moved beside the
  title, with S. STEP goes to the next edge of the selected rows, every row's with none selected, and
  pauses. REPLAY became RESTART (`WorkbenchCommand.Restart`), since PLAY now does more than play again.
  **RUN TO gives the keyboard back** a frame after Enter or Escape (`ReleaseRunTo`): TextMesh Pro leaves a
  finished field selected, and RUN TO is the first field that stays on screen, so in the browser smoke
  test every workbench key was dead after Enter until a click elsewhere.
- **The chip says why playing stopped**: "$stop at 36 ns: PLAY runs on", "break: count = 3 at 12 ns",
  "ran to 10 ns", "$finish at 40 ns", "the run stopped at 5 ns"; a refused value or time until the next
  thing is done; otherwise, as before, the changes on their way or when the run settled. A reason's words
  are made once, as it is first shown.
- **Setting one.** The row menu gains BREAK ON X, BREAK ON VALUE (a field, as RENAME's) and NO BREAKS. A
  right-click on the waves or the axis (`WaveGesture.TimeMenu`) opens a menu for that moment
  (`WaveTimeMenu`): BREAK AT, or NO BREAK AT, and NO BREAKS AT ALL. Both menus share `WaveMenu`: a blocker
  beside the panel, Escape held, a control to `WorkbenchControlSample`.
- **Flags, in the cursor's colour** because red is x's: a moment's on the axis, under its number with a
  thin pole down the waves -- over the number at first, which a capture showed hiding half its digits --
  and a signal's in the gutter before its row's wave.
- **Checked.** `PlaybackTests` (28), `ProcessTests`' stops and `CursorTests`' right button under dotnet;
  in Play Mode, playing to the testbench's `$stop` and PLAY running it on, the speed by key and by
  button, STEP from edge to edge of a selected row, RUN TO, and its field giving the keys back after Enter
  and Escape without the Escape closing the workbench, BREAK ON VALUE from the row menu, the menu for a
  moment setting and taking away a breakpoint, and RESTART. In the browser build: PLAY at the reference's
  `$stop` ran it on, a breakpoint set from the menu for a moment paused it there, and the speed changed. Reference shot 30 now has a flag at 26 ns
  and one on out going x; 27 to 32 moved by the controls; 1 to 26 the same in all four looks.

**As built in Part 9b-3, its second half -- the layers (9b-3b-1)**: the time unit, an input edited, and
the run replayed for it. The view of them -- the unit chip, every time written in the unit, a double-click
on an input's row, undo and redo -- is 9b-3b-2.

- **One list of units** (`SystemVerilog.TimeUnits`): s, ms, us, ns, ps and fs, exactly as SystemVerilog
  writes them, used by the parser, the rules, the save and the importer. A field on screen reads them
  more loosely (`TimeText`). The parser's warning for `#10ns` no longer says ns: the unit is the
  design's, whatever it is.
- **The unit relabels and rescales nothing** (invariant 7): a delay of 3 is 3 ps once the project is in
  ps. That holds of a project, whose files name its unit as their precision; a file read with a finer
  precision, `1ns/1ps`, counts in ticks of that precision (Part L7, section 6). `BoardEditor.SetSettings` sets it, `BoardRules` refuses one SystemVerilog does not have and a
  default gate delay below 0, and a save holding one that is not a unit loads in the default, ps, with a
  word to say so.
- **A project in a unit other than ps prints `` `timescale 1ns/1ns ``** (in its own unit) at the top of
  every file, the precision the unit itself since time is whole; one in ps, the default, prints none. D7
  says export adds none unless asked, and choosing a unit is the asking, while the course's ModelSim runs
  a file with none in ps -- every waveform in its own simulation reports reads "145 ps" for 145 units --
  so a ps project means the same with or without it. **The default was ns until the course audit
  (2026-10-19)**, on the belief that ModelSim reads a file with none in 1 ns; the course's own screenshots
  said otherwise, and Ahmad chose ps. The importer reads a timescale of one whole unit back as the
  project's; one of tens of a unit (`10ns/1ns`) has none a project can count in, and imports in ps.
- **An input edited** (`StimulusEdits`, committed by `BoardEditor.SetInput` and `InvertInput`): a value
  over a stretch, after which the input goes back to what it had, or a bit flipped over the stretch a
  moment is in, between the steps either side. Each keeps the fewest steps that say it -- a step giving
  the value already held is dropped, and an input is x before its first step, as the testbench's `logic`
  is. A clock is refused, being a period and not steps; so is a bus or an x flipped, which have no one
  other value; a value of the wrong width is refused by the rules, as any edit is.
- **An edit is replayed beside the run it replaces** (`WorkbenchSession`, over the editor and two
  `RunSession`s): the edited document compiled and run a slice a frame, taking over once it has reached
  the moment shown -- run on as far as the shown run had been, past a `$stop` -- so the waveform never
  goes blank and the playhead never jumps. The whole run is made again, since the testbench is one
  `initial` block driving every input; an input changes nothing before it changes, and
  `WorkbenchSessionTests` holds each replay to the shown run's history up to the edit, every value change
  and every finished transition. The latest edit wins over a replay still being made; undo and redo are
  replayed like edits; a change of unit is an edit too, since every time and delay on screen is written in
  it. RESTART takes a replay at once (`TakeReplay`), 0 being made already. `Playback.Switch` moves
  playing onto the new run, keeping the speed, the breakpoints, RUN TO, whether it plays and where.
- **Checked.** Under dotnet: `TimeUnitsTests` (22, the parser's warning included), `ProjectSettingsTests`
  (14: the rules, every printed file's first line, the import back, the save), `StimulusEditsTests` (13)
  and `WorkbenchSessionTests` (12). Nothing on screen changed: reference shots 1 to 32 identical.

**As built in Part 9b-3, its second half -- the view (9b-3b-2)**: the workbench edits its document.

- **The panel runs a `WorkbenchSession`**, where it ran a `RunSession`: each frame makes a slice of the run
  shown or of an edit's replay, and when a replay takes over the panel moves playing onto it
  (`Playback.Switch`), finds every row again by its key (`WaveList.Resolve`) and builds the stage round the
  new run with both zooms kept -- so rows, radixes, groups, names, breakpoints, the second cursor, the speed
  and the moment all carry over. RESTART takes a replay at once.
- **A double-click on an input's wave changes it** (`WaveGesture.EditInput`; the clicks counted by the panel
  on the game's clock, which a test drives). A one-bit input that is 0 or 1 there is flipped over the stretch
  the moment is in; a bus, or an x, is asked for a value in SET, a field titled with the stretch -- "SET s
  FROM 10 ps TO 19 ps" -- that reads a literal as BREAK ON VALUE does. An output, a wire inside or a clock
  is refused, and the chip says why. The moment is the pointer's own, not snapped: an edge snapped to could
  be the far side of the stretch meant. The legend line says the gesture, since the help line had no room.
- **UNIT on the waveform's toolbar, or U,** counts time in the next unit, s to fs and round, written as
  SystemVerilog writes it -- lower case, since MS would be megaseconds. Every time on screen is written in
  the run's unit: the moment, the axis's cursors and span, the chip's reasons, the speed (`3.2 ps/s`), the
  gates' delays, and RUN TO's field, which now reads a time with a unit or without (`TimeText.TryParse`:
  40, 40 ns, 1.5 us) and says why one does not read.
- **Ctrl+Z and Ctrl+Y (or Ctrl+Shift+Z)** take an edit back and make it again, each replayed; the chip says
  when there is nothing to. Neither is a browser's, and Ctrl with R, W, S, T, N and L stay refused.
- **The chip takes the controls row's room right of STEP**, one line high: a refusal can run longer than any pause's
  reason, and one longer than the line takes the lines it needs, down over the timeline's row until the next thing is
  done, taking no clicks (Part 14g; it was cut short with an ellipsis, and a library's refusal lost most of its cells).
- **Checked.** Under dotnet, `CursorTests`' double-click. In Play Mode: a double-click flipping a bit and the
  replay taking over with the rows and their radix kept; a double-click on a bus asking in SET and Enter
  setting the stretch; an output and a clock refused, the chip saying why; UNIT and U relabelling the
  speed, the moment and the delays, and RUN TO reading a time in another unit; Ctrl+Z and Ctrl+Y.
  Reference shots 27 to 32 moved by UNIT, the wider chip and the legend; 1 to 26 the same in all four looks.

**As built in Part 9b-4**: the run as a file. EXPORT on the waveform's toolbar, or E, offers two.

- **VCD: every signal** (`WaveExport`, ViewModel, 12 tests under dotnet): the run as far as it is made, in its
  unit (`$timescale 1ps` for a run in ps, the numbers unchanged), every signal of the design -- GTKWave picks
  its own -- written by the engine's `VcdWriter`, whose output the oracle already reads back against
  Icarus's. A run carried on past its `$stop` exports the timeline it grew to. Named for the sample, safe on
  every system: `full_adder.vcd`.
- **PNG: the waveform** (`WaveformPng`; Ahmad's decision 4): always Schematic Light whatever the look on
  screen, 1800 pixels wide and as tall as its rows, every row whatever the page is scrolled to, at the moment
  and over the window of time the screen shows (`WaveView.Match`). Not a screenshot: a second waveform drawn
  on a camera's canvas on layer 31, into an sRGB texture, at twice the scale -- at one to one a wave's line
  straddled two rows of pixels and filled neither, and printed pale -- then encoded and every object made for
  it destroyed in the same call. The drawing reads `SchematicPalette.Current`, so for that call it is
  Schematic Light (`SchematicPalette.Overriding`). **A workbench graphic now measures its pixel against its
  root canvas's scale**: on an overlay canvas the world scale is pixels, on a camera's it is world units,
  and the edges' smoothing came out thirty times too wide, every line a smear.
- **Saving** (`FileSaver`): in a browser a download -- `Plugins/WebGL/BitSorterFiles.jslib` makes the bytes a
  Blob and clicks a link with a download name, which follows a click the player made and so is allowed in
  the iframe Unity Play and itch.io run the game in; elsewhere a file in the Exports folder beside the save.
  `SaveGuard` points exports at a scratch folder for every fixture. The chip says what was saved and where.
- **Checked.** `WaveExportTests` (12) and `WaveViewTests`' match under dotnet. In Play Mode: E offering the two
  and VCD saving the run, read back with its input and output in ps, and EXPORT doing the same; the PNG as wide
  and tall as it should be, its ground the paper's and its 1s in the paper's colour while the screen is in
  the dark look, and the screen's look its own again after. Reference shots 27 to 32 moved by EXPORT; 1 to 26
  the same in all four looks. In the browser build: E saved `reference.vcd` as a download, `$timescale 1ps`,
  30 signals.

**As built in Part 10a**: placing, selecting and moving on the canvas.

- **Everything the canvas decides is the view-model's**, Unity-free and tested under dotnet (`CanvasTests`):
  - `CanvasHit` finds what a point is on, in the board's own cells and from the geometry the drawing uses: a pin
    before a body, a body before a wire.
  - `CanvasSnap` puts a placed or moved part on the quarter-cell grid, or exactly with **Alt**.
  - `AlignmentGuides` draws a line through a pin or a middle that lines up with another part's, and catches a free
    move to one within a fifth of a cell.
  - `CanvasSelection` holds what is selected: a click picks one, Shift adds, Ctrl toggles, a box takes what it
    touches.
  - `CanvasPointer` turns press, move and release into gestures, as `WavePointer` is the waveform's.
  - `PartsCatalog` lists the strip's entries and says what placing one does.
  - `CanvasEditor` holds the state between them -- the selection, the part in hand, a drag, a box -- and the
    overlay it draws (`CanvasOverlay`).
- **The mouse** (Ahmad's choice, 2026-10-20):
  - a left drag on a part moves the selection
  - a left drag on empty space draws a box
  - the middle button, or Space with a drag, pans
  - the wheel zooms
  - Space therefore plays and pauses **on its release**, and only when it panned nothing
  - Alt and a click on a wire still adds its signal
  - Q turns the selection a quarter and Shift+Q back, V mirrors it on screen however it was turned, Ctrl+A
    selects all
  - Delete takes away what is selected on whichever was clicked last, the circuit or the waveform's rows
  - the help under the stage is two lines, the circuit's gestures and then the waveform's: as one line it ran
    the width of the screen in a small size (Ahmad, 2026-10-20)
- **The parts strip is temporary**, a column of the canvas's own buttons left of the stage, until Part 11's shell
  gives the parts a panel. It was a row under the time first, and pushed the waveform off a 1080 screen; the stage
  keeps a strip's width free on both sides, so it stays in the middle.
  - A click takes a part in hand: it is drawn where a click would place it, and a click places it. With Shift it
    stays in hand for the next; Escape or the right button puts it down.
  - Every part is offered: input, output, clock, reset, constant, the gates, the tri-state buffer, mux,
    register, splitter, joiner, lamp, and every other module of the project as a block.
  - **An input placed in the top module is given 0 from the start**, so the waveform can change it at once
    (Part 9b-3).
  - A clock is an input given a clock of 10, starting low. A reset is held high until the clock's first falling
    edge. Each is one step for undo.
- **The document changes once, on release.** A drag is drawn where it is going, its wires routed after it,
  and moved in one step (`BoardEditor.MoveParts`); a group of edits is one step, all or nothing
  (`BoardEditor.Group`). An input taken away takes its stimulus with it, or the delete would be refused.
- **An edit is drawn at once.** A move, a turn or a mirror changes nothing the circuit does, so it is drawn again
  over the same run (`RunSession.Relayout`) and nothing replays. A part placed or taken away, an undo or a redo
  is drawn at once over the run there is -- a new gate shows no value for a frame or so -- and replayed as an
  input edit is (9b-3). `WorkbenchSession.LayoutVersion` counts each.
- **The waveform's rows follow the ports**: a port placed gains a row, a port taken away loses its row
  (`WaveList.FollowPorts`).
- **The window is sized when a document opens.** The circuit's window keeps its size through every edit. The
  view takes back the shift of the drawing's corner (`SchematicView.Relayout`), so nothing moves on screen, and
  the fit shrinks the whole drawing once it has outgrown the window. An empty circuit opens in a window
  860 by 360, on the narrowest stage there is, 900 wide. That is the waveform's width, not the circuit's: it
  was 760, from before the row of buttons gained EXPORT and the legend its second sentence, and MY CIRCUIT
  blank, the first stage that narrow, ran both past the card's edge (a Play Mode test now holds them on it).
- **MY CIRCUIT is kept** (Ahmad's choice): the first entry of the SAMPLE chip, a blank document the first time.
  - It is saved in the save's 5.0 documents under `workbench`, half a second after its last edit by real time,
    and on leaving it or the workbench.
  - The workbench opens on it once it has anything in it, and on the reference circuit until then.
  - The samples can be edited for the session and are never saved.
- **Checked.** `CanvasTests` (21), `SchematicTests`' new view rules, `BoardEditorTests` for the group edits, and
  `SignalListTests` for the rows, all under dotnet. In Play Mode (`WorkbenchCanvasPlayTests`), a real gesture each:
  - a part taken from the strip and placed with a click, dragged a cell on the grid, boxed with another, taken
    away with Delete and brought back with Ctrl+Z
  - MY CIRCUIT kept through another sample and through leaving the workbench
  - Space alone playing and pausing, and with a drag panning instead
  - Escape putting the part in hand down before it leaves
  - and, in `WorkbenchPlayTests`, a replay made whole outside a frame -- by `Finish`, as a capture or a test
    does -- is still the run the panel shows. Since 9b-3 the panel switched only when a frame said so, so a
    capture that finished an edit drew the run from before it.
  - Reference shots 1 to 26 identical to the pixel; 27 to 32 moved by the strip and the footer's second line;
    33 is new: MY CIRCUIT with two gates selected and dragged three cells, ghosts and dotted guides drawn.

**As built in Part 10b-1**: drawing wires. 10b is in two, as 9b-2b was: 10b-1 draws wires, and 10b-2 reshapes
them -- a selected wire's piece or corner dragged.

- **The gestures** (`CanvasPointer`):
  - a press on a pin starts a wire, and a drag draws it out: let go on a pin or a wire, it ends there; let go
    anywhere else, it bends there and goes on
  - a click on a pin starts one too, and then every click on nothing bends it and a click on a pin or a wire
    ends it; the right button, or Escape, lets it go, making nothing
  - Shift or Ctrl on a pin picks its part, as on its body
  - a drag out of a wire draws a new wire tapping it where the drag began; a click on a wire selects it, now on
    release, since a drag is something else. The plan had Shift and a press on a wire start a tap; Shift stays
    "add to the selection" everywhere instead
- **What is shown is what is made.** `WireDraft` keeps only the bends clicked, and the wire being drawn is
  routed through `WireGeometry.Through` -- or, with none, the automatic route -- as a finished wire is. A wire
  with no bends clicked keeps no route at all, and follows its parts wherever they go.
- **Taps.** A wire ending on a wire taps it there, the junction put on the quarter grid along the piece it is on
  (`WireDraft.TapPoint`) and kept as the wire's `Route.TapAt`. A wire drawn out of a wire ends on a pin --
  `Route` has one `TapAt`, so a wire cannot tap at both ends -- and one let go back on the wire it came out of
  bends there instead. **A tap off an upright piece leaves across**, towards the next point along its own wire
  (`WireGeometry.TapFacing`): leaving up or down, as every tap did, it ran along the very wire it tapped and the
  two were drawn as one. Off a piece that runs across it leaves up or down as before, so no sample moved.
- **The document's refusal is the word.** What the pointer is on is tried on a copy of the document's editor,
  once each time it moves onto something new. An end it would refuse is ringed in x's colour and crossed -- the
  shape says it as well as the colour -- and the hover chip says why before the click; the click says it as a
  refusal and leaves the wire being drawn. While a wire is drawn, nothing else is lit on hover. A wire between
  two widths is now refused with what would join them: a splitter, or a joiner.
- **Bends stay square.** A piece of a wire that has gone aslant -- a part moved after the wire's bends were made,
  and its pin with it -- is drawn with right angles, out of the pin the way it faces and round, as an automatic
  route is (`WireGeometry.Through`). A piece that runs straight is drawn as it always was, so no sample moved,
  and `WireGeometry.Tidy` drops a point a wire runs past and straight back from.
- **Checked.** `WiringTests` (10) under dotnet: every gesture from a scripted list of pointer events; a wire pin
  to pin made in one step, automatically routed, replayed and selected; a bend kept and made as shown, and square
  after a move; a tap on the grid joining its net; a refused end shown, said and still being drawn; a wire out
  of a wire; a wire let go when a part is taken or an undo takes its start away; and **the half adder drawn
  from scratch and run through all four combinations**. Under Board.Tests, `GeometryTests` for the bends and
  the taps, and `BoardEditorTests` for the splitter. In Play Mode (`WorkbenchCanvasPlayTests`):
  - **the plan's exit:** a half adder built from nothing with the fixture's own mouse -- placed from the strip,
    wired by dragging pin to pin, the AND's inputs dragged onto the XOR's wires as taps -- its inputs flipped by
    double-clicks on the waveform, and its sum and carry read there
  - a wire started with a click, drawn after the pointer, refused onto another output with the chip saying why,
    and let go with Escape before the workbench takes it
  - Reference shots 1 to 26 identical; 27 to 33 differ by the help line's first line (and 33 by MY CIRCUIT's
    words); 34 is new: MY CIRCUIT's half adder half wired, a tap's junction dot, and the AND's second input
    being drawn onto b's wire, the junction it would make ringed.

**As built in Part 10b-2**: wires reshaped.

- **A selected wire shows its corners**, a square on each (`CanvasOverlay.Handles`), filled with the ground so the
  wire does not run through it, and never round, as a junction's dot is.
- **A drag on a selected wire's piece moves that piece across itself** -- one running across moves up or down,
  one running up or down moves sideways -- landing on the quarter grid, or exactly with Alt. A drag on one of its
  corners moves the corner, the two pieces that meet there with it. On a wire that is not selected a drag still
  draws a new wire out of it (10b-1), so a selected wire is let go, or another clicked, to tap it.
- **The ends stay where they are** (`WireShape`): a pin, or a tap's junction. A piece that reaches one keeps the
  pin's stub before it turns, as the automatic route leaves a pin, so the wire still leaves the way the pin faces.
  A step dragged back into line with its neighbours is gone (`WireGeometry.Tidy`).
- **Layout only, and one step.** The wire is drawn where it is going while it is dragged, its selection band with
  it, and on release the whole route is kept as its bends (`WorkbenchSession.Reroute`): nothing replays. A wire
  that taps the reshaped one keeps its junction at the same fraction of the way along it.
- A wire being drawn ends on a selected wire's corner as on the wire.
- **Not built**, from the plan: a double-click adding a bend (10c gives a double-click the inspector, and a drag on
  a piece makes the bends), and Delete on one bend (dragging its step back into line takes it out). Putting a
  wire back on the automatic route goes in 10d's right-click menu.
- **Checked.** `WiringTests` (14 now) under dotnet: a piece moved across itself, at an end with its stub, a
  straight wire pulled round, a corner moved, a step dragged back into line; the gestures on a selected wire and on
  a corner; a piece dragged and let go rerouted through its new bends in one step with nothing replayed, and undone;
  a corner found under the pointer only on a selected wire, dragged on the grid, Escape mid-drag changing nothing.
  In Play Mode (`WorkbenchCanvasPlayTests`), a wire clicked, its two corners handles, and its fall dragged a cell
  right with the fixture's mouse. Reference shots 1 to 34 identical to the pixel; 35 is new: MY CIRCUIT's half
  adder wired whole, b's wire selected with its corners, and its rise being dragged a cell left.

**As built in Part 10c**: the inspector.

- **What each part has** (`Inspector`, Unity-free): a gate its name, inputs (none for NOT, BUF and the tri-state
  gates), rise and fall -- the course's Tpdlh and Tpdhl, which the gate's line says -- and width (none for a
  tri-state gate); a port its name, width and comment, and for an input of
  the top module its clock's period or its reset's release; a register its width, reset, reset value and starting
  value; a constant its value, which is its width; a mux its inputs and width; a splitter, a joiner and a lamp their
  width; a block its name; a wire its net's name. A field shows what is, as it would be typed: `2 ps`, `4'b0101`.
- **Read with words, judged by the document.** Text that is no number, time or value is refused saying which field
  and why; a width, a count or a name the document does not allow is refused in `BoardRules`' own words. Every field
  changed is one step for undo (`BoardEditor.Group`), so a clock renamed and its period changed go back together.
  Nothing changed is nothing done.
- **The popover** (`InspectorPopover`), beside what it inspects, built as RENAME is and holding Escape the same way.
  A double-click on a part, a pin or a wire opens it -- two **plain** clicks: a click that placed a part, a click
  with a modifier, or a press that became a drag begins no double-click (`CanvasPointer`), or placing a part and
  pressing it to drag it away would open it. Enter opens it on the one part or wire selected; Enter in a field, or
  APPLY, makes it; a refusal stays in the popover, in the text's colour; a click off it, Escape or CANCEL leave
  everything as it was. Temporary, as the parts strip is, until Part 11.
- **The run is played again with it** (`WorkbenchSession.Inspect`).
- **A rename keeps its row.** A net or a port renamed -- and a part renamed renames the net it drives -- moves its
  waveform rows to the new name (`Inspector.NetsRenamed`, `WaveList.Rekey`), in their place and radix, so the run
  played again finds them. Without it the row would have pointed at a name the new run does not have.
- **Not built, from the plan:** a wire's row radix in the inspector -- the row's menu and X already set it.
- **Checked.** `InspectorTests` (12) under dotnet: every kind's fields; a delay made in one step and replayed, and
  undone; words for what is no number or time, and the document's for what it forbids; nothing changed, nothing done;
  a clock renamed with its period as one step; a reset's release; a net named and given its own name back; a
  register's values at its new width and a constant's width from its value; a renamed net's row kept; the
  double-click as two plain clicks, and never a place or a modified click and then another; Enter's one selected
  thing. In Play Mode (`WorkbenchCanvasPlayTests`), **the plan's exit**: a double-click on the reference's AND opens
  its inspector, a new rise and Enter play the run again with it, and a width of 2000 is refused in the popover; NOT
  B's net, added to the waveform with Alt and a click, named through its inspector opened with Enter, says its new
  name on the hover chip and on its row, in its place. Reference shots 1 to 35 identical but for the help's first
  line in 27 to 35; 36 is new: the reference's AND with its inspector open and a width refused.

**As built in Part 10d**: copy and paste, align and spread, and the minimap. With it Part 10 is built: every editing
gesture of the brief's D3 and D4 is on the canvas.

- **Copy and paste** (`Clipboard`, Unity-free). Ctrl+C takes the parts selected and every wire whose two ends go
  with them -- a wire to a part left behind has nowhere to go -- by value, so a paste makes what was copied even
  after the document changed, and into another sample's circuit as well (the clip is the panel's, not a document's).
  Ctrl+V makes it again a cell right and a cell down, and each paste a step further on: new ids, the document's own
  new names, wires reconnected with their bends moved, a tap at its place along the pasted wire, an input of the top
  module given 0 as one placed is -- one step, replayed, what it made selected. A net's name is not pasted: two nets
  of one name would be refused. Ctrl+X copies and takes away.
- **Align and spread** (`Arrange`): the selected parts' outlines lined up by their left, right, top or bottom edge,
  or their middles in a column or a row, or spread at equal gaps across or down with the first and last where they
  are -- every new place on the quarter grid, layout only, one step. Two parts to line up, three to spread.
- **Route again**: the selected wires back on the automatic route, their bends forgotten and a tap's place along its
  wire kept (`WorkbenchSession.AutoRoute`), one step, layout only.
- **The right-click menu** (`CanvasMenu`, built like the row menu): a right-click on something not selected selects
  it alone; on the selection, or on nothing, keeps it. It offers only what does something for what is selected:
  ALIGN LEFT, RIGHT, TOP, BOTTOM, MIDDLES IN A COLUMN or A ROW, SPREAD ACROSS or DOWN, TURN, MIRROR, ROUTE AGAIN,
  COPY, CUT, PASTE, DELETE.
- **The minimap** (`Minimap`): the whole drawing small in the window's bottom-right corner (beside it since Part 10e), each part as its own
  outline -- the one the circuit draws, an AND's D, an OR's shield, a NOT's triangle and its bubble -- the ports
  as rounded chips and the wires as hairlines, the window's rectangle on it in the accent. It was blocks at first,
  every gate the same block; Ahmad asked for the gates' shapes at the browser smoke test. A splitter's bar and a
  bubble have a least size there, or a NAND would be an AND. A click or a drag on it puts the window's middle
  there. It is up **only while some of the drawing is out of the window**: the plan said "while the
  view is not the fit", but after an edit the view keeps its place and is no longer exactly the fit with the whole
  circuit still in sight, and a map of what the window already shows was clutter (shots 33 to 35 showed it).
- **An edit on the circuit never rescales the page** (`WorkbenchPanel.FitHeight`). The page is fitted to the
  window by its width and its height, and its height grew with every row: a port placed puts a row on the
  waveform, so each one shrank the whole page by a row and moved the board under the pointer -- two ports, 31
  pixels -- and the next click landed somewhere else (found in the browser smoke test). The height it is fitted
  by is now the rows it opened with -- an empty circuit as though it had a half adder's four -- or those the
  waveform's own controls last made: + SIGNAL, Alt and a click on a wire, a group opened or shut. Rows asked for
  are fitted, so they are seen; a row that came with a port scrolls the page instead. A Play Mode test places two
  ports and holds the board where it was, to half a pixel.
- **The browser smoke test** (2026-10-25), on a served WebGL build: the half adder built by hand on MY CIRCUIT --
  two inputs, an XOR and an AND, two outputs, the AND's inputs tapped off the inputs' wires -- its inputs flipped
  with double-clicks on the waveform, 1 + 1 and 1 + 0 read right; a gate's rise changed in the inspector and
  applied with Enter, and the run replayed with it; Enter on a port opening its inspector; the right-click menu
  offering only what applies, COPY and PASTE through it; Ctrl+X, Ctrl+Z and V; Space playing and pausing without
  scrolling the page, and with a drag panning; the minimap up only when zoomed in, and a click on it moving the
  view; MY CIRCUIT whole after a reload of the page. **Ctrl+V is the browser's first**: Unity's web runtime holds
  the key back until the browser's own paste event, which a real press brings and a synthetic key event does not
  -- so a test that types Ctrl+V into the page sees nothing, and one that sends the paste event too sees the
  paste. Found, and fixed here: the page rescaling under the pointer (above), and the minimap's blocks. Found,
  and fixed in Part 10e: an automatic route could run along another net's wire -- a tap from a down to the AND ran
  a cell along b's -- so two nets looked joined; the minimap could cover the circuit in the window's corner when
  zoomed in; and the waveform's rows came back in the layout's order after a reload, not the order the ports were
  placed in.
- **Checked.** `ArrangeTests` (8) under dotnet: what a copy takes and leaves; a paste a cell on with new ids and
  names, its wires, bends and tap, its input given 0, one step and a second paste further on; a cut and its paste;
  align by edge and middle on the grid with nothing replayed, and too few refused; spread with equal gaps; a wire
  routed again keeping its tap; the right-click's selection; the minimap up only off the whole drawing, the window
  on it, and a click on it centring the view. In Play Mode (`WorkbenchCanvasPlayTests`): Ctrl+C and Ctrl+V with the
  fixture's keyboard, one step back with Ctrl+Z; a right-click on two parts opening the menu, ALIGN TOP lining them
  up; the minimap up when zoomed in, a click on it moving the view, and FIT putting it away. Reference shots 1 to 36
  identical but for 28, zoomed in, which gains its minimap; 37 is new: the reference zoomed in with its minimap, the
  two ANDs selected and the menu open on them.

**As built in Part 10e**: the three things the browser smoke test found that Part 10d left.

- **An automatic route keeps clear of the other nets** (`WireGeometry.Avoiding`, `WireObstacles`). A tap from a to
  the AND ran a cell along b's wire, and a and b read as one net. Now a route that runs along another net's wire, or
  ends against one on its line under a quarter of a cell from it, gives way to the shortest route of right angles,
  with the fewest corners, that keeps clear: of every other net's wire, its line and its corners; of every pin but
  its own two ends; and of every part's body. It still leaves each pin the way the pin faces, and a tap up or down off
  the wire it taps; it goes across, or up and down, once, anywhere on the quarter grid within three cells of its
  ends; and with nothing clear it stays the automatic route. Wires are routed in the module's order, each clear of
  those before it, so a wire added never moves one already there. A wire being drawn keeps clear of the same
  things (`WireDraft.PathTo`), so it is made where it was drawn. A route the student bent keeps its bends.
- **The ports' rows keep the layout's order** (`WaveList.FollowPorts`): top to bottom, then left to right, which is
  the order the rows come back in when a circuit is opened again, since the layout alone makes them then. A port
  placed was given a row at the end, so a half adder built a, b, then y and z read a, b, y, z, and a, y, b, z after
  a reload. Now a port's row goes where the layout puts it, and a port moved takes its row with it; the ports' rows
  are put in order in the places they already hold, so the rows the student added, and a port's row in a group,
  stay where they are.
- **The minimap stands beside the window, never on it** (`WorkbenchPanel.PlaceMinimap`): in the column right of the
  stage, which the stage keeps free opposite the parts strip, its foot level with the window's. In the window's
  corner it covered the circuit there, and zoomed in that was whatever the student was looking for -- the smoke test
  lost an output under it. It is drawn at the screen's size whatever the stage's scale, as the strip is, as wide as
  that column leaves free up to its own most.
- **Checked.** Each of the first two as a bug, a red commit first: `RoutingTests` (7) under dotnet -- the smoke
  test's half adder in cells, its tap round b's wire, through no part, and a wire added moving none before it; a
  route with nothing in its way the automatic one, and one that cannot be clear the automatic one still; its own
  net no obstacle, and a quarter cell apart enough -- and in `WiringTests` a wire drawn round another net and made as
  it was drawn; `SignalListTests`, a port's row placed and moved where the layout puts it. Unity's Edit Mode 1347,
  Play Mode 275. Reference shots 1 to 37 identical but for 28 and 37, whose minimap moved out of the window.

**As built in Part 11a**: the shell's bridges, and the workbench's toolbar on it -- the first of Part 11's four
(the Part 11 plan approved 2026-10-25: 11b the docked layout, 11c the transcript and the
hierarchy, 11d the keyboard map and the docs).

- **A UI Toolkit panel made in code** (`ShellPanel`): a `UIDocument` on a `PanelSettings` made at run time, scaled
  as the game's canvas is (with its screen size, against `UiTheme.ReferenceResolution`, matching halfway) and sorted
  at 50, over the game's canvas (0) and under the frame-rate counter's (100). It is given an empty theme style sheet
  only so that UI Toolkit does not warn it has none; nothing in it is drawn by a theme. It shows and hides with the
  workbench and follows its fade.
- **Built in code, with no UXML and no USS** (`ShellTheme`, and `ShellRulesTests` in `Repo.Tests`): every element's
  layout, colours and sizes are set by the builders, from `SchematicPalette` and `UiType`, in the look of the
  workbench's uGUI controls -- outlined buttons, key caps, chips. The approved plan said USS would hold layout; with
  every element made in code there was nothing for it to do, and a style sheet would only be a second place a colour
  could hide. Its own controls: a text field with a quiet placeholder of its own, and a slider made of plain elements
  (`ShellSlider`), since no theme draws UI Toolkit's.
- **The typeface** (`ShellFonts`): IBM Plex Sans, regular and bold, as a TextCore font asset made at run time from
  the two TTFs, which the scene builder puts in the scene so they ship. A label is bold by the bold face alone: a bold
  style on it as well thickens it twice.
- **The bridges**, each held by a Play Mode test (`ShellPlayTests`):
  - *Pointer.* The event system sees the panel as one nameless object, so the panel is asked which of its elements is
    under the pointer: `WorkbenchControlSample` and `PointerGate` both ask it, and a tap on a shell button is the
    button's alone.
  - *Keyboard.* A focused shell field is never the event system's selection, so `UiText.Typing` asks the panel's
    focus as well. UI Toolkit reads the platform's keys, not the Input System's, so a test's simulated keyboard never
    reaches a shell field; a test hands it the key as UI Toolkit would (`ShellPanel.SendKey`), in the frame it presses
    the same key on the simulated keyboard -- what a real press does.
  - *Escape and Enter* let a field go a frame later, so the Escape that cancelled RUN TO cannot close the workbench in
    the same frame; and a press anywhere else lets it go, which UI Toolkit cannot see when it lands on uGUI.
  - *Scale and order.* A unit of the shell is a unit of the canvas, a Body label is Body's size, and the panel sorts
    between the canvas and the frame-rate counter.
  - *Fonts.* Every character the shell draws is in its font asset.
  - *Buttons take no keyboard focus*: a focused button would answer Space and Enter as well as the workbench's keys. A
    test presses one with `ShellPanel.Submit`.
- **The toolbar** (`WorkbenchPanel.Toolbar.cs`): the title and SAMPLE, RESTART, PLAY, STEP BACK, STEP and the chip,
  the slider, the speed, RUN TO and the time, and BACK -- where the uGUI rows stood, as wide as the stage is drawn. The
  uGUI rows, `ReleaseRunTo` and the strips are gone.
- **The browser build found three things the editor cannot**, each fixed and then seen working in a served WebGL
  build -- the first click into RUN TO, 20, Enter and Space ran the run and stopped it at 20.0 ps:
  - *Stripping.* The editor strips no managed code and the browser build does: a field's record made by
    `ConditionalWeakTable.GetOrCreateValue`, which builds by reflection, lost its constructor, and Enter threw. It is
    made by a constructor called in code now, and `ShellRulesTests` refuses `GetOrCreateValue`, `Activator.` and
    `Type.GetType` in anything the game runs. The browser's console is where it showed; a smoke test reads it.
  - *A delayed field.* RUN TO committed what was typed only after Enter was handled, so Enter read nothing. A shell
    field's value is what it shows, a keystroke at a time; Enter submits it, letting it go by a click elsewhere submits
    it as the TextMesh Pro field did, and Escape puts back what it held when it was clicked into.
  - *The first press.* The first press into a field after the workbench opened focused it, and UI Toolkit let it go in
    the same frame, with the app focused and the event system's selection unchanged. The shell gives a field a press
    focused the focus back, once, the frame after, if nothing else took it (`ShellPanel`).
  The editor cannot type into a UI Toolkit field at all -- it takes typing from the platform, which a test cannot
  drive -- so typing is proved in the browser, and the Play Mode tests set a field's value as typing does.
- **Checked.** `ShellPlayTests` (6): a tap on PLAY; RUN TO holding the keys and Escape letting it go before the
  workbench closes; what is typed and Enter setting RUN TO, and Escape putting the old value back; a click on the
  circuit letting the field go; scale, order and the fade; the font. The workbench's own suites moved onto the shell's
  controls. `ShellRulesTests` (4) under dotnet. Edit Mode 1347, Play Mode 281. Reference shots 1 to 26 identical; 27
  to 37 differ in the toolbar's band alone, drawn by UI Toolkit and looked at beside the uGUI one; two captures of the
  same code identical, all 37.

**As built in Part 11b-1**: the docked layout. 11b is three pull requests: 11b-1 the panels, 11b-2 the inspector
re-hosted in them, 11b-3 the popups moved into the shell.

- **Where each panel is, as a rule under dotnet** (`ShellLayout`, `ShellRegions`, in the view-model): under the
  toolbar, the parts panel down the left and the side panel down the right, each a fixed width; the circuit's window
  between them; the splitter; the dock, the window's whole width -- its header, the waves, the legend at its foot; and
  the status line, two lines of help until the keyboard map. **Panels keep their size in a small window** (Ahmad's
  choice): the circuit and the waves take what is left. **The dock's height is the student's**, dragged on the
  splitter, held between a header, a row or two of waves and the legend, and what leaves the circuit 160; kept in
  `Preferences` on release (`workbench.dock`), 370 until then -- the reference circuit's nine rows.
- **The stage is no longer one card scaled to the window.** Each drawing is placed, at its own size, under the panel
  that frames it, and moved when the layout changes, nothing built again (`PlaceStage`); only a change in the waves'
  width builds the waveform again. Gone: `StageScale`, `FitHeight`, `MinStageScale`, `MinStageWidth`, `Overflow`,
  the page and its scroll, and the card, whose outline the panels' frames replace and whose legend is a layer of its
  own (`WorkbenchGraphic.Layer.Legend`). A panel's middle takes no clicks, so a press on the circuit or the waves goes
  through the shell to them.
- **The circuit's window is resized, not rebuilt** (`SchematicView.Resize`): a view at the fit is the fit of the new
  window, any other keeps its zoom and its corner. **The fit centres the drawing** in a window that is now larger than
  it -- **except a blank one** (`SchematicView.Blank`), which starts at the window's top-left corner as a page does:
  centred, a half adder built from MY CIRCUIT blank down and to the right ran off the window's foot.
- **The waves scroll in a window that clips them**: the wheel over the names and the values moves the rows a row a
  notch, up and down and the page keys too, and a point outside the window is over none of them
  (`WaveformView`'s clip). The waveform's buttons -- + SIGNAL, GROUP, NAMES, UNIT, EXPORT -- are the dock header's, in
  the shell, beside a WAVES tab that 11c gives a neighbour.
- **The parts panel** files the catalogue under PORTS, GATES, MORE and BLOCKS (`CatalogEntry.Group`), three to a row
  and a block to a row of its own, and scrolls under the wheel when it is shorter than they are. **The minimap is at
  the side panel's foot**, which 11b-2 gives the quick fields above it.
- **The popups are on a canvas of their own** (`PopupLayer`), sorted over the shell: a uGUI menu is drawn under a UI
  Toolkit panel, and the canvas's menu, opened low on the circuit, ran under the dock's buttons, which took its clicks.
  11b-3 moves them into the shell and this layer goes.
- **The toolbar spans the window**, its header short of BACK, and the chip takes no more than it needs.
- **Checked.** `ShellLayoutTests` (7) under dotnet, and the panel's headings (`CanvasTests`). `WorkbenchDockPlayTests`
  (6): the circuit's window, the waves and the legend exactly where their panels are; the splitter dragged with the
  fixture's own mouse, the dock taller by the drag, the circuit fitted to its new window, the height kept; the wheel
  over the names of a short dock scrolling the rows; the dock's buttons and the legend inside the dock; every part
  inside its panel, and a click taking AND in hand; and the panel scrolling under the wheel. Edit Mode 1346 (the
  stage's scale test is `ShellLayoutTests` now). Reference shots 1 to 26 identical; 27 to 37 the new layout, each
  looked at.

**As built in Part 11b-2**: the inspector, docked (`WorkbenchPanel.Inspector.cs`). The popover of Part 10c is gone.

- **Two places, always there, as Ahmad chose**: the **bar** under the toolbar holds all of it -- the title, the line
  saying what it is, every field, APPLY and REVERT; the **side panel** holds the **quick** fields over the minimap
  (`Inspection.Quick`, under dotnet): a gate's rise and fall; a port's name and width, a clock's period, a reset's
  release; a register's reset value and starting value; a constant's value; a mux's, splitter's, joiner's or lamp's
  width; a block's name; a wire's net's name. Neither comes and goes, so the circuit's window never changes size with
  the selection.
- **It follows the selection**: one part or one wire is inspected; nothing, or more than one, is said in the bar's
  title ("NOTHING SELECTED", "2 PARTS SELECTED"). Both read the document again after every edit -- the document and
  the selection are compared by reference each frame -- so a change made in one shows in the other.
- **The bar is a form; a quick field is one field.** What is typed in the bar waits for Enter in any of its fields, or
  APPLY, and is made as one step, so fields that only make sense together -- a register made wider and given a wider
  reset value -- are made together; REVERT puts them back. A quick field is made on its own, on Enter or on being let
  go, as RUN TO is (`ShellTheme.Field`'s `submitOnLeave`).
- **A double-click, or Enter with one thing selected, puts the keyboard in the bar's first field**, its text chosen,
  where the popover opened. A refusal is the bar's line, in the document's words and the text's colour, until the next
  thing is made.
- **The toolbar is a row shorter**: the line describing the sample stands beside the title, cut short where it runs
  long, and the controls move up under it, so the bar costs the circuit 36 units rather than 74.
- **Until it is dragged, the dock is half the room under the bar**, and no taller than the reference circuit's nine rows
  (`ShellLayout.DefaultDockFor`): at 1080 that is the nine rows, and in a browser tab 1920 by 800 the circuit keeps
  half. At 1080 the reference fits its window at about 0.9 of its own size.
- **A shell field's text sits in its middle**: the theme the shell has none of is what stretches a field's text to its
  height, and every value in the bar sat at the top of its box until `ShellTheme.Field` stretched it.
- **Checked.** Under dotnet: each kind's quick fields, each one of its fields (`InspectorTests`); the bar under the
  toolbar and the dock's default (`ShellLayoutTests`). In Play Mode: a double-click on the reference's AND putting the
  keyboard in the bar, a rise typed and Enter making it and the run played again, a width of 2000 refused in the
  bar's line and REVERT putting it back; a wire selected and Enter putting the keyboard in its net's name, APPLY
  naming it, its row keeping its place; a quick rise made with Enter and read by the bar; and the bar saying nothing,
  and two parts, are selected; and a shell field's text in the middle of its box (`ShellPlayTests`, red first). Edit
  Mode 1346, Play Mode 288. Reference shots 1 to 26 identical; 27 to 37 the bar and
  the shorter toolbar, each looked at.

**As built in Part 11b-3**: the popups in the shell, and the canvas of 11b-1 that kept them over it gone.

- **One popup, five uses** (`ShellPopup`): a layer over the whole window and a panel on it, put up where it was asked
  for and kept on the screen. The four menus -- a row's, the moment's, EXPORT's and the canvas's -- share their column
  of items through `WaveMenu`, as they did in uGUI; RENAME, BREAK ON VALUE and SET are one field (`WaveRenameField`);
  and the + SIGNAL list (`WaveSignalPicker`). Each keeps its key, its place and its hold on Escape
  (`IHoldsEscape`): the component that reads the key is unchanged, and only what it draws moved.
- **The layer is a control**: the press that closes a popup is the layer's, so it never seeks the waves or selects on
  the circuit under it, as the uGUI blocker was a button. **The panel takes the presses on its own ground**, so a press
  between two items is the panel's.
- **Their fields are the shell's**: a click off RENAME, BREAK ON VALUE or SET makes nothing, as before
  (`submitOnLeave` false); the + SIGNAL list's search keeps the keyboard after Enter, for the next search
  (`ShellTheme.Field`'s `releaseOnEnter`), and Escape in any of them closes its popup.
- **Checked.** The workbench's suites unchanged but for reaching the items through the shell: the row menu's radix,
  REMOVE and UNGROUP; Escape closing a menu before the workbench, and a click off one closing it without seeking; the
  + SIGNAL list's search, CELLS and a click off it; RENAME; BREAK ON VALUE; SET over a stretch; the moment's
  breakpoint; EXPORT's VCD and PNG; and the canvas's menu aligning two parts. Edit Mode 1346, Play Mode 288. Reference
  shots 1 to 31 and 33 to 36 identical; 32 and 37, the + SIGNAL list and the canvas's menu, drawn by the shell and
  looked at; two captures identical, the list's field holding the keyboard included.

**As built in Part 11c**: the transcript and the hierarchy.

- **The transcript** (`TranscriptView`, under dotnet): the document's diagnostics first, then the run's messages in time
  order, appended as the run is made a slice at a time without reading the old ones again; a filter (ALL, ERRORS,
  WARNINGS, MESSAGES), a search ignoring case, and counts of everything whatever is shown. It reads the document's and
  the run's lists each frame and makes itself again only when either has changed.
- **The dock's TRANSCRIPT tab**, beside WAVES, with its filters and search in the header where the waveform's buttons
  were. **Each line says its severity in a word as well as a colour** -- ERROR in x's colour, WARNING in the second
  cursor's, INFO quiet -- and a message its moment in the run's unit. **A click on a message seeks to its moment, paused;
  a click on a diagnostic about a part or a wire of the circuit shown selects it alone** (Ahmad's choices), and the
  inspector follows. The tab counts the errors. Only the rows the dock has room for are made, and the wheel moves
  through the rest. The waves, their legend and their buttons step aside while it is shown.
- **The hierarchy** (`HierarchyTree`, under dotnet), from `SignalIndex.Root`: the testbench, the design, its blocks
  and generate blocks, never a library cell's scope nor a process's named block, each row opening and shutting on its
  + and -. **A click on a scope adds its ports to the waveform as one group named by its path** -- `h1`, or the
  module's name for the design itself -- and a scope with no ports adds what it declares (`WaveList.AddGroup`; a signal
  already shown moves into the group). The dock then shows the waves.
- **The parts panel has two tabs**, PARTS and DESIGN, where the plan put the hierarchy under the parts: the parts fill
  the panel at 1080, and under them it would have been out of sight. The dock's tools start further right, past the
  two tabs.
- **Checked.** Under dotnet (`TranscriptTests`): the order and the counts; the filter and the search; a growing run
  appended, and nothing new being no change; the full adder's hierarchy, never a cell, opening and shutting; and a
  click on h1 making a group of its four ports, h1.s taken out of where it was. In Play Mode: a lone AND's open inputs
  listed as warnings, said in a word, and a click on one selecting the AND for the inspector; ERRORS showing none;
  the reference's $stop line seeking to its moment; and h1 clicked in the full adder's hierarchy adding its group, and
  - shutting the design's row. Reference shot 38, new: the full adder's transcript beside its hierarchy. 27 to 37
  differ in the parts panel's tabs and the dock's header alone.

**As built in Part 11d**: the keyboard map, the status line, and the docs. Part 11 is built.

- **The keyboard map** (`KeyMap`, under dotnet): every key the workbench answers, from `WorkbenchKeys` alone, under
  RUN, CIRCUIT, WAVES and VIEW -- two keys doing one thing in one cap are one line -- and the mouse's gestures on the
  circuit and on the waves, which no table held. **`?` opens and shuts it** (a new binding, Slash, which the game and
  the browser leave free), a popup of the shell in the window's middle (`WorkbenchKeyMap`); Escape or a click off it
  shuts it before the workbench. Its caps show whatever SHORTCUT KEYS says: it is the place to look them up.
- **The status line is one line**, the gestures where the pointer is -- the circuit's over the circuit, the waves'
  over the waves -- and `?` for every key (`KeyMap.StatusFor`). The two lines of help along the bottom, which named
  every gesture at once and no key, go, and the 28 units they took go to the circuit and the waves.
- **No second copy of a key**: `ControlsReference` has no workbench part to take from `WorkbenchKeys` -- the
  workbench's keys were only ever listed in the help lines, which the map replaces. Ctrl+Z's words had stayed "undo the
  last edit to an input" since Part 9b-3; it undoes any edit, and says so.
- **Not done: `VectorPainter`'s tessellation into the view-model.** The plan made it optional, on
  `WorkbenchPerformanceTests` showing a frame no slower; nothing in Part 11 needed it, and moving it is a change to
  every drawing for no gain the student sees. Left for when a test under dotnet would want the meshes.
- **SHORTCUT KEYS** (asked for during 11d, its own commit and pull request before it): Settings' switch that hides
  every key beside a button, the whole game, shown by default (`KeyCaps`; CLAUDE.md, the controls).
- **The browser smoke test** (2026-10-26), on a served WebGL build, with a build that logged every focus change to
  the console to find out why: Space, `?` and Escape, the splitter, the transcript seeking, the hierarchy adding
  dut's ports, the fonts and the popups all worked. Two things were wrong, both in how a shell field is given the
  keyboard, and neither visible to a test's simulated input:
  - **A field given the keyboard was let go again by UI Toolkit**, to nothing, after the press that gave it --
    the + SIGNAL search after a click on + SIGNAL, the inspector after a double-click on a part, which it did in
    every frame the button was still down -- so the first keys typed went nowhere. Part 11a had seen it once, for
    a click into RUN TO, and given the field back once. Now the field given the keyboard keeps it until it is let
    go on purpose -- Enter or Escape, a later press anywhere else, or the field going -- and is given it back
    whenever it is let go to nothing (`ShellPanel`).
  - **A key that asks for a field took that field's key as well**: the platform hands a key to UI Toolkit too, and
    that half can arrive after the game has read it, so A typed an "a" into the search it had opened, and Enter
    submitted the inspector's first field and let it go. Both now give the field the keyboard the frame after
    (`ShellPanel.FocusAfterTheKey`).
  - Checked for the third time that **the pane's screenshot takes the page's focus** and UI Toolkit then lets a
    field go: two of the things first written down as bugs here were the test, not the game. Type and finish
    without a screenshot in between.
  - **Still open: the inspector's first field does not keep all of its text chosen in the browser.** Given the
    keyboard by Enter or by a double-click on a part, it should replace the part's name with what is typed; in
    the browser the caret is at its start instead, and "z" made the AND's name "zg2". The editor chooses all of
    it on focus, so no test can see it, and finding out why would take more browser builds than it is worth now.
    A rebuilt build checked the rest: every key typed into + SIGNAL's search landed, A and Enter were not typed
    into the fields they opened, Escape in a quick field put its value back and left the workbench up, RUN TO
    and Enter ran to the time and gave the keys back, `?` and Escape, and SHORTCUT KEYS hiding every cap; the
    console had no errors.
- **Checked.** `KeyMapTests` (3) under dotnet: every key on the map under one heading, ? on a key the game and the
  browser leave free, the gestures listed and the status line one line. In Play Mode: ? opening the map with every
  key's words on it, Escape shutting it before the workbench and ? again shutting it; the status line naming the
  circuit's gestures, the waves' and elsewhere ?; and SHORTCUT KEYS hiding the workbench's caps while the map keeps its
  own. Reference shot 39, new: the keyboard map over the reference; 27 to 38 differ in the status line and the room it
  gave back.

**As built in Part 12a**: stepping inside a block. The plan for Part 12, approved 2026-10-26, is six pull requests --
12a stepping inside, 12b making a block from a selection, 12c arrays of blocks, 12d code blocks, 12e the code panel,
12f CodeMirror -- with Ahmad's choices: a double-click steps in, the breadcrumb or Alt+Up steps out, arrays now, the
code editor in the circuit's window, and a block made from a selection asks for its name.

- **The groundwork came first** (its own pull request, merged with the plan): `BlockMaker`, a selection made into a
  block with the run unchanged, for 12b; and `DocumentSchematic.Build` for any module of the design, its signals read
  under an instance's path.
- **The session edits a module on a path** (`WorkbenchSession.Path`, `Module`): the instances stepped through from
  the top, the canvas drawing and editing the last one's module, its wires showing that instance's values. Every edit
  already went by module name, so none of them changed. **The run stays the whole design's**: a step draws again, as
  a moved part does, and replays nothing; a replay is laid out on the path it was made on, and drawn again on every
  step. An edit, or an undo, that takes away an instance stepped through steps out to the last one still there.
- **Editing inside edits the module**, so every instance of it changes, as in SystemVerilog; with nothing selected,
  the inspector's line says how many there are ("what you change here changes all 2 of them").
- **In and out** (Ahmad's choices): a double-click on a block steps inside it -- on any other part it still inspects
  -- as do STEP INSIDE in a block's quick panel and a second click on an instance in DESIGN. A crumb of the breadcrumb
  in the circuit window's top-left corner, or Alt+Up, steps out. Backspace was already a second DEL, so the key is
  Alt+Up: `KeyBinding` gains Alt, Up alone still scrolls the waves, and a binding on a key the browser keeps with Alt
  -- back, forward, home, the address bar, its menus -- is refused as Ctrl's are.
- A step lets go of what was selected or in hand: a part's id in one module is another part's in the next. The new
  drawing is fitted to the window; the waves keep their rows and their view.
- **PARTS offers no block that holds the module shown** (`PartsCatalog.For`): stepped inside a half adder, the full
  adder round it is no part to place, since the document refuses a module inside itself.
- **The quick panel says what its fields mean** (asked for during 12a, its own commit): the part's own line under
  them, as the bar reads it -- a gate's rise is Tpdlh, a change to 1, and its fall Tpdhl, a change to 0.
- **Checked.** `SteppingTests` (7) under dotnet: in and out with the inner module read under the instance, only a
  block or a scope of the design stepped into, an edit inside reaching both instances' signals in the run, an undone
  instance stepping out, the canvas letting go on a step, the parts list inside, and Alt+Up's binding. In Play Mode: a double-click on h1,
  the breadcrumb back out, STEP INSIDE on h2, Up alone staying and Alt+Up stepping out. Reference shot 40, new: the
  full adder stepped inside h1; 39 differs in the key map's two new lines.

**As built in Part 12b**: a block made from a selection.

- **MAKE BLOCK** in the canvas's menu, with parts selected, **asks for the new module's name first** (Ahmad's choice),
  offering `block1`, `block2`... in a one-line field (RENAME's, titled NAME THE NEW BLOCK). Enter makes it; Escape asks
  nothing and makes nothing. No key: Ctrl+B is Firefox's bookmarks.
- **What it does is `BlockMaker`'s** (the groundwork), through the session (`WorkbenchSession.MakeBlock`, drawn at once
  and replayed) and the canvas (`CanvasEditor.MakeBlock`, the new instance selected): the parts and the wires between
  them moved into a new module, **a port for each net that crosses the selection's edge** -- an input where it is
  driven outside, an output where inside, named for its net -- and one instance standing at the middle of where they
  were, wired to the same nets, **in one undo step, the circuit doing exactly what it did**. A port of the module, a
  net driven both inside and out, or a name taken is refused in the bar's line, in the document's words.
- **PARTS follows the document's modules** (`RefreshPartsPanel`): a block made, or an undo taking one away, makes
  its BLOCKS again -- and only then, since a panel made twice is laid out afresh in the frame something reads where
  it is.
- **Checked.** `BlockMakerTests` (7) under dotnet, each holding the run's settled outputs equal before and after:
  half a flat full adder, the other half, a block inside a block, gates whose inputs reach them through taps, and one
  gate of a tapped pair, its partner keeping the tap; one undo, and the refusals. `MakeBlockTests` (2): the session
  and the canvas making one, selected and replayed to the same run, and a port refused in the document's words. In
  Play Mode: h2 and the OR selected, MAKE BLOCK asking with block1 offered, Enter making `tail` -- selected, in PARTS,
  the waves' rows as they were -- and Escape making nothing, the workbench still up. Reference shot 41, new: MAKE BLOCK
  asking over the full adder's h2 and OR.

**As built in Part 12c**: arrays of blocks.

- **A block has `Copies`**, 1 to 64 (`BlockPart.Copies`, `BlockPart.MaxCopies`): that many instances of its module
  side by side, **each pin a bus of every copy's port, copy i's bits at i** -- a 2-bit port on four copies is an 8-bit
  pin, copy 2's bits at [5:4]. `BoardRules` refuses a count outside the range, and a pin wider than a bus can be.
- **Printed as a `generate for`**, in the course's style (`BoardCompiler`): `for (genvar i = 0; i < 4; i++) begin :
  ha` round one instance `u`, each port connected to its copy's bits -- `a[i]`, or `d[i*2 +: 2]` for a wider one. The
  elaborator already ran `generate`; **Icarus runs the printed text too**, and agrees on every signal: four half
  adders, and an 8-bit ripple-carry adder of eight full adders whose carry goes copy to copy through a splitter and a
  joiner. `BoardImporter` reads such a loop back as an array, so a printed design round-trips.
- **Saved as `copies`** on the block, a key a file before it reads as absent, so as 1.
- **The inspector has COPIES**, in the bar and the quick panel; the box is drawn with its copies stacked behind it,
  up and to the right, and named `half_adder ×4`, the name raised clear of the stack.
- **Stepping into an array steps into its first copy** (`ha[0]`, its signals under `ha[0].u`); any copy is stepped
  into by its scope from DESIGN or the transcript, and an edit inside is every copy's, as for any block.
- **A new sample, Four half adders**, is one block of four copies adding two 4-bit numbers bit by bit.
- **What it does not do yet.** Bit i goes to copy i and nowhere else, so a carry chained from copy to copy, or one
  pin shared by every copy, needs a splitter and a joiner round it, as the ripple adder has; a pin that chains or is
  shared would be a later change. And COPIES is set before wiring: changing it on a wired block makes its pins wider
  than its wires, and the document refuses the edit.
- **Checked.** `ArrayTests` (4) under dotnet: the pins, the printed loop, the counts refused and the ripple adder
  adding; two new sample boards in the round-trip and oracle tests; `SteppingTests` gains stepping into an array and
  COPIES in the inspector; the samples' tests the new sample, s and c copy i's sum of bit i. In Play Mode: COPIES 4 in
  the inspector widens the pins, and a double-click steps into `ha[0]`. Reference shot 42, new: Four half adders with
  its block selected; 27 to 41 differ in the sample chip, which is as wide as the longest sample's name.

**As built in Part 12d**: blocks written as code.

- **A module may be written** (`Module.Source`): SystemVerilog text in place of a drawing, with no parts and no wires.
  A block of it is placed, wired, copied and stepped into like any block. The top was drawn until Part 13b, which lets
  an imported design be written too (see "As built in Part 13b").
- **Its text is read by elaborating it** (`WrittenModules`): parsed, held to one module of its own name -- no
  interface, package, `timescale, `timeunit` or `timeprecision`, since a block keeps the project's unit -- then
  elaborated among the library's cells, the other written modules and a header of each drawn one. **Once as the top,
  for its ports**, whose widths are the language's own, parameters and all; **and once as an instance**, which is all
  a written module ever is in the design: there its inputs are wires, where at the top they are variables a process
  may write, and a text legal only at the top would have passed the rules and failed when the design ran (found by
  the review before the first commit). Each fault is a `BoardDiagnostic` at its line and column. A reading is kept for
  the same text in the same surroundings, since the rules ask on every edit.
- **Compiled as written, printed as written**: the compiler adds the parsed module and the cells it uses; the printer
  writes the text itself, which is what the run was read from. **Icarus runs a written sample as printed and agrees
  on every signal.** Renaming a module renames it in every text that says it -- its own header, and wherever another
  instantiates it -- and nothing else. Saved as `source` on the module, a key an older file reads as absent.
- **The editor is the circuit's window** (Ahmad left the place to me; `CodeEditorView`): stepping into a written block
  shows its text where the circuit is drawn, the dock with the waves still under it -- edit, run, and watch the waves
  in one view, the course's loop. **Typing applies itself** once it pauses for 0.7 seconds (`CodeDocument`), one undo
  step, and the run is made again; Ctrl+Enter applies at once. **Text with an error stays the editor's**: marked beside
  its line number (`x`, and the colour of an unknown value), listed under the text with a click to its place, and the
  document keeps the last text that worked, so the run is never of a design that does not build. A header that would
  leave a wire without its pin is said there too, in the document's words. An undo is followed. **REVERT** puts back
  the document's text.
- **Escape lets the code go and holds the press**, as RUN TO's does; a second closes the workbench. Ctrl+Enter is read
  from the keyboard while the field holds it (`UiText.FieldKeys`), since TextMesh Pro would type it as a new line.
- **The letters are a monospaced face the computer has** (`CodeFont`: Consolas, Menlo, DejaVu Sans Mono...), since none
  ships with the game; in a browser there is none, and CodeMirror takes the window's place in 12f.
- **+ CODE**, the last cell of PARTS' MORE, asks for a name, offering `code1`, makes the module from a template -- an input,
  an output, and the output following the input -- and puts a block of it in hand. Inside written code, PARTS says
  there is nothing to place.
- **A new sample, Written adder**: a four-bit adder written with a parameter for its width, inside a drawn top.
- **Not yet.** The code is TextMesh Pro's field, with no colouring of keywords; CodeMirror brings that in a browser
  (12f). A written top, and a written module importing a package, wait for a part that asks for them.
- **Checked.** Under dotnet: `WrittenModuleTests` (16 cases): pins from a parameterised header, compiled and adding, a fault
  at its line and an edit making one refused, what a text must be, cells and other written modules used and the
  design run, a module holding itself refused, renaming, a header changed under a wire, a top that stays drawn, the
  template, and text legal only at the top refused; `CodeDocumentTests` (6); a written sample in every round-trip test
  but the import's and in the oracle; and PARTS offering no written module whose text holds the module shown. In Play
  Mode: the written adder's code opened by a double-click, typing applied once it paused, a fault listed and a click
  on it, Escape letting go with the workbench still up, and + CODE naming a block and putting it in hand.
  Reference shot 43, new: the written adder's code with a fault typed, marked and listed; 27 to 42 differ in the + CODE
  cell, and 39 in the key map's new line too.

**As built in Part 12e**: the code panel.

- **The dock has a third tab, CODE**, beside WAVES and TRANSCRIPT: the SystemVerilog the document prints as, read-only
  and printed again as it is drawn -- a drawn module as the text the run was made from, a written one as its own
  (`CodeListing`, kept by the session for as long as the document is the same, so asking every frame costs nothing
  between edits).
- **A chip a file**, in the order a simulator reads them: CELLS, each module after those it uses, TESTBENCH last. The file
  shown follows the module the circuit's window shows -- stepping into a block shows its module -- until a chip is chosen.
- **Read-only, and selectable** (`CodeListingView`): a field rather than a label, so a student can select the code and
  copy it out; it holds Escape while it has the keyboard, as the code editor's does, and a click on it is never a wave
  gesture.
- **The tools moved right** to make room for the tab: + SIGNAL and the rest, and the transcript's filters.
- **Found by the review before the first commit**, each fixed with a test: a chip's file was dropped on every rebuild
  of the stage (an edit, a replay taking over); a field that finished because the other code field was clicked took the
  keyboard from that one too (`UiTheme.Defocus(field)` lets go of a field alone); a plain click in the listing took every
  workbench key with no caret to show it (it keeps the keyboard only while text is selected); and the wheel over its
  line numbers scrolled nothing.
- **Checked.** `CodeListingTests` (3) under dotnet: every file in a simulator's order with its caption, a written module
  as its own text, and the session's listing printed again only when the document changes. In Play Mode: CODE showing
  the full adder, a gate placed in it at once, the testbench's chip, the half adder's file on stepping into h1, and
  WAVES putting it away. Reference shot 44, new: the full adder's code in the dock; 27 to 38 and 40 to 43 differ in the
  dock's header, where the tab is and the tools moved.

**As built in Part 12f**: CodeMirror in the browser, and the code's own face.

- **In a browser build, a written block's code is CodeMirror 6**: a box the page lays over the canvas where the code's
  body is (`CodeMirrorBridge`, `Assets/Plugins/WebGL/BitSorterCode.jslib`), with SystemVerilog's words coloured, line
  numbers, undo, Tab indenting, and each fault from `CodeDocument` underlined at its place, in the game's own colours.
  Everywhere else -- the editor, a desktop build, the tests -- it is TextMesh Pro's field, and both read and write the
  one `CodeDocument`: typing goes in once it pauses, Ctrl+Enter applies at once, an undo or REVERT goes back out to the
  page, Escape lets the editor go and holds the press. The line over the code, REVERT and the faults' list stay the
  game's.
- **The page draws it over the canvas, so over every popup too**: it hides while a shell popup is open
  (`ShellPopup.AnyOpen`). **While it has the keyboard the game's keys are its** -- `UiText.Typing` asks the page, once a
  frame -- and the canvas stops taking every key (`WebGLInput.captureAllKeyboardInput`).
- **Bundled, never fetched**: `Tools/codemirror` pins every package in `package-lock.json`; `build.ps1` installs exactly
  those, with no install scripts run, and bundles them into `codemirror.jspre`, about 340 KB before the build compresses
  it, which Unity puts at the head of a browser build. It runs the editor's own tests first, under Node's test runner
  (`Tools/codemirror/test`, Part 14g), on what needs no page (`src/game-text.js`). Its head names each package, its version and its copyright line.
  The privacy scan reads it like any file; its one nine-digit number, CodeMirror's 10^9 less one, is on the allowlist
  with that reason.
- **IBM Plex Mono ships as the code's face** (Ahmad approved the download, 2026-10-26): the family of the game's Plex
  Sans, under the same licence, held by `ShellFonts` so every build has it. `CodeFont` takes it first, and a monospaced
  face the computer has only in a scene without it. Credited in the README, `LICENSE`, and -- CodeMirror -- the credits
  roll's BUILT WITH.
- **Checked.** In Play Mode: the code set in Plex Mono, and a popup open known. The bridge itself runs only in a browser,
  so **one browser build checks all of Part 12**: the page's half -- the editor made, placed, filled, marked, typed in,
  Ctrl+Enter and Escape -- checked by driving it in the served build (2026-10-26); the game driving it waits for a pane
  that runs at full speed. Reference shots 43 and 44 differ in the code's face.

**As built in Part 13a**: a project exported for ModelSim. The plan for Part 13, approved 2026-10-26, is six pull
requests -- 13a export, 13b import with the course's own testbench, 13c CONVERT, 13d files and the import flow, 13e the
course's templates, 13f limits and the browser -- with Ahmad's choices: an imported testbench runs as written, and
CONVERT turns any of them into editable stimulus; an import goes into MY CIRCUIT (until Part 15a made each a circuit of
its own); desktop keeps fixed folders; a
template is imported, then exported filled.

- **FILE, beside SAMPLE**, opens a menu (`FileMenu`): EXPORT PROJECT now, the rest as Part 13 goes on.
- **EXPORT PROJECT saves a zip ModelSim opens** (`ProjectExport`): a folder named for the project with every file it
  prints as -- the cells, each module after those it uses, the testbench last, a written module as its own text -- and
  a `run.do` that makes the work library, compiles them in that order with `vlog -sv`, simulates the top with every
  signal visible, adds them all to the wave window and runs. Unzipped, ModelSim's `do run.do` is the whole of it.
  Saved as every export is (`FileSaver`): a download in a browser, a file in the Exports folder elsewhere.
- **The zip is written in memory and stored, not compressed** (`ZipWriter`): no deflater to depend on in a browser,
  every unzip tool reads it, and the same project and moment make the same bytes.
- **EXPORT PROJECT exports the code on screen**, not the last code that applied. A written module's editor keeps typed
  text as its own until a 700 ms pause or Ctrl+Enter applies it, and text with an error as its own for good ("NOT
  APPLIED"), while the document holds the last text that applied -- so a student who typed a fix and exported at once got
  the code from before it, with nothing to say so. Found after 13a was built, when 13e met the same gap in EXPORT FOR
  SUBMISSION. The export now applies pending code first and, when it does not apply, saves nothing and puts what is
  wrong with it on the chip, never the last text that worked. The check is one private method,
  `WorkbenchPanel.ApplyTypedCode`, which EXPORT FOR SUBMISSION shares: the two differ only in the word for what then
  does not happen, "exported" or "handed in".
- **Checked.** `ProjectExportTests` (14) under dotnet: every sample's zip read back by .NET's own `ZipArchive`, its
  files and their order, the script's lines, the bytes and the moment kept, the checksum against the standard's own
  example, and a written module as its own text. In Play Mode: FILE, EXPORT PROJECT, and the zip in the redirected
  Exports folder; and in the written adder, typed code with a fault refused with nothing saved and the chip saying why,
  then typed code that applies exported as it stands on screen. That test types a minute ahead on the editor's own
  clock, which is in milliseconds, so the pause cannot run out and apply the text before the export looks. Reference
  shot 45, new: the FILE menu; 27 to 44 differ in the FILE chip.

**As built in Part 13b**: a course project imported, in the pure layers; 13d gives it a door in the workbench.

- **Files in, a project out** (`ProjectImporter`): the `.sv` and `.v` files read together, as a simulator compiles a
  list of files, and every note said with its file and line. **Each module is drawn when it can be, and written
  otherwise**, deciding the modules it uses first, so a structural module over a written one is drawn with it as a
  block (`BoardImporter` now takes the modules already written). **A module something looks inside** --
  `dut.state`, `dut.u1.q`, as three of the course's testbenches do -- is written, with every module on the way, so
  what is named is there under its own name; a drawing keeps only the nets. A module of a library cell's name is the
  library's, checked for the same ports (A to D, Z) and the same default delays of 1, which the course's gate files
  have; one that differs is said. A file that does not read is named at its line and left out; the import is refused
  only when no design can be made.
- **The testbench runs as written** (`Project.Bench`, Ahmad's choice): the module with no ports that nothing
  instantiates and that instantiates the design. It is a written module, compiled as the run's top instead of a
  testbench made from the stimulus, and `DesignPath` is its instance of the design. **The design** is what it
  instantiates -- of several, the one whose instance is called dut or uut, else the one with the most ports, since a
  clock generator or a model beside the design has fewer; the import says which. One made inside a generate if or for
  is refused, since which instance is the design is then the elaborator's to say.
- **The run's end is where the testbench stops**: the import runs it once -- to its `$stop` or `$finish`; to its last
  change and a tenth more when it simply runs out, as nine of the course's fifteen testbenches do
  (`Simulator.NextEventTime`, null once nothing is left to happen); or as far as `MaxRun` and `MaxEvents` let it, with
  a note -- and keeps that as the stimulus's `End`, with no tracks, so the timeline and the run's horizon are unchanged
  code. **While it drives the inputs, SET and invert are refused** with where to change them; CONVERT (13c) is the way
  to editable stimulus. Renaming the testbench takes it along; taking it out gives the inputs back to the stimulus.
  Without a testbench the design is the module nothing instantiates, and has no stimulus.
- **The top may be written** now, as an imported design that is not structural is; the circuit's window shows its
  code at the top, and the schematic and the testbench made from a stimulus take its ports from its header.
- **A written module is its own text, cut from its file** (`ModuleCutter`) at its own `endmodule`, wherever on its line
  that is: the comments and directives just above it, and in front the `define`s, includes and conditionals the file
  made between earlier modules, read without their comments -- so the seven modules of `processor/mcpu.sv` each read
  alone, and `ifdef SIM ... else ... endif` above a module still chooses. Left out: a guard round the whole file, a
  `timescale` (**which sets the project's unit** instead), an include of a file whose modules are the project's, and
  the line the export heads a module's file with, "// reg4: written in BitSorter." (`BoardPrinter.WithoutHeader`), so a
  project exported and imported again is the text it was -- until the 14g-4 review found it, each round added one.
  A file whose conditionals choose *between* modules cannot be cut, and the import says so. An include is named by its
  file's bare name, since that is how a project keeps its files.
- **A project has other files** (`Project.Files`, `ProjectFiles`): what a written module includes and what
  `$readmemh` reads, each by a bare name -- the course's three `.inc` parameter files and its memory images are bare
  names beside the sources -- and a `.sv` of nothing but defines. Every run is given a copy of them
  (`SimOptions.Files`), a written module's reading looks in them for its includes and is kept for the files its text
  names, an export puts them beside the modules, and the save keeps them. **A name matches in any case**, in an
  include, in `$readmemh`, in the reading's cache and in the rules, as on the Windows machines the course's ModelSim
  runs on. A `timescale` in one is taken out. A fault in one is marked at the include in the module's text and says
  which file and line it is in.
- **A file that declares something** -- constants, types, or an include of a file that does -- and was included
  outside a module is included inside each module that used it, once, which means the same to that module; a module
  whose header uses what it declares cannot have it there, and the import says so. It loses its include guard, which
  would empty it in every file but the first once the modules are files of their own read together -- unless another of
  the project's files includes it too, when the guard is what keeps it from being declared twice. The import says when
  it takes one out.
- **Saved** as `bench` and `files` on the project, keys an older file reads as absent.
- **Reviewed** by three Sonnet reviewers, cutting, files and the testbench; all twenty findings fixed, each with a
  test (`ProjectImportEdgeTests`), after the course folder was counted to see which bite: no file there uses a
  conditional, several modules in a file, or an include through a folder; the testbenches that run out, look inside
  the design or make two modules are there.
- **Checked.** `ImportOracleTests`: four projects -- an ALU with an include and a timescale, a mux of gates, a counter
  of three modules, and the processor with its include and memory images -- imported and run in BitSorter as Icarus
  runs the original files, every signal of the same name and every line printed; and each exported again, which
  Icarus runs as it ran the originals. `ProjectImportTests` (20): the testbench and the design, drawn and written, a
  drawn module over written blocks, a course include read inside its module and read again when it changes, a file of
  several modules cut, the unit, files that do not read, the cells, a testbench that never stops, the save, the files'
  rules, a memory image run, the testbench renamed and taken out. `ProjectImportEdgeTests` (19): the review's cases.
  `ImportedProjectTests`: an imported project in the workbench, run to its stop with its memory image, its written
  design's code at the top, its inputs not the stimulus's to set.

**As built in Part 13c**: CONVERT, in the pure layers; 13d gives it its place in the workbench.

- **CONVERT records what the testbench drove** (`BenchConverter`, `BoardEditor.ConvertBench`, one undo step): the project
  is run as written to its end, and every input of the design is read back from the run and becomes a track of the
  stimulus -- or a clock, when it toggles between 0 and 1 at one steady half period from time 0 to within half a period
  of the end, at least four times (a reset pulse stays steps). Because it reads the run rather than the text, it takes
  any testbench -- loops, tasks, `$random`, waits -- and the design sees the same inputs at the same moments.
- **It reads the testbench's own signal wired to each input, in the testbench's own rounds**: inside the design a port
  lags them by a round or two, and shows even a declaration's value as a change at time 0. What the signal held before
  anything ran is a declaration's, with no event, and the track keeps it as its `Start`, written into the testbench a
  stimulus makes as a declaration; a clock set by `initial clk = 1` instead is set at time 0 (`ClockTrack.SetAtZero`), an
  event, x to 1, as the original's was. A change made after the first round of its moment -- the nonblocking `d <= ~d`
  of a testbench answering a clock edge -- is a late step (`Step.Late`), made with `<=` so the edge sees the value from
  before; at time 0 the first round is the one the processes started in (`Simulator.ProcessesStartedIn`), after what has
  no delay settled. Every change is kept, not only each moment's last, so a pulse no time wide (`g = 1; #0 g = 0;`) is
  made again. An input wired to something other than a plain name -- a constant, an expression -- is read from the
  design's port as it settled. A testbench that gives the design parameters is refused with why, until a stimulus can.
- **The testbench module is taken out**, `Project.Bench` goes, and the inputs are the stimulus's to set again; the
  modules only it used stay, as any module may. Undo puts the testbench back. Saved as `start` on a track, `late` on a
  step and `setAtZero` on a clock, keys an older file reads as absent.
- **What a stimulus cannot keep is said** (`Conversion.Lost`): where the testbench prints, checks or writes files --
  `$display, in 3 places` -- counted where it is written rather than how often it ran, for the confirmation to show.
- **A session converts and runs again** (`WorkbenchSession.ConvertBench`): the design's signals move from under the
  testbench's instance to under the one the stimulus makes, so the drawing is made again with the run.
- **Reviewed** by a Sonnet reviewer, which found four moments the first version replayed wrongly -- a declaration's value
  made an event, a clock's time-0 event lost, a change after an edge made before it, a pulse no time wide dropped --
  and parameters lost; each is a test now, with the reviewer's own testbench.
- **Checked.** `BenchConversionTests` (15): for the four import projects, the processor included, every signal of the
  design run under the stimulus is the design run under the testbench, change for change; the four moments above and
  the refusal; the counter's clock found as a clock and its reset as steps; a testbench with a task, a loop and
  `$random`; what the ALU's testbench printed listed; the save; one undo step. `ImportOracleTests`: the same eight
  converted, exported and run in Icarus as the original files run there. `ImportedProjectTests`: converted in a
  session, the same values, and SET taken.

**As built in Part 13d**: IMPORT and CONVERT in the workbench.

- **The FILE menu brings a project in** (`FileImport`). On a desktop and in the editor, IMPORT reads every file in the
  Import folder beside the save -- the folders in it too, each file by its path inside it -- and OPEN THE IMPORT FOLDER
  makes it and opens it in the system's file browser. In a browser, IMPORT A PROJECT'S FILES and IMPORT A PROJECT'S
  FOLDER open the page's own file picker (`BitSorterFiles.jslib`), which reads what is picked as text and keeps it
  until the game takes it a frame or so later -- the pull the code editor's bridge uses, so nothing in the page calls
  into the game. Tests read a scratch folder (`FileImport.Redirected`, set by `SaveGuard`), never the player's.
- **An import went into MY CIRCUIT** (Ahmad's choice), asking first when MY CIRCUIT had a circuit in it. *Since Part
  15a an import opens as a circuit of its own, named after its project, and asks nothing* (see "As built in Part 15a").
  What the import said is in the
  TRANSCRIPT, each with its file and line, before the document's own lines (`TranscriptView.Notes`), while the project
  shown is the one it was said about -- the first edit or another document takes it away, so a refused import's reasons
  do not stay on a circuit they are not about. An import of which nothing at all could be made is refused, with its
  reasons there. Files in a hidden folder -- a project's `.git` -- are left out, in a folder read and a browser's pick
  alike, and a picker closed with nothing picked leaves nothing behind in the page. Until Part 15a, MY CIRCUIT counted
  as having something -- so was asked about -- when its top was written, or it had modules besides the top, as well as
  when it had parts: an imported design with a written top would otherwise have been replaced without asking.
- **CONVERT is in the FILE menu while a testbench drives the inputs**, and asks first, saying what the stimulus will not
  keep and that Ctrl+Z puts it back; refused with why when it cannot be. SET's refusal names it.
- **Checked.** `TranscriptTests`: what an import said, listed first until set again. Play Mode (`WorkbenchCanvasPlayTests`):
  a project put in the Import folder imported into MY CIRCUIT, run by its testbench to where it stops, a file that does
  not read named in the TRANSCRIPT with its line and what the testbench printed there too; the question before an
  import over a circuit, CANCEL keeping it and the answer replacing it (taken out with the question in Part 15a);
  CONVERT asking what it loses, the inputs then the stimulus's, and CONVERT no longer offered. Reference shot 46, new: a
  project imported into MY CIRCUIT -- since Part 15a, as a circuit of its own, its name on the chip -- running under its
  testbench, with what it printed in the TRANSCRIPT; 45 differs in the FILE menu's two new items.

**As built in Part 13e**: course templates, imported and handed back filled.

- **A template is a file that says where the code goes** (`CourseTemplate`): a `// Put your code here` line, and the
  `// End of your code` that closes it -- or, with none, everything up to `endmodule`, as three of the course's 35 have
  it, which also open in lower case. Matched without regard to case or spacing, and only as a comment on a line of its
  own, as every one of the course's is, so a banner that quotes the markers is not taken for them. The region is the
  lines between them. Filled, the student's lines end as the file's do: all 35 end theirs in CR LF.
- **An imported template's module is written, and remembers where it came from** (`Module.Template`): the file's bare
  name, every character of it, and the module's own text as the import first made it of the file (`Given`). The plan had
  the module made drawn or written with an empty body; it is always written, with the markers in it, because what a
  student hands in is the code they wrote between them -- a drawn body would be printed as the library's cells, which is
  not that -- and the markers in the code are where they write it. Saved as `templateFile`, `template` and
  `templateModule` on the module, keys an older file reads as absent.
- **EXPORT FOR SUBMISSION** (`SubmissionExport`, the FILE menu, offered while a module came from a template) zips each
  such file as the course gave it, under its own name, with what is between the markers in the module now put between
  the file's. Refused, saying which module and why, rather than hand in something that silently leaves the student's
  work out: a fault in the code (with its line); ports that are no longer the template's (each list spelled out); the
  markers gone; or any change outside them, its comments included -- what is handed in there is the course's, so a note
  written there would be lost without a word. Spacing and line endings are not a change. The check outside is against
  `Given`, never the file: the import moves an include of declarations into the module and drops a `timescale`, and the
  three course templates that include a file of parameters would otherwise have been refused untouched. (None of the
  three uses its parameter in its header, which 13b's import refuses.) Those three have no end marker, so the moved
  include lands among the student's lines; it is taken out of them, since the file has it already where the course did.
- **What else it refuses, and what it says.** One file is one module to fill: a file of two marked modules, or two files
  of one name from different homework folders -- 17 of the course's file names repeat across its folders -- is refused
  rather than one of them lost. A module renamed is told the name it is graded by. A template's file name is held to a
  file's, so a hand-edited save cannot reach out of the zip's folder. And what the files filled use from the project
  without handing it in -- the course's own modules, or one the student made here, which the project cannot tell apart --
  is named on the chip, so a helper written outside the markers is not left behind unknown.
- **The code on screen is what is handed in.** The code editor keeps typed text as its own until it applies, and text
  with a fault for good; EXPORT FOR SUBMISSION applies typed text first, as the editor would after its pause, and refuses
  text that cannot apply with what is wrong with it, rather than hand in the last text that did. (EXPORT PROJECT had the
  same gap, from 13a, and was fixed apart, as a bug, with its own test; the check is one method both share, see "As
  built in Part 13a".)
- **Checked.** `CourseTemplateTests`: the markers found and the file filled between them, everything around them kept,
  a lowercase opener with no end filled up to `endmodule`, code already indented kept as written, the code read back from
  a file that keeps its markers, a banner quoting the markers not taken for them, and a file with none no template.
  `SubmissionTests`: a template imported is remembered and kept written with its markers; the submission is the template
  with the code between its markers, the lowercase kind filled up to `endmodule`, a CR LF template's lines all CR LF, a
  template with an include handed in with the include where the course had it, and once when the import moved it among
  the code; refusals for changed ports, a rename, missing markers, code or a comment changed outside, two modules of one
  file and two files of one name, a file name that is no file's, and nothing to hand in; spacing not a change; a module
  the code uses and the zip does not hold named; the template saved and read back. Oracle (`ImportOracleTests`): a
  combinational and a clocked template, filled and handed in, run by Icarus with their testbench exactly as the project
  ran here, signal for signal and line for line. Play Mode (`WorkbenchCanvasPlayTests`): EXPORT FOR SUBMISSION not
  offered before an import, offered after; code typed with a fault refused and nothing saved; code typed and not applied
  applied and handed in; and every item the FILE menu shows fitting it, measured -- which CONVERT's caption, from 13d,
  did not, at 436 px in an item of 416, until it lost its article. No reference shot moves: the new item is hidden on
  every shot's document, and so is CONVERT.

**As built in Part 13f**: an import's limits, the security review, the measurement that decides on the levelized path,
and one browser build for Part 13's checks -- and Part 12f's.

- **In a browser** (the WebGL build of this part, served locally and driven in the desktop app's browser pane,
  2026-10-27; the page's file picker handed its files by a script, since nothing there can click the system's dialog,
  and its downloads read back the same way). A template and its testbench imported through the picker into MY CIRCUIT,
  a 4.5 MB file among them left unread and named, a handout no module names left out; the template's code typed in
  CodeMirror (12f), which kept the indent, applied it, and hid behind the FILE menu; EXPORT FOR SUBMISSION saved the
  template as given with that code between its markers; EXPORT PROJECT saved the module, its testbench and a run.do;
  CONVERT asked what it would lose and made the inputs steps; and a design whose function calls itself with no way out
  ran under its testbench, the run saying once that the call was stopped a hundred deep -- the browser's own stack, which
  is the one that matters, held. The FILE menu offered each item only where it should, every caption whole.

- **The measurement: the levelized path is not needed.** Every one of the course's nine project folders, imported as a
  student would from the course folder itself -- locally, never into this repository -- and run to where its testbench
  stops with everything recorded, as the workbench runs it (.NET 10, best of five, 2026-10-27): the slowest elaborates in
  46 ms and simulates in 17 ms, 10,764 events; the rest take under 2 ms. Part 6's processor, the budget's yardstick,
  simulates in 188 to 206 ms here and 77 to 194 in a browser. The trigger -- a design measured over the budget, its time
  in delta cycles -- is nowhere near met, so the levelized path waits, as the plan says it should. (One folder does not
  import as it stands, and one has no testbench to run.)

- **What an import takes** (`ProjectImporter.MaxFiles`, `MaxFileSize`, `MaxTotalSize`): 200 files, 4 MB a file and
  16 MB in all, where the course's largest project is eleven files of under 80 KB. The wrong folder is the usual way in,
  so too many files, or too much in all, is refused asking whether this is one project's folder, and a file too large is
  left out with its size and the rest imported -- and is no part of the whole, so a lecture recording beside the code does
  not refuse the code. A size is rounded up, so one past a limit never reads as the limit itself. **The readers look
  before they read** and ask the importer's own rule
  (`ProjectImporter.Reads`): the desktop's Import folder and the browser's picker -- handed the limits by the game -- read
  nothing over them, leave a hidden folder's files out before counting, and pass a file they leave unread as
  `SourceFile.Unread`, its path and size, for the import to name. A file the desktop cannot open is left out the same
  way rather than thrown.
- **The security review** took an import as untrusted text and tried each layer's limits against it. Three were missing,
  each found, held by a red test and fixed:
  - **Macros and includes that make too much.** Each macro of a chain using the one before twice makes two to the power
    of its length, and a chain of 31 is inside the depth limit of 32: two thousand million tokens from a few lines; files
    that each include the next twice do the same. The preprocessor now pays for what it makes before making it -- each
    use, each token, each argument put in, each character a `` `"...`" `` spells, each token of a file included -- from one
    `ExpansionBudget` for the whole import, and stops at a million (`MaxExpansion`, SV0109), said once, at the use or the
    include. Its `` `` `` joins are made in one pass.
  - **A call with no way out.** A call from an expression runs on the machine's own stack, so a recursion with no base
    case -- a common slip, and one a student can type into the workbench -- overflowed it, which nothing can catch: the
    editor, a desktop build or a browser's page ended there. The calls under way now hold at most `FunctionRunner.MaxFrames`
    (600) frames between them, each call costing its body's deepest expression (`Expr.Height`) and its own, and at most a
    hundred calls, across every function; the call past either gives x. `MaxSteps` counts everything under one call from
    an expression, and why a call was stopped is said by the run that stopped it, each reason once -- the postponed
    region's `$strobe` included -- and never by the next run.
  - **A design bigger than a run can hold.** A signal and a memory were bounded alone but not the whole, so a million
    words of a million bits elaborated and the run asked for terabytes. A design now holds at most 64 million bits
    (`Elaborator.MaxBits`) and 262,144 signals, every memory word one of them (`MaxSignals`, since a word costs hundreds
    of bytes whatever its width), refused as too large before anything is allocated. A wide concatenation or replication
    is built in one pass, where it had been quadratic: `{1048576{1'b1}}`, one line, took about an hour.
  And one found by the measurement above, held and fixed the same way:
  - **A folder's other files.** A course's project folder holds more than its code -- across the course, two handout
    PDFs, a zip, four ModelSim waveform logs and five assembly files beside the sources -- and a folder imported whole kept
    each as one of the project's files, read as text: binary in the save and in every export. A file is the project's
    now only when a module names it, in quotes and in any case -- by an include, or as a file it reads -- or a file so named
    does; all ten of the course's memory images and include files are named so. The rest is left out, said so. Each text
    is read for the names in its quotes once, however the files name each other.
  **A second look**, a review of this part's own guards, found each of them could still be got round, and every finding is
  fixed above with a test: a call cap counted in calls rather than stack, a stopped call said by the wrong run, memories
  costing a signal a word, argument copies and stringified text not paid for, include fan-out unbounded, the budget per
  file rather than per import, two quadratic paths in merged code (`` `` `` joins, and wide concatenation), a quadratic
  search for named files, and an oversized file refusing a whole folder.
  What held already: the parser's nesting (200), includes nesting and cycles, the elaborator's depth, instances and
  generate loops, the engine's delta cycles, a process that never waits, what a run keeps (`MaxRecorded`, which counts
  lines printed too), the importer's own run (`MaxRun`, `MaxEvents`), names a file can have, and `$fopen`, which writes
  only through the host and so nowhere in an import's run.
- **Checked.** `ImportLimitsTests`; `FileImportTests` in Edit Mode, on a scratch folder; the red tests and their fixes
  in `PreprocessorTests`, `SubroutineTests` and `ElaboratorTests`. `ImportHostileTests` takes each attack the review tried
  through a whole import and a run, as the workbench runs it -- each back in under a third of a second, saying why.
  `ImportFuzzTests` makes the corpus's four importable projects wrong twelve ways each from fixed seeds -- cut off, characters
  changed, lines lost or said twice -- and each imports or is refused, compiles if it imported, and never throws or hangs.

**As built in Part 14a**: the timing analysis core, in the pure layers; 14b gives it the TIMING tab. The plan for Part 14,
approved 2026-10-27, is six pull requests -- 14a the core, 14b the tab and the critical path on the canvas, 14c step tests
and the measured worst, 14d gate-library mode, 14e lint, 14f tri-state buses -- with Ahmad's choices: path delays both
computed and measured, registers given a clock to Q and a setup, a library mode with a course preset and a custom one.

- **The timing graph** (`Sim/Timing/TimingGraph`) is built from the flattened `Design`, so a drawn module and a written
  one are reckoned alike: **every bit of every signal a node** (`NameOf` gives `adder.c[1]`), every way a change travels
  an arc -- a gate at its rise (`Tpdlh`, the output going to 1) and fall (`Tpdhl`), an assignment at its delay, a
  combinational process at none -- each with how it passes a change: the same way, the other way, or either.
- **Bits, because a bus is fed bit by bit.** The first version was a node a signal, as the course draws a bus, and the
  review found what that costs: a ripple adder's carries are one vector each copy feeds the next from, so the vector fed
  itself -- a loop -- and every gate in it was dropped from every path. The drawn eight-bit ripple adder's worst path was
  4; it is 26, the carry through every copy. Which bit of a value depends on which, and which way round, is each
  expression's own (`Expr.Depends`, `BitDeps`): a read, a select, a part-select, a concatenation, a widening and a shift
  by a constant only move bits; `~` inverts each; AND and OR pass a change the same way round bit by bit, XOR either way;
  a sum carries from the bottom bit up; a condition reaches every bit either way; anything else, every bit to every bit.
  So a gate passes its own sense through what its input reads -- NOT of `~a` is the same way round -- and **an
  assignment that only moves bits at no delay is a connection**, gone through but not listed: a port joined to `c[1]` is
  one. A delayed assignment keeps its sense, rising at its rise.
- **A junction** stands between many bits and many where every one depends on every one -- a combinational process, a
  value too wide to follow bit by bit (over 4,096 bits) -- so they cost an arc each rather than one for every pair; a
  process copying a 256-word memory of four-bit words was 66,820 arcs and is 2,088. It is a node of no signal, named for
  its driver.
  A combinational process reaches **each signal it writes from what that signal's own assignments read**, and what its
  decisions -- its `if`s, `case`s and loop conditions -- read, from outside: what it writes and then reads is followed
  through to what that was worked out from (`t = a & b; y = t | c` gives y a, b and c), what it reads of its own before
  writing it is its own working (`y = y ^ a[i]`), and a loop's variable no part of anything. The first version joined
  every read to every write, and the second review found what that costs: the course's processor sets its ALU's
  operands in one `always_comb` and reads the ALU's result there for another output, so its operands depended on the
  result -- a loop through the ALU, 194 bits of it. Writes that read the same things share a junction.
- An edge-triggered process is a register: it captures the bits its statements read, its clock and asynchronous reset
  left out, and launches the bits it writes; two writing one variable launch it at the later clock to Q. A tri-state's
  control is taken at the latest of its rise, fall and turn-off, since the change it makes may be to z (tri-state buses
  are 14f's). A register's delay that calls a function is the run's to know, and taken as 0. A loop of logic -- a latch
  of two gates -- is found (strongly connected components, walked without recursion) and its arcs left out of every
  path. Gates are counted by primitive and inputs. A design of more than a million bits (`TimingGraph.MaxBits`) is
  refused, in words, rather than reckoned.
- **Path delays** (`PathDelays`): every path from an input bit to an output bit, walked without recursion and capped at
  10,000, each one's delay from its start rising and from it falling -- each gate adding its rise where its output rises
  and its fall where it falls, a change flipping at an inverting gate, both ways followed and the later kept where a gate
  can pass it either way. **The walk only goes where an output can be reached** (worked out first, back from the
  outputs): the review timed the first version going down every way through 26 stages that split and met again before a
  register -- 5 seconds, four times longer for each two stages more -- and finding none, since only an output counts and
  the cap never came. Identical paths -- the same gates at the same delays in the same order -- are one, counted, with
  every pair of ends; the worst comes first.
- **Static timing** (`StaticTiming`): the latest each bit can rise and fall, worked out once in topological order from
  every register's launch at its clock to Q, and once from every input at 0. The slowest register-to-register path sets
  the **minimum period** -- clock to Q, the logic, and the capturing register's setup -- and beside it the slowest input
  to register, register to output, and input to output, each walked back along the arcs that set it.
- **A register's clock to Q and setup** (`RegisterPart.Tcq`, `Tsetup`; saved as `tcq` and `tsetup`, read as 0 from an
  older file; neither may be less than 0). The clock to Q is a delay on the register's write, `q <= #Tcq d`, so the run
  shows it and Icarus agrees; the import reads it back as the register's. The setup is checked, never simulated -- the
  course's simulations do not -- so the compiler hands it over beside the code (`CompiledModule.Registers`) and
  `TimingAnalysis` gives it to the graph by full name in every instance of its module. A written module's register takes
  its clock to Q from its own `#` and has no setup. A setup does not survive an export and an import, since the code
  cannot carry it.
- **`TimingAnalysis.Of(project)`** (Board) compiles the project, elaborates its design alone as the top -- never the
  testbench round it -- and returns the graph, the paths and the static timing, or a problem in the player's words.
- **Checked.** `TimingTests`: by hand, an inverter chain (8 rising, 7 falling), a mux of gates (four paths, three of them
  identical, the select through the NOT the worst at 9), XOR passing a change either way, a NAND latch's loop found and
  never followed, a path through two instances listing only their gates, a pipeline's minimum period (clock to Q 1, logic
  3, setup 2: 6), a counter feeding itself, and the gate count; and from the review, a NOT chain along the bits of one
  vector (no loop, 8 and 7, and 8 between registers), a ripple adder of four full adders on a carry vector (19, the
  carry), 2^40 ways to no output never walked and 2^20 to one stopped at the cap, a bit-select and a delayed assignment
  keeping their sense, a parity process and a memory copy, a function in a register's delay, a tri-state's turn-off, and
  two processes on one register. `TimingAnalysisTests`: the Johnson counter with every register at 2 and 3 clocked no
  faster than 6, an ideal register costing nothing as before, the clock to Q in the run (every change 2 later), the save,
  a negative value refused, a drawn full adder's two reckonings agreeing on its slowest path, and the drawn eight-bit
  ripple adder's 26. Oracle: the timed Johnson counter, a new sample board, printed and run under Icarus as here.

**As built in Part 14b**: the TIMING tab, and the path picked lit on the canvas.

- **`TimingView`** (view-model) reads `TimingAnalysis` into lines: for a design with registers the **minimum period**
  as its sum (`clock to Q 2 + logic 3 + setup 1 ns`), the **maximum frequency** it gives in the unit that keeps it over
  1 (`166.7 MHz`), and the critical path -- the slowest register to register -- with the slowest input to register and
  register to output beside it; for one without, the critical path is the slowest input to output. Then the **gate
  count** by kind and inputs, the most first (`5 gates: 2 AND2, 2 XOR2, 1 OR2`, a block counting its contents, and a
  drawn gate four bits wide four -- it prints as one bitwise assignment, which the graph sees no gate in), any loop of
  logic named, and **every path**, the slowest first, each with its ends -- identical ones together, `a → cout, b →
  cout` -- the gates it passes through, and its delay rising and falling. A path is named by the bits it starts and
  ends at, a register's d included: the counter's critical path is `count[0] → next[2]`. The minimum period says
  when nothing on its paths has a delay yet, as the course's processor, written as RTL, has none -- its period is
  honestly 0 -- and when a loop of logic was left out of it. A frequency is reckoned in decimal, where a power of ten
  is exact (in binary 1 ns read "1000 MHz"). The list stops at 200, saying how many are left. A circuit with errors
  has one line saying so. `WorkbenchSession.Timing` works it out again only once the **design** has changed
  (`TimingView.SameDesign`): a part moved or a stimulus edited is the same timing for nothing, where the review timed
  a processor's at 200 ms a time. The project's files are part of the design -- an include is code, a memory image what
  a run reads -- compared by value once their references differ; `SameDesign` left them out until the 15g-6 review
  found STATES keeping its machine past a changed file.
- **Every path is named from the design's top** (`h1.s`, `c[1]`), so one picked in the top's drawing is the same path
  inside a block. **`TimingLight`** finds what a drawing has of it: the signals it has a net for are the path through
  that drawing -- a bit of a bus is the bus, a cell's output the net it drives -- and between two of them the part that
  reads the one and drives the other is the step taken, and the branch into that part the wire; a path starting at a
  register lights the register, one ending at a port or a register lights the branch into it. **A signal inside a block
  of the drawing is the block** (`u.q` is u's), so a path that goes into a block, ends in one or never leaves one lights
  it -- the review found a register's input path stopping short of the block it ended in, and a path wholly inside a
  block lighting nothing. An input port is lit where the path comes in and an output where it goes out, never one it
  only passes: a register's output that is also a port is where a path starts. **Every identical path of a line is
  lit**, not only the first (`TimingPath.ArcsOf`). So the full adder's critical path lights both blocks and the OR from
  the top, and h1's XOR between h1's own ports from inside h1 -- and h2's AND inside h2. A drawn net knows its driver
  and each branch's load (`NetLayout.Source`, `Loads`), a drawn port its part (`PortLayout.Part`), and a layout where
  it is in the design (`SchematicLayout.InstancePath`) for this; the editor keeps what it lit for each layout and path
  rather than working it out at every redraw.
- **The dock's fourth tab** (`WorkbenchPanel.TimingTab`), a shell list like the TRANSCRIPT: a line a row -- its label,
  what it says, its numbers on the right -- the wheel scrolling it, and a click on a path picking it. The critical path
  is picked until another is, and an edit that leaves the picked path there keeps it (`TimingView.Find`, by the path's
  signals: two lines can read alike and be paths through gates of other delays). **The path is
  lit while the tab is up**, and another tab puts it out, so the canvas is never left lit by something not on screen.
- **Lit, not the rest dimmed.** The plan said the rest of the drawing dims. The path is drawn over it instead, in the
  editing overlay and the second cursor's colour -- a band along each of its wires, wider than a selection's, and an
  outline round each part and port further out than a selection's, so a part both selected and on the path shows both.
  Dimming would draw every chunk of the wires and parts again in a second set of colours on every pick, and leave the
  values of the wires off the path harder to read while the run plays; the band reads at once without it.
- **A register's clock to Q and setup in its inspector**, in the bar and as quick fields beside its values, with a line
  saying what each is: CLOCK TO Q (Tcq) and SETUP (Tsetup), times in the project's unit, never below 0, set together as
  one step. Typed into, the TIMING tab's period follows.
- **Checked.** `TimingViewTests`: the full adder's critical path (a to cout, 4 each way), its gate count and paths; the
  counter's period of 3 becoming 6 with a clock to Q of 2 and a setup of 1, and the frequency; frequencies in four units;
  a broken circuit; the critical path lit from the top, inside h1 and inside h2; a register-to-register path lighting the
  register; the session working the timing out again only on an edit, keeping the line picked; and from the review, 1
  ns as 1 GHz, a path into a block's register and one wholly inside a block lighting the block, the counter's path
  named by its d bit, two lines that read alike found apart, a move not working the timing out again, and a wide gate
  counted a bit at a time. `InspectorTests`: the
  register's quick fields, its clock to Q and setup in one step, a negative refused. Play Mode: the tab lighting the
  full adder's critical path and another picked, inside h1, put out by WAVES, and the counter's period moving from 3 to
  5 with a clock to Q typed in its quick field. Reference shot 47: the TIMING tab over the full adder, its critical path
  lit.

**As built in Part 14c**: step tests, and the measured worst.

- **`StepTests`** (Board) makes two stimuli from the top module's input bits (a bus a bit at a time, named `a[2]`):
  **STEP TEST's**, every input at 0 and then each bit in turn rising, left to settle, falling, left to settle -- a delay
  read off the waves with the cursors -- and **the measuring run's**, each bit stepped up and back down from every value
  of the others, those walked in Gray order so one changes between one pair of steps and the next. Each step is left
  **the slowest computed path and half again** (`SettleFor`, at least 2): static timing's path is the latest any change
  can arrive, so every change a step makes falls before the next step. Exhaustive is for at most ten input bits, 5,120
  pairs of steps; STEP TEST steps up to 64 and holds the rest at 0, and an input driven as a clock keeps its clock --
  each step then left its longest period more, since what a step does reaches a register only on the next edge.
- **`StepMeasure`** (view-model) runs the measuring stimulus on a copy of the document -- its own stimulus, a written
  testbench set aside -- a slice a frame at half a run's budget, and reads the waves: after each step, the last change of
  each output bit before the next, the slowest kept for each input and output bit, rising and falling apart, with the
  inputs as they stood before the step that showed it (`a = 0, b = 1, cin = 0`). **A glitch counts**: a change that comes
  and goes is a change, as it is on the waves. Only a circuit without registers, which one with them is timed by its
  clock, and of at most ten input bits; otherwise the tab says why. **And only where the computed slowest is a bound**,
  since every step is left to settle for longer than it: no loop of logic, and no delay the run works out
  (`TimingGraph.UnknownDelays`). A change seen later than the slowest all the same, or a run that stops short of its end,
  gives no measure at all (`Problem`) rather than one that may be wrong. The run keeps no transitions in flight, and is
  let go once read.
- **Static timing reckons what a process waits** (the 14c review, a fix to 14a): a combinational process's delay is
  every `#` it waits and its latest `<= #`, on the way in, so `always @* #2 y = a & b;` takes 2 from a to y; and an
  assignment takes the latest of its rise or fall and its turn-off, as a tri-state's control already did. Both had
  under-reckoned the slowest path the settle time is worked out from.
- **`TimingView.With`** lays what was measured on the computed timing: the slowest seen beside the critical path
  (MEASURED WORST), a line for each input and output, and **every computed path longer than anything measured from its
  input to its output marked "never fires"** -- a false path. Identical paths are one line, but a false path can have the
  same gates in the same order as a true one, so a line of both is split in two, each lighting its own path
  (`TimingPath.ArcsOf` keeps each identical path's arcs for this), the MEASURED WORST right under the part of the critical
  path that fires. A run in which no step moved anything marks nothing: it says the run saw nothing, not that no path
  fires.
- **The TIMING tab** measures while it is up and an edit that changes the design starts it again -- a move or STEP TEST
  does not, so what was measured stands -- its header saying how far it has got, or why nothing was measured; a change
  of unit works the tab out again in the new one. **STEP TEST** beside it makes the document's stimulus a step test, one
  undo step, refused for a design a written testbench drives. A bit is named by its signal's range in both reckonings (`TimingGraph.BitName`), so `[0:3]` agrees too.
- **Checked.** `StepTestsTests`: the settle time; STEP TEST stepping a, b and cin in turn; the measuring run's 24 steps on
  three inputs, each standing at its word before it with only its bit moving and nothing else while it settles; a bus a
  bit at a time; eleven bits refused; a clocked design's steps left a period more. `TimingTests`: a process's `#2` and
  `<= #3` on its paths, a delay reading a signal flagged, an assignment's turn-off of 7 past its rise and fall.
  `StepMeasureTests`: the full adder seen at its computed 4 (and cin's 2), nothing
  marked; a false path -- y = s ? (s ? a : b) : c of gates, b's path of four computed at 8 and never moving y, marked,
  split from a's identical one, which fires at 8, and s's slowest a glitch at 9, as computed; a register refused; the
  document's own stimulus untouched; STEP TEST in one undo step; y = (a | b) & b, whose one critical line is split, the
  measured worst under b's part that fires and a's that never does after it; y = a & 0 marking nothing; a latch refused;
  a run that stops short giving no measure; a move and STEP TEST keeping the analysis. `TimingViewTests`: a change of unit
  said in the new one. Oracle: the full adder and the false path driven by the
  measuring stimulus (`full_adder_steps`, `false_path_steps`) run under Icarus change for change, 668 and 1,069 settled
  changes -- what is measured here is what Icarus would measure. Play Mode: the TIMING tab measuring the full adder to
  its 4, and STEP TEST making the stimulus, keeping what was measured, and undone.

**As built in Part 14d**: gate-library mode.

- **`GateLibrary`** (Board, `ProjectSettings.Library`, null for none): the cells a design may be built of -- each a kind,
  a number of inputs, a rise and a fall (`LibraryCell`) -- whether constants are refused, and a budget of gates, -1 for
  none. Saved as `hasLibrary` and `library` beside the settings (the reader cannot leave an object null, as with the
  stimulus); an older file reads it as absent, and a saved cell BitSorter has no gate for is left out and said so. (Since Part 15c a
  library may hold a tri-state gate too, of two inputs: `GateLibrary.CanHold`.)
- **The course's preset is BitSorter's own cells** (`GateLibrary.CourseCells`): INV, BUF, and AND, OR, NAND, NOR, XOR
  and XNOR of two to four inputs, twenty, each at the default delay to begin with. **The course's own table of delays is
  its handout's, which nothing here reads**, so the table is the student's to fill in: a delay typed on any gate in its
  inspector is its cell's, and every gate of the cell follows, one step (`GateLibrary.Retimed`) -- the library is then no
  preset's, CUSTOM. Ahmad's choice between a preset and a custom table (2026-10-27) became one table a preset fills;
  whether the preset should carry the course's delays is his to settle, from his handout.
- **Kept wherever a design is drawn.** `BoardRules` refuses a gate the library has no cell for, a gate off its cell's
  delays, a MUX -- no cell, and so built of the library's gates -- and a constant where constants are refused, each in
  words naming the part. `BoardEditor.SetLibrary` sets it and gives every gate of a cell its delays in the same step,
  refused in the rules' words when what is drawn breaks it, and no step at all when it is the library there already
  (`GateLibrary.Alike`, cell by cell, since record equality compares the lists of cells by reference). **A gate added
  takes its cell's delays wherever it comes from** (`BoardEditor.Add`), so a gate copied before the library changed
  pastes at the new ones. PARTS offers only what it allows (`PartsCatalog.For`), a gate placed takes the fewest inputs a
  cell of it has and that cell's delays, and the inspector says which cell a gate is. **A written module was not held to
  it** in 14d, its code checked as SystemVerilog alone; since 14g-3 it is, gates only (see "As built in Part 14g").
- **The budget is one count, on the TIMING tab**: the design's gates, a wide gate's every bit, a block's contents each
  time it stands, written code's gates included, laid against the budget and a problem when over -- never refused, since
  gates come one at a time on the way to fewer. The rules warned of it as well until the 14d review, counting drawn gates
  alone, so a written design over its budget was over it on the tab and not in the transcript. Since 14g-3 the
  transcript warns again, from the tab's own count, so the two cannot disagree.
- **The LIBRARY menu** (`LibraryMenu`, under the TIMING tab's LIBRARY button, which says what the design is held to):
  no library, or the course's cells afresh, keeping the budget and the rule on constants; and, with one, constants
  refused or allowed, the gate budget (asked in a one-line field), and each kind of gate taken out or put back
  (`GateLibrary.Toggled`). PARTS is made again when the library changes, and the TIMING tab is worked out again when the
  library does, though no gate moved.
- **Checked.** `LibraryTests`: the preset is every cell BitSorter has at the delay asked; setting it gives every gate its
  cell's delays and a delay in the table every gate of the cell, undone in a step each; a library without XOR refused
  for the full adder, in words naming XOR2 and the cells it has; a BUFIF1, a gate off its delays, a MUX and -- where none
  are taken -- a constant refused, a wide gate of a cell allowed; a library refused over a drawn MUX; a kind taken out and
  put back; a design over its budget never refused; the save, an older file and an unknown cell.
  `LibraryViewTests`: PARTS without MUX nor BUFIF1, and without CONST under no constants; a gate placed taking its cell,
  a NAND of three where the library has only NAND3; a rise typed on the OR the OR2's for every OR2, one step; the gate
  count against the budget, said at once when only the budget changes, and as one sentence with no gates; a gate copied
  before the library changed pasted at its cell's delays; the same library set again no step. Play Mode: COURSE CELLS from the menu, PARTS without MUX, a NAND placed as the library's
  NAND2, and XOR refused from the menu with the rules' words on the chip. Reference shot 48: the LIBRARY menu over the
  TIMING tab, the full adder held to the course's cells.

**As built in Part 14e**: lint, as `vlog` warns.

- **Four warnings beside the latch check (SV0401)**, in the elaborator's lint pass once the design is built
  (`Elaborator.Lint.cs`, `LintDesign`), each at the signal's declaration or the assignment's line and said once however
  many times its module is placed; never an error, so a run goes on:
  - **SV0402**, a signal something reads that nothing drives or sets -- it reads z, x for a variable, 0 for a 2-state
    one. Every read counts: values, conditions, delays, where a write lands, what is waited on and printed, and what
    the functions called read in turn, from a process or a continuous assignment (`ProcessInfo.CollectAllReads`,
    `CollectReadsOf`); a `force` sets.
  - **SV0403**, a signal set that nothing reads -- **only inside an instance of a module**: the top's signals are
    watched on the waves, and so are a testbench's, which is the top, and an interface's are there for what is outside
    it, as a port's are.
  - **SV0404**, a net with drivers on the same bits not all of which can let go: a tri-state gate can, and so can an
    assignment whose value can be z -- a z written in it, or a net it passes on whose own drivers all can, as a port
    connection or `assign bus = ya` does, followed through selects, joins and replications (`Expr.CanFloat`) -- so a
    bus driven the course's way, through modules or not, is said nothing of. Found with a sweep along the bits, so a
    generate loop's thousands of one-bit drivers cost no more than sorting them.
  - **SV0405**, a plain value -- a name, a select, a concatenation of those -- given to something narrower, its top
    bits dropped. Anything with an operator is sized by its context, a number or a named constant fits or is cut as
    written, an int is a testbench walking its values and a real is converted, so none of those is said.
- A port is left alone by the first two -- what is outside drives or reads it, and an input of an instance left
  unconnected is SV0313 where it is placed -- though its drivers inside are held to SV0404; and a process's own working
  variable, an event and a memory are left alone by all.
- **A written block is linted where it is written** (`WrittenModules`): as the top for SV0402, SV0404 and SV0405, as an
  instance for SV0403, and its warnings reach the TRANSCRIPT at its own lines. **A drawn module's** are the rules':
  `BoardRules` already said an input nothing drives, and refuses two drivers on a wire and wires of different widths;
  14e adds **a gate, a MUX or a register that drives nothing** -- a splitter's spare bit, an unused input and a block's
  spare output being the ordinary way of things.
- **Icarus is a quieter judge** (`Docs/oracle-notes.md`, "Lint"): `-Wall` says none of the four, and
  `-Wfloating-nets` says every net with no driver, the top's inputs included. The oracle's whole corpus raises none.
- **Checked.** `DesignLintTests`: each warning raised by a design written to raise it, at its line, and not by the
  cases it leaves alone -- an interface's signal, a top's, two tri-state gates or two assignments that let go, bits of
  their own, a sum, a number, a named constant, an int, a real -- and the corpus raising none; and, from the 14e
  review, a bus driven through a tri-state block placed twice, through a net between and with a z replicated said
  nothing of, a mask read in a function an assignment calls read, a forced net set, a 2-state variable reading 0, and
  thirty thousand one-bit drivers of one bus checked in a moment. `LintTests` keeps the latch check's.
  `BoardEditorTests`: a gate that drives nothing a warning. `WrittenModuleTests`: a written block's lint at its lines.
  `ProjectImportTests`: a testbench that never prints the output it watches said nothing of.

**As built in Part 14f**: tri-state buses, and `inout`.

- **A bus on the board.** A wire may have more than one driver when every one is a tri-state gate
  (`BoardRules.IsBus`); any other second driver is refused as before, the refusal saying what a bus is driven by. A
  bus is printed as a `tri` net -- `tri logic`, as a port too -- and where two drive at once it reads x, as the waves
  show. A block's output is no driver of a bus, even one a tri-state gate inside drives: what is inside a block is its
  own. **A testbench declares an output the design makes a net as a `wire`**: Icarus joins a port to what it is
  connected to, and a variable there takes one driver. *Since 14g-4 a block's inout pin may drive a bus too, and a
  wire one is on is a `tri` though it has no other driver (`BoardRules.IsTri`).*
- **`inout` is joined, not assigned.** An inout port is driven from both sides, so the elaborator joins it to the whole
  net it is connected to -- declared as that net under the port's name (`JoinInouts`, `DeclareSignal`), and found by
  that name as well (`DesignBuilder.Alias`) -- where every other port is a zero-delay assignment one way. An assignment
  each way would hold a bus at whatever it last was. Icarus joins them the same way. One left unconnected floats, a
  net of its own; one given a select, a concatenation, a variable or a net of another width is refused, saying it joins
  a whole net of its own width. **The engine is unchanged**: a joined net is one signal with drivers from both sides,
  resolved as any net is.
- **Written code may use it; a drawn board, not yet.** A written module may have an inout and be the top, its
  testbench's end a net. A block of one was refused in words, a drawn port may not be one, and an imported module with
  one comes in as written code. *Since 14g-4 a block of one has an inout pin* (see "As built in Part 14g"); a drawn
  port still may not be one, as Ahmad chose.
- **The 14f review's fixes.** A laid-out net keeps every driver (`NetLayout.Drivers`), so a timing path through a bus's
  second gate lights that gate, not the one the drawing roots the bus at, and pointing at a bus names both drivers and
  neither as a load. A written top's port is a net or a variable as its elaboration made it (`ModulePort.IsNet`), so a
  port declared the old way, `inout io; wire io;`, is a net in its testbench too. CONVERT refuses a design with an
  inout, whose testbench's driving a stimulus could not keep. An inout joined to a module's input is refused, since an
  input carries a value in only; one of the wrong width is said once, and declared a net of its own after.
- **Checked.** `BoardEditorTests`: two tri-state gates on one wire a bus, a third driver of another kind refused. The
  bus sample (two BUFIF1 on one output -- each alone, both at once, neither) runs under Icarus as here and through every
  sample test: compile, import, geometry, export and the save. `InoutTests`: the join driven from both sides -- a value
  either way, x where both drive and disagree, z where neither does -- one left floating, and a select, a variable and
  the wrong width refused. `WrittenModuleTests`: a written top with an inout compiled and elaborated, its testbench's end
  a `wire`, and a block of it refused (since 14g-4 a block of it has an inout pin, and only a block of several copies
  is refused). Oracle: the corpus's shared bus -- two devices on one `tri` through inout ports,
  one inside a wrapper, the testbench driving it too through a gate and an assignment -- agrees with Icarus on every
  signal and every line printed.

**As built in Part 14g**, the follow-ups Ahmad asked for once Part 14 was in (2026-10-27), in four PRs.

- **A module can be deleted (14g-1).** Deleting a block leaves its module in the project -- it is the player's drawing
  or code -- and nothing in the workbench could take one out: `BoardEditor.RemoveModule` had no caller, so code1 stayed
  in CODE and PARTS after its block went. A right-click on a module's row in PARTS asks first (`AskMenu`) and deletes
  it, one step Ctrl+Z puts back. `BoardEditor.WhyNotRemoveModule` refuses the top, a module a block of which is placed,
  and one another module's code makes, each saying where, and the workbench asks it before asking the question. Deleting
  a module's last block says, in the bar's line, that the module stays and how it goes (`CanvasEditor.Note`); over a
  BLOCKS row the status line names the gesture (`PointerArea.Blocks`), and the keyboard map lists it under IN PARTS. A
  block in hand of a module taken away is put down (`CanvasEditor.Relaid`), and PARTS keeps its scroll when it is laid
  again. Only the written testbench's going makes the run again: a module nothing uses takes nothing from it.
- **The code editor keeps its keys (14g-2).** Asked whether Ctrl+V was the workbench's while code is typed: it is the
  circuit's paste only while no field has the keyboard (`UiText`), which a Play Mode test now holds for every binding
  of `WorkbenchKeys`, in the code and in RUN TO, beside the paired Ctrl+V that does paste on the circuit. Holding it
  found a bug of Part 11's: after any click on the shell, an arrow was UI Toolkit's navigation and put the keyboard in
  a field, so the shell ignores navigation now (`FocusController.IgnoreEvent`; a red test, then the fix). The desktop
  build's editor, TextMesh Pro's field, answers what CodeMirror does: Ctrl+Z and Ctrl+Y through the typing
  (`CodeDocument.UndoTyping`, a burst of typing one step, an edit of lines one of its own, started afresh when the
  document is undone under it), Tab and Shift+Tab by four spaces and Ctrl+/ (`CodeEdits`, Unity-free), and Ctrl+S. The
  browser's takes `@codemirror/search` 6.7.2 (Ctrl+F, Ctrl+D, in the palette's colours) and Ctrl+S, and counts its
  whole box as having the keyboard, find's fields included -- its text alone did, and an S typed into find would have
  been the game's. The keyboard map lists the code's keys under IN THE CODE, its columns named in `KeyMap.Columns` and
  held to every heading once.
- **The library, as Ahmad chose it (14g-3).** Over its gate budget the TRANSCRIPT warns too
  (`WorkbenchSession.Diagnostics`), from the TIMING tab's own count (`TimingView.OverBudget`, worked out once a view),
  so the two can never disagree, and a click on it opens the TIMING tab. **Written code is held to the library, gates
  only**: the elaborator takes the library as `GateLevelRules` (Sim, which knows no library of its own) and checks each
  module it names as it builds it (`Elaborator.Library.cs`):
  - a primitive or a cell's module is one of the library's cells at its delays, as its parameters and generate loops
    work them out;
  - everything else only connects (`Expr.IsWiring`: a read, a fixed select, a concatenation, a replication, a
    widening -- a select by a signal is a multiplexer): a continuous assignment, with no delay of its own; a gate's
    input; a port's connection;
  - a process is a register of the drawn register's shape -- clocked by a plain signal, `q <= d`, or `if (reset) q <=
    constant; else q <= d` with a one-bit reset, its inverse, or one compared with a constant -- or an `initial` giving
    starting values; anything else, ifs nested into a function, a case, an enable that keeps the value, is refused;
  - no constant where the library takes none, by any name: a number, a parameter, a supply net, a widening's 0s, a
    constant loaded on every clock (a reset or starting value is no tie, and stays).

  Each fault is an error, SV0501, at its line, said once however many copies a generate loop makes, so the code editor
  marks it, the text does not apply, and a library set over code that breaks it is refused with that fault -- a
  module whose only faults are the library's keeps its ports, so a block of it is not refused instead for a wire on a
  pin it lost. `WrittenModules` reads each written module held to it but the testbench -- the one the project runs,
  `Project.Bench` -- the library part of what a reading is kept under. **A cell's delays default to the library's**
  (`CellLibrary.Build`), for the run and the printed cells alike, so `NAND2 g1 (...)` takes them as a course's own
  library file gives them, and runs so under Icarus too (the `written_cells` sample). A cell retimed is refused while
  code pins it at other delays, and followed by code that leaves them out. **Two limits, written down by the 14g review**:
  each module is held at its own parameters' defaults, so a parent overriding a child's delay parameter is not held
  there; and a project exported under a library and imported again keeps its cells' delays but not the library, which
  is a setting of the project rather than of its files.
- **Inout pins on blocks of written devices (14g-4).** Ahmad chose them for written devices only: a drawn port is still
  an input or an output. A block of a written module with an inout has a pin of both ways (`PinDirection.InOut`),
  drawn on the box's right under its outputs. The netlist counts it a driver and lists it apart (`Net.InOuts`); a bus
  is several drivers that can each let go -- tri-state gates' outputs and inout pins -- and anything else beside an
  inout pin is refused as before, the message naming both kinds. A wire an inout pin is on is printed a `tri` net,
  ports included (`BoardRules.IsTri`), since the elaborator joins an inout only to a whole net. Refused, saying why: an
  input port of the module on a wire with an inout pin, which the elaborator would refuse too -- an input carries a
  value in only, so the bus is driven from it through a BUFIF1 -- and a block of several copies of such a module, each
  of which would take a slice of the wire. The `devices_bus` sample -- two written devices and a drawn BUFIF1 on one
  output -- agrees with Icarus change for change, and goes through every sample test but printing, importing and
  printing again, which is for drawn boards, as for the other written samples. **The 14g-4 review's fixes**: an inout pin
  reads its wire as well as driving it, so a gate whose output only an inout pin is on is not said to drive nothing; a
  device on a bus flashes for its own output, and for its inout's wire only when it has none, since a bus flips for
  every driver on it; and `InoutPinTests` holds a wire with one inout pin a `tri` where it is no bus -- inside, read by
  a gate, and as an output four bits wide, its testbench's end a `wire`.
- **The 14g browser smoke test** (the served WebGL build, 2026-10-27) found two faults, each a red test and then the fix:
  CodeMirror's undo took the block's code itself out after the first thing typed, the putting in of it a step of that
  undo -- a block's code is now where the editor's undo starts (`CodeMirrorBridge.Load`, `blockState`, held under Node);
  and the name field + CODE and MAKE BLOCK open under the pointer lost the keyboard to their own click, read a frame
  late, and came back without its free name chosen -- it takes the keyboard the frame after now, and the shell gives a
  field back with what was chosen in it. The review of 14g-4 found a third, older one: an exported written module came
  back from an import with the export's header in its code, one more each round (`BoardPrinter.WithoutHeader`). Two
  things it showed are seen to as well: find's checkboxes were the browser's white boxes, and are the editor's theme's
  now (`Tools/codemirror/src/theme.js`, held under Node like the undo); and the chip cut a long refusal short, and
  takes the lines it needs now. The review of these found REVERT joined to typing within half a second of it, one
  Ctrl+Z taking back both -- it is a step of its own (`isolateHistory`) -- and a written block stepped into from another
  opening at that one's scroll; it opens at its top.

**As built in Part 15a: the student's circuits.** Part 15 makes BitSorter open like a simulator rather than a game
(Ahmad, 2026-10-27): on what was open last, or a start page, with the Exercises on the workbench. Its first PR gives the
workbench more than one circuit.

- **A circuit is a document the save keeps under its key** (`Documents`, a view-model rule, tested under dotnet):
  - MY CIRCUIT under `workbench`, always there, never renamed or deleted;
  - a circuit made with NEW, or imported, under `doc/` and its name;
  - each 4.x free-play board, which the save's migration keeps under `free-play/` and its name (Part 7b) -- once it runs on
    the player's save, which it does not yet (Part 15d).
  - A level's migrated board is no circuit: it is that exercise's (Part 15d).
- **Names** are 1 to 32 letters, digits, spaces, dashes, underscores and dots, unique by any case, MY CIRCUIT's
  included. A new circuit is offered circuit1, circuit2..., and its top module is its name as SystemVerilog takes one
  (`Migrator.NameFrom`).
- **FILE** gains NEW CIRCUIT, OPEN A CIRCUIT, and RENAME and DELETE THIS CIRCUIT, the last two only for a circuit of
  the student's own.
  - OPEN lists every circuit the latest opened first (`DocumentMenu`), from the save's new `recent`, which keeps
    sixteen and is absent, so empty, in every older file. It lists twelve at a time, OLDER and NEWER stepping through the
    rest: it stopped at twenty with nothing past them, and with an import a circuit of its own a term's homework gets
    there. OPEN on the circuit already open does nothing, rather than building it again.
  - Each exports -- PNG, VCD, the project, a submission -- under its own name; every one was exported as MY CIRCUIT.
  - RESET PROGRESS (Settings) takes the circuits with the save: the workbench lets the one it had open go without
    writing it back, and opens MY CIRCUIT, blank, the next time it opens. (Part 15g: it starts the exercises over and
    keeps the circuits; see "As built in Part 15g-1" and "As built in Part 15g-2".)
  - DELETE asks first and cannot be undone, and MY CIRCUIT opens in its place.
  - The SAMPLE chip's first place is the circuit open, its name on the chip.
  - The workbench keeps the circuit open under its own key (`KeepMyCircuit`).
  - Opening another keeps the one open first, and never writes it under the next one's key.
- **An import opens as a circuit of its own**, named after its project and numbered when the name is taken (`mux2`,
  `mux2 2`). So it never replaces anything, and the question it asked before replacing MY CIRCUIT is gone.
- **Checked.** dotnet: `DocumentsTests` (which keys are circuits, names, free names, the order). Edit Mode: the recent
  list, a rename and a delete through a restart. Play Mode: NEW, RENAME refused by a name taken, OPEN's list with an
  old free-play board in it, an export under its name, DELETE asking and opening MY CIRCUIT; OPEN paging through sixteen;
  RESET PROGRESS leaving MY CIRCUIT blank and nothing written back; an import as a circuit, twice, MY CIRCUIT as it was.

**As built in Part 15a, second half: where BitSorter opens.** As a simulator, Ahmad's choice (2026-10-27): on the last
thing that was open, and with nothing, on a start page like a good IDE's.

- **The last thing open is in the save**, as `lastOpen`: a circuit's key, a sample's (`sample/` and its name), or empty
  for the start page. Absent, so the start page, in every older file; a rename follows it.
- **At launch** (`WorkbenchPanel.Launch`) the workbench opens on that, if it is still there (`Documents.LaunchKey`, a
  view-model rule): a circuit the save still keeps, MY CIRCUIT always, or a sample still of that name. Otherwise it opens
  on MY CIRCUIT, with the start page over it.
- **The start page** (`WorkbenchPanel.Home.cs`) is the shell's, covering everything under the header:
  - START: NEW CIRCUIT, OPEN A CIRCUIT and IMPORT A PROJECT;
  - HELP: the keyboard map and Settings;
  - RECENT: eight circuits, the latest opened first;
  - SAMPLES: the five samples.
  HOME brings it back. While it is up the run under it is paused, and the workbench reads no keys and no pointer of its
  own, so nothing hidden is played, scrolled or deleted. Escape takes it down onto the circuit under it. Exercises join
  it in Part 15d, and the guided tutorial in 15f.
- **The header is the app's**: BITSORTER, then HOME, FILE, HELP and SETTINGS, the sample's line, and SAMPLE. There is no
  BACK, and no PREVIEW chip. HELP (`HelpMenu`) has the keyboard map, the start page, and ABOUT, which is the credits roll;
  the roll comes back to the workbench.
- **Escape never leaves the workbench.** It closes a popup, then the start page or a part in hand, and nothing more.
- **Settings opens over the simulator** (`SettingsPanel.OpenOver`) and BACK comes back to it as it was left. Its
  backdrop is solid there, where it is see-through over the 4.x board: the board under the workbench is the 4.x
  game's. A change of look reloads the game and puts Settings back over the same thing (`OpenAgain`). (Part 15g: Settings
  is a page of the workbench, in the style's colours, and has no backdrop of its own; see "As built in Part 15g-2".)
- **The 4.x game is one quiet row down**, until Part 16 deletes it: THE 4.x GAME beside CREDITS in Settings opens its
  title screen, whose WORKBENCH row became SIMULATOR -- and in Part 15g its first and largest button, BACK TO THE
  SIMULATOR, with SIMULATOR beside MENU on a 4.x board. Nothing of 4.x is on a first screen (Ahmad: "not deep but not
  plainly").
  - The tests written against the 4.x boot keep it: `AppLaunch.Classic`, set only by `TestScene.Load`.
    `TestScene.LoadSimulator` boots as the game does.
  - The level the simulator loads under the workbench has not been seen. Analytics reports it started only when the 4.x
    game first comes on screen (`AppLaunch.GameShown`), and the 4.x tutorial offers itself only once the title screen
    is left.
- **The music is off until switched on** (Ahmad's choice). A player who had switched it on keeps it on.
- **Checked.**
  - dotnet: `DocumentsTests`, what it opens on, and a circuit or a sample since gone opening the start page.
  - Edit Mode: `lastOpen` through a rename and a restart; the music's default.
  - Play Mode (`SimulatorLaunchPlayTests`):
    - a fresh save on the start page, the music off, and no 4.x level or tutorial started;
    - a circuit and a sample open last opening again;
    - RECENT, HOME, SAMPLES and Escape;
    - HELP's keyboard map, and the credits coming back;
    - Settings over the simulator and back;
    - THE 4.x GAME, and SIMULATOR back.
  - The tests of BACK became tests of SETTINGS and back, and of Escape never leaving.
  - Reference shots: every workbench shot moves with the header; shot 1 (SIMULATOR), 17 (THE 4.x GAME), 39 (the keyboard
    map's Escape line) and 45 (FILE under its new place) differ; 50, the start page, is new.

**As built in Part 15b: the Exercise model, grading and the self-checking testbench.** Pure Board code, tested under
dotnet, that the next parts build on: 15c writes the exercises, 15d opens them on the workbench.

- **An exercise** (`Exercise`, from `ExerciseRecord`) is read from a file. The fields are plain, in the shape Unity's
  `JsonUtility` reads, and dotnet's `System.Text.Json` reads them too (`ExerciseRecordTests` holds the two to one shape).
  A file holds:
  - `id`, `title`, `order`, `chapter`, `goal` and `hint`;
  - the `inputs` and `outputs`: a name, a width, and a value at each vector -- a character a vector for a bit, or binary
    words with spaces between. An x in an output leaves that bit free (in a value with a z, x means x). Every input
    has as many values; every output as many again, or more when a register hands one on after the inputs run out;
    and the outputs must ask something, or an empty board would pass;
  - the `parts` it stocks: a gate's `kind`, `Register`, or a `block`'s module, each once, and a `count` (absent, any
    number);
  - `delay`, whether `constants` may be drawn, a `clock`, the `cycle`;
  - `settleBy` for a timing exercise and `period` for a pipelining one;
  - the `start` circuit and the `blocks` it stocks, as structural SystemVerilog;
  - whether `code` may answer it.
  What is wrong with a file is said in words, every fault at once.
- **Its test is the migration's timing** (`MigrationResult`).
  - Each vector has a cycle: its inputs change at its start, `clk` (when there is one) rises half-way, and the outputs
    are read just before that edge.
  - The cycle is worked out unless the file gives one. It is twice through every part stocked, at least ten, and past
    a timing exercise's limit with a moment to spare: a path settling at the very instant the outputs are read would be
    read before it settled (the #85 review). A cycle given must leave a gate's delay and that moment before the read.
  - So a 4.x board migrated reads what its level expected, at the moments it is graded.
- **It opens as a document** (`ToProject`):
  - its start circuit drawn by `BoardImporter`, or its ports on a blank board (inputs down the left, the clock first,
    outputs down the right);
  - each stocked block as a module, drawn on its own when the start circuit does not use it;
  - its test as the stimulus;
  - a library of its stock (below).
  A start circuit takes its cells' delays, as setting a library gives them, is held to every rule, its stock included,
  and must have the test's ports. A stocked block must be a module of it, and not its top.
- **The stock is the library's** (`GateLibrary.Counts`, `PartCounts`):
  - Its cells are each gate kind stocked, at the exercise's delay. No constants unless the exercise gives them.
  - A count per part: past it, a part is refused, as 4.x's parts list ran out; -1 is any number. The gate budget only
    warns. A register not stocked is stocked at 0: an exercise without one is no place for one.
  - A gate is counted by kind and a register as one, one per bit, once in every instance. A block is counted by its
    module, once per copy. A module is counted once and its count multiplied, so a deep hierarchy costs its modules.
  - A block's insides count too, unless its module is stocked, capped or not. Then the block is the part: its module,
    and every module only it reaches (`BoardRules.StockedInsides`), is held to neither the cells nor the counts. **15d
    must keep a stocked block's modules from being edited**, or its insides would be a way past the stock.
  - Written code is a way past it too: an exercise not answered in code takes no written module (Part 15e holds one
    that is to the gate-level rules).
- **The self-checking testbench** (the brief's D5): `Stimulus.Checks` -- an output, a moment, a value wanted -- printed
  into the testbench.
  - Each check prints as `if ((s ==? 1'b0) !== 1'b1) begin $display("FAIL s at %0t: %b, wanted 0", $time, s);
    errors = errors + 1; end`, and `%0d of N checks failed` closes the run.
  - It fails anything but a definite match. An output read as x where a value is wanted fails; `if (s !=? 1'b0)` would
    have passed it, since `if` takes an x as false.
  - A value with a z is compared with `!==`.
  - A stimulus with no checks prints the testbench it always did. Checks are saved (`checks`), follow their port when
    it is renamed and go with it when it is removed, and must be on an output, as wide as it, at a moment of the run.
- **The grader** (`ExerciseGrader.Grade`) runs a design under the exercise's own test and library, whatever its
  document has come to say. The failures it reports are the testbench's own FAIL lines, so the grade is what the
  TRANSCRIPT will show and what the exported testbench prints in ModelSim. A run that stops before the line closing the
  test -- a loop that never settles, a `$finish` -- is no pass: the checks it never made could never fail. A design
  breaking a rule is not run. A test that makes no check solves nothing. It also gives:
  - the rules broken (a part not stocked, one too many);
  - the parts, and the gates by kind, a stocked block counted whole;
  - the static settle time (the slowest input-to-output path, Part 14a) against `settleBy`;
  - the minimum period against `period`, every path the clock times counted -- register to register, and from an
    input to a register and a register to an output, as an exercise's inputs change and its outputs are read a clock
    apart. The TIMING tab's minimum period is register to register alone; two registers at the output met a period that
    way with the slow path still in front of them (the #87 review).
- **Checked.**
  - dotnet: `PartCountTests`, `SelfCheckTests` and `ExerciseTests`:
    - the file and what is wrong with one;
    - the document;
    - right and wrong designs;
    - over the stock, too slow, clocked, a register counted;
    - blocks and a start circuit, a block stocked with no count and the modules inside it;
    - and the #85 review's: a run stopped early, a test checking nothing, a cycle leaving its moment, written code, a
      stock validated, a check following its port, a hierarchy thirty deep.
  - Oracle: `SelfCheckOracleTests` -- Icarus prints the same lines in six cases, an output read as x and an exercise's
    grade among them.
  - Edit Mode: `JsonUtility` reads a file as dotnet does.

**As built in Part 15c, first half: the thirty levels as exercises.** Each level is an exercise of the same id, in
Assets/Resources/Exercises, so its progress and its migrated board carry over (15d).

- **Made from the levels, not copied by hand.** A one-off Edit Mode tool (not kept) read each level and its own
  solution -- the one its tests prove, `MigrationTests` -- migrated it, and wrote:
  - the exercise's inputs and outputs, named as the migration names them;
  - its test, from the level's streams and expectations;
  - its stock, from the parts list;
  - its start circuit and blocks, printed;
  - its reference solution, printed, to Tools/ci/Exercises/solutions.
  Every wire was put back to one tick first: in 5.0 nothing is balanced by a wire's delay, and the migration's buffers
  would be parts the stock lacks.
  - **Out of step keeps its slow wires**, as BUFs it stocks, since slowness is what is wrong with it: it settles in 4
    against a limit of 3, and the student takes them out.
- **Its time limit** is the level's latency less one -- a 4.x path of k gates was k + 1 wires of a tick each, and in 5.0
  is k gates of 1 ns -- or, for the four wire-delay levels, what their solution settles in. Odd one out keeps none: it
  comes before timing is taught (chapter 1), and its lesson, a tree beating a chain, belongs to chapter 9.
- **Its words are new**: an input or an output, never a source or a bin; nanoseconds, never ticks; the K-map exercises
  say their function, as there is no truth-table panel to point at. Each goal of a first time says so -- a time limit
  and the TIMING tab, the clock, a register, a circuit to open on, a block.
  - **Route the bit keeps its ports' names**, `binOne` and `binZero`, which the save's migration gave the level's board,
    so the board a student left there opens again in it. Its second output was the one `-` of the thirty -- a 4.x bin
    that had to stay empty -- and is free (`x`) now: an output nothing drives reads z, not "empty", and leaving it
    alone is what the goal asks.
  - **Two time limits could not be missed with the parts given**: the slow lane's and balance the paths', whose stock
    had no more gates than the limit had nanoseconds. Part 15g made both matter (see "As built in Part 15g-1"); out of
    step's and the long way round's could always be missed.
- **A register starts at 0 in the nine sequential exercises** (`registerStart`), as every 4.x register did and their
  tests assume; 5.0's own flip-flop starts at x, which start from nothing, in chapter 7, teaches. **15d places a register in
  an exercise at its `registerStart`, and 15e has to start a written one there too**: an `always_ff` with no
  initial value starts at x, and would fail all nine. Exercises count time in ns (`Exercise.TimeUnit`), the course's
  unit, and their goals and limits say ns, so **15d holds an exercise's unit to it**.
- **Owed to 15f**: no first-time hint or tutorial text of 5.0's exists yet, so the four-word rule between them and the
  exercises' goals and hints has nothing to hold. 15f writes them as pure data, where `ExerciseCurriculumTests` can.
- **Chapters follow the course's order**: 1 gates, 2 K-maps, 3 NAND and NOR, 4 delay, 5 building blocks, 6 adders, 7
  flip-flops, 8 counters and state machines; 9, critical path and pipelining, came with the five new exercises below.
- **Checked.** dotnet, `ExerciseCurriculumTests`:
  - every file opens and its reference solution solves it;
  - every start circuit fails as it opens;
  - the thirty levels are all here under their ids;
  - the run is in order, chapter by chapter;
  - every exercise has a goal and a hint, the hint naming at most one of its parts and never which makes which output;
  - the first exercise to ask each thing says so;
  - an answer left free comes after the don't-care exercise, either way it is left free -- an x, or a combination
    left out of a combinational test -- and the don't-care exercise is itself one;
  - nothing before chapter 7 has a clock or a register, and chapters 7 and 8 are clocked throughout;
  - every start circuit fails for the reason its goal gives -- right in every value but too slow, by its settle or
    its period, or wrong;
  - no goal or hint speaks of ticks, tokens, sources, sinks or bins.
  Oracle, `ExerciseOracleTests`: every reference solution passes its own self-checking testbench under Icarus.

**As built in Part 15c, second half: five new exercises.** Written for BitSorter, their starts and solutions drawn with
the board's own editor and printed.

- **Share the wire** (chapter 4): one output, `bus`, that two BUFIF1s share -- a, b, or z when neither drives it; two
  disagreeing make an x, left free. Its test asks for a z (`!==`). **A library may hold a tri-state gate**
  (`GateLibrary.CanHold`): the course's cells have none, and two drawn blocks cannot share a wire, which only tri-state
  gates and a written device's inout pins may.
- **Start from nothing** (chapter 7): a register starts at x here (no `registerStart`) -- what the converted sequential
  exercises' `registerStart` of 0 hides -- and rst is 1 on the first clock and again on the last, where d is 1, so q
  reads 0 there only through a reset that clears it at once. A register that starts at 0, and a reset that waits for
  the clock, both fail it (the #87 review: rst on the first clock alone let a register that starts at 0 pass). Its
  first value is free: an exported testbench's rst going 1 at time 0 is a race with the register's process starting,
  which ModelSim need not see as an edge.
- **Find the slow path** (chapter 9): odd one out's parity, opening on a chain of three XORs that settles in 3 against
  a limit of 2; a tree of the same gates meets it. The lesson odd one out lost by coming before timing.
- **Cut it in two** (chapter 9), the first with a **period**: a XNOR b two clocks late, opening with four gates between
  two registers (a period of 4); moving one register into the middle meets 2 with the same latency. Its first two
  vectors are free, while its registers fill. Two registers at the output do not meet it: an exercise's period counts
  the paths from an input to a register and from a register to an output too (15b's grader, fixed here).
- **Keep in step** (chapter 9): a reaches the XOR through two pipeline registers and b straight away; b needs its own
  two. Its b is no copy of a shifted, so delays that cancel -- a through three registers, b through one -- do not
  pass.
- **Checked.** The curriculum's tests take all 35, and the oracle holds every solution to its testbench under Icarus,
  the bus's z among them (its x is left free; two drivers disagreeing are the 14f corpus's `shared_bus`).
  `AWrongAnswerThatLooksRight_IsNotSolved` holds the three above to failing the wrong answers that once passed, and a
  reset at the clock, written as code, to failing start from nothing. `LibraryTests` holds a tri-state cell to its
  delays and through a save, and refuses one of the wrong inputs saying why; an exercise stocks one, and refuses one
  of three inputs.

**As built in Part 15d: Exercises on the workbench.**

- **An exercise's document is the exercise's where it says so** (Board). `ProjectSettings.Exercise` names it and
  `RegisterStart` says where a register placed starts (`PartsCatalog`), both saved (`exercise`, `registerStart`).
  `BoardEditor` refuses, in words and before any other check, an edit to its ports, its test (the stimulus), its
  library, its top, its time unit -- its goals and limits say ns -- its delays and its registers' start, or the insides
  of a block it stocks, and leaves the student everything built round them. A select-all and DEL, or a cut, takes
  everything but the ports (`WorkbenchSession.Remove`); it was refused whole, saying the test drives the inputs (the
  #88 review).
- **It is kept under its id**, as a circuit is under its key: the thirty share their levels' names, so a 4.x board
  the save migrated, or one migrated now (`SaveMigration.MigrateOne`, for a board the 4.x game kept since), opens in
  its exercise. `Exercise.Adopt` makes a kept answer the exercise's again -- its test, library, settings and stocked
  blocks as it gives them, every gate retimed to its cell -- and refuses one that is no answer (another top, a port the
  test drives missing); that one becomes a circuit of the student's own, `<title> old`, unlocked, and the notice says
  where, rather than written over. A 4.x board is migrated for it once only (`exercise-migrated/<id>`, a milestone), so
  one moved aside is not made again on every open.
  - **Not handled**: a 4.x board whose wires were lengthened migrates them as BUFs, which no exercise stocks, so CHECK
    calls them faults until they are deleted; and a block an exercise stocks that holds another module is locked
    only while that module is not placed on its own. No shipped exercise is either.
- **The workbench** (`WorkbenchPanel.Exercises.cs`, the view-model's `ExerciseRun`):
  - EXERCISES in the header opens a page of the run in three columns, chapter by chapter, each exercise SOLVED or
    NEXT; the start page has a fourth column, CONTINUE with the next, ALL EXERCISES, and how many are solved. A circuit
    opened from FILE or the SAMPLE chip takes the page down.
  - PARTS offers no port in an exercise (they are its test's), no register where it stocks none, and each part it
    counts says how many are left (`XOR 1`); SPLIT, JOIN, LAMP and the student's own blocks stay.
  - An exercise open says its number, title and goal in the header; GOAL opens its brief (`ExerciseCard`), the hint
    behind SHOW THE HINT, and it opens on it. CHECK grades it (`ExerciseGrader`): the verdict on the chip, every
    reason at the head of the TRANSCRIPT, each failed check at its moment, which a click goes to, and the run taken
    to the first. Solved, it is marked complete, its best kept (`ProgressStore.exerciseBests`: the fewest gates and
    the quickest settle, each on its own, the settle none for an exercise of registers alone; 4.x's tick latencies are
    not comparable and are not carried), and a card says what it took -- the blocks it gives counted whole, `4 FA`,
    and a pipeline's period -- and the student's best, and offers NEXT.
  - A circuit opened says its own unit on the speed and RUN TO at once; it said the last one's until an edit (red
    test, then the fix).
  - Only the student's own numbers: what this design took, and what theirs did before.
  - An exercise open last opens again (`exercise/<id>`); a circuit opened, made or imported, or RESET PROGRESS,
    leaves exercise mode.
- **Analytics** reports an exercise opened and solved as a level started and solved, under its id, once a session.
- **Checked.** dotnet: `ExerciseLockTests` (the lock, the unit, every exercise through a save, adopting and refusing a
  kept answer), `ExercisePartsTests`, `ExerciseRunTests`, `DocumentsTests`. Play Mode: `ExercisePlayTests` -- the
  start page and the page, the brief and its hint, the test refused and the answer kept, CHECK failing at its moments
  then solving and NEXT, an exercise open last, and Escape. Four new reference shots.

**As built in Part 15e: answering in code.**

- **WRITE IT**, beside GOAL and CHECK in an exercise that may be answered in code (every one of the 35), turns the
  answer's top into a written module (`Exercise.Written`, `BoardEditor.ReplaceModule`): the drawing as it prints --
  every gate a cell of the course's, wired as drawn -- or, where it does not print, its ports, under a header with the
  goal, what it may be built of, and where a register starts. **DRAW IT** asks first and draws the exercise's own top
  again (`Exercise.Drawn`), its ids counted on so none is given twice; Ctrl+Z undoes either, one step each.
- **Code is held as a drawing is.** 14g's gate-level rules hold it to the library's cells and refuse an operator, and
  since this part the grader says such a fault at its line (`half_adder, line 7: ...`), as the code editor marks it.
  **The stock counts it too**: drawn parts are counted by `PartCounts`, which sees nothing in code, so the grader
  counts the design it builds -- every gate and register bit `TimingAnalysis` finds, less the drawn ones and those
  inside the blocks the exercise gives, which count one an instance wherever they are placed -- a register a bit each,
  however many processes write a vector. Worked out by what is left over, so a gate is never put down to the wrong
  module by a port that joins two nets. A design too big to time cannot be counted, and is a fault rather than a pass.
- **A written answer is kept as one**: opened again, its ports are its header's (`WrittenModules.PortsOf`), so it is
  adopted like a drawn one; it was moved aside as no answer (the #89 review).
- **A written register starts at x** unless the code gives it a start, as in ModelSim: the header of an exercise whose
  registers start at 0 says `logic q = 1'b0;`. Nothing starts it for the student, so what runs here is what an export
  runs anywhere.
- **Checked.** dotnet, `ExerciseCodeTests`: a written answer graded as a drawing, its gates counted; an operator
  refused by the editor saying why, and graded at its line; one gate too many a fault; a register solving with its
  start and failing without; four stocked full adders in code counted four; every one of the 35 solutions, written as
  code, still solving; drawn again with its ids counted on; a stocked block not replaced. Play Mode: WRITE IT, a
  written half adder through CHECK, and DRAW IT asking first.

**As built in Part 15f: first-time hints and the guided tutorial.**

- **First-time hints** (`SimulatorHints`, view-model): a gate's delay, the first time a gate's output moves after the
  run began; a clock, the first time a design's stimulus has one; a z on a port; a glitch -- an output that changes and
  changes back with no input changing between, as balance the paths' goal says its waves show (an adder rippling its
  carry glitches too, and earns it as rightly) -- both moves answering one input event, within the design's slowest
  path of it, so an input pulse narrower than a path's delay followed along one path is none; and an x at a check,
  from CHECK's grade. A written top's ports are read from its header. Each is looked for every thirty frames while none
  is up, marked seen in `hintsSeen` as it goes up (ids ending in 5, beside 4.x's), and taken down with GOT IT. The
  #90 review found the glitch rule firing on a narrow pulse, and the glitch and z hints never coming on code.
- **The guided tutorial** (`GuidedTutorial`): on route the bit, from the start page's TUTORIAL and HELP's. Take the NOT,
  put it down, wire in to it and it to binOne, CHECK. Its step is read off the document and what is in hand every frame,
  so nothing blocks input and deleting the NOT or an undo goes back by itself; it lights the NOT in PARTS and then
  CHECK, the steps between being on the canvas, and SKIP stops it. It has the strip while it runs.
- **The four kinds of teaching** are held apart by `SimulatorHintTests`: no four-word run is shared between a hint, a
  tutorial step and any exercise's goal or hint -- what 15c owed.
- **Checked.** dotnet: each hint fired by what a reference solution's run shows, and once; the x at a check; the four-
  word rule; the tutorial's steps over an edited document, an undo and a deletion taking them back. Play Mode: the
  tutorial from the start page to CHECK, lighting the NOT and CHECK; skipped from HELP; a hint coming up once, kept as
  seen, and not again.
- **The browser check of Part 15** (2026-10-28, a WebGL build in the browser pane): a fresh save opened on the start
  page; TUTORIAL ran route the bit -- the NOT lit and its edge put back, a wire in and a wire out, CHECK solved it and
  the card offered NEXT; an empty exercise 2's CHECK listed 16 of 16 failed checks at their moments and the x hint came
  up; a reload reopened exercise 2 with its XOR; the music was off; THE 4.x GAME in Settings opened the title screen
  and SIMULATOR came back. No console error. It found one thing: the page marked the exercise after the one open as
  NEXT while the open one was unsolved (red test, then the fix). The release build's link-time optimisation ran out of
  memory on the night's machine, so the check was of a build without it; nothing in Part 15 depends on the link.

**As built in Part 15g-1: what the first playtest asked for** (Ahmad played the Part 15 browser build, 2026-10-28).

- **The way back from the 4.x game.** Inside it the levels played and nothing said how to leave: the way was MENU, then
  a title-screen row captioned SIMULATOR like the rest, and Escape there showed the board under it. The title screen's
  first and largest button is BACK TO THE SIMULATOR now, a 4.x board has SIMULATOR beside MENU, Escape on the title
  screen goes back, and its SETTINGS is the simulator's. The path itself had worked: a test replaying it passed first.
- **S on the start page** shows the next sample, as the chip it is drawn beside does: the page held every key.
- **RESET PROGRESS starts the exercises over and keeps the circuits** (`ProgressStore.ForgetTheExercises`): solves,
  answers kept under an exercise's id and their levels' 4.x boards, bests, milestones and hints go; MY CIRCUIT, named
  circuits and free play stay. It had emptied the whole save.
- **DRAW IT draws the code** (`Exercise.Drawn`): the project printed and read again, the written top drawn by
  `BoardImporter` over the other modules; the exercise as it starts, with why, only where the code is not drawable.
  Every exercise's solution goes through WRITE IT and DRAW IT and solves with the same parts.
- **One clock period for TIMING and CHECK** (`TimingReport.ClockPeriod`): register to register, input to register and
  register to output; TIMING shows it as CLOCK PERIOD where it is more than the minimum period.
- **The two time limits matter.** The slow lane is 3 ns: as written its expression is four gates deep, and it
  simplifies to `b | (a & ~c)`, three deep as `b | (a & (a ^ c))` with the XOR, AND and OR it gives -- so its lesson is
  to simplify first. Balance the paths stocks a buffer: on `b` it balances the paths and out no longer flickers, on the
  XOR's path it is late. A slow answer of each is right at every check, late, and failed on time.
- **A caret in every shell field**, drawn by the shell: UI Toolkit's took its colour from a deprecated property the
  browser build ignored.
- **Every field in the browser lost the keyboard a tenth of a second apart**, and the shell gave it back a frame later:
  the transcript search's placeholder flickered. A focus log in a browser build found the field's own text element
  blurring itself every ~110 ms, nothing of BitSorter's asking. UI Toolkit edited the fields as on a phone and polled an
  on-screen keyboard that a computer never shows. WebGL's mobile keyboard switch did not change that, but each field's
  `hideSoftKeyboard` does: set on a computer's browser by `ShellTheme.Field`, and a field kept one focus in and none out
  in the browser after it. The editor has no such keyboard, so a test there can only hold the fields to it.
- **Found on a 64-bit ALU imported with its testbench**: a written top's inputs are its header's, so a double-click on
  one names the testbench that drives it and CONVERT, where it said "not an input of the design"; a wave's value beside
  its name is cut to its room; and the browser's code editor lays itself again on every resize and scroll of the page
  and stops above the hint strip, where it had stayed where the canvas had been, over PARTS.
- **Measured for 15g-7** (dotnet, this machine): a drawn 128-bit adder (1026 parts) takes 267 ms to time, 1,159 ms to
  build TIMING's lines, and 1,684 ms to lay out; the course-sized written CPU 135 ms to time. The glitch hint's timing
  analysis on every edit while it was unseen is gone: it runs once a glitch-shaped change asks for it.

**As built in Part 15g-2: Settings and the credits, pages of the workbench.**

- **Settings is a shell page** (`WorkbenchPanel.Pages.cs`: `AddPage`, `ShowPage`, `HidePage`), under the header as the
  start page and EXERCISES are -- Settings and the credits one at a time, over the start page or EXERCISES where one of
  those was up, and any of them `PageShowing`. It is built from `ShellTheme` and
  the style's `SchematicPalette`, so every style colours it; it was a uGUI screen in the 4.x board's colours whatever
  the style. The sections and their controls are the same, the volume and the frame cap are `ShellSlider`s (which
  gained `Released`, so a drag writes the preferences once, and `wholeNumbers`, so the thumb rests on a stop as uGUI's
  did), and the page scrolls where the window is short instead of shrinking (`FitScale` stays, for the help panel and
  free play's setup).
- **What a page over a workbench in sight changed** (the 15g-2 review). Under the uGUI screens the workbench was out of
  sight and came back through `Resume`; as pages over it, three paths still assumed that:
  - **RESET PROGRESS with an exercise open** let the exercise go and left its circuit on the stage until `Resume`. Left
    by the header's HOME instead of BACK, that circuit was MY CIRCUIT's to keep, and its next edit wrote it over the
    student's own. MY CIRCUIT now takes its place at once, under Settings, and the start page goes up when Settings comes
    down, unless something else has been opened first.
  - **Keys under a page**: S, the sample chip's key on the start page, changed the circuit under Settings unseen, and the
    key that took the credits down also reached the workbench whenever the credits updated first -- Delete deleting the
    selection. While Settings or the credits are up, and in the frame one comes down, the workbench takes no key and no
    click of its own (`PageHoldsTheKeys`).
  - **A circuit opened from the header over a page** -- the SAMPLE chip, FILE, HELP's TUTORIAL -- left the page up over
    it. Opening a circuit, the start page, EXERCISES or the other page takes it down through its own controller
    (`AddPage`'s `closed`), so what Settings does on its way down is done then too.
- **What the plan said and what was built.** The plan moved the settings' logic into a plain class and deleted the
  uGUI panel. Rewriting `SettingsPanel` in place as the page's builder did the same with less to move: the object, its
  name and its controls' names stay, so the scene, `GameAudio`'s menu music, `LookSwitch`'s reopening and the tests
  reach it as they did. `CreditsPanel` likewise.
- **The credits roll on a page of their own**, rising from its foot and resting on the farewell, any key or release of
  a mouse button going back. 5.0's lines: the SystemVerilog language, the exercises and the workbench as Ahmad's roles;
  IBM Plex Sans and Mono (SIL Open Font License) under BUILT WITH; Icarus Verilog under CHECKED AGAINST; and a
  dedication to every student of digital systems, naming no university.
- **The workbench's shots move by a few pixels of type.** Settings now draws its words in the shell's font, so the
  shell's glyph atlas is laid out differently and every workbench shot's header text differs by 2 to 11 pixels at three
  levels or fewer. Nothing moved; Settings and the credits are reshot.

**As built in Part 15g-3: a lead on every pin.** Ahmad asked for "a small tiny line that we can wire from" (2026-10-28).

- **The bodies stepped back; the pins did not move.** A gate's front and back always sat half a cell in from its pins.
  A port's box and a constant's stood on their pins, and a block's a quarter of a cell from them; each now stands
  `SymbolGeometry.Lead`, half a cell, back. A port keeps its outline, which is what routes keep clear of, so no route
  can move: its box gives up the half cell, and its name still fits, since `PartGeometry.PortWidth` allows 0.17 of a
  cell a character and the type takes about 0.12. A constant's literal is sized tighter, so its outline grew by the lead
  away from its pin, and its box kept the literal's room -- the one outline that changed, so a route passing within half
  a cell behind a constant may run again. A block's box is two cells wide, from two and a half; a pin's name has the
  box's width where nothing is across its row and half where something is, cut short with an ellipsis rather than run
  into the other, on whichever side a mirrored block draws its pin.
- **A wire to a port runs on to its box**, as one to a gate always ran on to the body (`DocumentSchematic.TryBody`), and
  `PortLayout` carries its pin, its lead's end and whether it is wired.
- **An unwired pin's lead ends in an open circle** (`WorkbenchDrawing.PinCircle`), and the pin a press would wire from is
  lit under the pointer (`CanvasOverlay.PinLit`, from `CanvasEditor.PinOver`), with the status line saying "drag to wire"
  (`PointerArea.Pin`) -- asked again only when the pointer, the zoom or the drawing changes, by index rather than through
  boxed enumerators, and not under FIT, the minimap or a pan. While a wire is drawn, the pins it may end on show theirs
  (`PinEnds`, at most `CanvasEditor.MaxDraftEnds`, drawn after the wire and as many as fit the mesh): an inout always; an
  output while the start's net has no driver, or as a second driver on a bus (`BoardRules.IsBus`); an input no wire
  reaches yet while the net has a driver. They are found as the draft starts and again after any edit. The document
  still has the last word as the wire ends: a guide, not a promise, since asking it of every pin would build a document
  per pin.
- `PinLeadTests` holds every pin of every part the strip makes to a lead of at least a quarter cell, the ports' boxes
  inside their outlines, and the pins of the parts that stepped back where they were.

**As built in Part 15g-4: INPUTS, sequences without code.** Ahmad asked for a way to make input sequences and vectors
without code, for buses as well as bits (2026-10-28): a double-click on the waves flipped a bit or asked for one value.

- **The board's edits** (`StimulusEdits`, pure, each a stimulus or a refusal in words): `TrySetStep` (a cell typed or
  emptied), `TryRemoveRow` and `TryMoveRow` (every input's step at a moment), `TryFillCombinations` (every combination of
  the chosen inputs, the first the most significant, up to `MaxCombinationBits`, 10), `TryFillCount` (a bus counting up,
  `MaxCount`, 256, at most), `TryMakeClock` and `TryUnmakeClock`, and `TrySetEnd`. Each keeps the fewest steps that say the
  same thing, so a value an input already holds is no step, and a fill ends the run a step after its last row if it ended
  sooner.
- **The view-model's table** (`InputTable`): a column per input of the top -- a drawn top's ports or a written one's
  header -- in its own radix, BIN, HEX or DEC; a row per moment something changes, each cell what an input changes to then,
  or empty. Its own state -- radixes, chosen columns, STEP, a row added with + ROW and not filled -- is carried from the
  table before, each change in a copy. A cell takes digits in its column's radix or any literal. Every edit gives the
  stimulus to commit and the moment the run changes from, committed by `WorkbenchSession.SetStimulus` as one undo step and
  replayed from there, as SET's is.
- **In an exercise it is read only** and shows the exercise's own test: its rows, and a column of the value wanted for each
  output it checks. An edit is refused in the document's words -- `BoardEditor.ExerciseDrivesTheInputs`, now one constant
  for SET and the table -- and with a written testbench, `BoardRules.BenchDrivesTheInputs`.
- **The tab** (`WorkbenchPanel.Inputs.cs`) sits beside WAVES, the other tabs a tab further right. Its header has + ROW,
  STEP, ALL COMBINATIONS, COUNT, CLOCK and END; a column's name chooses it, its radix under the name changes that. The
  wheel scrolls the rows, Shift with it the columns. An edit that changes nothing -- a field let go as it was, a value the
  input already holds, the end it has -- commits nothing and is no undo step (`StimulusEdits.Same`), and a value the input
  holds already says so. A cell finds its row by its moment and its column by its port, and the table is made again before
  every edit, so a commit as a field is let go and the click that let it go are both made on the stimulus there is; the
  table is not redrawn under a held mouse button, whose click a redraw would take away. A new document starts the table
  afresh. Times go no further than `StimulusEdits.MaxTime`, an import's own limit: a run needs a finite end.
- **An import without its testbench says which inputs nothing drives** (`InputTable.Undriven`): they read z and what they
  feed x, and the notice says how to drive them -- INPUTS, a double-click on the wave, or importing the testbench too.

**As built in Part 15g-5: LOGIC, the truth table, expressions and K-maps.** Ahmad asked for a way to know more about what
is built (2026-10-28).

- **The truth table is found by running the real engine** (`LogicTable`): `StepTests.Combinations` walks every combination
  of the input bits in Gray order, each held for the settle time the measured worst uses -- the slowest path and half
  again -- on a copy, a slice a frame while the tab is up. Each output bit is read at the end of each hold; an x, a z, or a
  row the run did not reach reads x. Rows count up with the first input most significant: ports in order, a bus's highest
  bit first, each bit named as the design declares it (`VariableNames`: an `input [7:6] a`'s are a[7] and a[6]). Refused with the reason, as the measured worst is, for registers, a loop of logic, a delay the run works
  out, no input or output, or more than ten input bits -- saying how many rows that would be.
- **Each output a minimal sum of products** (`Minimizer`, up to eight variables): Quine and McCluskey's prime implicants,
  then an exact cover -- the fewest products, then the fewest literals -- searched with the row fewest primes cover first,
  bounded at `MostTried` tries: an eight-input lookup with no pattern to it reaches that, and keeps the best found,
  `Result.Proven` false, which the tab says. The search is a stack of choices rather than a recursion
  (`Minimizer.Search`), so the tab makes it 4 ms a frame; however sliced, it tries the same covers in the same order to the
  same result. An x row is a don't-care, and the tab says so. Written the textbook's way (`a'b + ab'`; names longer than
  a letter spaced apart, `b cin + a cin + a b`) and SystemVerilog's (`assign s = (~a & b) | (a & ~b);`).
- **A K-map for four inputs or fewer** (`KarnaughLayout`): the first half of the variables down the side and the rest
  across, Gray order both ways, x where the output reads x, its 1s shaded. No groups are drawn: the expression is beside it.
- **The tab** (`WorkbenchPanel.Logic.cs`) sits right of TIMING: the table on the left, each output's block -- its
  expression both ways and its map -- in columns on the right; the wheel scrolls whichever the pointer is over, the table
  three rows a notch and the expressions no further than they reach. It is made again only when the design changes
  (`TimingView.SameDesign`, the project's files included), so an input's steps edited keep it, and the old
  table is cleared while a new one is made.
- **Fixed after review**: the names on ranged ports, the restart on every edit, the scroll with no end, the expressions
  all minimized in one frame and never saying when one was cut short, and products run together -- each a red test, then
  its fix.
- **Tests**: the minimizer against brute force on every three-variable function and sixty random four-variable ones with
  don't-cares, and the search in slices against the search at once on five to eight variables; the full adder tabled and
  its carry minimized; ranged ports named as declared; the counter refused; and every combinational exercise's reference
  solution, tabled by running it, agreeing with that exercise's own test at every check.

**As built in Part 15g-6: STATES, the state diagram.** Ahmad asked for a state diagram (2026-10-28).

- **Found by running the real engine** (`StateExplorer`). The clock is an input called clk or clock, or else the
  stimulus's clock; the reset another input called rst... or reset..., active low when its name says so the usual ways
  (rst_n, resetn, nrst, rst_b, reset_l). A name is a guess, so a machine that comes out as one state, or starting on an x,
  is explored again with the reset the other way round, and the fuller kept. Every other input bit, four at most, labels
  the arrows. Six register bits is checked on the timing's own graph before anything is compiled. Each run compiles a copy whose stimulus is the reset for a cycle, if there is one, then the
  inputs that reach a state, one a cycle, then one combination more. The state is read half a period after the edge from
  the register bits the timing analysis names (`TimingGraph.Of` on the copy; six at most, each register's high bit first),
  and the outputs just before the edge. The period is twice the measured settle time, or the design's minimum period if
  that is longer.
- **Breadth first from the start**: every state reached is run with every combination, up to sixteen states (`Cut`
  past them). **A state with an x in it is run on from like any other**: it is what a register holds from power-up until
  written, and the next state may well be known -- a two-bit shift register with no reset goes xx, then x0 or x1, then
  somewhere known. **The start is the state after the reset**, or the power-up state where there is none -- not the
  project's own stimulus, which the plan named but the sequential exercises' solutions do not have. A drawn register
  starts at 0; a written one with no reset at x.
- **Moore** when every output is the same on every arrow out of each state, otherwise **Mealy**.
- **Each bit is named from the compiled design** as the timing names it, so an `input [7:6] a`'s bits are a[7] and a[6]
  and an `output [0:1] y`'s high bit is y[0]: the 15g-5 review found LOGIC naming every bus from 0.
- **The tab** (`WorkbenchPanel.States.cs`) sits right of LOGIC. The diagram is on the left (`StateDiagramView`), drawn
  with UI Toolkit's own painter rather than the waves' uGUI the plan named, since the dock is the shell's:
  - the states round an oval, evenly along it, in the order reached, the start ringed in the accent with an arrow in;
  - an arrow for each way on, labelled with its inputs (a Mealy machine's as in/out), bowed apart where both ways are
    taken, and a loop where a state keeps itself;
  - a Moore machine's outputs under a bar in each circle;
  - a circle shrinks only once its neighbours would crowd it.
  The next-state table is on the right, under a key line naming the bits; the wheel scrolls it, Shift with it across,
  never past its ends. It is made a slice a frame while the tab is up, and made again only when the design or its clock
  changes (`TimingView.SameDesign`, the project's files included): an input's steps edited keep the machine
  (the 15g-5 review found LOGIC restarting on every edit). The old diagram is cleared while a new one is made, and the
  progress shown never goes back. In a short dock the oval keeps its height before the room round it; shorter still, the
  tab says to drag the splitter up.
- **Fixed after review**: a reset's way round guessed wrongly and silently, states with an x never run on, a clock
  chosen from the stimulus over one called clk, the clock taken for the reset too, the restart missing a changed file, a
  flattened oval putting states on each other, and progress going back -- with tests for each that the first tests
  would have passed.
- **Tests**: one-clock-late's two states; spot-the-pattern's three-state Mealy detector, every arrow of it; one-clock-behind's
  three states as Moore; the counter's eight states in order from 000; a written register on [7:6] and [0:1] ports named
  as declared; a reset's way round by its name and by trying both; a shift register run on from its x states; thirty-two
  states cut at sixteen; clk the clock whatever else the stimulus clocks; an adder refused. In Play Mode, the counter
  drawn and tabled with no circle over its neighbour, an input edit keeping the machine, two states side by side and
  apart, and the adder told why there is none.

**As built in Part 15g-7: big circuits stay smooth.** Ahmad asked whether TIMING would slow a big project down
(2026-10-28). Measured under dotnet on drawn ripple adders -- 8n + 4 parts, so the 128-bit one is 1,028 -- the drawing was
the slower of the two, and it was made more than once an edit.

| 1,028 parts | before | after |
| --- | --- | --- |
| routing every wire | 1,039 ms | about 300 ms |
| times the drawing is routed an edit | 2, or 3 while a wire is drawn | 1 |
| a step of a drag | 302 ms | under 1 ms |
| the TIMING tab | 216 ms (the analysis 84) | about 80 ms, the analysis itself |

- **The router asks only what is near** (`WireObstacles`): corners, pins and part bodies are kept by the square of the
  board they are in, two cells a side, a body in every square it covers, and a piece asks only the squares under it. Each
  of the three is a hit only where it touches the piece's own extent, so the answer is the one asking all of them gave.
- **A route tried is made only when it is asked about** (`WireGeometry.Avoiding`): a wire across a drawing hundreds of
  cells wide tries thousands of columns and heights, and each was two lists of points. Each try is kept as what makes it
  and what it sorts by; sorted as before -- the same keys from the same tries in the same order, through the same unstable
  sort -- it picks the same route, and its points are made again only for those asked whether they are clear.
- **Every route is as it was**: a dump of every wire's points and every junction, over every sample, adders of 4, 16 and
  64 bits, every exercise's solution and the oracle's designs -- fifty drawn modules -- is the same to the character before
  and after, and the reference shots to the pixel.
- **Routed once a document** (`WorkbenchSession.RoutesOf`): the drawing (`DocumentSchematic.Build`, given the routes), the
  canvas's hit map (`CanvasHit`) and a wire being drawn each routed the whole module for themselves. Kept by the module and
  the project's modules -- a block's outline is its module's -- so an input's steps edited route nothing.
- **A drag routes only the wires that follow it** (`WireGeometry.Following`): the wires on the moved parts, round the
  others where they lie; routing every wire again at each step was what made a drag of a big drawing stutter. A preview:
  the drop routes the whole module, and a following wire may then take another way round a wire that moved too.
- **TIMING names each signal once**: two hundred paths listed, each look-alike, each signal along them named again, made
  the view two and a half times its analysis.
- **Not sliced across frames**, though the plan said so: it was planned for the 1,159 ms the TIMING tab took when measured
  for the plan; at about 80 ms for a thousand parts, worked out only while something asks for it, slicing would buy less
  than it costs. Nor routed a slice at a time: an edit's drawing is shown whole or not at all.
- **What is left**: routing is still about 300 ms for a thousand parts -- making, sorting and asking the tries, a third
  each -- and more in a browser. Pruning tries by where a run first meets something was looked at and left: a tidied route
  can fold back over its pin's stub, so a run's being blocked does not always make its route blocked, and only an exact
  saving keeps every drawing as it is.
- **The desktop is faster, and a browser says so** (`DesktopHint`): the session keeps the slowest its drawing or its
  timing took since the panel last asked (`TakeSlowestMilliseconds`), by its own clock; in a browser, the first time that
  is more than 0.3 s, the notice line says once a run of the game that the desktop version runs it faster -- one codebase,
  the same features in both builds (agreed with Ahmad, 2026-10-28). The slowest, not the last: an edit draws the document
  twice, the second time from the routes the first made, and the review found a 500 ms edit told as 6 ms.
- **Tests** (`BigDrawingTests`): a drag keeps every other wire as it lay and ends each following wire on its moved pin; the
  session routes a document once, an input's edit keeps the routes, and a move and its undo route afresh to what routing
  from nothing gives; the hit map and the drawing take the routes they are given; the hint's rule, the session keeping the
  slowest until asked, and a slow edit of the thousand-part adder heard as slow; and that adder drawn, timed and dragged
  within budgets, each the fastest of three after a warm run -- the drag's tight, since the old way was three hundred times
  slower. **`WireObstaclesTests`** hold the index of squares to the old way of asking everything, kept beside it in the
  tests (`ObstaclesAskingEverything`), on 40,000 random routes round square edges, tolerances and negative coordinates;
  the index's tolerance made a millionth of a cell narrower fails it.
- **The review** (Sonnet) found no route changed -- it held the old and new routing to 40,000 random modules, rotations,
  blocks, taps on taps, kept bends and ties among them, byte for byte, and the index to eight million random cases -- and
  three things fixed here: the hint told the last drawing rather than the slowest, a time budget measured cold, and tests
  that asserted less than the docs claimed.


**As built beside Part 15h: Settings on one screen, and ABOUT BITSORTER.** Ahmad asked on 2026-10-28.

- **Settings is two columns** (`SettingsPanel`): AUDIO, DISPLAY and STYLE on the left, PRIVACY, PROGRESS and ABOUT on
  the right, under one title. One column ran past the foot of a 1080 screen, and CREDITS, last, was found only by
  scrolling. A shorter window still scrolls. PRIVACY's line says it reports which exercises people get stuck on, as
  it does since Part 15d.
- **ABOUT BITSORTER** (`WorkbenchPanel.About.cs`), from HELP, is a page of the workbench's own:
  - what the simulator is, in a line, and what it does, under the workbench's own names: draw, write, run, read the
    waves, drive it, analyse it, bring your own project, practise, and how it simulates;
  - where it runs, and where it came from, which is the only place the course is named, in full: Digital Systems and
    Computer Structure;
  - at its foot, who made it and three buttons: CREDITS, which roll and come back to the page; KEYBOARD MAP; and GITHUB,
    the project's page.

  HELP's item opened the credits roll until then. Escape takes the page down, first of everything.
- **Tests**:
  - `PlayerTextTests` holds every line of the page to the buttons it names;
  - a Play Mode test goes HELP, ABOUT, CREDITS and back, then Escape;
  - Settings is held to fitting a 1080 screen;
  - reference shot 60 is the page.

**As built beside Part 15h: what Ahmad's own ALU found.** On 2026-10-28 Ahmad imported a 64-bit ALU of his own, written
as code with its testbench, into a browser build. These are the fixes; the drawing of code is the rest of Part 15h.

- **TIMING's rows fold** (`TimingFold`, view-model). His ALU listed two hundred paths, each cut short.
  - A section folds under an arrow: a labelled line and the unlabelled lines under it, such as PATHS and its paths. It
    starts shut past eight lines.
  - A line too long for its row opens onto the rows under it. It is cut after a comma, or at the gap between a path's
    ends and its gates, so no name is split.
  - A choice is kept by what it is about: a section by its label, a line by its text. So it outlives the view made again
    after every edit.
  - The arrow is a Painter2D triangle. A press on it picks nothing; a press on any row of a line picks the line.
- **INPUTS under a written testbench shows what it applies** (`InputTable.AppliedBy`).
  - The rows are what CONVERT would record from a run of the testbench (`BenchConverter`). The table was column heads
    over nothing.
  - It is read only:
    - the editing tools are hidden, with CONVERT in their place, asking first;
    - a line over the table says in full whose values they are and how to change them;
    - its cells are text.
  - A testbench CONVERT refuses has no rows and says why (`CannotConvert`).
  - The conversion runs only while the tab is up, once per document change.
- **A click on the waves places the cursor, and Shift and a click moves the playhead.**
  - The waves are drawn only up to the playhead, so a click back in a finished run hid the rest of it.
  - A press within 6 px of the playhead's line still drags it.
  - The values column reads at the cursor while one is placed (`WorkbenchModel.RowValueAt`), at the playhead
    otherwise: Ahmad asked for it once a click placed the cursor. The circuit still follows the playhead.
- **The waves zoom out past the run** (`WaveView.MinZoom`): as far as the run across a quarter of the strip. The fit is
  a stop on the way in and out. Zoom stopped at the run's end, and Ahmad asked for a limit "not at the limit of the line".
- **An empty field shows its caret in a browser.** It is placed at the text's start, measured from nothing. Only a
  browser build showed the fault.
- **The desktop hint says "this design"**, never "a drawing": written code is as slow to work out, and his has no drawing.
- **LOGIC and STATES say why they are empty, where it is read** (after the merge, from the same ALU in the browser build).
  - Empty was right: LOGIC tables up to ten input bits, and his ALU has 131; STATES is for registers, and it has none.
    But the reason was the header's dim line alone, cut short, so both looked broken.
  - Now the reason runs across the top of the tab's body, and the header's line is left empty rather than saying it twice.
  - The count in it was wrong: "131 input bits are 8 rows". A 64-bit 1 shifted 131 places wraps round to one shifted
    3, and STATES capped its own counts at 2^30 and 2^62. `LogicTable.PowerOfTwo` writes the number itself below 63
    bits, and 2^131 past it. It was a red test, then the fix.
  - STATES says "and LOGIC tables it" only where LOGIC can.
- **Tests**:
  - `TimingFoldTests`, `InputTableTests` (a testbench's rows, and one CONVERT refuses), `CursorTests` and
    `WaveViewTests`, all under dotnet;
  - Play Mode: the fold's arrows and rows on a 16-bit adder, INPUTS and its CONVERT on an imported testbench, the cursor
    placed by a click, the playhead by Shift and by its line, and an empty field's caret.


**As built in Part 15h-1: the netlist of a module's code** (`Sim/Rtl`). The first step to drawing written code: one scope
of an elaborated design as cells and the nets between them, word-level, the way a synthesis tool's RTL view has it.

- **`RtlExtractor.Of(design, scope)`** makes an `RtlNetlist` from the elaborated design, where parameters, generate
  loops and widths are already worked out.
  - **A driver belongs to the scope it is written in** (`DriverInfo.WrittenIn`, which the elaborator records), not its
    target's. A scope's own logic is what is written in it and its generate and named blocks.
    - An instance inside it is one `Instance` cell, its pins the instance's ports, named from the scope (`slice[3].s`).
      The values on its inputs are the zero-delay connections the elaborator writes in the parent (`port dut.h1.a`), so
      the instance's own netlist has its input ports for inputs.
    - An inout joined to its net is a pin on that net (`Design.Aliases`). A signal driven from more than one scope -- an
      inout bus -- has in each scope only what that scope gives it, on a net of its own (`bus (driven here)`); its
      resolved value is the design's.
  - **Each expression node is one cell**, however many places use it (`RtlBuilder.Of`): `{cout, sum} = a + b + cin` is
    sliced once for each, and is still two adders.
  - **Each node of an expression is one cell** (`Expr.ToRtl`): `Constant`, `Gate`, `Unary`, `Binary`, `Mux`, `Concat`,
    `Replicate`, `Slice`, `Bit` and `Part` (a select by a signal), `Resize`, `Signedness`, `TwoState`, and `Word`, a
    memory's read port.
  - **Every driven signal is the output of a `Join`**: its drivers' outputs fitted to their slices, resolved as a wire
    resolves them, and held as the signal holds them -- its width, sign and 2-state type -- exactly as the engine does
    (`Simulator.ResolveOwn`, `Shape`). One driver of the whole signal is a wire, which a drawing collapses.
  - **A process was a box** in this part, what it reads in and what it writes out; Part 15h-2 opens each that describes
    hardware (below). **A node that is no hardware** -- a string, a real, `$random` -- is an `Opaque` box, and so was a
    call of a function until 15h-2 opened those too.
- **`RtlEvaluator.Evaluate(netlist, signals)`** works out every net from the values of what feeds it: the inputs, and
  what the instances and processes drive, read off the design's signals.
  - Each cell applies the engine's own operator. `Expr.Apply`, `TryOffset`, `PartAt` and `WordAt` are what the engine's
    nodes evaluate with as well, so the two cannot drift apart.
  - A loop of logic is cut where it closes, each net in it read off its signal.
- **Tests** (`RtlNetlistTests`, Board.Tests): every scope's netlist held to the engine.
  - Every net a join drives -- every signal the scope's own logic gives a value -- reads what the engine has there, and
    every instance's input pin what the instance's port has, over random vectors far enough apart for every delay to
    settle, with an x or a z now and then.
  - A net whose value passes through an opaque box is left out: the engine leaves a function's value stale when a
    signal it reads but is not given changes, as Icarus does, which no circuit computes.
  - And every netlist's inputs are signals of its own scopes or of what is inside them, never its parent's.

  The designs:
  - every exercise's reference solution, its cells' insides included;
  - every module with ports in the oracle corpus's structural, behavioural and smoke designs, read with their folder's
    includes, and every testbench run as written to its end;
  - a module of every operator.

  The shape is held too: the full adder's five instances joined by the nets the code names, alu64's 64 slices, a shared
  value one cell, a child's inputs its own, and an inout bus driven in each scope by what that scope gives it.
- **The Sonnet review** found the extractor putting a port's connection into the instance's own netlist, an inout bus's
  drivers into the wrong scope, and a shared value drawn twice -- every value right, every structure wrong for a drawing
  -- and the test too weak to see any of it; all of it is fixed and held now.
- **Found on the way:** the shell's caret was placed on a timer of 50 ms of real time, and a full Play Mode run's driven
  frames now and then came in under it. It is placed every frame the field has the keyboard (a red test, then the fix).

**As built in Part 15h-2: processes as muxes and registers** (`Sim/Rtl/RtlProcess.cs`, `RtlFunction.cs`). Every process
that describes a circuit is opened into cells, and what is no circuit stays a box that says why.

- **A process runs symbolically** from its wait to its next, every path at once. A path keeps the value each signal has
  on it, so a blocking write is seen at once, as the language has it.
  - **A branch** -- decided as the engine decides it, by `LogicVec.Truth` or `Matching.CaseMatches` -- parts the path in
    two, unless its condition is a constant. Paths meeting again are joined two at a time, the two that parted last
    first: two that parted at one split by a mux on its condition, so an `if` is one mux a signal and a `case` of single
    labels a mux for each, on its `Match`.
  - **Any other two are joined by a mux on whichever went through splits of its own past the ones they share**, and the
    joined path keeps a split that says it is the one or the other -- a `case` arm of several labels, a path that left
    early by a `break` or a `return`. No run takes both paths, so where one's splits hold the other's do not.
  - **In the order of their splits, the two that parted last are neighbours**, so each join looks along the paths once:
    a `case` of 256 labels opens in a fraction of a second.
  - **A value made on a path is used again until the path writes something**, so a `case`'s subject is one cell however
    many labels it is matched against.
  - **A loop whose condition folds to a constant is simply run**, so constant bounds unroll, and `break` and `continue`
    are jumps. Its counter is a constant on each pass, so `y[i]` is written at its place and `a[i]` is the bit itself.
  - **A write is the old value's bits either side of it and the part between, concatenated**, and a slice reads through
    concatenations, slices, sign changes and widenings to the net its bits came from. So a signal written a bit at a
    time keeps nothing of its old value once every bit is written, and a ripple carry written in an `always_comb` reads
    each carry where the pass before made it. A splice cell would have looped every such signal back into itself.
  - **A write at a place worked out as it runs**, `y[sel] = d`, is one `Put` cell, a demultiplexer: the part goes where
    the engine's own `TryOffset` and `Clip` put it, and nothing is written where the place is unknown.
- **What a process drives** -- every process opened first, then what they drive, then the boxes beside them:
  - An `always_comb`, or an `always` on levels, drives its outputs through joins.
  - An `always_ff`, or any `always` on edges, is a `Register` a signal. Its D is the value the body gives it: the
    reset's mux and the enable's, the register's own output fed back. Its pins are the edges it waits on. Its `Clock` is
    the edge the body never tests; one it does test, `if (!rst_n)`, is an asynchronous reset.
  - **Several processes each writing some bits of one signal** -- a generate loop's `q[i] <= d[i]`, two `always_comb` on
    `y[0]` and `y[1]` -- are each a register or a value on their own bits (`RtlCell.Lowest`, `Width`), joined into the
    signal by their places as continuous drivers are.
  - A word of an array written at a constant place is a signal like any, so a pipeline written into an array is a
    register a word.
  - An output left as it was on some path is fed back too, so an `always_comb` that is a latch draws as one.
  - `RtlNetlist.Processes` says of every process what became of it: opened, or a box and why, and what it writes.
- **A function is opened where it is called**, once a call, as a synthesis tool inlines one (`RtlBuilder.Call`). It
  mirrors `FunctionRunner`:
  - the arguments are shaped as its inputs hold them;
  - an automatic function's own signals, or those of a call that comes back in, start afresh;
  - a write lands only in its own signals, and anything a function cannot do ends the call;
  - a recursion whose end the arguments fix is run to it, and folds to a constant.
- **What is no circuit stays a box, with why** (`RtlCell.Why`):
  - an `initial`; a delay, `#d`, which makes a testbench's clock or timer; a wait inside the body;
  - a loop past 200,000 steps, or more than 1,024 paths open at once;
  - a write into a memory at a place worked out as it runs, a signal written both blocking and nonblocking, a string, a
    real, `$random`, `$time`;
  - a recursion more than 32 calls deep with no constant end, and a static function reading one of its own signals
    before writing it, which gives what its last call left;
  - **a process that reads a signal it does not wait on**: `@(a)` reading `b`, or an `@*` calling a function that reads
    one, which `@*` does not wait on (IEEE 1800, 9.2.2.2.1), or a memory read at a place its wait leaves out. The engine
    leaves it as it was when that signal changes alone, which no circuit does, so a circuit drawn there would show what
    the simulation does not do;
  - **a process on levels that reads something it writes before writing it**, `y = t; t = a;`: it sees what its last
    run left there, which no circuit holds. A value kept where a path does not write it -- a latch -- is no such read.

  In a continuous assignment, a call that cannot be opened, or one whose function reads the module's signals, stays an
  `Opaque` box.
- **Nothing is left over** (`RtlBuilder.Prune`): the constants and values a loop or a function folded away are taken
  out, and the ids given again in order.
- **Tests** (`RtlProcessTests`, and the register check in `RtlNetlistTests.HoldToTheEngine`):
  - Every netlist is held to the engine before and after each clock edge. A clock -- a one-bit input a register takes as
    its clock, or one named as one -- moves half a vector after the data, and every register its edge reaches holds the
    D worked out just before it, each register checked at least once. Every signal an opened process writes is driven,
    and no cell is left unused.
  - Each construct is held to its shape: an `if`, a `case`, an unrolled loop, a ripple written a bit at a time, functions
    in an assignment and in a process, recursions with and without a constant end, a static function's memory, a stale
    sensitivity list, a register with an asynchronous reset and an enable, a demultiplexer and a latch.
  - Every process of every exercise solution and corpus design opens, but the initial blocks and the three listed: two
    testbench clocks, and one `@*` whose function reads what it does not wait on.
  - **240 random `always_comb` programs** -- if and else, `case` arms of one label or several with a default or none,
    loops with `break` and `continue`, signed and not -- each opened and held to the engine. With either half of the join
    fix taken out, it fails.
- **The Sonnet review** found the join wrong for a `case` arm of several labels and for a path that left early, a second
  writer of a signal dropped, a word of an array written and never drawn, a memory read not held to the wait, and a read
  of what a process writes before writing it opened as a circuit -- each confirmed against the engine, none in the
  exercises or the corpus, so no test had seen them; all are fixed and held now. Also its findings on structure: a
  `case`'s subject made once a label, the unused constants of unrolled loops, and a join of many paths taking time in
  the cube of them.
- **Where the netlist and the engine still part, and why it is right to:** an `always @*` or `@(a or b)` waits from
  time 0, and inputs set at time 0 land before it waits, so the engine shows its outputs as x until an input first
  changes, while the cells show the value the inputs give -- the circuit, which has no first moment (the course's
  `always_comb` runs once at time 0 for exactly this). A blocking temporary in an `always_ff` is a register, as the
  engine holds it from edge to edge.
- **Left for the drawing (15h-4):** a `case`'s chain of muxes on one subject drawn as one N-input mux; a `Resize` or a
  `Signedness` that changes nothing on screen drawn as a wire; a memory as a box with its ports. The plan's latch box is
  not needed: a mux fed back is the latch.

**As built in Part 15h-3: word-level parts, and every part turned to any side** (`Board/Model/Operators.cs`,
`Board/RtlGates.cs`).

- **`OperatorPart`** is one of the language's operators as a part -- an adder, a comparison, a shifter, a reduction --
  drawn as a box with its symbol. It compiles to one continuous assignment (`assign y = a + b;`), so it computes what the
  language's operator does in this engine and in every simulator.
  - a is `Width` bits, and so is b, but for a shift's amount and a logical operator's operand, each self-determined and
    any width (`BWidth`). y is one bit for a comparison, a logical operator and a reduction, and a's width otherwise: the
    operator's own width (IEEE 1800, table 11-21), so nothing is widened out of sight.
  - A shift's amount comes in on top, as an ALU's control is drawn. `Signed` only where a sign changes the answer -- a
    comparison, `/`, `%` and `>>>` -- and `$signed` in the code there alone, never on a shift's amount.
  - `Operators` is the one table of what each kind is: its token, its operands, its width, whether a sign matters, and
    the engine's own operator of the same name.
  - It is saved (`"operator"`), held to the board's rules -- a gate library refuses it as it does a MUX -- and inspected
    (OPERATOR, WIDTH, AMOUNT or B WIDTH, SIGNED). It is not in PARTS: the drawing of code places it (15h-4).
- **GATES** (`RtlGates.Open`): an operator or a mux of up to four bits opened into the textbook's gates, as a module of
  its own whose ports are the part's pins -- so it compiles and runs as any drawing does.
  - Bitwise operators and reductions are gates; `+` and `-` a ripple of full adders, a half adder where nothing is
    carried in; a comparison a subtractor's carry, signed by inverting both top bits first; `*` the array multiplier; a
    shift a barrel of muxes, any higher amount bit giving every bit the fill; a mux AND-OR on its decoded select.
  - A divider and a remainder, `===` and `!==`, and anything wider than four bits stay boxes, and say why.
  - On 0s and 1s it computes what the box does, every input checked. With an x or a z the gates may know more than the
    operator, which gives x for a whole sum with one unknown bit where a ripple still knows the bits below it.
- **Every part turns to any side** (`PartTurnTests`): placed at each quarter turn, mirrored and not, each pin of every
  kind is on its outline's edge on the side it faces, and a wire from four cells out that way reaches it straight
  without crossing the part.
  - **It found a bug in the router** (a red test, then the fix): `Orthogonal` went round "a cell above both ends" when
    the far end lay behind the start, which comes back over a pin facing up or down -- a mux's select wired from below
    and to the right came down into the mux from inside it. A route that leaves both pins the way they face is kept as
    it was, so no drawing moves; one that does not is tried again round a column or a height, half-way or a cell beyond
    both, the fewest corners and then the shortest (`WireGeometry.Round`).
  - A block turned a quarter names each pin along it, turned as the pin is, ending at a top edge and starting at a
    bottom one; a part with wires leaving its top has its name under it, or beside it when wires leave its foot too.
- **Tests:** an `operators` sample board -- every kind, signed where it matters, every 3-bit input then x and z -- is
  saved, routed, printed, exported and run under Icarus with every other sample (84 signals agree), and held here to
  `LogicVec`'s own operators; every opening into gates runs on every 0/1 input against its operator (195 cases, a broken
  adder caught); shots 61 and 62 show operator parts, two turned, and the full adder with a block turned a quarter.

---

## 11. The parts

Each part is one pull request into `v5`, ends with every suite green, and leaves something usable.

| Part | What it delivers |
| --- | --- |
| 0 | This document, the three empty layers, CI for them, the oracle harness, the privacy scan |
| 1 | 4-state values and every operator |
| 2 | The structural engine: drivers, resolution, inertial rise and fall delays, history, VCD |
| 3 | A walking skeleton: a glitch circuit playing in the new look on the real engine, rewindable |
| 4 | Structural SystemVerilog (tier A), checked against Icarus |
| 5 | Behavioural SystemVerilog and processes (tier B), checked against Icarus |
| 6 | The processor tier (C), and a measured performance budget in the browser |
| 7 | The board document: compile, print, undo, and migration of every 4.x board |
| 8 | The canvas on the real document, still and moving layers drawn apart, and the looks with a Settings switch |
| 9 | The waveform and run controls; the workbench's view-model in a Unity-free layer of its own |
| 10 | Editing on the free canvas -- in four: 10a placement and selection **built** (see "As built in Part 10a"); 10b wiring **built**, drawn (10b-1) and reshaped (10b-2); 10c the inspector **built**; 10d copy, align and the minimap **built**; 10e what its browser smoke test found **built** |
| 11 | The workbench shell, in UI Toolkit -- **built**, in four: 11a the bridges and the toolbar; 11b the docked layout -- its panels (11b-1), the inspector docked (11b-2), the popups in the shell (11b-3); 11c the transcript and the hierarchy; 11d the keyboard map, the status line and the docs |
| 12 | Hierarchy, and written blocks, edited in CodeMirror in a browser -- in six: 12a stepping inside a block **built** (see "As built in Part 12a"); 12b making a block **built** (see "As built in Part 12b"); 12c arrays **built** (see "As built in Part 12c"); 12d code blocks **built** (see "As built in Part 12d"); 12e the code panel **built** (see "As built in Part 12e"); 12f CodeMirror **built** (see "As built in Part 12f") |
| 13 | Export and import (and a levelized path in the engine, only if a measurement asks for it) -- in six: 13a exporting a project **built** (see "As built in Part 13a"); 13b importing one **built** (see "As built in Part 13b"); 13c CONVERT **built** (see "As built in Part 13c"); 13d files **built** (see "As built in Part 13d"); 13e templates **built** (see "As built in Part 13e"); 13f limits, the security review, the measurement and the browser check **built** (see "As built in Part 13f"; the levelized path is not needed). **Part 13 is built.** |
| 14 | The course's tools: path delays, static timing, gate-library mode, step tests, lint, tri-state -- in six, **built**: 14a the timing analysis core, 14b the TIMING tab, 14c step tests, 14d gate-library mode, 14e lint and 14f tri-state buses and inout (see "As built in Part 14a" to "14f"); and 14g, its follow-ups: deleting a module, the code editor's keys, the library held in written code and over budget, and inout pins on written blocks (see "As built in Part 14g") |
| 15 | A simulator first, the Exercises and the tutorial -- in six: 15a **built** in two (see "As built in Part 15a"): the student's circuits, and opening on the last thing or a start page, the app's menus for BACK, the 4.x game in Settings, music off; 15b the Exercise model, grading and self-checking testbenches **built** (see "As built in Part 15b"); 15c the 35 exercises **built** (see "As built in Part 15c"); 15d Exercises on the workbench **built** (see "As built in Part 15d"); 15e answering in code **built** (see "As built in Part 15e"); 15f first-time hints, the guided tutorial and the browser check **built** (see "As built in Part 15f"). **Part 15 is built.** Then 15g, what the first playtest asked for: 15g-1 the quick fixes **built** (see "As built in Part 15g-1"); 15g-2 Settings and the credits as pages of the workbench **built** (see "As built in Part 15g-2"); 15g-3 a lead on every pin **built** (see "As built in Part 15g-3"); 15g-4 INPUTS, sequences without code **built** (see "As built in Part 15g-4"); 15g-5 LOGIC **built** (see "As built in Part 15g-5"); 15g-6 STATES, the state diagram, **built** (see "As built in Part 15g-6"); 15g-7 big circuits stay smooth **built** (see "As built in Part 15g-7"). And 15h, a drawing of any code: 15h-1 the netlist of a module's code **built** (see "As built in Part 15h-1"); 15h-2 processes as muxes and registers **built** (see "As built in Part 15h-2"); 15h-3 word-level parts and every part turned to any side **built** (see "As built in Part 15h-3"). |
| 16 | The old game retired, and a smaller download; 5.0.0 released |

**Agreed after Part 9, 2026-10-19: the language, before Part 10.** A course audit ran every one of the
course's own projects -- the four homework designs and their testbenches -- through BitSorter and Icarus,
and held the result against the waveforms in the course's own ModelSim screenshots, read from its
reports: everything ran, and every edge and end time that could be read agreed with ModelSim, where
Icarus ends the second exercise's two runs early (`Docs/oracle-notes.md`, "A busy given a default and then
set"). Eighteen designs written from scratch, one per thing the course's workshops teach, matched Icarus
but for three gaps; thirty-two more probed the SystemVerilog beyond the course. Ahmad asked for the
course first, then general SystemVerilog, before Part 10 -- and much of the rest of the language after
5.0.0, if ever. Each is one pull request, like a part:

| Part | What it delivers |
| --- | --- |
| L1 | The course's gaps: 2-state types (`int`, `bit`, an enum on no base) start at 0 and hold no x; `X ~& Y` and `X ~\| Y` read with a warning; a top input nothing drives reads z; time counted in ps by default, as the course's ModelSim counts it |
| L2 | Functions and tasks -- **built**, see "As built, L2" below |
| L3 | Expressions: `inside` and `case inside`, casts, enum methods, `$countones` and its kin, `$random` -- **built**, see "As built, L3" below |
| L4 | Data types: packed structs and unions, packed and unpacked arrays of more than one dimension, `foreach` -- **built**, see "As built, L4" below |
| L5 | Testbench control: `fork`/`join`, named events and `wait`, `disable`, `force`/`release`, immediate assertions -- **built**, see "As built, L5" below |
| L6 | Packages and `import`, macros with arguments, strings and `$sformatf`, writing files -- **built**, see "As built, L6" below |
| L7 | `real` numbers, and delays in fractions of a unit -- **built**, see "As built, L7" below |
| L8 | Interfaces and modports -- **built**, see "As built, L8" below |

Classes, constraints and random stimulus, concurrent assertions, coverage and DPI wait until after
5.0.0. What L2 to L8 add runs in the engine and is written in code; it does not become a part on the
canvas, and Part 12's written blocks are where it is edited.

**As built, L2: functions and tasks** (`Function.cs`, `Elaborator.Subroutines.cs`). Two ways in, because
two kinds of call need them:

- **A function called inside an expression** -- an `assign`, a condition, an `always_comb` -- is compiled
  once into a `FunctionInfo` and run by a node of the expression (`Expr.Call`) where it is evaluated, all at
  once, as the standard has a function (IEEE 1800, 13.4). It may compute, branch, loop and call functions,
  and call itself when it is automatic; it may write only its own signals, and it may not wait, make a
  nonblocking write or print -- each refused with why (`SV0323`).
- **A task, or a function called as a statement**, has its statements written into the calling process
  where the call stands, between its arguments copied in and its outputs copied back (13.3). So a task's
  time is its caller's: it may wait, print and write the design's signals, and a `void` function may print.
  A task that calls itself is refused (`SV0309`): written in place it would never end.
- **Its own signals are the design's own working variables**, made once, the first time a call needs them,
  and shared by every call: static storage, the standard's default, with an automatic function or task
  starting each call from its locals' starting values. A function called again while it is running --
  recursion -- keeps the running call's signals aside and puts them back after, so each call has its own;
  only such a call copies anything. The calls under way hold at most a stack's worth of frames between them, each
  costing its body's deepest expression, and at most a hundred calls, across every function; the call past either gives
  x, since each runs deeper on the machine's own stack (Part 13f; see "As built in Part 13f").
- **What a call waits on is its arguments.** A continuous assignment and `@*` are evaluated again when an
  argument changes, never when something the function's statements read does; an `always_comb` waits on
  those as well (9.2.2.2.1), transitively through the functions it calls. Icarus agrees on all three
  (`corpus/behavioural/function_sensitivity.sv`), and `corpus/behavioural/subroutines.sv` -- a function in
  an `assign` and an `always_comb`, one with a loop and locals, the old style of argument, a default, a
  recursive one, a static one that keeps a local, a task that waits and gives values back, a `void` one
  that prints -- agrees with Icarus on every signal and every line.
- **Not yet:** a function in a constant expression (a parameter's value), arrays inside a function or a
  task, output arguments of a function called inside an expression, a task called through the hierarchy
  (`dut.task()`), and `disable` of a task (L5).

**As built, L3: expressions.**

- **`v inside {1, 3, [6:9]}`** (IEEE 1800, 11.4.13) binds as the relational operators do; the value and
  every member are sized together as an equality's operands are, a value matched by `==?` and a range by
  `>=` and `<=`, giving 1 if a member matches, x if none does but one might, else 0. **`case (v) inside`**
  takes its labels as set members, ranges included, the first that matches winning (12.5.4). Icarus 12
  reads neither, so the standard alone decides them (`ExpressionTests`).
- **`==?` and `!=?`** (11.4.6): an x or z on the right matches anything. `OperatorOracleTests` holds them
  to Icarus over its random 4-state cases.
- **Casts** to `int`, `shortint`, `longint`, `byte`, `bit`, `integer`, `logic`, `time` and to a typedef
  take the type's width and signedness, and a 2-state type holds x and z as 0: `int'(4'b1x01)` is 9.
  `signed'` and `unsigned'`, which Icarus 12 cannot read, read the same bits another way.
- **An enum's methods** (6.19.5): `first()`, `last()`, `num()`, `next(n)` and `prev(n)` round the ends,
  a value that is no member giving the type's default (x, or 0 for a 2-state enum), and `name()` -- the
  member's name as a string, empty for no member -- on a signal of an enum type, a cast to one, or another
  method (`s.next().name()`). `%s` writes a string exactly where it writes a vector's leading zero bytes as
  spaces (`PrintSegment.IsString`), as Icarus does both.
- **`$countones`, `$onehot`, `$onehot0`, `$isunknown`** (20.9), x and z never counted as 1.
- **`$random(seed)` is the standard's generator** -- the C of IEEE 1364's annex, `rtl_dist_uniform`, which
  1800 keeps (20.15.2) so a seed gives the same numbers in every simulator -- and gives Icarus's numbers
  for every seed tried; without a seed it uses the design's own, from 0. `$urandom` and `$urandom_range`
  are left to each simulator by the standard: BitSorter's `$urandom` is `$random`'s bits read unsigned,
  `$urandom_range` the standard's uniform draw, on seeds of their own.
- **A random call has no side effect of its own.** The process compiler writes the number into a working
  variable and the seed moved on, both worked out from the seed as it was, just before the statement that
  asks for it (`ExpressionBuilder.Random`), so the seed's new value is recorded like any write and the
  expression evaluates purely. So such a call belongs in a process statement only, and is refused in a
  continuous assignment or a constant.
- **Found on the way, and fixed:** `%s` of a vector wider than its text dropped the leading zero bytes
  where Icarus prints spaces (`oracle-notes.md`, printing). And `OperatorOracleTests` met, for the first
  time, an Icarus departure from the standard: z kept where an ambiguous `?:` should give x
  (`oracle-notes.md`).

**As built, L4: data types** (`PackedType.cs`, `ExpressionBuilder`'s selects and patterns).

- **Every packed value is still one vector in the engine.** What changed is the elaborator's map of it:
  a `PackedType` says what a signal's bits are -- a vector, a packed array of elements (`logic [3:0][7:0]`
  is four bytes, the first range the outermost), or a struct or a union of members (IEEE 1800, 7.2.1,
  7.3.1, 7.4.1) -- and every select and member access walks it to find its bits and the type of what it
  found. A struct's first member is its top bits; a union's members all start at bit 0 and must be the
  same width. A waveform shows a struct or a packed array as the flat vector, `[Width-1:0]`.
- **`pkt.kind` reads as a hierarchical name does**, so a dotted name is looked up through the hierarchy
  first and, where that leads nowhere, as members of the signal its first parts name -- here or through
  an instance, `dut.pkt.kind`. After an index, `prog[i].op`, it is a member of the element.
- **An element is unsigned unless its own type is signed** (7.4.1): `logic signed [3:0][7:0]` reads
  signed as a whole and each byte unsigned; a packed array of a signed typedef has signed elements.
- **An index known only as the run goes** is the engine's `Expr.Part` for an element, x where it falls
  outside; written, an index outside its own element writes nothing, rather than the bits of the element
  beside it. A variable part-select now works on a vector declared low to high too.
- **A memory of several dimensions** (`grid [0:3][0:7]`) is one memory of every word, row by row, each
  word named as written (`grid[1][2]`); a word picked outside any dimension reads x and is not written.
  `$readmemh` into one is refused for now.
- **`foreach`** (12.7.3) is a loop per dimension named, a memory's unpacked dimensions first and then
  its words' packed ones, each from its left bound to its right as declared -- `[3:0]` counts 3 down to
  0 -- with its own `int`; an empty place, `grid[, c]`, walks no loop. `break` and `continue` belong to
  the innermost loop.
- **An assignment pattern** (10.9) is laid out on what it is assigned to -- a procedural assignment, an
  `assign`, a declaration's value, a parameter, an argument or a `return`: a struct's members or a packed
  array's elements all in order, or by name or index with `default:` for the rest, each sized as an
  assignment to its member is and itself perhaps a pattern. A pattern anywhere else, or for a union, is
  refused with why.
- **Icarus 12 parts from the standard four times here** (`oracle-notes.md`, "Data types"), so
  `corpus/behavioural/data_types.sv` holds what both agree on and `DataTypeTests` the rest by the standard.
- **Not yet:** unpacked structs and unions, unpacked arrays as ports, a slice of a memory, `$readmemh`
  into a memory of several dimensions, `$size`, `$left`, `$right`, `$low` and `$high`, type keys in a
  pattern (`'{int: 0}`), and a waveform that lists a struct's members.

**As built, L5: testbench control** (`Simulator`'s fork, disable and force; `Elaborator.Processes`).

- **A fork's branches are processes of their own** (IEEE 1800, 9.3.2), compiled beside the one that forks
  them and asleep until the fork runs (`ProcessInfo.Dormant`). The fork starts them straight after its
  process in the time step, in the order written, then waits as its join says: for every branch, the
  first to end, or none. `wait fork` waits for every branch the process has forked; `disable fork` ends
  them, and whatever they forked in turn. **One of each branch runs at a time**: a branch forked again
  while it still runs -- a `join_any` in a loop with no `disable fork` -- stops the run with how to wait
  for it or end it, where the standard would start a second copy. Every testbench pattern seen so far
  waits or ends its branches first.
- **`disable`** of a named block, a named fork or a task (9.6.2) sends every process inside one of its
  places on past it -- the disabling process at once, any other in the next delta -- and ends the forks in
  it. A block may be in another process, and a task is written into every caller, so what a `disable`
  reaches is resolved once every process is compiled. A process's resumption carries a generation, so a
  delay it was disabled out of is dropped when it comes due.
- **Named events** (15.5): `event e;` is a bit the design keeps, which every `-> e` turns over -- so `@(e)`
  wakes on a trigger as on any change, with no new kind of wait -- and `->> e` turns over in the NBA
  region. `e.triggered` is 1 from a trigger to the end of its time step: the time of the last trigger is
  kept beside the bit. An event is not a value, and reading one anywhere but an event control is refused.
  Neither bit is recorded: a trigger is a moment, not a value, and a waveform shows it through what it
  wakes.
- **`force` and `release`** (10.6.2) lay a value over a wire, bits of one or a whole variable, above every
  driver and every write, and the forced value follows its expression as a continuous assignment does.
  Released, a wire goes back to its drivers and a variable nothing drives keeps the forced value until it
  is next written.
- **Immediate assertions** (16.3) are an `if`: the pass action on 1, the else on 0, x or z, or an `$error`
  naming the assertion and its line. `assume` runs as `assert`; `cover` has no else. Concurrent and
  deferred assertions are refused as they are read.
- **Icarus 12 parts from the standard, or cannot run it, five times here** (`oracle-notes.md`, "Testbench
  control"), so `corpus/behavioural/testbench_control.sv` holds what both agree on and
  `TestbenchControlTests` the rest. **Found on the way, and fixed:** a task with a named block could be
  called only once (L2): the second call declared the block again.
- **Not yet:** a second copy of a running branch, `disable` inside a function called in an expression,
  events in a block or in an array, events passed through ports, and concurrent and deferred assertions.

**As built, L6: packages, macros, strings and files.**

- **A package is a scope of its own** (IEEE 1800, 26): its parameters, typedefs -- an enum's names among
  them -- functions and tasks, declared the first time anything imports from it or names into it, seeing
  what it imports itself and not the compilation unit. `import p::*;` makes p's names visible after a
  scope's own, so a local name wins; `import p::name;` declares that one name; either may stand in a
  module's header, among its items, or outside every module. `p::name` reaches a name p declares -- not
  one p only imports, as the standard has it and Icarus agrees. A package holding a variable or a wire
  is refused.
- **Macros take arguments** (22.5.1). The preprocessor works on tokens, so a use's arguments are read
  past its line, split at the commas outside brackets, put in, and the result expanded again; a missing
  argument takes its default, and ``` `` ``` and `` `"...`" `` work as the standard has them.
- **A string is a value of 256 characters** (6.16; `Strings`): 2-state, its characters at the bottom and
  zero bytes above -- the layout a string literal has already, so a literal given to a string, or compared
  with one, needs nothing more. Joining with `{}` and comparing are done as text when either side is a
  string; a value given to a string drops its zero bytes; a longer string keeps its first 256 characters.
  Its methods are all there: `len`, `getc` and `s[i]`, `putc`, `toupper`, `tolower`, `substr`, `compare`,
  `icompare`, `atoi`, `atohex`, `atooct`, `atobin`, and as statements `itoa`, `hextoa`, `octtoa`, `bintoa`.
  `$sformatf` gives the line `$display` would print, as a string, and `$sformat` and `$swrite` write it into
  a variable; `%s` and a bare `$display(s)` print a string's text. A signal knows it is a string
  (`SignalInfo.IsString`), and Icarus leaves one out of its dump, so the corpus compares what is printed
  of it.
- **Files are written, not read** (21.3). `$fopen(name, "w")` or `"a"` gives a descriptor with the top bit
  set, and `$fopen(name)` one bit of a multichannel descriptor, bit 0 being the transcript -- the lowest
  free one each time, as Icarus gives them. `$fdisplay`, `$fwrite` and `$fstrobe` print to it; descriptor 1
  and 2 are the transcript. What is written is handed to the run's files (`ISimFiles`) on `$fclose` and
  `$fflush` and whenever a run hands back, never a line at a time. `$fopen` in an expression is run just
  before its statement, as `$random` is. The corpus test now compares every file a testbench writes with
  Icarus's, word for word.
- **Icarus 12 cannot run four of these** (`oracle-notes.md`, "Packages, macros, strings and files"), so
  `corpus/behavioural/library.sv` holds what both agree on and `LibraryTests` the rest.
- **Not yet:** reading files (`$fgets`, `$fscanf`, `$fread`), `$fmonitor`, `export`, a string longer than
  256 characters, a string's own radix in the waveform, and a dynamic format (`$sformatf(fmt, ...)` with
  the format in a variable).

**As built, L7: reals, and time in fractions of a unit** (`Reals`, `Expr`'s real nodes, `Scope.UnitTicks`).

- **A real is a value of 64 bits, a double's** (IEEE 1800, 6.12; `PackedType.Real`), 2-state, moved by the
  engine as bits like any value and made a number by the expression nodes in `Reals.cs`. An expression is
  real when any operand of an arithmetic operator is (11.3.1): a whole number beside a real is made one,
  `+ - * / **` are done in doubles, a comparison compares numbers, and a real where a whole number is
  wanted -- an assignment, an index, an argument -- is rounded half away from zero, as `$rtoi` cuts toward
  zero instead. A real as a condition is true when it is not 0, so -0.0 is false. Every whole-number
  operator -- `~`, `%`, the bitwise ones, the shifts -- refuses a real.
- **Real parameters** take their type, `parameter real T = 0.5`, or their value's, `parameter T = 0.5`;
  a function may take and give reals, and a port may be one: a value given to a port is given as an
  assignment would give it, so a real stays a real. A `shortreal` is held as a `real`, its 64 bits rather
  than the standard's 32, the one place this departs from the standard on purpose.
- **Printing**: `%f`, `%e` and `%g` with a width and a precision write one as C's printf does; a real with
  no format is written as `%#g` (`2.50000`), and `%d` of one rounds it with no padding -- both as Icarus
  does. A VCD writes a real signal as a real (`r2.5`), which GTKWave draws as a number; a signal knows it
  is one (`SignalInfo.IsReal`).
- **Time in fractions of a unit.** The reader gives every module the `` `timescale `` written before it.
  A tick is the finest unit or precision any of them names, each module counts in its own (`Scope.UnitTicks`,
  `PrecisionTicks`), and a delay -- a process's, a gate's, an assign's -- is scaled to ticks and rounded
  to the module's precision (22.7). `$time` gives the module's units, rounded; `$realtime` the fraction;
  `%t` writes ticks, as the default `$timeformat` has it. A module with no `` `timescale `` beside ones
  that have one counts in the first one written; with none anywhere, a tick is a unit, as it always was,
  and a fraction rounds to a whole one -- so the course's files and every project are untouched.
- **Icarus agrees on all of it**: `corpus/behavioural/reals.sv`, two modules of different timescales
  among it, matches on every signal and line. `RealTests` holds BitSorter's own choices.
- **Not yet:** `$timeformat` and `$printtimescale` (still skipped with a warning), a `shortreal` of 32
  bits, a real shown as a number in the waveform (it shows its bits), `real` nets with more than one
  driver, and `timeunit`/`timeprecision` declarations, which are read and skipped.

**As built, L8: interfaces and modports** (`ModuleDeclaration.IsInterface`, `InstanceEntry.Interface`, `ModportInfo`).

- **An interface is read as a module is** and kept apart from the modules (`SyntaxTree.Interfaces`), so the top
  is never one and a board never draws one. An instance of it is made as a module's is -- its parameters, its
  ports, its signals, assigns, processes, functions and tasks -- under its instance's name, so `hs.data` is
  `top.hs.data` in the waveform and in a VCD, and anything reaches it by name as it reaches a module's.
- **An interface port is the instance it is given** (IEEE 1800, 25.3): the port's name stands for that instance
  inside the module, so `bus.data` is a name through the hierarchy like any other, read and written, in processes
  and assigns, and `bus.send(v)` and `bus.fire()` call the instance's task and function. What each port is given
  is worked out before the module is made -- by name, by `.bus`, by `.*`, or in order -- and must be an instance of
  that interface (or of any, for `interface bus`), here, through the hierarchy, or the module's own port passed on.
  The interfaces in a scope are made before its modules, so the order they are written in does not matter.
- **A modport limits what a port reaches** (25.5): only the names it lists, an `input` read and not written --
  in a process, an assign, or an output's connection -- an `output` or `inout` both, `ref` read as `inout`, and
  only the tasks and functions it imports may be called. A port names one, `bus_if.sink bus`, or a connection
  does, `.bus(hs.sink)`; both may, if they agree. A port passed on keeps its modport: it can be named again and
  not changed. Each modport is checked against what the interface declares, once an interface.
- **`bus_if bus` reads as a port of a typedef's type would**, so the reader takes a bare name with no direction
  written before it, that is no type it has met, for an interface's; the elaborator finds a typedef of that name
  if there is one and makes it the port it would have been, its direction the last one written. Both print alike.
- **Icarus 12 reads interfaces but no interface port, and no modport `import`**, so interface ports are held to
  it by `InterfaceOracleTests`: one design written with them and again with every `bus.x` spelled out as
  `top.hs.x`, BitSorter running both and Icarus the second, every line and every shared signal the same.
  `corpus/behavioural/interfaces.sv` is the rest of it, run in both. `InterfaceTests` holds every refusal.
- **Not yet:** arrays of interface instances and of interface ports, virtual interfaces (they need classes),
  modport expressions (`.name(expr)`), `export` and clocking in a modport, an interface declared inside a module,
  and drawing a module that uses one on a board, which comes in as written code (Part 12).

**Agreed after the plan, 2026-10-14: five optimizations**, each to be written into this document by
the part that builds it, as every part rewrites what it makes stale.

| # | What | Part |
| --- | --- | --- |
| 1 | The workbench's view-model -- its layout, routes, the run read at a moment, pan and zoom, and later the editing gestures and the shell's state -- in a layer of its own, as pure as the three below it, so `dotnet test` and CI hold it without the editor | 9 to 11; the layer and the move built in 9a |
| 2 | Still and moving drawn apart, in chunks under uGUI's vertex limit, the waveform revealed by a mask (section 10) | 8, built |
| 3 | A levelized path for logic with no delay at all, only if a design measured in the browser goes over Part 6's budget and the time is in delta cycles; the same settled history as the event engine, held to it by a differential test | whichever measurement asks; most likely 13 |
| 4 | The shell in UI Toolkit, the canvas staying uGUI; code edited in a CodeMirror editor laid over the canvas in the browser, bundled with the build, and in a TextMesh Pro field on the desktop | 11, 12 |
| 5 | A smaller download: what goes with the 4.x game, and the build's settings, each measured before and after; the 4.0.0 build is 16,931,273 bytes | 16, settings begun early |

**The build's settings went first** (2026-10-15, from the web, audio and TextMesh Pro audits after
Part 8). The download went from 17,208,015 bytes to 14,402,572:

| Change | Bytes saved |
| --- | --- |
| The fonts' atlases not readable, and TextMesh Pro's fallback and emoji out of `Resources` | 312,733 |
| No Unity splash screen | 91,020 |
| The menu's recordings at 96 kbps in the browser build | 1,215,108 |
| Managed stripping at Medium | 924,404 |
| WebAssembly 2023 | 268,496 |

Each change that alters how the code runs was smoke-tested in a browser build first. Doing so found
the workbench blank in the browser since Part 8, which `5701303` fixed, for 6,318 bytes back.

**The rest waits for Part 16, as one pass over everything**, with the 4.x game gone and every byte
it leaves measured again (Ahmad's choice, 2026-10-15). The largest single item known now: the
menu's two recordings are about 3.6 MB of the 6.7 MB data file, all of it downloaded before the
first frame. They could come after start-up instead -- an asset bundle built with their own import
settings, so their loudness figures still hold -- with the menu silent until one arrives. A
browser plays nothing before the first click anyway. The cost is a loader and a little engine
code. The analytics package's code stays: it is what reports the one measurement the game
collects, and removing it would break what players were told.
