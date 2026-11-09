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
| `processor/` | Three original RV32I processors written from the published RISC-V specification, sharing the numbers in the included `rv32i.svh`. `mcpu.sv`, multicycle, in the shape of the course's third exercise: a state-machine controller, a datapath, 1,024-word memories loaded from `mcpu_program.hex` (its assembly beside each word) and `mcpu_data.hex`, and a testbench that checks its results against `mcpu_expected.hex`, worked out by an independent emulator. `scpu.sv`, single-cycle, and `pcpu.sv`, pipelined with forwarding, a load stall and a flush; these two and `mcpu.sv`'s second testbench, `mcpu_demo_tb`, run one of three programs on `rv_data.hex`, chosen by the testbench's `PROGRAM` (Part 15h-6c) -- `rv_sum.hex`, the sum of 1 to 10; `rv_fib.hex`, ten Fibonacci numbers; `rv_isa.hex`, every instruction they run once, checking itself -- and check the program's own `_expected` image. The names are one length: a `?:` picks them, and Icarus 12 reads no file name a shorter string padded with a NUL. They are the start page's three processor samples (Part 15h-6). `BehaviouralOracleTests` compares every signal and line printed of each testbench, named; `GuardrailTests` and the benchmark build time mcpu. |
| `system/` | System tasks and memories: `printing.sv`, every `$display` conversion the course's testbenches use with x and z values, widths, `$write`, `$strobe` and `$monitor`; `memories.sv`, a RAM loaded by `$readmemh` and arrays declared every way round, with its images `memories.hex` and `memories.bin`. `BehaviouralOracleTests` compares every signal and every line printed. |
| `behavioural/` | Behavioural designs and testbenches: a Moore machine with a `typedef enum`, `always_ff` with an asynchronous reset and `always_comb` with defaults; `multiplier.sv`, a Mealy controller and a shift-and-add datapath with a testbench in the shape of the course's second exercise; and `automatic_tasks.sv`, automatic tasks giving each call its own variables -- at once in a fork, calling themselves and each other, and a block disabled from deep inside their calls; and `scheduling.sv`, where processes run relative to each other -- the start order at time 0, nonblocking against blocking, `#0`, `always_comb` against `@*`. `BehaviouralOracleTests` runs each testbench in both and compares every signal. **Races only on purpose**: where the standard lets two simulators differ, the corpus either pins the order -- the start order at time 0, in `scheduling.sv`, which BitSorter begins as ModelSim does since M5 and as Icarus does under `StartOrder.Icarus`, the order the Icarus oracle runs in -- or keeps out, and `Docs/oracle-notes.md` says which. |
| `import/` | Projects in the shape a course hands them in, one folder each, for importing (Part 13b): `alu/`, a behavioural ALU taking its operation codes from an include inside the module, and a testbench that says its `timescale`; `mux/`, a multiplexer of gates over a small gate library, which an import draws; `counter/`, three modules wired by name and a testbench with a free-running clock. `ImportOracleTests` imports each folder, and `processor/` with them, runs the project in BitSorter as Icarus runs the folder's files, and runs it exported again. |

Every `.sv` file here is also read by the SystemVerilog reader's tests, which hold each to reading with no
diagnostic and printing to a fixed point (`Tools/ci/SystemVerilog.Tests/Syntax/PrinterTests.cs`). Their
includes are `.svh` files, which those tests and the elaborator's give a read to; a course's `.inc` is covered by
`ProjectImportTests` instead.
