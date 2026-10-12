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

Later parts add folders for the structural, behavioural and processor corpora.
