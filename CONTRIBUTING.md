# Contributing to BitSorter

BitSorter is a SystemVerilog simulator for the Digital Systems and Computer Structure
course. You draw a circuit or write it, run it on a 4-state event-driven engine with real
gate delays, read its waves, and check it against thirty-five Exercises. Think of a
simulator such as ModelSim, with a drawing board built in. Contributions are welcome: bug
reports, Exercise ideas, new Exercises, fixes and features.

This file is the practical guide. The architecture and the reasons for it live in
[Docs/architecture-5.md](Docs/architecture-5.md), and the rules and the decisions behind
them in [CLAUDE.md](CLAUDE.md). You do not need to read either before reporting a bug.
Skim both before changing code. [Docs/design.md](Docs/design.md) holds the design notes of
versions up to 4.0.0, kept for the history.

## Issues

Everything starts as an issue: a bug, an idea, an Exercise, a question. Open one from the
[Issues tab](https://github.com/AdzAhm/BitSorter/issues/new/choose) and pick a form. Each
form asks for what is needed and applies the right label for you:

- **Bug report**: something doesn't work the way it should.
- **Feature request**: something the simulator could do that it doesn't.
- **Exercise idea**: a concept from the course that would make a good Exercise.

A blank issue is fine for anything else. Search first: if someone has already reported it,
a comment there with what you saw is worth more than a second issue.

A bug report needs four things: the version (at the foot of the start page), whether you
were in a browser or on Windows, what you drew or wrote, and what you expected on the waves
against what BitSorter showed. For the third, the best thing is the project itself: FILE,
EXPORT PROJECT saves a zip of every file the circuit is, which anyone can open with IMPORT.
Say which look you had, under SETTINGS, STYLE, if the bug is in how something is drawn, and
attach a screenshot.

Three things that are not bugs, so you do not spend time on them:

- **A glitch on an output.** Gates have delays, so paths of different lengths into one gate
  make a short wrong pulse, as in hardware. BitSorter shows hazards; it does not teach
  them, and does not remove them.
- **A race between processes.** Two processes touching one signal at the same moment can
  run in either order, and ModelSim and Icarus Verilog disagree with each other about it.
  BitSorter runs them in a fixed order, begins time 0 as ModelSim does, and warns in the
  TRANSCRIPT where the order decides a value. Change the testbench, or use `<=`.
- **Rankings and scores.** Nothing compares anyone with anyone else or with a par score,
  on purpose. Timing is a fact about your own circuit.

If a bug is that BitSorter and Icarus Verilog or ModelSim give different results for the
same code, say so, and give the code: that is the most useful report there is.

### Labels

Labels say what an issue or pull request is about, so it can be found and picked up. The
forms add the first one; more are added as it is looked at.

| Label | Used for |
|---|---|
| `bug` | Something isn't working. |
| `enhancement` | A new feature or an improvement to an existing one. |
| `exercise` | An Exercise idea, or a new or changed Exercise. |
| `simulator` | The engine and the language: values, delays, the SystemVerilog it reads and prints. |
| `interface` | What you see and click: the workbench, its panels, the drawing and the waves. |
| `accessibility` | A barrier for people with disabilities: contrast, colour, text size, input. |
| `browser` | Happens only in the browser (WebGL) build. |
| `documentation` | The README, this file, the architecture notes or code comments. |
| `good first issue` | Small and well described: a good place to start. |
| `help wanted` | Nobody is on it yet, and help is welcome. |
| `question` | Needs an answer before anything can be done. |
| `duplicate`, `invalid`, `wontfix` | Closed without a change, and why. |

If you want to work on an issue, say so in a comment first, so two people do not build
the same thing.

## Suggesting or writing an exercise

An Exercise is one JSON file in `Assets/Resources/Exercises/`, with its reference solution
as SystemVerilog in `Tools/ci/Exercises/solutions/<id>.sv`. Start from one close to the one
you want; `pick-a-lane.json` and `pass-it-on.json` are short.

| Field | What it does |
|---|---|
| `id` | What it is known by for good: solves, saved answers and analytics are kept under it, so it never changes. The first thirty are the 4.x levels' names. |
| `title`, `order`, `chapter` | Its name, and where it sits in the run. Orders need not be tens. The run goes chapter by chapter. |
| `goal` | The objective, stated plainly. It may name parts. |
| `hint` | A nudge towards the answer. It may name at most one of the parts it stocks, and never which part makes which output; `ExerciseCurriculumTests` checks. |
| `inputs`, `outputs` | Each a `name`, its `width` -- 1 when left out -- and its `values`: one character a vector for a bit (`0110`), or for a bus a binary word of exactly `width` digits a vector, with spaces between (`"width": 4, "values": "0101 1100"`; left at 1, each word is refused as "no value of 1 bit"). An `x` in an output leaves that answer free. |
| `parts` | What it stocks: a gate's `kind` (or `Register`), or a block's module under `block`, each with a `count` -- -1, or left out, is any number -- and a gate's `inputs` where it is not the usual one for NOT and BUF and two for the rest. |
| `delay`, `constants`, `clock`, `cycle` | Every gate's delay; whether constants may be drawn; whether it has a clock, `clk`; how long each vector has (0 works it out). |
| `settleBy`, `period` | A timing Exercise's slowest input-to-output path, or a pipelining one's clock period, that the design must meet. |
| `start`, `blocks` | The circuit it opens on, and the blocks it stocks, as structural SystemVerilog. A `start` must fail as it opens, for the reason its goal gives. |
| `code`, `registerStart` | Whether it may be answered in code; what a register starts at (`0`, `1`, or empty for x). |

`ExerciseCurriculumTests` holds every file: it opens, its solution solves it, it is in
order, its goal and hint keep their rules, and a thing is taught before it is asked for.
`ExerciseOracleTests` runs every solution's testbench under Icarus Verilog too.

An idea for an Exercise needs the concept it teaches, the course topic it belongs to, the
goal in a sentence or two, and how CHECK would judge it: the inputs it drives and the
outputs it expects. The Exercise idea form asks for those.

Stay inside the syllabus. Boolean algebra, K-maps, functional completeness, propagation
delay, combinational components, adders, flip-flops, state machines, critical path and
pipelining are taught. The course's RISC-V hardware is simulated but not taught, hazards
are shown but not taught, and assembly and memory addressing are out.

## Setting up

1. Install **Unity 6.3 LTS (6000.3.11f1)** from Unity Hub. Other versions may upgrade the
   project in ways that are hard to review.
2. Clone the repository, or your fork of it (see
   [Forking and pull requests](#forking-and-pull-requests)), and open the folder in Unity
   Hub.
3. Run **BitSorter → Build Play Scene** once. The scene is generated from code, so this is
   how you get a scene that matches the code you have.
4. Press Play.

To work on the engine, the language or the document you need no Unity at all. Install the
.NET SDK (10.0) and run `dotnet test Tools/ci/BitSorter.Ci.slnx`. Install
[Icarus Verilog](https://steveicarus.github.io/iverilog/) too, or the oracle tests are
skipped.

## Rules the code keeps

These are enforced by tests where they can be, and by review where they cannot.

- **`Assets/Scripts/SystemVerilog`, `Sim`, `Board` and `ViewModel` are pure C#** with no
  Unity references, no reflection, no threads and no file access, each its own assembly,
  pointing one way: SystemVerilog <- Sim <- Board <- ViewModel <- View.
  `Assets/Scripts/View/` draws them and changes them only through their commands.
  `Tools/ci/Repo.Tests` holds this, and refuses a file under `View/Workbench` that uses no
  Unity, because that belongs in the view-model.
- **The engine is deterministic.** The order drivers are visited in within a delta cycle
  can never change a run, and a test shuffles it. Do not add anything that breaks that.
- **Nothing from the course comes into this repository.** The course's files hold student
  ID numbers and the university's own code, the repository is public, and git keeps
  everything. Write examples from scratch, in the course's style. A test scans every
  hand-written file for anything that looks like an ID number, and fails on one; do not
  add one to its allowlist unless you have checked that it is not an ID. If you have the
  course's files on your machine, never copy one into the repository, a test, an issue or
  a pull request.
- **The scene is generated.** `HalfAdderDemoSceneBuilder` is the only authority on what is
  in it; anything added by hand is lost on the next build.
- **The interface is built in code.** Colours come from `Palette` and the looks'
  `SchematicPalette`s, text sizes from `UiType`. No colour or size literals.
- **Keys go through one place:** `UiText.Keyboard` for reading keys, which is null while a
  text field has them.
- **Anything shown is derived.** The truth table, the K-map and the state diagram are the
  engine run, never a second copy.

## Tests

- **Without Unity:** `dotnet test Tools/ci/BitSorter.Ci.slnx` builds the pure layers
  against netstandard2.1, the profile Unity compiles them against, and runs their tests,
  the repository's rules and the oracle. CI runs it on every push.
- **The oracle.** Icarus Verilog is the simulator BitSorter's engine is held to. Every
  construct gets a test, and a comparison against Icarus wherever it can run it, with the
  designs in `Tools/ci/Oracle/corpus`. Where Icarus and the language standard disagree, the
  standard wins and the difference is written in [Docs/oracle-notes.md](Docs/oracle-notes.md).
  CI installs Icarus and fails if it is missing, rather than skipping. ModelSim is a second
  oracle, run wherever it is installed and skipped where it is not, since it is licensed.
- **In the editor:** **BitSorter → Run Tests**, or Window → General → Test Runner. There are
  two suites: Edit Mode (no scene, fast) and Play Mode (the real scene, on a scratch save
  that never touches your own progress). Use the PlayMode tab, not the Player tab, which
  builds a whole player first.
- **A bug fix is two commits:** a test that fails and still compiles, then the fix that
  makes it pass. That keeps the history bisectable.
- **Visual changes are checked against the reference shots.** Run **BitSorter → Capture
  Reference Shots** before and after. Two captures of the same code are identical to the
  pixel, so a change that claims not to move anything can be held to it. Say in the pull
  request which shots changed and why.

## Forking and pull requests

You do not need write access to contribute. The usual GitHub flow:

1. **Fork** the repository with the Fork button at the top of its GitHub page. That makes
   your own copy under your account.
2. **Clone your fork** and add the original as `upstream`, so you can stay up to date with
   it:

   ```bash
   git clone https://github.com/<you>/BitSorter.git
   cd BitSorter
   git remote add upstream https://github.com/AdzAhm/BitSorter.git
   ```

3. **Make a branch** for your change, from an up-to-date `main`. One branch per change
   keeps each pull request about one thing:

   ```bash
   git fetch upstream
   git switch -c fix-wave-cursor upstream/main
   ```

4. **Commit** as you go. One change per commit, with a short message in the style of the
   history: `feat: ...`, `fix: ...`, `test: ...`, `docs: ...`, `chore: ...`. Keep unrelated
   changes apart: a fix, a re-saved scene and a panel tweak are three commits. If your
   change makes a paragraph in `CLAUDE.md`, the README or the architecture notes untrue,
   correct it in the same change.
5. **Push the branch to your fork** and open a pull request against `main` on
   `AdzAhm/BitSorter`. GitHub offers the button as soon as the branch is pushed:

   ```bash
   git push -u origin fix-wave-cursor
   ```

6. **Fill in the template.** It asks what the change does, which issue it closes ("Closes
   #12" closes it when the pull request is merged), how you tested it, and for screenshots
   of anything visible.
7. **Review.** Expect questions and small requests; push more commits to the same branch to
   answer them. CI runs the tests that need no Unity on every push.

If `main` moves on while you work, bring your branch up to date with `git fetch upstream`
then `git rebase upstream/main`, and push again with `git push --force-with-lease`.

Two kinds of change need an issue and a yes first, because they change a promise made to
the people who use BitSorter: anything about what data it sends (see "What it collects" in
the README), and anything that ranks people against each other or against a par score.

## Licence

By contributing you agree that your work is released under the project's MIT licence
([LICENSE](LICENSE)). Anything you bring in from elsewhere (a font, an image, a library)
must carry a licence that allows shipping it in a program, and needs a credit in the
README's Licence section, and in the credits if its licence asks for one, with a link to
where it came from.
