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
| P12 | `bufif1 #(5, 2, 7)` from 0, `#(2, 5, 7)` from 1, `#(6, 5, 2)` from z, `assign #(5, 2, 7)` from 0, each to x at 20 | x at 22, all four | going to x takes the smallest of three delays, wherever it comes from |

**Going to x with three delays** (probed 2026-11-09, in Icarus 12 and ModelSim 2020.1, for Ahmad's decision of
2026-11-03 that a gate's transition to x follow the standard's table, IEEE 1800 table 28-9, by where it comes from,
rather than take the smallest of the three). The table's three-delay column gives min(d1, d2, d3) for 0 to x, 1 to x
and z to x alike -- where it comes from changes nothing there; it decides only the other rows (x to 0 takes d2, x to 1
d1, x to z d3), which `Delay.For` already gives by the new value. So the engine already followed the table, and P12
holds every to-x row against both simulators, each with a different one of the three the smallest. The two-delay
column's min(d1, d2) is what `Delay.Of(rise, fall)` gives too, its turn-off being the smaller of the two.

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

### `~&` and `~|` between two operands

The standard has `~&` and `~|` only as reductions (IEEE 1800, 11.4.9); there is no binary NAND or NOR
operator. Icarus reads `X ~& Y` as NAND and `X ~| Y` as NOR anyway, binding them as `&` and `|`, and the
course's first workshop writes `assign Z = X ~& Y;` as one of three ways to make a NAND. Here the course
wins over the standard, with a warning: BitSorter reads them as Icarus does and says `~(X & Y)` is the
portable spelling. `NonStandardOracleTests` runs the three NANDs, a NOR and a vector chain of both in
the two simulators, outside the corpus, whose files must read with no diagnostics at all.

### `?:` with an unknown condition, where both choices are z

With the condition x or z, `?:` gives each bit both choices agree on as 0 or 1, and x everywhere else
(IEEE 1800, table 11-20) -- including where both choices are z. Icarus keeps z there:
`2'bxx ? 19'sbzz... : 36'sb...z...` comes out with z wherever both sides are z. BitSorter follows the
standard and gives x. It went unseen until Part L3 added `==?` and `!=?` to `OperatorOracleTests`,
which moved its random cases onto exactly that combination; the test now allows that one difference --
an x here against a z there, on an ambiguous `?:` -- and nothing else, and holds the standard's answer
with a case of its own.

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

So BitSorter started processes in Icarus's order -- an instance's before its parent's own, a module's own
as written -- and holds to that order whenever processes are woken together (`Docs/architecture-5.md`, section 5).
How time 0 begins follows ModelSim since M5 (below, "Time 0, as ModelSim begins it"); Icarus's start is a setting,
and the Icarus oracle runs it.

### A delay that works out negative (the review of 16c, probed 2026-11-03)

The standard reads a negative delay as a two's-complement unsigned number as wide as a time variable (IEEE 1800, 9.4.1):
2^64 less its size. BitSorter counts time in 63 bits, so such a delay, like any past what a run can count, never comes
due; it ran one as `#0` until the review of 16c. `scheduling.sv`'s S9 asks it of both simulators with an integer, which
neither lets come due before the `$finish`; `ProcessTests.ADelayThatWorksOutNegative_...` holds the rest.

| Delay | Icarus 12 | ModelSim 2020.1 | The standard, and BitSorter |
| --- | --- | --- | --- |
| `#d`, `integer d = -5` | waits 2^64 - 5 | waits 2^32 - 5 | 2^64 - 5: never due |
| `#(-1)` | waits 2^32 - 1 | 0, warning at compile time that a negative delay is set to zero (vlog-2556) | 2^64 - 1: never due |
| `#r`, `real r = -2.5`, in a `` `timescale 1ns/1ps `` module | waits about 2^64 ticks | 0, with a suppressible error at run time (vsim-3465) | rounded to the precision, -2500 ticks, then 2^64 - 2500: never due |
| `#(1'bx)` | no delay | no delay | no delay (9.4.1) |

A gate's or a continuous assignment's delay is a constant, so one cannot work out negative -- the elaborator refuses a
negative one -- but it can be past what a run can count: `assign #1e19 y = a;`, `#(64'hFFFFFFFFFFFFFFFF)`, or `#10000`
under a `` `timescale 1s/1fs ``. It too never comes due, and the elaborator says so with a warning (SV0311). Until the
16d sweep (elaborator-7, engine-4) it was held at the largest count and added to the time unchecked, so a change after
t = 0 came due at once, at a negative time; `ProcessTests.AGateOrAssignDelayPastWhatARunCanCount_...` holds it.

### Two nonblocking writes to one variable in one moment (probed 2026-11-09)

```systemverilog
initial begin #10 a <= 0; a <= 1; end      // a was 1
```

The standard applies each nonblocking update as an event of its own, in the order the statements ran (IEEE 1800,
10.4.2 and 4.9.4), so the fall between is a value change, and a process waiting on it is woken. Until Ahmad's decision
of 2026-11-03 BitSorter landed the region's updates together and lost the edge.

| What waits on it | Icarus 12 | ModelSim 2020.1 | BitSorter |
| --- | --- | --- | --- |
| `@(negedge a)`, `@(posedge a)`, `@(a)` | each woken once, and each reads 1 | the same | the same |
| `@(s)`, `@(posedge s)` of a vector, `@({c, e})`, `@({1'b0, c})` | woken | woken | woken |
| `@(s[3])`, `@(posedge s[3])`, `@(s[3:2])`, from `s <= 0; s[3] <= 1;` | not woken | woken | as ModelSim |
| `@(a ^ b)`, `@(~c)`, `@(posedge e or (a ^ b))`, `wait (a == 0)` | not woken | not woken | not woken |
| `@(count == 5)`, from `count <= 0; count <= 5;` | woken | not woken | as ModelSim |
| `@(m[1])` and `@(m[i])`, a memory's word, from `m[1] <= 0; m[1] <= 3;` | woken | not woken | as ModelSim |
| `@($signed(w))` | woken | not woken | as ModelSim |
| `@({2{c}})` | woken | not woken | as ModelSim |
| `assign y = a;` and `@(negedge y)` | y falls and rises: woken | y never moves | as ModelSim |
| `v[0] <= 0; v[7] <= 0; v[3] <= 1;` with `@(v[3] ^ v[0])`, from `00000001` | not woken | not woken | not woken |
| A VCD of `a` | `1` at 10 | `0` then `1` at 10 | the history keeps both, in deltas apart; the waves and a VCD show `1` |

Every update of the region lands before anything it wakes runs -- both simulators do that -- so a process woken by the
fall reads the 1 the last write left. Which waits hear the value between the standard leaves open: an update event
schedules an evaluation of whatever is sensitive to the variable (4.5), and whether a wait on an expression is judged
before or after the next update of the region is any order the scheduler likes. The two simulators settle it
differently, and BitSorter settles it as ModelSim, the course's own: a wait on a variable, a select of one at a fixed
place, or a concatenation of them -- a constant among them -- hears each update as it lands; a wait on anything else --
an operator, a cast, a replication, a memory's word, a `wait` -- and a continuous assignment are judged once the region
has landed, on what it left against what was there before (`Simulator.ChangedBetween`, `Expr.IsSelection`). Icarus
wakes no select and some expressions, which looks like how its functors happen to be built rather than a rule. Writes to
bits apart land together in all three, whatever order they are written in: BitSorter splits the region only where an
update writes a bit an earlier one of its delta wrote (`TakeNba`). Until the review of the 16d engine batch it judged
every wait in every delta of the landing, which woke `@(a ^ b)` and `@(count == 5)`, and split by the span of bits
written, which put `v[3]` in a delta after `v[0]` and `v[7]` and woke `@(v[3] ^ v[0])`.

`corpus/behavioural/nba_landing.sv` holds eighteen waits through one landing to ModelSim on every signal and line
(`BehaviouralOracleTests.TheNonblockingLanding_AgreesWithModelSim_OnEverySignal`), and Icarus's line to the table above
(`Icarus12_WakesNoSelectOnANonblockingValueBetween_AndFourExpressions`); `scheduling.sv`'s S10 holds the wake both agree
on -- `a <= 1; a <= 0;` wakes `@(posedge a)` -- against both; replication, a concatenation with a constant and the assign
were probed by hand. `ProcessTests.TwoNonblockingWritesInOneStep_...` and `ProcessTests.NonblockingWritesLandingInTurn_...`
hold BitSorter to every row.

### Automatic tasks (probed 2026-11-09)

An automatic task gives each call variables of its own (IEEE 1800, 13.3.1). Icarus 12 and ModelSim 2020.1 agree on
every line of `corpus/behavioural/automatic_tasks.sv` -- two branches of a fork running one task at once, each keeping
its id and its wait across them; a task calling itself twice in its body, a block's variable and an output into a
local of the call above among them; two tasks calling each other; a `disable` of a named block from the third call,
which ends every call made from inside the first call's block -- a middle call outside the block among them -- the
first going on after it -- and on a `disable` of a task from deep inside its own calls, which ends them all.
BitSorter shared one set of signals between every call written in, and refused a task that called itself, until
Ahmad's decision of 2026-11-03; `BehaviouralOracleTests` now holds the file to both. Until the review of the 16d engine
batch a `disable` of a block left only the innermost call's, and the calls above went on inside theirs.
A static task's two calls at once share theirs in every simulator, but which branch's values they then show is the
order a fork's branches start in, which the two simulators settle differently ("Testbench control", below).

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
| `%s` of a vector wider than its text | every byte a character, a leading zero byte a space: 40 bits holding `"RUN"` print as `  RUN`; `%0s` drops them, `%8s` pads the text (found in Part L3, which fixed it) |
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
expects, so BitSorter did the same (`ProcessTests.AtTimeZero_AsIcarusBegins_...`) -- until M5, which follows
ModelSim, where it prints x again (below).

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

## Functions and tasks (Part L2, 2026-10-19)

`corpus/behavioural/subroutines.sv` and `corpus/behavioural/function_sensitivity.sv` run in both and
agree on every signal and line. One rule the second pins down, where it is easy to guess wrong:

| A function reads a signal it is not given, and that signal changes | Icarus, and so BitSorter |
| --- | --- |
| called from an `always_comb` | evaluated again (IEEE 1800, 9.2.2.2.1: `always_comb` is sensitive to what the functions it calls read) |
| called from an `always @*` | not evaluated again: `@*` waits on a call's arguments only |
| called from a continuous assignment | not evaluated again: a call's operands are its arguments |

BitSorter first evaluated the `assign` again as well, which reads as more helpful and is not what either
the standard or Icarus does; the corpus design is the reason it no longer does.

## Interfaces (Part L8, 2026-10-20)

Icarus 12 reads interfaces themselves: their parameters and ports, signals, assigns, `always` blocks,
functions and tasks, instances of them -- in a generate loop too, and in an array -- and names into
them from outside, read, written and called. `corpus/behavioural/interfaces.sv` runs in both and agrees
on every signal, the interfaces' own among them, and on every line.

| Question | Icarus 12 |
| --- | --- |
| A port of an interface's type, `module m (bus_if b)`, `bus_if.mp b` or `interface b` | a syntax error: no interface port of any kind |
| `import t` in a modport | "sorry: modport task/function ports are not yet supported" |
| A modport with only directions | read, and nothing comes of it, with no port to use it |

So interface ports are held to Icarus through what they mean (`InterfaceOracleTests`): the same design
written once with them and once with every `bus.x` spelled out as `top.hs.x`, which Icarus runs. All
three runs print the same, and BitSorter's two record the same history on every signal they share.

BitSorter's own choices, which `InterfaceTests` holds: a scope's interfaces are made before its modules,
so their processes start first among its instances; `ref` in a modport is read as `inout`; and a modport
limits the module given the port, not a name reaching through that module from outside, `s.bus.sent`.

## Reals and time (Part L7, 2026-10-20)

`corpus/behavioural/reals.sv` runs in both and agrees on every signal and line, and probed beside it:

| Question | Icarus 12, and BitSorter |
| --- | --- |
| `$display(r)`, a real with no format | `%#g`: 2.50000, 0.333333 |
| `%d` of a real | rounded, with no padding: 3 |
| A real given to a whole number | rounded half away from zero: -2.5 is -3, 200.7 is 201 |
| `-0.0` as a condition | false |
| `#1.5` in a `` `timescale 1ns/1ps `` module | 1500 ticks; `$time` is 2, rounded; `$realtime` 1.5; `%t` 2000 for `$time`, 1500 for `$realtime` |
| `#0.0004` and `#0.0006` there | no time at all, and one tick: a delay is rounded to the precision |
| `#1.25` in a `` `timescale 10ns/1ns `` module beside it | 13000 ticks: 12.5 ns rounded to 13 |
| A tick, with two timescales | the finest precision of either |

BitSorter's own choices, which `RealTests` holds: a `shortreal` is held as a 64-bit `real`; a module with no
`` `timescale `` beside ones with one counts in the first one written, where Icarus uses its own default
of 1 s.

## Packages, macros, strings and files (Part L6, 2026-10-20)

`corpus/behavioural/library.sv` runs in both and agrees on every signal, every line and every file it
writes. Where Icarus 12 cannot run it, `LibraryTests` holds it to the standard:

| Question | Icarus 12 | The standard, and BitSorter |
| --- | --- | --- |
| `s.toupper()`, `s.getc(i)`, `s.compare(t)` | "Method ... is not a string method" | supported (6.16) |
| `s.putc(i, c);` | "Enable of unknown task" | supported |
| `pkg::t(...);`, a task called by its package's name | a syntax error | supported (26.3) |
| `q::A` where q only imports A | "Unable to bind" | refused as well: a package does not pass on what it imports |
| `` `define M() `` with nothing in the brackets | "malformed `define directive" | read as a macro of no arguments (the standard's grammar wants at least one) |
| A string variable in `$dumpvars` | left out of the dump | kept, and marked `IsString`; the corpus compares what is printed of it |
| A second `$fopen` after a `$fclose` | the descriptor just freed | the same: the lowest free one |

## Testbench control (Part L5, 2026-10-19)

`corpus/behavioural/testbench_control.sv` runs in both and agrees on every signal and line: the three
joins, a fork in a fork, `wait fork`, `disable fork`, `disable` of a named block, of a named fork and of
a task, events, `force` and `release` of a wire, its bits and a clocked variable, and assertions with
pass and fail actions -- and, since the review of 16c (2026-11-03), of a wire nothing drives, which is z again once
released (10.6.2), as both print and BitSorter had not: it kept the forced value. Where Icarus 12 runs otherwise or
not at all, BitSorter follows the standard, and `TestbenchControlTests` holds it:

| Question | Icarus 12 | The standard, and BitSorter |
| --- | --- | --- |
| The order a fork's branches start in, in one moment | last first | left open by the standard; BitSorter starts them in the order written, as ModelSim does |
| `force w = a & b`, then `b` changes | "sorry: procedural continuous assignments are not yet fully supported" -- evaluated once | the force follows its expression (10.6.2) |
| `->> e` | "sorry: ->> is not currently supported" | turns the event over in the NBA region |
| `e.triggered` | "Event name e can't have member names" | 1 from a trigger to the end of its time step (15.5.3) |
| An assertion with no else fails | `ERROR: file:line:` and no message | an `$error` naming the assertion and its line |
| Two processes resumed in one time step, by delays scheduled at different times | in the order the delays were scheduled | in the fixed order (`architecture-5.md`, section 5) -- a race either way, which the corpus keeps out |
| A named fork's branches ended by a `disable` from another process, their parent at `wait fork` (probed 2026-11-03) | waits for good, even once another child ends | goes on as the last of them ends (9.6.1, 9.6.2). ModelSim 2020.1 goes on only once another child ends after the disable, and so waits for good where none does -- the watchdog idiom, ended from outside |

## Data types (Part L4, 2026-10-19)

`corpus/behavioural/data_types.sv` runs in both and agrees on every signal and line: packed structs, a
union, packed arrays of two and three dimensions and of structs, a memory of two dimensions, `foreach`
over memories, and selects at indices known only as the run goes, inside and outside. Where Icarus 12
parts from the standard, BitSorter follows the standard, and `DataTypeTests` holds it:

| Question | Icarus 12 | The standard, and BitSorter |
| --- | --- | --- |
| `'{...}` on a packed struct or array | "sorry: I do not know how to elaborate assignment patterns", in an `assign` and a process alike; keys and `default:` are a syntax error | laid out on the target's type (10.9) |
| `$bits(s.head.imm)`, a member of a member, 8 bits | 15, the whole outer struct's width -- so `%h` prints it four digits wide; the value is right | 8 |
| `foreach (b[i])` with `logic [3:0] b` | i = 0, 1, 2, 3 | 3, 2, 1, 0: from the left bound to the right (12.7.3), as ModelSim counts |
| `sb[1]` of `logic signed [1:0][7:0] sb` | signed: 8'hFF reads -1 | unsigned, 255: an element is signed only when its own type is (7.4.1) |
| `a.st.name()`, a method of a member | "Method name nesting is not supported yet" | the member's enum's name |
| `v[i][3:0]`, `prog[i].op`: a select after an index known only as the run goes | "must be constant here" | supported |

## Expressions (Part L3, 2026-10-19)

`corpus/behavioural/expressions.sv` runs in both and agrees on every line: `==?` and `!=?`, the bit
counters, casts, an enum's methods, and `$random` with and without a seed. What was probed on the way:

| Question | Icarus 12 | BitSorter |
| --- | --- | --- |
| `$random(seed)` | the standard's `rtl_dist_uniform`, number for number: seed 7 gives -2146999808, then 1181502348; seed 0 starts from the generator's own constant | the same generator, held to Icarus's numbers |
| `$random` with no seed | an internal seed from 0 | the design's own, from 0 |
| `$random(seed)` in a `?:` arm passed over, or the right of a decided `&&` or `\|\|` | not worked out: the seed stays (ModelSim the same) | the same since the review of the 16d engine batch; it moved the seed on |
| `$random(seed)` in an arm of an x condition | both arms worked out, the seed moved on once | the same |
| `$urandom(1)` | 69120 -- its own algorithm, as the standard allows | `$random`'s bits read unsigned; no test compares them |
| `int'(4'b1x01)`, `bit'(1'bx)` | 9, 0: a 2-state cast drops x | the same |
| `signed'(x)`, `unsigned'(x)` | a syntax error | read as `$signed` and `$unsigned`, as the standard has them |
| `inside`, `case inside` | "sorry: not supported yet" | by the standard (11.4.13, 12.5.4), held by `ExpressionTests` |
| `s.next().name()`, a method of a method | a syntax error | supported; the corpus writes `t = s.next(); t.name()` |
| `.name()` of a value that is no member; `next()` from one | an empty string; x | the same; 0 for a 2-state enum, its default |
| `$display("%s", e.name())`, `%10s`, `%-6s` | the name exactly; padded; padded left | the same |

## Lint (Part 14e, probed 2026-10-27)

BitSorter warns, as ModelSim's `vlog` does, of a signal read that nothing drives (SV0402), one set inside a block
that nothing reads (SV0403), a net with drivers that cannot all let go (SV0404), and a plain value given to something
narrower (SV0405). Icarus 12 is a quieter judge, so the oracle checks the lint only where it speaks:

| Question | Icarus 12 | BitSorter |
| --- | --- | --- |
| `-Wall` on a design that does all four | no warning at all | each of the four, at its line |
| `-Wfloating-nets` | a warning for every net with no driver -- the top's own inputs included, which a testbench drives | SV0402 for a net or a variable that is read: an input is left alone, and a variable reads x |
| `assign n = x;`, four bits into two | silent | SV0405, unless the value is a sum, a number, an int or a real |
| two `assign`s to one `wire` | silent; the bits resolve, x where they disagree | SV0404, unless every driver can let go: a tri-state gate, or a value that can be z |

The corpus is held to none of the four (`DesignLintTests.TheCorpus_RaisesNone`): every one of its designs is
driven, read, driven once and sized to fit, which is the course's style.

## The classic slips (Part 15i-2, probed 2026-11-02)

Each slip BitSorter answers in a student's words, Icarus 12 refuses too -- but one:

| Slip | Icarus 12 | BitSorter |
| --- | --- | --- |
| `assign y = a & b;` inside an `always_comb`, `always_ff` or `initial` | takes it: a procedural continuous assignment, with "cannot be synthesized" in an `always_comb` or `always_ff`, and "not yet fully supported" -- its right side read once | runs it, as the standard has it, its right side followed (M4); in an `always`, with a warning (SV0406) saying what was likely meant |
| `’` or `–` pasted into code | "syntax error" | reads it as `'` or `-` with a warning, and exports it so |
| `logic s[1:0]` given `{a, b}` by an `assign` | stops on an internal assertion; in an `always_comb`, "sorry: ... not yet supported" | refuses it, saying to put the range before the name |

The standard has the procedural `assign`. BitSorter refused it until M4 (Ahmad's morning decision of 2026-11-02, #10),
and runs it now, as ModelSim does -- below, "Procedural assign".

## Procedural assign (M4, 2026-11-02)

`assign` and `deassign` inside a process (IEEE 1800, 10.6.1), probed under both simulators before the engine was built.
ModelSim follows the standard on every question; Icarus 12 says "sorry: procedural continuous assignments are not yet
fully supported. The RHS of this assignment will only be evaluated once", and does just that for an expression -- a
lone variable on the right it does follow:

| Question | Icarus 12 | ModelSim, the standard, and BitSorter |
| --- | --- | --- |
| `assign y = a + b;`, then `a` changes | y keeps the sum it was given | y follows it |
| `y = 9;` while y is assigned | ignored | ignored |
| A second `assign y = a - b;` | takes the first one's place, read once | takes the first one's place |
| `force` over an assign, then `release` | the assign's value as it was first read | the assign's value as it is now |
| `deassign y;` | y keeps its value | y keeps its value until it is next written |
| `deassign` under a force, then `release` | the forced value kept | the forced value kept |
| `assign {r, s} = {a, b};`, then `b` changes | r and s keep what they were given | s follows b |
| An `assign` inside `always @(posedge clk)` | read again at each edge | followed from the first edge on, and again at each |
| What an assign reads changes, and the process reads the variable in the same moment | the old value | the new one: an assign follows at once |
| The same with a `force` | the old value | the old value: a force follows a delta later -- the standard leaves the region open, and BitSorter does as ModelSim |
| `y = 9;` or `y <= 10;` in the moment after what the assign reads changed | the old value | the new one, kept |
| `assign count = count + 1;` at an edge | one more | one more: what an assign changes does not set it going again |
| `assign g = h + 1; assign h = g + 1;` from 0 | g=1, h=2 | g=3, h=2: h's change works g out again, and g's does not set h going, which is working out |
| `assign m[1] = a;`, a word of an array | takes it; refuses a word picked by a variable ("index must be a constant") | refuses it: "Slice of unpacked array not allowed in procedural continuous assignment" -- BitSorter too |

`corpus/behavioural/procedural_assign.sv` asks each of these but the array word, and is held to ModelSim on every signal and line
(`BehaviouralOracleTests.TheProceduralAssign_AgreesWithModelSim_OnEverySignal`) rather than to both: where CI runs, only
Icarus is there, so `TestbenchControlTests` holds BitSorter to ModelSim's lines, to the character, and
`Icarus12_ReadsAProceduralAssignsRightSideOnce` to Icarus's difference -- a later Icarus that follows the standard fails
it, and the design can join the list both simulators run.

## Races (Part 15i-3, 2026-11-02)

The traffic light's testbench changed `walk` with `=` straight after `@(posedge clk)`, so the state's flip-flop and the
testbench woke on one edge, and the state it took depended on which ran first. BitSorter runs a design's processes in a
fixed order -- an instance's before its parent's -- and Icarus 12 happened to agree on every signal, so the oracle
passed it. The race warning found it; it drives `walk` with `<=` now, which every simulator settles the same way, and
every corpus testbench is held to no race as the oracle runs it.

## Time 0, as ModelSim begins it (M5, 2026-11-02)

Ahmad's morning decision of 2026-11-02 (Q1): follow ModelSim's order at time 0 now, and decide again after Part 16's
sweep. The standard leaves it open (4.7). Measured first, with 42 small designs run under ModelSim ASE 2020.1 and
Icarus 12 (in the session's scratchpad, never the repository). ModelSim's rule, as measured:

| Question | ModelSim | Icarus 12 |
| --- | --- | --- |
| An `initial` raises `r` at time 0; an `always @(posedge r)` written after it | sees the edge | misses it |
| The same with an `always_ff` on a reset, or across an instance either way | sees it | sees it only if the always's instance starts first |
| `r = 1; r = 0;` in an initial; `always @(posedge r)` and `@(negedge r)` written after it | sees both | sees neither |
| A clock raised from x at time 0, an `always_ff @(posedge clk)` | an edge | no edge |
| An `initial @(posedge r)` written after the initial that raises `r` | misses it | misses it |
| An `always begin x = 1; #5; end` written after `initial y = x;` | y = 0: the initial first | the same |
| Initials printing, an instance's between its parent's two | as written, the instance's in its place | the instance's first |
| An `initial` reading a wire, or an output through the hierarchy, at once | x: not settled yet | its value |
| The same after `#0` | its value | its value |
| An `always @(w)` on a wire that settles at time 0 | sees the change | sees nothing |
| An `initial` reading an `always_comb`'s output at once | x | x |
| Two always blocks of one module woken by one edge | as written | last first |
| Two instances' always blocks and the parent's, woken by one edge | the instances first, in their order, then the parent's | the parent's, then the instances last first |
| A generate loop of initials and an instance | iteration by iteration, the instance's in its place | the instance's first, then each initial for every iteration |
| A generate `if`'s initial, between two others | where it is written | first |
| A child's initial reading an input port tied to a constant, at once | x | its value |

So ModelSim has every `always` headed by an event control waiting before anything runs; then the other processes run
as written, depth first, an instance's at its instance statement's place, `always_comb` and `always_latch` last; and
the continuous logic settles after them, as changes. BitSorter does just that by default since M5
(`StartOrder.ModelSim`, `ModelSimStart`), "as written" being the order its elaborator's walk meets the processes in,
and matches ModelSim on all of the designs but three. A stimulus drawn on the workbench writes at time 0 as the exported
testbench's initial would, after the processes start. The three it does not are not
time 0: **a port costs BitSorter a delta**, so an instance's processes woken through one run a delta after the parent's,
where ModelSim, which joins a port's two sides into one net, runs them first. No corpus design races on it.

`StartOrder.Icarus` is BitSorter's start before M5, and the Icarus oracle runs every comparison in it, so those stay
exact; the ModelSim oracle runs ModelSim's, which took the time-0 rows out of `ModelSimDiffers` below; and where CI runs,
with no ModelSim, every corpus testbench is run in both starts, racing in neither and printing alike but for the
printing testbench's read through the hierarchy (`TheTestbench_RacesInNeitherStart_AndPrintsAlikeInBoth`). No exercise's
grade depends on it (`ExerciseCurriculumTests.NoExercisesGrade_DependsOnHowTimeZeroBegins`), and COMPARE WITH MODELSIM'S
RUN agrees with ModelSim on every sample from t = 0 (`ModelSimCompareOracleTests`).

## ModelSim (Part 15i-4, ModelSim ASE 2020.1, 2026-11-02)

Every corpus testbench runs under ModelSim as well as Icarus (`BehaviouralOracleTests`), compared on every signal and
every printed line. Where ModelSim settles what the standard leaves open another way than Icarus -- whom BitSorter
follows -- or prints in its own style, the test names it (`ModelSimDiffers`) and it is written here; nothing else is
let through.

| Testbench | ModelSim | Icarus 12 and BitSorter |
| --- | --- | --- |
| `printing`, `$display("%5d|%-5d|%05d|%3h|%8b|")` | `%05d` is padded with spaces, `%3h` and `%8b` with zeros: `    5|005|00000101` | `00005|  5|    0101` |
| `expressions`, `$random` with no seed | its own first seed | Icarus's |
| `reals`, a real printed with no format, and `%d` beside it | the shortest exact form, `2.5 0.3333333333333333`, and `%d` padded to its width | `2.50000 0.333333`, `%d` unpadded |

Two things the harness learned:
- ModelSim keeps, in the time step a `$stop` ends the run in, what ran before the `$stop` -- before that step's NBA
  region -- where Icarus keeps nothing of it. That step is left out of both comparisons.
- It dumps a module's vector input one variable a bit, `a [7]` to `a [0]`. `VcdReader` read them as eight signals
  called `a`, the last of them, bit 0, standing for the whole. It reads them back whole now.

**And one bug it found in BitSorter**: an exported run.do gave a chosen program as `-GPROGRAM=1`, which sets every
parameter called PROGRAM in the design, a processor's memory's image name among them -- ModelSim opened "" and the
program never ran. It gives `-G/<testbench>/PROGRAM=1` now (Part 15h-6c's export, fixed in 15i-4 with a red test first).

**Races.** On every race-shaped testbench tried -- the traffic light's button pressed with `=` on the edge, two
`always @(posedge clk)` written with `=` in either order -- ModelSim and Icarus settled it the same way, and so does
BitSorter. The standard leaves each open, and a simulator, or a setting of one, may settle it otherwise: that is what
the race warning (Part 15i-3) is for.
