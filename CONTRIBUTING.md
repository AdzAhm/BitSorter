# Contributing to BitSorter

BitSorter is a digital-logic simulator you play: bits travel through gates you
wire yourself, on a deterministic clock, and each level is a topic from a digital
systems course. Think of a waveform simulator such as ModelSim, but where you
build the circuit on a board and watch every bit move. Contributions are welcome:
bug reports, level ideas, new levels, fixes and features.

This file is the practical guide. The reasoning behind the design lives in
[Docs/design.md](Docs/design.md), and the full set of decisions (with the
mistakes that led to them) in [CLAUDE.md](CLAUDE.md). You do not need to read
either before reporting a bug. Skim both before changing code.

## Issues

Everything starts as an issue: a bug, an idea, a level, a question. Open one from
the [Issues tab](https://github.com/AdzAhm/BitSorter/issues/new/choose) and pick a
form. Each form asks for what is needed and applies the right label for you:

- **Bug report**: something doesn't work the way it should.
- **Feature request**: something the game or the simulator could do that it doesn't.
- **Level idea**: a topic or a puzzle that would make a good level.

A blank issue is fine for anything else. Search first: if someone has already
reported it, a comment there with what you saw is worth more than a second issue.

For a bug, the version (bottom-right corner of the main menu), where you were
playing, the level, and a screenshot of the board are usually all it takes.

Two things that are not bugs, so you do not spend time on them:

- **A correct truth table that still fails.** Bits that reach a gate on different
  ticks collide, and the run fails even if every answer would have been right.
  That is the timing lesson, not a grading error. The verdict names the gate.
- **Throughput and scores.** Nothing ranks you against anyone or against a par
  score, on purpose. See "The line on scoring" in the design notes.

### Labels

Labels say what an issue or pull request is about, so it can be found and picked
up. The forms add the first one; more are added as it is looked at.

| Label | Used for |
|---|---|
| `bug` | Something isn't working. |
| `enhancement` | A new feature or an improvement to an existing one. |
| `level` | A level idea, or a new or changed level. |
| `simulator` | The logic core: ticks, gates, timing and grading. |
| `interface` | What the player sees and clicks: the board, panels and controls. |
| `accessibility` | A barrier for people with disabilities: contrast, colour, text size, input. |
| `browser` | Happens only in the browser (WebGL) build. |
| `playtest` | Found by watching someone play. |
| `documentation` | The README, this file, the design notes or code comments. |
| `good first issue` | Small and well described: a good place to start. |
| `help wanted` | Nobody is on it yet, and help is welcome. |
| `question` | Needs an answer before anything can be done. |
| `duplicate`, `invalid`, `wontfix` | Closed without a change, and why. |

If you want to work on an issue, say so in a comment first, so two people do not
build the same thing.

## Suggesting or writing a level

A level is one JSON file in `Assets/Resources/Levels/`. Start from a level close
to the one you want; `pick-a-lane.json` and `pass-it-on.json` are short.

| Field | What it does |
|---|---|
| `name`, `order` | The title, and where it sits in the run. Orders need not be tens: a level between 70 and 80 can be 72. |
| `goal` | The objective, stated plainly. It may name gates. |
| `hint` | A nudge towards the answer. It **may not** name the gates that solve it; `CurriculumTests` checks. |
| `fixtures` | Sources (with a `stream` of 0s and 1s, one per vector) and sinks, each on a `cell`. |
| `budget` | The parts the player may place, with counts. A kind that is not listed cannot be placed. |
| `expected` | Per sink, the values it must receive: `0`, `1`, `x` (either is fine) or `-` (nothing). |
| `tickLimit` | When a circuit that never settles is given up on. |
| `maxWireDelay`, `delayBudget` | The longest one wire may be, and the total extra delay across all wires. |
| `maxLatency` | The longest the critical path may be, in ticks. |
| `clockPeriod` | Ticks between vectors. Sequential levels need one; see the design notes. |
| `board` | `{ "columns": 11, "rows": 7 }` for a board bigger than 9 by 5. Odd sizes, up to 13 by 7. |

A **vector** is one row of input values: every source plays its next bit at once,
and the sinks' expectations line up with the same rows.

Every level ships with a test class, `Assets/Tests/EditMode/<Name>LevelTests.cs`,
that builds a reference solution and shows it passes, fits the parts and delay
budget, and that the obvious wrong answers fail. Add the level to the list in
`CurriculumTests.TheRun_IsEveryLevelInItsTaughtOrder`, and to the level table in
the README. Work the timing out tick by tick before writing the file: "The
arithmetic every level design uses" in the design notes is the whole method.

Stay inside the syllabus. Boolean algebra, K-maps, functional completeness,
propagation delay, combinational components, adders, flip-flops, state machines,
critical path and pipelining are in. Assembly, the RISC-V datapath, memory
addressing and number representation are out. Hazards cannot be expressed at all,
because bits are discrete tokens with nowhere for a glitch to live.

## Setting up

1. Install **Unity 6.3 LTS (6000.3.11f1)** from Unity Hub. Other versions may
   upgrade the project in ways that are hard to review.
2. Clone the repository, or your fork of it (see
   [Forking and pull requests](#forking-and-pull-requests)), and open the folder in
   Unity Hub.
3. Run **BitSorter → Build Play Scene** once. The scene is generated from code,
   so this is how you get a scene that matches the code you have.
4. Press Play.

## Rules the code keeps

These are enforced by tests where they can be, and by review where they cannot.

- **`Assets/Scripts/LogicCore/` is pure C#** with no Unity references, and has
  its own assembly. It is the simulator. `Assets/Scripts/View/` draws it and
  never changes it.
- **The simulator is deterministic.** Nodes within one tick must give the same
  result in any order. Do not add anything that breaks that.
- **The scene is generated.** `HalfAdderDemoSceneBuilder` is the only authority
  on what is in it; anything added by hand is lost on the next build.
- **The interface is built in code.** Colours come from `Palette`, text sizes from
  `UiType`, and where a piece of the HUD sits from `UiRows`. No colour or size
  literals.
- **Keys and focus go through one place each:** `UiText.Keyboard` for reading
  keys, `UiTheme.Defocus` and `UiTheme.Focus` for focus, `PointerGate` for the
  mouse.
- **Anything shown to the player is derived.** The truth table, the K-map and
  every label come from the level itself, never from a second copy.

## Tests

Run them from **BitSorter → Run Tests**, or Window → General → Test Runner.
There are two suites: Edit Mode (no scene, fast) and Play Mode (the real scene,
on a scratch save that never touches your own progress). Use the PlayMode tab,
not the Player tab, which builds a whole player first.

- Every new simulator component gets a truth-table test.
- **A bug fix is two commits:** a test that fails and still compiles, then the
  fix that makes it pass. That keeps the history bisectable.
- **Visual changes are checked against the reference shots.** Run
  **BitSorter → Capture Reference Shots** before and after. Two captures of
  the same code are identical to the pixel, so a change that claims not to move
  anything can be held to it. Say in the pull request which shots changed and why.

CI runs the simulator's own tests on every push without Unity; locally that is
`dotnet test Tools/ci/LogicCore.Tests/LogicCore.Tests.csproj`.

## Forking and pull requests

You do not need write access to contribute. The usual GitHub flow:

1. **Fork** the repository with the Fork button at the top of its GitHub page. That
   makes your own copy under your account.
2. **Clone your fork** and add the original as `upstream`, so you can stay up to
   date with it:

   ```bash
   git clone https://github.com/<you>/BitSorter.git
   cd BitSorter
   git remote add upstream https://github.com/AdzAhm/BitSorter.git
   ```

3. **Make a branch** for your change, from an up-to-date `main`. One branch per
   change keeps each pull request about one thing:

   ```bash
   git fetch upstream
   git switch -c fix-collision-warning upstream/main
   ```

4. **Commit** as you go. One change per commit, with a short message in the style
   of the history: `feat: ...`, `fix: ...`, `test: ...`, `docs: ...`, `chore: ...`.
   Keep unrelated changes apart: a fix, a re-saved scene and a HUD tweak are three
   commits. If your change makes a paragraph in `CLAUDE.md`, the README or the design
   notes untrue, correct it in the same change.
5. **Push the branch to your fork** and open a pull request against `main` on
   `AdzAhm/BitSorter`. GitHub offers the button as soon as the branch is pushed:

   ```bash
   git push -u origin fix-collision-warning
   ```

6. **Fill in the template.** It asks what the change does, which issue it closes
   ("Closes #12" closes it when the pull request is merged), how you tested it, and
   for screenshots of anything visible.
7. **Review.** Expect questions and small requests; push more commits to the same
   branch to answer them. CI runs the simulator's tests on every push.

If `main` moves on while you work, bring your branch up to date with
`git fetch upstream` then `git rebase upstream/main`, and push again with
`git push --force-with-lease`.

Two kinds of change need an issue and a yes first, because they change a promise
made to players: anything about what data the game sends (see "What it
collects" in the README), and anything that ranks players.

## Licence

By contributing you agree that your work is released under the project's MIT
licence ([LICENSE](LICENSE)). Anything you bring in from elsewhere (a sound, a
font, an image) must carry a licence that allows shipping it in a game, and needs
a credit in the README with a link to where it came from.
