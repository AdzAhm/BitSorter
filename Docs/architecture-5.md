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
| D7 | One time unit per tick. The display unit is a setting (ns by default). Export adds no `` `timescale `` unless asked. |
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
      |  reads, and edits through commands
 BitSorter.Board   the document: modules, parts, wires, layout, stimulus, editing commands, undo,
      |            import and export, migration of older saves, the course's analyses
 BitSorter.Sim     the engine: elaboration, scheduler, processes, system tasks, history, VCD
      |
 BitSorter.SystemVerilog   the language itself: its 4-state values, lexer, preprocessor, parser,
                           syntax tree and printer

 BitSorter.LogicCore   the 4.x token engine: frozen, deleted when 5.0 is finished
```

- **`SystemVerilog`, `Sim` and `Board` are pure C# 9** — no engine references, no reflection, no threads, no file
  access of their own (files go through interfaces the host provides). They have to run unchanged under
  the plain .NET SDK, Unity's IL2CPP and a single-threaded browser.
- **The layers point one way**: `SystemVerilog ← Sim ← Board ← View`. The view never changes the engine or the
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
  Choosing a look in Settings stores it and reloads the scene behind a fade.
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
  with the way out. Every signal starts at x, an undriven net at z, and a net driven in slices at x where a
  driver reaches and z where none does -- as Icarus shows it. Every driver evaluates once at time 0.
- **Running.** `RunTo` runs whole time steps until a time or an event budget, so the browser can run in
  slices; a delta limit stops a zero-delay loop with a message naming what was still changing. Testbench
  writes are `Stimulus` until procedural code arrives (Part 5).
- **Recorded.** Every value change with its delta, every transition with its outcome; rewind and step
  back read them. Once its arrays have grown, a run with recording off allocates nothing (a test holds
  it), and recording grows its buffers by doubling. On the development machine a 64-bit ripple adder of
  320 gates ran at about 1.4 million events a second.
- **Checked.** The probe circuits' waveforms by hand and against Icarus's VCD, signal by signal
  (`Docs/oracle-notes.md`); wire resolution and every primitive by table; replay, slicing and VCD
  round trips; the zero-delay loop; allocation and throughput floors (`Tools/ci/Sim.Tests`).

### The document (`Board`)

```
Project     name, modules, the top, the testbench, settings (time unit; default gate delay 1)
Module      name, drawn or written, ports
              drawn:   parts, connections, layout
              written: SystemVerilog source, parsed by the SystemVerilog layer
Port        name, direction, width, comment
Part        id, name (g1, g2 ...), kind, parameters, terminals
              gates (and, or, nand, nor, xor, xnor, not, buf; tri-state later), constant, clock, reset,
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
| Syllabus extras | tri-state (`bufif`, `notif`, `tri`), `integer`, `$time`, `` `timescale `` read as a display hint, `**` | 14 and later |
| Not supported | `function`, `task`, interfaces, structs, classes, `fork`, `defparam`, DPI — each refused with file, line and a plain message |

**Written in the course's style:** an ANSI header with inputs first and outputs last, one commented
port per line; `logic` everywhere, except a net with more than one driver, which must be a `wire` to
compile at all (section 6); vectors MSB first; an ideal gate as a primitive (`and(z, a, b);`), a
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
  beside the including file and then as written, with cycles and depth caught; `` `define `` without
  arguments, `` `ifdef `` / `` `ifndef `` / `` `elsif `` / `` `else `` / `` `endif `` (so include guards
  work), `` `undef ``, and `` `timescale `` noted. A macro with arguments is refused with the way out.
- **Syntax tree** (`SyntaxTree.cs`): modules with ANSI or non-ANSI ports, header and body parameters,
  declarations of any keyword type, net type or typedef name, enums, generate regions, for- and
  if-generate, primitives with delays, module instances by name or position, continuous assigns,
  `initial` / `always` / `always_comb` / `always_ff` / `always_latch`, functions and tasks, and every
  statement the workshops use and the common general ones (`for`, `while`, `do`, `forever`, `repeat`,
  `wait`, `case` / `casez` / `casex` with `unique` and `priority`, delay and event controls, system task
  calls). Every node knows its file, line and column.
- **Parser** (`Parser`, recursive descent): the course's operator table, plus `**` and the wildcard
  equalities. A name followed by a name is a declaration of a typedef's type or an instance, told apart by
  what follows. It never throws and never hangs: after an error it skips to a `;` or a keyword that starts
  something; every loop makes progress; nesting deeper than 200 stops the read with a message rather than
  the browser's stack. A missing `;` is reported on the line it is missing from. Classes, interfaces,
  packages, assertions, `defparam`, `fork`, drive strengths and the switch primitives are refused where
  they stand, their text kept.
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
- **Refused, with the part it arrives in** (`SV0306`): processes, functions and tasks (5), arrays and
  memories (6), `inout` (14), a variable with a starting value (5), packed arrays of more than one
  dimension, net types other than `wire`, `tri`, `uwire` and the two supplies. Simulating a design
  without them would be simulating something else, so they are errors, never warnings.
- **Diagnostics** `SV0301` to `SV0318`, each at the item's file, line and column: no top, unknown module
  or name, not a constant, a width mismatch at a port (a warning), a bad port, parameter, select, delay or
  left side, two drivers on a variable's bit (with the way out: a wire), recursion, an implicit net and an
  unconnected input (warnings), a duplicate, an unsized number in `{ }`, and a design too large. A read
  that failed is not elaborated, so the read's own errors stand alone.
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
  buffer of delay d − 1 on its route. Registers become flip-flops on one clock part whose period is the
  old `clockPeriod`; blocks become modules.
- **What migration promises is every output's settled value after each vector, not the exact tick.**
  A board that only worked by collision or stalling has no meaning in the new model; it still loads,
  and shows what it now does. Every shipped level's own circuit is migrated and checked.

---

## 9. Invariants

These take the place of 4.x's core decisions as each part lands.

1. **Determinism.** The same design and stimulus give the same history — value changes and transitions
   — whatever order the design was built in. The engine gets it from its two phases a delta (section 5),
   and a test holds it by building random circuits in shuffled orders.
2. **The engine never throws on a design.** A broken design is a diagnostic; a runaway one ends with a
   message (delta limit, event budget, end time). Nothing hangs a browser tab.
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
  of playback makes no garbage. The circuit and where it is drawn are `ReferenceCircuit`, the stand-in
  Part 8 replaces with the board document and its layout.
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
| 8 | The canvas on the real document, and the looks with a Settings switch |
| 9 | The waveform and run controls |
| 10 | Editing on the free canvas |
| 11 | The workbench shell: toolbar, panels, transcript |
| 12 | Hierarchy, and written blocks |
| 13 | Export and import |
| 14 | The course's tools: path delays, static timing, gate-library mode, step tests, lint, tri-state |
| 15 | The Exercises and the tutorial |
| 16 | The old game retired; 5.0.0 released |
