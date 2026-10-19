# Oracle notes

What Icarus Verilog does, checked by running it, and where it parts from the SystemVerilog standard.
BitSorter 5.0's engine is checked against Icarus (`Tools/ci/Oracle`, the `oracle-compare` skill),
but **the standard wins**: where the two disagree, BitSorter follows IEEE 1800 and the difference is
written down here, so an oracle test never quietly teaches the engine Icarus's quirk.

Icarus 12.0: the Windows build on the development machine (`C:\iverilog\bin`) and Ubuntu's
`12.0-2build2` in CI. Both agree on everything below.

---

## Gate and assignment delays (probed 2026-10-12, Part 2)

**Icarus is inertial, exactly as the standard says**, for gate primitives and for `assign #d` alike.
Every probe below is a block of `Tools/ci/Oracle/corpus/delays/probes.sv` and a circuit of
`Tools/ci/Sim.Tests/Probes.cs`, and `EngineOracleTests` compares their two VCDs signal by signal.

| Probe | Circuit | Icarus, and the standard | What it pins down |
| --- | --- | --- | --- |
| P1 | a 1-unit pulse into `and #3` | output never moves | a pulse shorter than the delay is swallowed |
| P2 | a 3-unit pulse into `buf #3` | 1 at 13, 0 at 16 | a pulse as long as the delay passes |
| P3 | `buf #(5, 1)`, 2-unit high pulse | output never moves | the rise is cancelled by the fall |
| P4 | `buf #(1, 5)`, 2-unit low pulse | output never moves | the fall is cancelled by the rise |
| P5 | `buf #(2, 5)`, input to x | x at 12 | going to x takes the smallest delay |
| P6 | `buf #4`, 0 to 1 then x a unit later | x at 15 | a pending change is replaced, and the new one takes its own delay from then |
| P7 | `bufif1 #(2, 3, 4)`, enable falls at 10 | z at 14 | going to z takes the turn-off delay |
| P8 | `assign #3 y = a & b` with P1's pulse | output never moves | continuous assignments are inertial too |
| P9 | `y = a & ~a`, NOT 3, AND 2, a rises at 10 | 1 at 12, 0 at 15 | the reference visual's glitch, to the unit |
| P10 | the same with NOT 1 | output never moves | a glitch shorter than the gate after it is swallowed |
| P11 | `xor #(2, 3)` driving a `logic`, inputs set at 1 | x, then 0 at 4 | a gate output starts at x; the first fall takes the fall delay |

These are also the engine's hand-computed waveform tests (`WaveformTests`), so the expectation is
written down twice: once as Icarus printed it, once as the standard predicts it, and they agree.

## Where Icarus departs from the standard

### Which delay a vector assignment takes

```systemverilog
assign #(2, 5, 7) y = a;   // y and a are 4 bits
```

The standard (IEEE 1800, 28.16) decides by the whole value: from nonzero to all zeros takes the fall
delay, to all z the turn-off delay, anything else the rise delay. Icarus takes **the longest delay of
the bits that change**, each bit's by its own change:

| Change | Standard | Icarus |
| --- | --- | --- |
| `0101` → `0100` | 2 (rise: nonzero to nonzero) | 5 (the one bit falls) |
| `zzzz` → `1z00` | 2 (rise) | 5 (one bit rises, two fall) |
| `0110` → `0000`, `0000` → `0101`, `0x00` → `zzzz` | 5, 2, 7 | the same |

This note once said Icarus decided "by something like the lowest bit". Part 7a's printed buses, with
`assign #(2, 1)` on a four-bit XOR, ruled that out: `1101` → `0101` took 1 and `1011` → `0101` took 2,
the lowest bit unchanged in both. The longest-changing-bit rule fits those and every row above.

BitSorter follows the standard (`Delay.For`, `WaveformTests.AVectorAssignment_...`). The oracle corpus
keeps out of this case, and so do the sample boards the oracle runs. **It barely reaches students**: the
course puts delays only on one-bit cells, and a board warns when a gate wider than a bit is given
different rise and fall delays, since its export would then run differently elsewhere.

## Two drivers

| Declaration | Two continuous drivers, 0 against 1 |
| --- | --- |
| `wire y;` | x, and z against 1 gives 1: the standard's resolution |
| `logic y;` | **an error**: "Unresolved net/uwire y cannot have multiple drivers" |

The standard agrees: a variable may have one continuous driver at most (IEEE 1800, 6.5), and `logic`
declares a variable. Two gates on one `logic` are refused the same way. Consequences for 5.0:

- **The engine** refuses a second driver on a variable when the design is built (`NetlistBuilder`),
  with the reason and the way out: declare it a wire.
- **Export** declares a net with more than one driver as `wire`, not `logic`, or the exported code
  would not compile in ModelSim either. "Two drivers read x" is still a lesson the canvas can show.

## Structural designs, read by both (Part 4b)

From Part 4b both simulators read the same SystemVerilog: `StructuralOracleTests` elaborates each
design of `Tools/ci/Oracle/corpus/structural` in BitSorter, writes one random stimulus out twice -- as a
testbench for Icarus and as writes for BitSorter -- and compares every signal of the design by its place
in the hierarchy (`oracle_tb.dut.slice[3].s.fa.sum` against `alu64.slice[3].s.fa.sum`). The writes
include all-x, all-z and stray unknown bits, and gaps shorter than the design's slowest path.

**They agree on every signal**: the mux, the full adder, the ALU slice and the 64-bit ALU, 2,062 signals
and more than 77,000 settled changes (2026-10-13). Nothing in this tier departs from the standard.

**One difference in how, not what.** Icarus joins a port and what it is connected to into one net;
BitSorter keeps the port a signal of its own, driven by a zero-delay assignment, which takes a delta
cycle. The value every time step settles on is the same, and that is what both the oracle and the
waveform compare; only a delta-by-delta trace would tell them apart.

**Unnamed generate blocks are named alike.** Probed: a module whose first generate construct is an
unnamed loop and whose second an unnamed `if` gives `genblk1[0]`, `genblk1[1]` and `genblk2` in Icarus's
VCD -- the standard's rule, numbering every generate construct in the scope, named or not -- and the
same in BitSorter (`ElaboratorTests.AGenerateLoopWithNoLabel_...`).

## Processes (probed 2026-10-13, Part 5a)

`BehaviouralOracleTests` runs a testbench in both simulators from the same file and compares every
signal: `corpus/behavioural/scheduling.sv`, the traffic light's own testbench, and the delay probes,
now read from their SystemVerilog. They agree. What the probes pinned down:

| Question | Icarus, and so BitSorter | The standard |
| --- | --- | --- |
| An `initial` raises a signal at time 0 that an `always` in the same module waits on | the `always` sees the edge if it is written first, and misses it if it is written after | leaves the start order open |
| The same, with the `always` inside an instance | the instance's processes start first, wherever the instance is written: a reset raised at time 0 reaches its flip-flops | leaves it open |
| `a <= b; b <= a;` on a clock edge | they swap; with `=` the second copies the first's new value | the same |
| `always_comb` against `always @*` on an input that never changes | `always_comb` runs once at time 0; `@*` waits and never runs | the same |
| `#0` | the process runs again after the rest of the instant's processes, before the nonblocking writes | the inactive region |
| `a = #5 b`, `q <= #3 d` | the value is taken when the statement runs and written after the delay | the same |

So BitSorter starts processes in Icarus's order -- an instance's before its parent's own, a module's own
as written -- and holds to it on every run (`Docs/architecture-5.md`, section 5).

### The last time step of a `$stop`

With `vvp -n`, as the oracle runs it, `$stop` ends the run like `$finish` does, **but Icarus's VCD
loses every change of the time step it ran in**: the time is written, the values are not. `$finish`
keeps them. The values did change -- in the traffic light's testbench the clock's rise at 155 is what
woke the process that called `$stop` -- so BitSorter's history keeps them, and after a `$stop` the
oracle compares up to the step before.

BitSorter suspends the run at a `$stop` rather than ending it (Part 9b-3), as ModelSim does and as
Icarus does without `-n`, where it drops into its prompt. Up to the suspension the two runs are the
same run, so the oracle compares them there and never runs on.

### A process that hears its own write back through an assign

```systemverilog
logic [3:0] count = 0;
wire  [3:0] next = count + 1;
always @(next) if (next < 9) count = next;
```

Icarus pushes a blocking write through continuous assignments at once, so `next` has changed before the
`always` is back at its `@(next)`, and the loop stops after one step: `count` is 1. BitSorter evaluates
the assign a delta later, when the `always` is waiting again, and `count` climbs to 8. **Both are
allowed**: the standard lets a simulator interleave an assignment's update with a process or not (IEEE
1800, 4.7). The corpus keeps out of it. A course design meets it only by feeding a process's output back
to its own sensitivity through a wire, which is a combinational loop either way.

### A busy given a default and then set (Part 5b)

```systemverilog
always_comb begin
    busy = 1'b0;                          // the default, first
    if (running && count <= 4) busy = 1'b1;
end
// ...and in the testbench, across the module's port:
@(negedge busy) ...
```

Each time the `always_comb` runs, `busy` is 0 for an instant inside it before it is set back to 1.
**Icarus lets that instant through to the testbench**: it joins a port and what it connects to into one
net, so the testbench's `busy` dips as well, and `@(negedge busy)` fires the first time the block runs
again -- at 25, in `ProcessTests`' version, when `busy` really falls at 65. **BitSorter does not**: a
port is a continuous assignment, as the standard makes a variable port, and by the time it is evaluated
`busy` is 1 again. The testbench is released at 65.

This is the course's own pattern, so it decides the question. The course's second simulation exercise
has two designs that give `busy` a default of 0 in an `always_comb` and set it to 1 in eight places, and
two testbenches that wait on `@(negedge busy)` (counted on 2026-10-13, never copied). Those run in
ModelSim, so ModelSim does not let the instant through either; under Icarus they would race ahead.
BitSorter follows the standard and the course's simulator. Within one module, a process waiting on the
same variable the `always_comb` writes does see the instant, in BitSorter as the standard has it -- it is
the port that hides it.

The corpus multiplier writes its `busy` once, from the state, so the oracle can compare the rest of it
with Icarus; `ProcessTests` holds BitSorter to the course's pattern.

### `$finish` lets its time step settle

`$finish` ends the run at the end of the time step it runs in. Probed: at the instant a testbench
calls it, Icarus still applies that instant's nonblocking writes, runs the processes waiting on `#0`, and
settles continuous assignments, so the values it ends on agree with each other. BitSorter does the same.

## Memories and printing (probed 2026-10-13, Part 6a)

`corpus/system/printing.sv` and `corpus/system/memories.sv` run in both, and every line they print is
compared with Icarus's, as well as every signal. They agree. What the probes pinned down:

| Question | Icarus, and so BitSorter |
| --- | --- |
| `%d` with no width | padded to the widest value the bits hold: 2 places for 4 bits, 3 for 8, 4 for a signed byte (`-128`), 11 for an `integer` |
| `%h`, `%o`, `%b` with no width | every digit, leading zeros kept; `%0h` drops them |
| a digit with unknown bits | `x` all x, `z` all z, `X` some x, `Z` some z; `%d` treats the whole number as one digit |
| `%t` | padded to 20; `%0t` not |
| `%5d`, `%-5d`, `%05d`, `%3h`, `%10s` | padded with spaces, left-aligned, with zeros, and so on |
| an argument with no format, `$display("a", x)` | written as `%d` at its default width; every string argument is a format for the ones after it |
| `$strobe` and `$monitor` in one step | both at the end of it, the monitor first; `$monitor` only when a value other than `$time` changed |
| `$readmemh` with no range | from the lowest word up, whichever way the array is declared (the 1364-2005 rule; Icarus warns that 1364-2001 differed) |
| `@4` in an image | the next word goes to word 4 of the memory, counted as declared |
| an image shorter than the memory | the rest is left as it was; Icarus warns, and BitSorter does too |

### Time 0: processes start once the wires have settled

Icarus works out every continuous assignment from the starting values before any process runs, so an
`initial` that prints a wire at time 0 prints its value. BitSorter started its processes in the first
delta of time 0 until Part 6a, and printed x. Both are allowed; Icarus's is plainly what a student
expects, so BitSorter now does the same (`ProcessTests.AtTimeZero_ProcessesStart_...`).

### What `$writememh` writes

Icarus heads its file with a comment (`// 0x00000000`); BitSorter writes the words alone, one a line.
Either reads back with `$readmemh`. The oracle does not compare the files.

## The processor (Part 6b)

`corpus/processor/mcpu.sv` is an original multicycle RV32I processor -- a state-machine controller, a
datapath with registers between the steps, 1,024-word instruction and data memories loaded with
`$readmemh` from images named by a string parameter, and the opcodes and ALU operations in an included
`localparam` file. Its program sorts, sums, multiplies, counts Fibonacci and takes every kind of branch;
its testbench checks 23 results against `mcpu_expected.hex`, worked out by an independent RV32I emulator,
and against Icarus it agrees: done after 2,716 cycles at 27,160, every one of its 98 signals and some
125,000 settled changes the same, and the same three lines printed.

Two things Icarus does that the corpus works around, neither a difference in what the design does:

- **An enum takes a `?:` only with a cast.** `state_next = c ? MEM_READ : MEM_WRITE;`, both choices
  of `state_t`, is an error in Icarus ("requires an explicit cast"). BitSorter does not check enum
  assignments yet; the corpus writes it as an `if`.
- **`always_comb` sensitivity is whole vectors.** A constant select in an `always_comb`
  (`b[4:0]`) draws a "sorry: ... all bits will be included" from Icarus: it wakes the block on any bit of
  `b`, which only means waking it more often than needed, and changes no value.

## 2-state types (the course audit, 2026-10-19)

`bit`, `byte`, `shortint`, `int` and `longint` hold only 0 and 1 (IEEE 1800, 6.11.2), and so does an
enum that names no base type, which is an `int`. Icarus starts them at 0 and turns an x or a z written to
one into 0; BitSorter treated every type as 4-state until the course audit found it. The course meets it in
its own state-machine template, `typedef enum {IDLE, ...}`: before the reset, the state reads IDLE in
Icarus and in the course's ModelSim, and read x here. `corpus/behavioural/two_state.sv` holds the two
to the same values -- a state machine on an `int` enum read before its reset, and x and z written into
each 2-state type beside a `logic` that keeps them -- and `TwoStateTests` holds the rest. `integer` and
`time` stay 4-state, as the standard has them.
