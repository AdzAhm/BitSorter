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
delay, to all z the turn-off delay, anything else the rise delay. Icarus decides by something like the
lowest bit:

| Change | Standard | Icarus |
| --- | --- | --- |
| `0101` → `0100` | 2 (rise: nonzero to nonzero) | 5 |
| `zzzz` → `1z00` | 2 (rise) | 5 |
| `0110` → `0000`, `0000` → `0101`, `0x00` → `zzzz` | 5, 2, 7 | the same |

BitSorter follows the standard (`Delay.For`, `WaveformTests.AVectorAssignment_...`). The oracle corpus
keeps out of this case. **It does not reach students**: the course puts delays only on one-bit cells.

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
