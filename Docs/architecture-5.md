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
 BitSorter.Hdl     the language: 4-state values, lexer, preprocessor, parser, syntax tree, printer

 BitSorter.LogicCore   the 4.x token engine: frozen, deleted when 5.0 is finished
```

- **`Hdl`, `Sim` and `Board` are pure C# 9** — no engine references, no reflection, no threads, no file
  access of their own (files go through interfaces the host provides). They have to run unchanged under
  the plain .NET SDK, Unity's IL2CPP and a single-threaded browser.
- **The layers point one way**: `Hdl ← Sim ← Board ← View`. The view never changes the engine or the
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

- **Decision.** New assemblies `Hdl`, `Sim` and `Board` are built alongside `LogicCore`, which is frozen
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

### The language (`Hdl`)

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

### The document (`Board`)

```
Project     name, modules, the top, the testbench, settings (time unit; default gate delay 1)
Module      name, drawn or written, ports
              drawn:   parts, connections, layout
              written: SystemVerilog source, parsed by Hdl
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
| `#(rise, fall)` on a gate | Chosen by the output's **new** value: to 1 takes rise, to 0 takes fall, to `x` the smaller, to `z` the turn-off delay (the smallest). A single `#d` applies to every transition; none means 0. |
| Inertial | A new evaluation cancels the gate's pending output event, and schedules a new one only if the new value differs from the output's current value. A pulse shorter than the delay is swallowed, and the history records it as swallowed. |
| `assign #d` | The same rule as a gate. With no delay, the update is in the Active region. |
| `#d` in procedural code | A plain wait. |
| Two drivers on one net | The standard resolution: equal values stay, `0` against `1` is `x`, `z` gives way to anything, `x` wins. |
| Zero delay | Delta cycles; past a delta limit in one time step the run stops with a diagnostic naming the loop. |
| Library cells | Rise and fall default to 1, as the course's cells do. A freshly placed gate is a cell with delay 1; "ideal" (0) is one switch. |
| Order inside a region | Stable, by creation, the same on every run. Where Icarus and the standard disagree, the standard wins, and the difference is written down in `Docs/oracle-notes.md`. |

**Pinned so far** (`Tools/ci/Oracle`): an XOR with rise 2 and fall 3, inputs at 0 from time 0, reads
`x` until time 3 (a fall), rises at 12 when an input rises at 10, falls at 23 when it falls at 20.

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
port per line; `logic` everywhere; vectors MSB first; an ideal gate as a primitive (`and(z, a, b);`), a
gate with delay as a library cell (`NAND2 #(.Tpdlh(5), .Tpdhl(5)) g1 (.A(a), .B(b), .Z(n0));`) with
BitSorter's **own** cell files beside it; named association; one module per file; instances `g1, g2 …`;
`always_comb` with defaults first; `always_ff` with an asynchronous reset; enums for states; testbenches
with `initial` steps or `forever #half clk = ~clk;`.

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
   — whatever order the design was built in. Tested by shuffling the order.
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
