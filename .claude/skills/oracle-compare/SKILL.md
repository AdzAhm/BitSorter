---
name: oracle-compare
description: Run SystemVerilog designs under Icarus Verilog, BitSorter 5.0's oracle, and compare them with BitSorter's own simulator. Use when adding a design to the oracle corpus, probing how a construct or delay behaves in a real simulator, deciding what the engine should do, or when an oracle test fails.
---

# The oracle

BitSorter 5.0's engine is checked against **Icarus Verilog**: 4-state and event-driven with gate delays,
as the course's ModelSim is. (Verilator is 2-state and ignores delays — the wrong tool.) The harness is
`Tools/ci/Oracle`; the designs are in `Tools/ci/Oracle/corpus`. `Docs/architecture-5.md` section 6 is
the delay contract both are held to.

**The standard wins.** Where Icarus and the SystemVerilog standard disagree, BitSorter follows the
standard and the difference goes in `Docs/oracle-notes.md` (from Part 2). The oracle is a second opinion.

## Where Icarus is

- Windows: `C:\iverilog\bin\iverilog.exe` and `vvp.exe` (Icarus 12). **Not on PATH**; call it by full
  path. GTKWave is in `C:\iverilog\gtkwave\bin\gtkwave.exe`.
- CI: `apt-get install iverilog`, and `BITSORTER_REQUIRE_ICARUS=1` makes "not found" a failure.
- The harness looks in `ICARUS_HOME`, then PATH, then `C:\iverilog\bin`.

## Run the oracle tests

```bash
cd "/c/Users/55556/Unity projects2/BitSorter"
dotnet test Tools/ci/Oracle/Oracle.csproj -c Release
```

A skip means Icarus was not found. On this machine that is a fault, not a pass.

## Probe something by hand

For a question like "does this pulse get swallowed?", write a scratch design **in the scratchpad**, never
in the repo, and run it:

```bash
iv=/c/iverilog/bin; d="<scratchpad>/probe"; mkdir -p "$d" && cd "$d"
timeout 60 "$iv/iverilog.exe" -g2012 -o design.vvp probe.sv < /dev/null && timeout 60 "$iv/vvp.exe" -n design.vvp < /dev/null
```

- **Always `< /dev/null` and a `timeout`:** `iverilog -V` with no sources waits on standard input forever.
- **`vvp -n`:** `$stop` ends the run instead of opening vvp's prompt. The course's testbenches end on `$stop`.
- `$monitor("%0t %b", $time, y)` prints each change. `$dumpfile`/`$dumpvars` writes a VCD for GTKWave.
- Write what was learned into `Docs/oracle-notes.md`, and turn anything the engine must do into a corpus
  design with a test.

## Add a design to the corpus

Follow `Tools/ci/Oracle/corpus/README.md`: **original only** (written in the course's style, never copied
from it), no ID numbers even as operands, a header comment saying what it exercises, one idea per design.
Then run the `privacy-scan` skill: its private check compares the corpus line by line with the course folder.

## Generated cases: the operator oracle (Part 1)

`OperatorOracleTests` shows the pattern for checking many small facts at once without a corpus file:
generate the cases in C#, write one testbench that assigns literal operands and `$display`s each
result as `"<index> %b"`, run it once, and compare every printed line with BitSorter's own answer. It
writes the generated file to a temp folder and deletes it, so nothing generated lands in the repo.
Declare each result exactly as wide as the operator's own result, so SystemVerilog's context sizing
does not widen the expression behind your back.

## Comparing with BitSorter's engine (from Part 2)

From Part 2 the same design runs in both, and the two VCDs are compared per signal, normalised to the
last value per (time, signal). This section gets the exact calls when that lands.
