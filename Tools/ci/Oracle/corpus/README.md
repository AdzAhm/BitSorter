# The oracle corpus

SystemVerilog designs and testbenches that run under both Icarus Verilog and BitSorter's own
simulator, so the two can be compared. See `Docs/architecture-5.md` for why there is an oracle at all,
and `Docs/oracle-notes.md` (from Part 2) for where Icarus and the language standard part ways.

## The rules

- **Every file here is original.** BitSorter is built for a university course, and the corpus is
  written *in the style* of that course's exercises so that the simulator is tested on the code
  students actually write. Nothing is copied from the course: not a handout, not a given file, not a
  solution. A design that resembles an assignment is rewritten from scratch.
- **No ID numbers, ever**, not even as test operands. Use made-up values. `Tools/ci/Repo.Tests` scans
  this folder with everything else, and its private check compares it line by line against the course
  folder on the developer's machine.
- **Each file starts with a comment** saying what it exercises and that it was written for BitSorter.
- **Small and to the point.** One idea per design where possible, so a disagreement between the two
  simulators points straight at its cause.

## Layout

| Folder | What is in it |
| --- | --- |
| `smoke/` | Proves the harness works and pins the first facts down. |
| `delays/` | How gate and assignment delays behave: the probes `Docs/oracle-notes.md` describes. Each has a C# twin in `Tools/ci/Sim.Tests/Probes.cs`, so the engine is checked with and without the front end: `EngineOracleTests` runs the twin, `BehaviouralOracleTests` the file itself. |
| `structural/` | Designs in the shape of the course's first simulation exercises: BitSorter's own cell library with `Tpdlh` and `Tpdhl` parameters, a mux with a static hazard, a full adder, an ALU slice and a 64-bit ALU built with `generate` and a 65-bit carry. Files include each other, behind include guards. `StructuralOracleTests` reads and elaborates each in BitSorter, generates one random stimulus as a testbench for Icarus and as writes for BitSorter, and compares every signal of the design. |
| `processor/` | `mcpu.sv`, an original multicycle RV32I processor in the shape of the course's third exercise: a state-machine controller, a datapath, 1,024-word memories loaded from `mcpu_program.hex` (its assembly beside each word) and `mcpu_data.hex`, the shared numbers in the included `mcpu_defs.svh`, and a testbench that checks its results against `mcpu_expected.hex`, worked out by an independent emulator. Written from the published RISC-V specification. `BehaviouralOracleTests` compares every signal and line printed; `GuardrailTests` and the benchmark build time it. |
| `system/` | System tasks and memories: `printing.sv`, every `$display` conversion the course's testbenches use with x and z values, widths, `$write`, `$strobe` and `$monitor`; `memories.sv`, a RAM loaded by `$readmemh` and arrays declared every way round, with its images `memories.hex` and `memories.bin`. `BehaviouralOracleTests` compares every signal and every line printed. |
| `behavioural/` | Behavioural designs and testbenches: a Moore machine with a `typedef enum`, `always_ff` with an asynchronous reset and `always_comb` with defaults; `multiplier.sv`, a Mealy controller and a shift-and-add datapath with a testbench in the shape of the course's second exercise; and `scheduling.sv`, where processes run relative to each other -- the start order at time 0, nonblocking against blocking, `#0`, `always_comb` against `@*`. `BehaviouralOracleTests` runs each testbench in both and compares every signal. **Races only on purpose**: where the standard lets two simulators differ, the corpus either pins the order BitSorter chose to match Icarus -- the start order at time 0, in `scheduling.sv` -- or keeps out, and `Docs/oracle-notes.md` says which. |

Every file here is also read by the SystemVerilog reader's tests, which hold each to reading with no
diagnostic and printing to a fixed point (`Tools/ci/SystemVerilog.Tests/Syntax/PrinterTests.cs`). A
later part adds the processor corpus.
