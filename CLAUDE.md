# BitSorter

A SystemVerilog simulator for the Digital Systems and Computer Structure course,
built in Unity: draw a circuit or write it, run it on a 4-state event-driven
engine, read its waves, and check it against thirty-five Exercises. It began as
BitSorter 4.x, a puzzle game in which bits fell through logic gates into bins;
4.0.0 is still played on Unity Play, and its thirty levels are the first thirty
Exercises.

## This document
This file is authoritative, and it drifts silently — nothing recompiles
when it goes stale, and no test turns red. When you hit a section that no
longer matches the code, say so and correct it in the same change. Do not
quietly work around a stale paragraph, and do not treat one as binding
just because it is written down here.

## 5.0 (released 2026-11-10)
**5.0.0 is released** (tag `v5.0.0`): on GitHub Pages, and on Windows as a zip on its
GitHub release. It was built on a branch of its own, `v5`, a PR a part, and `main`
was fast-forwarded to it at the release, when `v5` and every merged branch were
deleted. **What comes after 5.0.0 is built on `v5.1`** (Ahmad, 2026-11-11), made from `main`:
fixes and additions each a PR into it, and `main` fast-forwarded to it when 5.1.0 is
released. BitSorter was re-founded as a SystemVerilog simulator for the course, with the
thirty levels kept as side *Exercises* (the brief's decisions, 2026-10-11).
**`Docs/architecture-5.md` is the architecture, the data model and the parts --
read it before any 5.0 work.** 5.0 was built beside the 4.x game, which Part 15
replaced and **Part 16a deleted**: its token engine (LogicCore), its board and
HUD, its levels, free play, its tutorial, its title screen and its save's boards
are gone, and an old save's 4.x keys are not read (Ahmad, 2026-11-02: "a new
phase in the project"). Part 16 continues in `Docs/architecture-5.md`'s plan.

**BitSorter opens as a simulator**: on the last thing that was open -- a circuit,
an exercise or a sample -- and with nothing, on the workbench's start page. HOME,
FILE, EXERCISES, HELP and SETTINGS head the workbench, and there is no BACK:
Escape closes what is on top and nothing more. **An exercise is a document kept
under its id**, the 4.x level's name for the thirty, and a solve marks that name
complete -- the name analytics reports it under too.

## Architecture rule (do not break this)
- Assets/Scripts/SystemVerilog/, Sim/, Board/ and ViewModel/ = the four pure
  layers -- SystemVerilog, the engine, the document and the workbench's
  view-model -- each with its own asmdef: C# 9, no engine references, no
  reflection, no threads, no file access of their own, so they run unchanged
  under dotnet, IL2CPP and a browser. They point one way, SystemVerilog <- Sim
  <- Board <- ViewModel <- View, and the view changes the document and the
  engine only through their commands. `Tools/ci/Repo.Tests` holds all of it:
  the asmdefs, what the CI builds actually reference, and the source -- and
  refuses a file under View/Workbench that uses no Unity, which belongs in the
  view-model. **The view-model's points are `Vec2`, not Unity's `Vector2` nor
  `System.Numerics`'**: Unity's arithmetic to the bit, held by an Edit Mode test
  against the real thing, so a test under dotnet speaks for what is drawn.
- Assets/Scripts/View/ = Unity MonoBehaviours. Reads the engine and the
  document, renders them, and changes them only through their commands.

## Conventions
- Every construct gets a test, and a comparison against Icarus Verilog
  wherever the oracle can run it (`Tools/ci/Oracle`, the `oracle-compare`
  skill). Where Icarus and the language standard disagree, the standard wins
  and the difference is written down.
- **Exercises are JSON in Assets/Resources/Exercises/** (Part 15c), each with
  its reference solution in Tools/ci/Exercises/solutions/, held under dotnet by
  `ExerciseCurriculumTests` and, against Icarus, `ExerciseOracleTests`. The
  thirty keep their levels' file names as ids; five more are new (share the
  wire, start from nothing, find the slow path, cut it in two, keep in step).
  Each names its own `order` and `chapter` -- play order is authored -- and the
  run goes chapter by chapter, the combinational chapters before the clocked
  ones; two exercises in one place is an error.
- **An exercise may open on a circuit** -- `start` -- for one about finding what
  is wrong with it. It must fail as it opens, and for the reason its goal gives
  (`ExerciseCurriculumTests`).
- **`goal` states the objective and may name parts outright. `hint` may not**:
  a hint names at most one of the parts it stocks, and never says which part
  makes which output. Before `goal` existed the 4.x hint had to carry both
  jobs, which is how the half adder ended up with a hint that named both its
  gates and said which output each produced.
- A thing is taught before it is required: the first exercise to ask each
  thing says so in its goal (`ExerciseCurriculumTests`).
- **A don't-care is written one of two ways** (`Docs/level-roadmap.md`, "On
  `x`"). A combination that can never happen is left out of the inputs, which
  is why `dont-care` has ten rows and not sixteen. An answer that matters to one
  output and not another is an `x` in that output's values, since rows are
  shared by every output. `dont-care` teaches the idea, and
  `AnAnswerLeftFree_ComesAfterTheDontCareExercise` keeps every exercise that
  uses it after it.
- **There are three kinds of teaching, and they must stay apart** (Part 15f): an
  exercise's `goal` and `hint`; the simulator's first-time hints
  (`SimulatorHints`, the view-model's) -- a gate's delay, a clock, a z, a glitch
  on an output, an x at a check -- fired by what the run shows, each once, kept
  in `hintsSeen` under ids of their own and marked as they go up, on a strip
  along the circuit window's foot; and the guided tutorial (`GuidedTutorial`),
  on the first exercise, from the start page and HELP, which teaches which input
  does what, reads its step off the document, lights the control it means and is
  skipped with SKIP. `SimulatorHintTests` holds every hint and tutorial step to
  the four-word rule against each other and every exercise's goal and hint: no
  run of four words shared, so nothing is read twice.

## My level
I'm a 3rd-semester CE student, new to Unity and git.
Explain things as you go and tell me when I'm about to do something dumb.

## Syllabus scope

What BitSorter teaches, what it only simulates or shows, and what it leaves
alone (the brief's D1, D10 and D11, 2026-10-11).

**Taught, in the Exercises:** boolean algebra, K-map minimization, functional
completeness, propagation delay, combinational components, adders,
flip-flops, FSMs, critical path, pipelining.

**Simulated, not taught:** the course's RISC-V hardware, multicycle included,
with its memories and `$readmemh` (D10). The simulator has to run it; no
Exercise has to teach it.

**Shown, not taught:** hazards. In the 4-state model with gate delays a glitch
is real and appears on screen, but no Exercise sets out to teach static or
dynamic hazards (D1, reconfirmed after the syllabus listed them; the course's
own simulation work does not ask students to guard against them).

**Displayed, not taught:** number representation. A signal reads in binary,
hex, or unsigned or signed decimal (D11); what two's complement *is* stays a
different game.

**Timing is a fact about the student's own circuit, never a ranking.** Static
timing (Part 14a, on the workbench in 14b's TIMING tab) works out a design's
minimum period -- the slowest register-to-register path, clock to Q and setup
included -- and the frequency it gives, so cutting a long path with a register
shortens the period and raises the rate, which the pipelining Exercises are
built on. Since Part 15i-1 throughput is shown as the course gives it -- one
result a clock period -- beside a K-stage pipeline's latency, K periods; a
design whose paths from its inputs to its outputs cross different numbers of
registers, or run round a state machine's loop, is said to have no one latency
(see "Still off limits" below).

**Out of scope:** assembly programming and memory addressing -- software,
not hardware. Don't reach for them even when an Exercise looks like it could
stretch that way.

## Core decisions

- **The order drivers are visited in can never change a run.** Each delta cycle
  applies everything due before any driver is evaluated, so the history is the
  same in any order (`Docs/architecture-5.md`, sections 5 and 9; a test shuffles
  it). Processes are the one place the language itself allows a race -- a
  blocking write is seen at once -- so they run in a fixed order instead: an
  instance's processes before its parent's own, then as written, as Icarus
  starts them -- except that a scope's interfaces are made before its modules,
  which may be given one through a port (Part L8). Never let that order depend
  on anything else. A fork's branches (Part L5) start straight after the process
  that forks them, in the order written, once it waits -- and a later fork's
  ahead of an earlier one's not yet started, as ModelSim starts them (16c); Icarus 12 starts them
  last first, and the standard leaves it open (`Docs/oracle-notes.md`). Time 0
  begins as ModelSim begins it, Icarus's way one setting away (M5,
  `SimOptions.StartOrder`).
  **Where that order decides a value, the run says so** (Part 15i-3): a
  process woken by an edge -- 0 to 1 or 1 to 0, never out of x -- that reads,
  on its way to its next wait, a signal another process changed by a blocking
  write in the same region of the same moment gets one TRANSCRIPT warning for
  the pair. Each change carries the process it came from through drivers of no
  delay and through logic it woke (`Simulator`'s origins); never a nonblocking
  write, which lands in the NBA region; never two processes a chain of causes
  orders -- the writer made the edge, however far back, the reader set the
  writer going, or one forked the other; never a change from several processes
  at once. It is by signal, not by bit: a write to `data[7]` is a race with a
  read of `data[0]`. No sample, exercise answer or behavioural, system or
  processor corpus testbench races (`RaceFreeRunsTests`, and the corpus as the
  oracle runs it): the traffic light's testbench did, and drives its button
  with `<=` now.

- **An id is given once and never again.** A module gives a part or a wire an id
  from `Module.NextId`, and a deleted one's id is retired, never reissued, so a
  layout, an undo step or a picked signal keyed by id never lands on something
  else (`Docs/architecture-5.md`, section 5). The 4.x engine kept the same
  promise for its nodes and edges, and the reason has not changed.

- **Undo is a stack of whole documents, not inverse operations** (`BoardEditor`).
  The document is immutable, so a snapshot is a reference rather than a copy,
  and every edit is checked by building the document it would make -- one that
  adds an error is refused with that error, and leaves no step. A group of edits
  -- a paste, MAKE BLOCK, a drawn import -- is one step, checked once as the
  document it ends on: an error one edit in it makes and a later one mends is no
  refusal, and one it leaves refuses the whole group with the first error new to
  that document (the 16d sweep: each edit checked the whole project, so a paste
  cost the square of the drawing). An edit whose whole design no run can be made
  of, which the rules let through, checking each module alone, leaves no step
  either: the session refuses it once it tries the run, and takes it back with
  the history as it was, redo included (`BoardEditor.TakeBack`, the 16d sweep).
  A drag is one
  step, as a run of scroll notches on one wire was in 4.x. Inverses were
  rejected there because the edits that remove things take wires with them, so
  their inverses are snapshots of part of the board anyway.

- **The save is `progress.json`, `format: 5`** (`ProgressStore`): the documents
  under `projects`, what is open and recent, the solves and first-time hints by
  name, the bests, the locked and the imported, each sample's program, and the
  code typed into a document's written modules and not applied, by module
  (`drafts`; Ahmad, 2026-11-03: code typed with an error is kept as a draft,
  shown back marked not applied, never lost on stepping out, another circuit,
  a sample or quitting) -- `Docs/architecture-5.md`, section 8. Every key added
  later is read as absent by an older file. 4.x had no version field, and its boards, free play and
  blocks are no longer read -- its solves and hints still are, being the same
  names. **Nothing is written over a save this window did not read** (Ahmad's
  decisions, 2026-11-03): every save carries a fresh `stamp`, first in the file,
  and a store whose save was written since it read it -- a second BitSorter, or
  in a browser a second tab, which the page tells through localStorage
  (`BrowserSave`) -- is held (`SaveHold`), as is one whose file was there and
  could not be read: the chip says so until FILE, RELOAD THE SAVE reads it again.
  A save from a newer BitSorter -- its format past 5, or a key this one does not
  know at any depth (`SaveFile.NotKept`) -- is said to be one as BitSorter opens,
  and copied beside itself, dated, before the first write. A document the save
  keeps that cannot open is left as it is, the reference circuit shown in its
  place (`WorkbenchSession.TryOpen`).

  a sample or quitting -- but a browser runs nothing as its tab closes, so
  there the typing of the two seconds before it closes can be) --
  `Docs/architecture-5.md`, section 8. Every key added
  later is read as absent by an older file. 4.x had no version field, and its
  boards, free play and blocks are no longer read -- its solves and hints still
  are, being the same names.

## Working agreement
- **The loop below, with its exact calls, is the `unity-test-loop` skill**
  (`.claude/skills/unity-test-loop/`): compile, check the DLL, Edit Mode, Play
  Mode, capture and diff, rebuild the scene, commit. Its scripts are the shot
  differ and a hunk stager. This section says why; the skill says how. **One
  editor serves every session on this machine**, and a compile during another
  session's test run kills it (2026-09-28), so agree who drives first.
- **Everything that does not need Unity runs with `dotnet test
  Tools/ci/BitSorter.Ci.slnx`**, as CI runs it: the pure layers built against
  netstandard2.1 (Unity's profile, so an API Unity lacks fails here first) and
  their tests, the repository rules, and the Icarus oracle. CI installs Icarus
  and sets `BITSORTER_REQUIRE_ICARUS`, so a skipped oracle there is a failure
  rather than a quiet pass. **ModelSim is the second oracle** (Part 15i-4,
  `Tools/ci/Oracle/ModelSim.cs`): every corpus testbench runs under it too where
  it is installed -- this machine's Intel Starter Edition -- and is skipped where
  it is not, CI among them, since it is licensed and no runner has it. What it
  settles otherwise than Icarus is named, each case written in
  `Docs/oracle-notes.md`, and nothing else is let through.
- **Nothing from the course comes into this repository.** Its files hold
  student ID numbers and the university's own code, the repository is public,
  and git keeps everything. Examples are written from scratch in the course's
  style. `Tools/ci/Repo.Tests` scans every hand-written file for anything that
  looks like an ID; the `privacy-scan` skill also checks the working tree and
  all of history against the course folder itself, and is run before every
  push. A number it finds in a commit is a stop: ask before pushing anything.
- Use Plan mode for anything touching more than one file.
- After editing scripts, recompile and run the tests yourself, and don't ask
  me to click anything. An `AssetDatabase.Refresh` plus
  `CompilationPipeline.RequestScriptCompilation` through the MCP server
  rebuilds with the window in the background, and Play Mode runs unfocused too
  now that nothing in the suite waits on wall clock. If some future thing does
  need the window in front, Windows hands focus to `SetForegroundWindow` after
  an `AttachThreadInput` from the current foreground thread, with
  `WScript.Shell.AppActivate` as a fallback -- but prefer fixing whatever
  needed it.
- **Check the editor is idle before starting a run.** `TestRunnerApi.Execute`
  during play mode throws `InvalidOperationException: This cannot be used
  during play mode`, and the exception surfaces as an unhandled log message
  inside whichever test was running -- so it fails somebody else's test and
  looks like their bug. Ask `EditorApplication.isPlaying` and `isCompiling`
  first. This happened once, to a run Ahmad had started.
- **When Unity stops recompiling, reimport the `.asmdef`.** An editor left
  open for a long session can stop rebuilding entirely: `AssetDatabase
  .Refresh`, `ImportAsset(ForceUpdate)`, `CompilationPipeline
  .RequestScriptCompilation` and even a play-mode cycle all report success
  and produce nothing. Reimporting the assembly definition marks the
  assembly itself dirty and is the trigger that works.

  **Check the DLL, not the test count.** A stale domain runs the previous
  assemblies and reports a full green pass that proves nothing — that
  happened three times in one session before the count not moving gave it
  away. `Library/ScriptAssemblies/*.dll` timestamps, or grepping one for a
  symbol you just added, is the honest check.

  To verify compilation without Unity at all, Bee leaves the exact compiler
  invocation in `Library/Bee/artifacts/*/BitSorter.*.rsp`; redirect `-out`
  and run it through the editor's own Roslyn. That catches compile errors in
  seconds and is independent of whatever state the editor is in.
- **Play Mode tests load the real scene, and must put it back.** `TestScene.Load`
  and `TestScene.Clear` are the only way in and out. `LoadSimulator` boots as the
  game does, on the start page with a fresh save; `Load` boots the same and then
  opens the reference circuit, the first sample -- where every workbench fixture
  written before Part 16a begins, since the 4.x title screen's button opened the
  workbench there on a fresh save. A fixture that loads the game and simply
  finishes leaves it loaded for whatever runs next, and NUnit orders fixtures
  alphabetically -- a fixture once ran first and handed a fully built game
  to a fixture that built its own canvas and assumed nothing else was on screen.
  Two of its four tests went red with none of its own code changed. A test
  leaking into another test is worse than either failing: the red lands in the
  fixture that is still correct.

  **`TestScene.Clear` takes the save with the game**, deleting it after the
  unload, because the game writes the save until it is gone: the workbench
  writes MY CIRCUIT half a second after an edit, and code typed into it two
  seconds after (its drafts), by real time, so a circuit from one load would
  reach the next whenever a frame ran slow. A fixture's own
  `[TearDown]` cannot do it: Unity Test Framework runs it before the
  `[UnityTearDown]` that unloads, with the game still loaded. And a test that
  loads the game more than once clears it between loads too.

  **They would also touch the player's own files.** `SaveGuard.Redirect` points
  the save, the PlayerPrefs settings, the workbench's exports (`FileSaver`, Part
  9b-4) and its Import folder (`FileImport`, Part 13d) at scratch copies before the
  scene loads (see "Never move the player's save" below), and has every import's
  Windows Open dialog cancelled unless the test answers it (`FileImport.Chooser`,
  `SaveGuard.AnswerTheNextChoice`; 5.1) -- the dialog is modal and waits for a person,
  so one opened by a test would hold the run up for ever -- and QUIT's answer quitting
  nothing (`WorkbenchPanel.Quitter`; 5.1) -- and every fixture then
  turns analytics off -- `GameAnalytics.Boot` runs on AfterSceneLoad, and an
  exercise opened or solved would otherwise post real events from a test run,
  into the one measurement the game collects.

  **Drive the clock, do not wait for it**, and **an assertion about absence needs
  a paired test proving the thing is possible.** A 4.x test once waited ninety
  frames for the clock to produce something; it produced nothing, with no
  explanation, and the half of the pair asserting that nothing happened *passed*
  -- an assertion of absence is trivially satisfied when nothing could have happened.

  **And while a test has the game loaded, its clock is driven**: `TestScene.Load`
  makes every frame `TestScene.FrameSeconds`, a sixtieth of a second of game time,
  and `TestScene.Clear` gives real time back (`TestClockPlayTests` holds both). A
  cap in frames used to mean seconds only at the editor's frame rate. Headless,
  frames are drawn unthrottled: on 2026-10-12 a 1200-frame cap meant to cover 1.5
  seconds of a hint covered as little as 0.57. A fixture that wants its own step
  sets it before the load and keeps it (the reference shots run at a 120th); one
  that changes the step mid-test puts `FrameSeconds` back, never 0. **The
  unscaled clock is not driven** and stays real time, so a wait on something
  counted in it -- the save's delay, the frame-rate counter -- caps in unscaled
  seconds, never in frames.

  **Never move the player's save.** `SaveGuard` points `ProgressStore.Redirected`
  at a scratch file and the real `progress.json` is not written, moved or deleted
  by any test -- it is only read, for a dated safety copy beside it whenever it has
  changed since the last one. That copy was meant to be once a session, but its
  flag is a static that every Play Mode run resets, and 210 identical copies had
  piled up before anyone looked. Two earlier designs moved it aside and moved it back, and
  both made the player's data depend on a run finishing cleanly: the first
  destroyed a save outright — after an interrupted run the stash held the real
  file and the live path held test debris, and the "clean up the stale stash"
  branch deleted the wrong one — and the second still left the file *missing* on
  an interrupt, so the game opened looking like a fresh install. Redirecting
  removes the class instead of handling it.

  **Results come from the framework, not from a callback.** Entering play mode
  reloads the domain and destroys any `TestRunnerApi` callback registered from a
  RunCommand, so Play Mode results cannot be collected that way. Unity writes them
  to `TestResults.xml` under `persistentDataPath` regardless. Read that.

  **A run no longer needs the window focused, and nothing here should start
  waiting on wall clock again.** It used to: play mode in the background renders
  far fewer frames a second, and seven of the 4.x game's audio tests (gone with the
  sound, Part 16b) waited five seconds of *wall clock* for music that was built a
  slice a frame -- too few frames fit in those five seconds, the track never
  finished, and every one of them failed saying the level track never replaced the
  menu track. Reproducible, so it did not read as a flake, and the messages
  pointed straight at the music. It cost a bisect on 2026-09-20 before anyone
  thought to check focus.

  The fix was to wait on the condition instead: drive the clock, do not wait for
  it. The suite now passes with the editor behind something else, and on the day
  ran in 42 seconds rather than 102 (twice the tests later, about 85). **A `yield`
  loop counting seconds is the bug, not the frame rate** -- anything here that
  needs to wait waits on the condition, with a generous cap that fails loudly
  rather than returning quietly. And a helper that waits only for something to
  stop moving must say no more than that: one that waits for the very thing a test
  is about to assert makes every such assertion pass by construction.

  **Run them from the PlayMode tab, never the Player tab.** The Player tab is a
  different thing wearing the same name: it builds a player for the active target
  and runs the tests inside it. With WebGL active that means linking a
  *development* WebGL player, which is minutes of work, produces a 127 MB wasm,
  and is not what any test here needs -- `TestScene.Load` loads the real scene in
  the editor and that is the whole point. A Player-tab failure says nothing about
  the suite: it fails in `PlayerLauncher` before a single test is reached, and the
  error it reports is a native link error from Unity's own libraries.

  It has failed exactly that way once, on
  `undefined symbol: unitytls_ssl_set_client_transport_id` out of
  `WebGLSupport_UnityPlayer.TLSModule_Dynamic.a`. Worth knowing three things
  before chasing it. Every module variant, release included, references that
  symbol as undefined, so it comes from a unitytls library outside
  `BuildTools/lib` -- the archives there are not the problem. The release build
  resolves it and has shipped. And the failure window opened when
  `com.unity.pipeline 0.6.0-exp.1` was added to the project; a development
  player had linked cleanly the day before.

  **That package has been removed** -- it was never added deliberately. Whether
  removing it restores the development link is not yet known, because no
  development WebGL player has been built since. If the Player tab fails the same
  way again, the package was not the cause. This paragraph once said the package
  repinned `com.unity.test-framework`. It did not: it declares a dependency on
  1.1.33, and the project's own 1.6.0 pin resolved the same before and after.
- **A browser frees memory only between frames, so whatever a frame makes the page must hold** (Part 15h-6d,
  2026-11-02). The build may grow to 2 GB; opening the pipelined processor made 1.8 GB in one frame -- cache keys made of
  the whole project's text, made again on every read of a written module -- and the page died, and again on every launch,
  since BitSorter opens on the last thing open. dotnet's tests never saw it: they time and measure a session, not a frame,
  and the worst of that frame was the sample being imported and checked a part at a time. Measure a frame in the editor
  with a `ProfilerRecorder` on "GC Allocated In Frame"; name where it comes from with the profiler's GC call stacks
  (`ProfilerDriver.memoryRecordMode = GCAlloc`, read through `RawFrameDataView`), and switch the profiler off after;
  under dotnet, `GC.GetAllocatedBytesForCurrentThread` round a step finds the same in seconds (Part 16b: the import's
  attempt to draw each module, 460 of the 601 MB left). `BrowserMemoryPlayTests` holds the open to 256 MB (it makes
  107), and `ImportCostTests` a processor's import to 64 MB. **But .NET 10 keeps on the stack what Mono and IL2CPP put on
  the heap** -- above all a `foreach` enumerator through an interface, 32 to 40 bytes each, which hid some 5 KB of a still
  frame (Part 16e): measure with `DOTNET_JitObjectStackAllocation=0` set, and hold such a walk itself rather than its bytes,
  with a list that throws if it is enumerated (`LookupCostTests`). A lookup asked every frame walks its list by index.
- **`Editor.log` accumulates across sessions.** A warning found in it may
  be from an old compile and describe code that has since changed, so
  verify against a fresh compile before acting on one. Reading history as
  present tense produced a sweep finding that two `GridPulse` fields were
  dead when a later commit had started using them; deleting them would
  have broken the grid pulse. Check the log position against the most
  recent compile, or better, get the warning from a build you just ran.
- After any change that builds or modifies the demo scene, verify the
  saved scene file itself — serialized references can be `{fileID: 0}`
  even when the setup code looks correct. A scene that opens fine on
  this machine can still be broken for a fresh clone. **A rebuild
  renumbers every file ID**, so its diff runs to hundreds of lines whatever
  changed and says nothing by its size: find a new reference by its field
  name in the saved file. A reference written into the scene by hand is a
  stand-in until the builder runs, never a substitute for it.
- Commit after each green test run, with a short descriptive message.
- Bug fixes land as two commits: a failing test that reproduces the bug,
  then the fix that makes it pass. The red commit must still compile —
  write the test against the existing API so it fails on an assertion and
  not on a missing symbol, otherwise the commit is useless to bisect and
  leaves the editor broken for anyone who lands on it.
- Unrelated changes go in their own commits, never swept in with a fix.
  A commit that fixes a bug and also re-saves the demo scene and retunes
  the HUD cannot be reviewed, reverted or bisected. `5a70be7` is the
  example not to follow: the level-switch tests, the fix they cover, and
  a scene re-serialisation all landed as one commit.

## The view layer
- **The interface is built in code.** The drawings -- the circuit, the waves,
  the legend, the minimap -- are uGUI on one canvas, from `UiTheme`'s builders;
  the shell round them is UI Toolkit. The scene is generated, so an authored
  hierarchy would be dozens of RectTransforms for the builder to reproduce and
  get subtly wrong.

  **The workbench's shell is UI Toolkit, built in code the same way** (Part 11):
  a panel made in code (`ShellPanel`), scaled as the canvas is and sorted over
  it, under the frame-rate counter; `ShellTheme`'s builders set every style from
  the palette and `UiType`; and there is no UXML or USS anywhere
  (`ShellRulesTests`, which also refuses a colour literal under View/Shell and a
  value made by reflection anywhere the game runs). **PARTS, the side panel and the
  dock each fold away** (Part 16d; Ahmad, 2026-11-08, "a larger view of the map"):
  an arrow on the edge facing the circuit folds its panel to a strip at the window's
  edge holding the arrow, H folds all three while any is open and opens them once
  all are, and the folds are kept between launches (`ShellFolds`, `FoldsKey`); a
  folded panel's drawings -- the waves, the legend, the listing, the minimap -- are
  switched off with it, the circuit's window takes the room, and the guided tutorial,
  a failed CHECK and a comparison with ModelSim open what they need. Its panels are docked
  (`ShellLayout`, a view-model rule): the toolbar, the inspector's bar, PARTS
  and DESIGN, the circuit's window, the side panel's quick fields and minimap,
  the splitter, the dock with WAVES, INPUTS (Part 15g, `InputTable`: what the
  testbench gives the inputs as a vector table to type in -- a column per input,
  a bus one column in its own radix, BIN, HEX or DEC; a row per moment something
  changes, an empty cell no change -- with + ROW, STEP, ALL COMBINATIONS, COUNT
  and CLOCK on the columns chosen by their names, and END; each edit one undo
  step, replayed as SET's is; read only in an exercise, its test's rows and the
  values it wants shown, an edit refused in the document's words; and read only under
  a written testbench, what it applies as CONVERT would record it from a run of it, its
  line in full over the table and CONVERT where the editing tools were, Part 15h:
  it was column heads over nothing), TRANSCRIPT, CODE (the SystemVerilog the
  document prints as, read-only, Part 12e; linked to the drawing of code, Part 15h-5:
  the lines what is selected there comes from marked and scrolled into view, a click
  on a line selecting the parts it makes, and a written module's file numbered from
  its own first line, as its editor numbers it, so a line is one line everywhere) and TIMING (the paths, the critical
  path, the minimum period and the gate count, the path picked lit on the
  canvas while the tab is up, Part 14b -- on the drawing of code every part of
  an assign between two of its signals, the longest way through them, out of a
  bus through the slice that takes the path's bit, and the slice into an
  instance, 15h-4's fix; the worst measured by stepping every
  input, the paths that never fire marked, and STEP TEST, 14c; and LIBRARY, the gate
  library the design is held to, 14d; and the CLOCK PERIOD CHECK holds an exercise
  to, where an input's or an output's path is slower than register to register,
  15g; and PATHS folding under an arrow, shut to begin with past eight, and a line
  too long for its row opening onto the rows under it, 15h, `TimingFold`; and
  LATENCY and THROUGHPUT, 15i-1, `PipelineDepth`: a design whose every path from an
  input to an output crosses K registers is a K-stage pipeline, K periods late and
  one result a period, and any other says why it has no one latency) and LOGIC (Part 15g, for logic without registers of up to ten input bits:
  the truth table found by running the engine through every combination, a
  slice a frame as the measured worst is, `LogicTable`; for eight input bits or
  fewer each output as a minimal sum of products, the textbook's way and
  SystemVerilog's, a slice a frame too and marked where its search was cut
  short of proving it the smallest, `Minimizer`, each shown whole, on as many rows as
  it needs, cut between its products (15h-4d, `TextRows`: it ended in "..."); and for
  four inputs or fewer its K-map, no groups drawn, `KarnaughLayout` -- as the course draws one, the
  first variables across the top and the rest down the side, the corner split by a diagonal with each side's names
  beside their codes; it was transposed, under a line of names, until the course was read, 2026-11-02) and STATES
  (Part 15g, for a clocked circuit of up to six register bits, four input bits
  besides its clock and reset, and sixteen states reached: found by running the
  engine breadth first from its start, a run for each state and input, `StateExplorer`;
  Moore or Mealy as the runs show it; the states round an oval with their arrows,
  `StateDiagramView`, and the next-state table beside them; with nothing to show,
  LOGIC and STATES say why across their body, never only in the header's dim line --
  a 64-bit ALU's empty tabs looked broken, Part 15h), and a one-line status that
  names the gestures where the pointer is. **QUIT stands at the status line's right end**
  (5.1, Ahmad: "I couldn't exit the simulator unless I exited full screen"; `WorkbenchPanel.Quit.cs`),
  in a desktop build and the editor alone, red as RESET PROGRESS is (`ShellTheme.Destructive`),
  and over the start page and every other page too, which cover the status line: it asks
  first, above itself so a double-click cannot answer, and its answer keeps the circuit and
  the code typed and not applied before it quits. **The drawings stay uGUI** -- the
  circuit, the waves, the legend, the minimap -- each placed under the panel
  that frames it, at its own size, **the waves on whole screen pixels** at any
  dock height, their window, scroll and every line's level (Part 15h-4d: off a
  whole pixel the same line came out pale at one height and bright at another); a panel's middle takes no clicks. Its popups
  are the shell's (`ShellPopup`): the menus, the + SIGNAL list, the one-line
  fields and the keyboard map, which `?` opens (`KeyMap`, every key from
  `WorkbenchKeys` and the gestures, so the keys are listed once).

  **The shell's faces are made full and kept, and the code's reads no kerning**
  (`ShellFonts`, `CodeFont`). Unity 6000.3's FontEngine keeps the kerning and
  ligature tables of one face at a time: glyphs given to a face other than the
  last one given glyphs read that face's tables again -- about 12 MB for Plex
  Sans -- and the copy dropped is never freed, not even by
  `FontEngine.UnloadAllFontFaces`. The shell's two faces were made empty in every
  domain, so every entry to Play Mode, and new letters after it, switched
  between them: a day of test runs grew the shared editor by about 20 GB under
  FontEngine until it could not compile (2026-10-29). Each face is now given
  every printable character as it is made and kept for the editor's session
  (`HideAndDontSave`, found again by name after a domain reload), so drawing
  never gives it a glyph; the code's face asks for no tables at all. Measured
  with memory snapshots (2026-11-01), a whole Play Mode run adds nothing under
  FontEngine where it added about 400 MB. **A runtime font asset that is given
  glyphs is made full and kept, or reads no kerning** -- one made empty per
  domain brings the leak back. An Edit Mode run still added about 27 MB on
  2026-11-01, not traced. The suspect named then, `UiThemeTests` measuring the
  4.x levels' goals through the dynamic fallback, went with the 4.x game in Part
  16a, and no word of the app's own reaches that fallback now -- every character
  in its strings and its exercises is in Plex's static atlas but μ, which only
  the shell's faces are given -- so the 27 MB wants measuring again before anyone
  chases it.
- **Every colour comes from `Palette`**, board and interface alike -- no colour
  literal, and no serialized colour field on a renderer. They used to live
  wherever each was first needed, and nothing could compare them: the interface
  took its accents from the node colours on purpose, and the side effect nobody
  weighed was that every source was `Good`, every sink `Bad` and the NAND gate
  `Accent`, to the last digit. **Never change a palette in place** -- derive a
  new one -- and choose it before the scene loads, because renderers take their
  colours when they build. Since Part 16b a palette holds only what is still
  read -- the ground and its traces, a wire's core, a bit, the burn, the
  interface's panel, text, accent and buttons -- the 4.x board's colours for its
  gates, fixtures, wires and cards having gone with the board.

  **Every look draws 5.0's workbench as well, from a `SchematicPalette` of its
  own** (`Look.Canvas`, and its glow `Look.CanvasGlow`), under the same rules: the
  canvas draws values, x, z and buses rather than tokens, and borrowing fields
  named for sources and sinks would make two looks share colours by accident.
  Schematic's is the reference's, as Ahmad chose it; Schematic Light is the same
  on paper, for a report's screenshots; Neon Board's and Classic's are made from
  their own boards' colours. **Code is written in the palette's code inks** (`SchematicPalette.CodeInk`, 5.1; Ahmad: "all the text is white"):
  a keyword in the accent, a number or a string in a 1's colour, a comment in the muted grey, a system name or an
  operator in the cursor's -- as the browser's CodeMirror coloured code since Part 12f -- each `Legible` on the ground at
  4.5:1, and the browser given the same inks. The desktop's editor and the CODE tab colour their TextMesh Pro vertices
  as the text is drawn (`CodeInkView`, `CodeColouring` reading each character as CodeMirror's Verilog mode does, with
  the simulator's own keywords), never marked-up text, so every caret and column stays the text's own.
  **The workbench's own buttons, chips and slider take
  the canvas's colours too**: Neon Board's bordered panels darken a body whatever
  colour it is given, and on paper every button came out a dark slab. Faint
  colours go through `SchematicPalette.Over`, because the project blends in linear
  space and a glow at 35% over a dark ground would otherwise look nearer 60%
  (`Docs/architecture-5.md`, section 10).

  **The game is drawn in `Looks.Schematic`, `Look.Default`, unless the player
  chooses another under STYLE in Settings.** Schematic and Schematic Light keep
  Neon Board's interface -- Neon Board was chosen out of three directions
  rendered side by side for the 4.x board -- and differ from it on the canvas
  (Ahmad approved it with the plan, 2026-10-12, and confirmed it at Part 8's
  review, 2026-10-14). `Look.Classic` is 2.0's look, kept so a change that claims
  to touch no look can be held to Classic's reference shots, pixel for pixel.
  **There is no bloom** (Part 16b). The workbench is a Screen Space Overlay canvas
  and a UI Toolkit panel, and both are drawn after the camera's post-processing,
  so the bloom a look set in the scene's volume profile blurred an empty camera
  image every frame and never touched anything on screen. The volume, its
  profile, `Look.ApplyBloom` and the camera's post-processing are gone, and so
  is the 2D renderer's post-processing data, which put URP's post-processing
  shaders in every build (Part 16's URP pass, `RenderSettingsTests`);
  `LookBoot` stays for the one thing else it does, which is to put the player's
  chosen look on before the first scene in a build. A look is now a palette, how
  its panels are drawn, and its canvas and glow: the 4.x board's gate bodies,
  bits, trails, glows, grid and board tile went with the board.
  `LookBriefTests` holds every look to the canvas's brief: every value and
  outline at 3:1 on its ground, every word at 4.5:1 -- the cursor's colour
  included, since its time is written in it (Schematic Light's was 4.10 until the
  accessibility review, 2026-11-03) -- and a row's readout and a ring's digit,
  written in their value's colour, taken toward the text as far as they must to
  read at 4.5:1 on the ground or a row's band (`SchematicPalette.Legible`, and
  `WordOf` for a digit) -- a bus's x over its hatched box too, its bars as they
  are drawn (`XBoxWord`, `HatchBar`): the value colours are held to a wire's
  3:1 and no more, since Schematic Light's z darkened to read came out its
  outline's grey. Text selected in a code field seen, its fill opaque and 1.5:1
  off the ground with the code on it at 4.5:1 (`SchematicPalette.CodeSelection`,
  Part 16d's sweep: it was 1.06 to 1.15:1). A 0 and a 1 apart by weight
  as well as colour, an undriven wire apart from both and from an outline and a
  bus, and reduced motion leaving
  out the glow and the flashes -- and the looks but Classic to the interface's,
  where text and buttons read on a panel.

  **A button says what it is for** -- `ButtonRole`, and `UiTheme.FillOf` is the
  one place a role becomes a colour. The primary is the one thing a screen asks
  for next, and the only solid button, so an outlined look tells it apart by shape
  as well as colour. Quiet is quieter by its edge, never its caption: a dim
  caption is how a button says it cannot be pressed.

- **Ambient animation keeps one clock, `ViewTime`; an event counts from its own
  start.** A caret blinks and a hint breathes on `ViewTime.Now`, so the reference
  shots can pin it; a change on a wire marks a moment and counts from it.
- **Visual changes are checked against the reference shots.**
  `BitSorter/Capture Reference Shots` captures fifty-five states of the simulator: the
  workbench preview with two changes on their way, whole, zoomed in, with a wire
  pointed at, with two rows in other radixes and two breakpoints, with a block's
  inside grouped under full names, the add list, MY CIRCUIT being edited, wired
  and reshaped, the inspector, the minimap and the canvas's menu, the transcript
  beside the hierarchy, the keyboard map, a block stepped into, MAKE BLOCK, an
  array of blocks, written code with a fault marked and in the CODE tab, the
  FILE menu, an imported project, the critical path from TIMING and the LIBRARY
  menu, two devices on a bus, the start page, the EXERCISES page, an exercise's
  brief, failed CHECK and solved card, the guided tutorial, a first-time hint,
  the INPUTS, LOGIC and STATES tabs, ABOUT BITSORTER, operator parts and a turned
  block, the drawing of code with and without GATES, locked code, the waves
  zoomed, the three RISC-V processors and their controllers, the drawing and
  code linked, the PROGRAM chooser, a block resized by its handles, the panels folded away, code folded (77, 5.1), QUIT asking (78, 5.1), a row dragged by its name (79, 5.1) -- and Settings asking whether to reset, and
  the credits part-way up -- every other shot taken with the hints seen, as an
  Explicit Play Mode fixture, so it runs under `SaveGuard`. Shots 1 to 26 but 17
  and 18 were the 4.x game's and went with it; the rest keep their numbers.
  Frame time is fixed, particle systems are seeded, `ViewTime` is pinned and the
  real mouse and keyboard are swapped for the fixture's own -- a click on the
  Game view mid-capture once put a refusal toast into two shots -- so **two
  captures of the same code are identical to the pixel**; a refactor that claims
  to change nothing is held to that, with no tolerance. **In one editor session,
  that is**: a restarted editor makes the code's font again, and its glyphs come
  out a level or two apart in a few pixels -- shots 43, 44, 66 and 73 moved by 2 to
  7 pixels after a restart on 2026-11-03, the same on v5 as on the change being
  held -- so after a restart capture the base again before comparing. It needs the Game view,
  so not batch mode. **The README's GIFs are recorded the same way**, by the
  Explicit `ReadmeGifs` fixture (`Assets/Tests/PlayMode/ReadmeGifs.cs`: a scenario
  a GIF, its frames in `Captures/gifs/<name>`) and made from those frames by
  `Tools/gifs/make_gifs.py` into `Docs/gifs/`.

  **An animation that ends must end on its final value, not on its last
  frame**: anything timed that stops updating takes its finished value on the
  first frame past its deadline, or counts down to exactly zero. A 4.x scorch
  mark was left at whatever the last frame before its deadline had made it, and
  float rounding in the game clock decided whether that frame counted, so one
  capture in a dozen came out a few levels fainter. **Two things that can overlap
  at one sorting order need different depths**: Unity breaks a tie in sorting
  order by depth and leaves a tie in depth to chance, which once made one 4.x
  capture in three differ where two bits' trails crossed. A difference in a
  capture is a real one.

  **A capture can be of any look**, named in the `BitSorter.Capture.Look`
  session key (or `-captureLook` on the command line) and saved under a folder
  of that name. The fixture chooses it before the scene loads and fails on a
  name that matches nothing, rather than capturing the game as it is into a
  folder that says otherwise.
- **SHORTCUT KEYS in Settings hides every key beside a button** (`KeyCaps`, asked
  2026-10-26): the caps beside the workbench's buttons -- the uGUI ones by their
  alpha (`KeyCapView`, never by switching themselves off), the shell's by
  `ShellTheme.KeyCapClass` -- shown to begin with, the whole simulator, and at
  once. What exists to list keys keeps them -- the keyboard map and the guided
  tutorial -- and every key works either way.
- **Text is sized by what it is for, never by a number.** `UiTheme.Label` takes a
  `UiType` -- Micro, Caption, Label, Body, Numeral, Lead, Heading, Title, Display --
  and `UiTheme.SizeOf` is the one place those become sizes. Seventeen sizes from
  10 to 54 had grown a label at a time, so full-screen titles alone came in three
  and two things of one kind drifted a point apart. Nothing is under twelve:
  the 4.x game's music credit, the one line a licence required be shown, was eleven.
  `UiTypeTests` refuses a size set by number.
- **`HalfAdderDemoSceneBuilder` is the only authority on scene contents.**
  Anything added by hand is wiped by `BitSorter/Build Play Scene`.
- **The workbench arbitrates its own pointer** (Part 9b-2b). It is a full-screen
  panel, so the pointer is over the interface everywhere on it, and "over the
  interface" cannot tell a button from the waves. `WorkbenchControlSample` asks the
  narrower question -- is the topmost thing under the pointer a control: a button,
  the slider, a menu -- and asks it at `-30000`, before anything reacts to the
  frame's input: a tap puts press and release in one frame, and a button that
  closes its menu would otherwise leave the waves under the pointer by the time
  the workbench asked. **The shell's
  controls are asked of its panel** (`ShellPanel.OverControlAt`, Part 11a): the
  event system sees a UI Toolkit panel as one nameless object; and the
  panel reads its mouse as gestures (`WavePointer`), where a press on a control is
  never one. The row menu, the menu for a moment (a right-click on the waves, for
  a breakpoint there; Part 9b-3), the export menu (VCD or PNG; Part 9b-4), the FILE
  menu (Part 13a) with OPEN's list of circuits (Part 15a) and COMPARE WITH MODELSIM'S
  RUN (Part 15i-5: the `modelsim.vcd` an exported run.do has ModelSim write, held to
  the run by `RunComparison` -- the first moment and signal they differ at, at the
  TRANSCRIPT's head and the run taken there, a difference from t = 0 that ends said apart, and only as far as the
  dump goes), the question asked before a
  testbench is converted or a circuit or a module is deleted, or BitSorter quit (`AskMenu`, Parts 13d, 14g,
  15a and 5.1), the add list and the two fields -- RENAME and
  BREAK ON VALUE -- each hold Escape (`IHoldsEscape`), as an exercise's card does (Part 15d),
  and with nothing holding it Escape takes down the EXERCISES page, then the start
  page, or puts a part in hand down, and never closes the workbench: BitSorter opens
  on it, and nothing is behind it (Part 15a). HOME, FILE, EXERCISES, HELP and
  SETTINGS head it, HELP a shell menu (`HelpMenu`) with the keyboard map,
  the start page, the tutorial and ABOUT BITSORTER -- a page saying what the simulator
  is and does, in the workbench's own words, with the credits, the keyboard map and
  the project's page at its foot (Part 15h, `WorkbenchPanel.About.cs`; it opened the
  credits roll until Ahmad asked for it, 2026-10-28); SETTINGS opens Settings,
  and BACK there comes back -- both pages of the workbench since Part 15g, as the
  start page is, one at a time over the start page or EXERCISES where one was up,
  and either up is `PageShowing` too (`WorkbenchPanel.Pages.cs`). **While one is
  up, and in the frame it comes down, the workbench takes no key and no click of
  its own** (`PageHoldsTheKeys`): S changed the circuit under Settings unseen, and
  the key that took the credits down reached the workbench too whenever the
  credits updated first -- Delete deleted the selection (the 15g-2 review). A
  circuit, the start page or EXERCISES opened from the header takes it down
  through its own controller. **The start page is the shell's** (`WorkbenchPanel.Home.cs`), over
  everything under the header, and while it is up the workbench reads no keys and
  no pointer of its own -- but ? and S, the sample chip's key, which shows the next
  sample and leaves the page (Part 15g: it did nothing there); so is EXERCISES' page (`WorkbenchPanel.Exercises.cs`), and
  either up is `PageShowing`. **The start page gives the three RISC-V processors a section
  of their own** (Part 15h-6c, Ahmad, 2026-11-02): RISC-V PROCESSORS under EXERCISES, a card each
  with its name, a line on what it shows, and SUM, FIBONACCI and ISA, each opening it on that
  program; they are not rows of SAMPLES, though the S chip still steps through them. Each opens
  its waves on the rows that show it working after clk and rst, the only ports of its top
  (`WorkbenchSamples.StartingRows`, 5.1). **A
  testbench with a parameter of an enum type has a PROGRAM chip** (Part 15h-6c) at the controls'
  row's right end -- PROGRAM: FIBONACCI -- its menu the enum's members, choosing one step of the
  document and the run made again, measured again to where that choice stops it -- and shown to
  there, never run on to where the last program's run had got to (`RunSession.RanOn`: only a run
  carried on past a `$stop` is replayed as far); the program running chosen again is no step, and
  a choice the design cannot be made with is refused in the elaborator's words. Kept with the
  project (`ProjectSettings.BenchParameters`) -- a sample is made afresh as it opens, so the save keeps
  the program last chosen on it, from a card, the chip or an undo of one, and puts it back as it
  opens, no step to undo (`samplePrograms`, `BenchChoices.Applied`; Ahmad, 2026-11-02: it opened on SUM
  every time) -- never refused by the lock, the first such parameter's alone, and given to
  ModelSim by an export as the member's value, by the testbench's path, `-G/tb/PROGRAM=1` (a bare
  `-GPROGRAM` sets every parameter of the name, a processor's image name among them; its name ModelSim reads as
  characters, and runs another program without a word), or said in a comment where only the
  code works the value out. **An exercise open puts GOAL and CHECK at the
  controls' row's right end** in its place, the chip beside them made room for: GOAL its brief,
  the hint behind SHOW THE HINT, and CHECK the grade, every failed check a
  TRANSCRIPT line at its moment. An answer is written as code through the CODE switch every circuit
  has (5.1), under the exercise's header (`Exercise.Written`), held to the library and the stock as a
  drawing is; WRITE IT and DRAW IT stood beside GOAL and CHECK until then (Parts 15e, 15g), and went
  at Ahmad's choice (2026-11-11), DRAW IT's code drawn back into gates with them. **The circuit's window is the canvas's** (Part 10a):
  its presses go to `CanvasPointer`, never to `WavePointer` -- a left drag moves
  the selection or draws a box, the middle button or Space with a drag pans, so
  **Space plays and pauses on its release**, and Alt held while dragging or
  placing places freely. **A press on a pin draws a wire** (Part 10b), and a drag
  out of a wire taps it -- out of a selected one, it reshapes it instead, as a
  drag on one of its corners does. **Every pin has a lead to wire from** (Part
  15g, `SymbolGeometry.Lead`): a port's box, a constant's and a block's stand half
  a cell back from their pins, as a gate's body always did -- the pins, and so
  every wire and saved circuit, where they were. A port's and a block's outline,
  which routes keep clear of, did not change; a constant's grew by the lead away
  from its pin, to keep its literal's room, so a route passing within half a cell
  behind one may run again. An unwired pin's lead ends in an open circle, lit with
  the pointer near enough to wire from it while the status line says "drag to
  wire" (`CanvasOverlay.PinLit`), asked again only when the pointer, the zoom or
  the drawing changes; and while a wire is drawn the pins it may end on show
  theirs (`PinEnds`, at most 300) -- a guide by direction and the bus rule, found
  again after any edit; the document still has the last word as the wire ends.
  Ahmad asked for "a small tiny line that we can wire from" (2026-10-28). **Every
  part turns to any side** (Part 15h-3, asked 2026-10-28; `PartTurnTests` holds each
  kind at each quarter turn, mirrored and not): a wire comes into a pin the way it
  faces, round a column or a height where the first way round would come back over
  it (`WireGeometry.Round`), a turned block names its pins along them, and a part
  with wires leaving its top has its name under it or beside it. **A selected block is
  resized by its handles** (asked 2026-11-02, `BlockResize`): one block, or one box of the
  drawing of code, selected shows eight squares on its selection's outline -- not on its own,
  a press on which moves it, so the two never compete -- and a drag of one moves the sides it
  is on by half cells, the sides across staying put, so every pin stays on the quarter grid;
  never below the block's natural size nor past 64 cells (`PartGeometry.MaxSize`). Its pins
  spread down its sides, its port names take the room, and its wires follow it dashed while it
  is dragged. The size is the placement's (`Placement.Width` and `Height`, 0 natural, as a save
  from before reads), in the layout or the module's arrangement and never in the module, so it
  changes nothing the circuit does: one step on release, refused where a move is, kept by a
  turn, a paste and an arrangement, dropped by TIDY, and RESET SIZE in the canvas's menu puts
  it back. **An operator part**
  (`OperatorPart`, Part 15h-3) is one of the language's operators as a box with its
  symbol, compiling to one `assign`; not in PARTS yet, for the drawing of code (Part
  15h-4) to place. **Every circuit has the CODE / DRAWING switch at its window's top right** (5.1,
  Ahmad: "just a button to switch to the code and edit it", everywhere): over a drawn circuit CODE shows
  it as it prints, to type into, and changes nothing (`WorkbenchSession.DrawnAsCode`, `TextOf`; a blank
  circuit shows + CODE's template, an exercise's answer its header), DRAWING going back as it was; the
  first text that applies makes it a written module, one step that Ctrl+Z takes back, and until then
  its drawing takes no edit (`CodeShownRefusal`); an exercise answered by drawing alone shows none
  (`CodeAllowed`). **A written module is shown as the drawing of its code, or as its
  code** (Part 15h-4): **it opens as its drawing** (15h-4d: a double-click on a written
  block showed its code, and Ahmad wanted the drawing first) -- unless CODE was chosen
  for it, which is kept for each module, or its code cannot be drawn; + CODE,
  an imported course template and an exercise answered in code open on the
  code, to be typed into (`WorkbenchSession.ShowCodeOf`), and text typed and not yet
  applied -- into any module, a drawn one's typed as code included, on screen, behind its drawing or stepped out
  of, each a draft the save keeps -- is what EXPORT, the hand-in, the lock and
  CHECK apply first, or are refused by, saying whose it is
  (`WorkbenchSession.ApplyTyping`, `WhyTypingStops`; the 16d sweep: it was the
  module shown's alone), and what leaving the circuit applies where it can, the
  rest kept as its draft with nothing asked (`WorkbenchPanel.LeaveTheTypedCode`);
  never the text of an editor the document has moved under, by an undo or an edit
  of the drawing, which it sets aside (`CodeDocument.Behind`). **A circuit can be
  locked** (Part 15h-4d, asked
  2026-10-29; `WorkbenchSession.Locked`): the padlock in the circuit window's top strip,
  left of FIT's place and of FIT's key, F, which it stood over until a reference shot
  showed FIT without its key -- not the lower corner Ahmad first asked for, which a
  browser's code box covers just when the code is to be unlocked. An imported project opens locked, but
  for a course template, imported to be written into; a sample and an exercise never
  lock. Locked, every edit of the circuit and its code is refused saying how to unlock it
  -- a testbench converted, a gate library (it retimes the gates), and an undo or a redo
  that would change them, included, each refused before any name is asked -- and the
  code's editor is read only in both builds (CodeMirror's `readOnly`, through a
  compartment); its inputs and unit are tried as ever. The lock is no edit:
  no undo step, the run not made again; the save keeps it beside the document
  (`locked`, absent before it). **FILE, BACK TO AS IMPORTED puts an import back as it
  came in** (asked 2026-10-29), from a copy the save keeps beside it (`imported`, through
  a rename, gone with a delete): it asks first, is one undo step, and locks it again --
  not refused by the lock, since it puts the circuit back rather than changing it by
  hand; unlocked, Ctrl+Z brings the changes back. **Each window's zoom has a control of its own, never a
  zoom of its own** (Part 15h-4d): the circuit's a slider at its window's lower right,
  even in the zoom's ratio (`ZoomSlider`), down over code; the waves' a range bar at
  the legend row's right end (`WaveRangeBar`, `WaveRange`, in the shape Ahmad drew from a
  scrollbar, on its side) -- a trough for the whole run and a thumb as long as the share
  of it on screen, its ends dragged zooming and the thumb dragged moving through time;
  both read their window back each frame, so the wheel,
  the keys and FIT move them too. CODE and
  DRAWING sit at the right of the code's top strip -- in a browser the code is a box
  laid over everything under that strip (`WorkbenchSession.ShowsDrawingOfCode`). The
  drawing is what the code builds (`RtlDrawing` from `RtlExtractor`'s netlist, laid
  out in columns by `LayeredLayout`, a column far taller than the drawing is wide
  standing as several side by side -- Part 15h-6), held beside the project and never in it; its
  values are the run's, a wire no signal holds worked out from the ones that are
  (`DrawnValues`). **Unlocked, it takes an edit, written into the code** (Part 15h-4g,
  asked 2026-10-29): what is placed, wired, deleted, pasted or typed in the inspector on
  it is made on the drawing and printed as the module's code (`DrawingPrinter`), one
  undo step, the drawing made again from it -- the ports in their order, every signal's
  name, every gate's delay, a register's edges and the comments over the module kept,
  the rest of the code's shape not. The first such edit of a module asks first
  (`WorkbenchSession.RewriteQuestion`). It is refused, saying which, where the drawing is
  not the code: what it does not show (`RtlDrawing.Unshown`: an `assign #d`, a
  `$display`, a starting value off a register), a box no expression is, a parameter, a
  child instance given parameters, an `inout` port, an inspector field that sets more
  than the code (a clock's period, a reset's release, a library), or
  -- the drawing written again unedited -- any difference from the code on random values
  (`DrawingPrinter.Differs`, asked once for each text of the module, and of every
  exercise answer and corpus design by `DrawingPrinterTests`). **A part of it is moved and turned by hand, the code untouched** (Part
  15h-4f): kept with the module by a key that names it as the code does (`port:a`,
  `inst:u`, `reg:state`, `sig:next`, else its kind and place, `Module.Arrangement`), one
  step a drag, the drawing laid out round it; a numbered key is forgotten once the code
  changes, an edit written into it from the drawing included; TIDY, beside GATES while a
  part of it stands where it was put, lays it out by itself again. A reshape is refused --
  it routes its own wires -- and so is any edit with GATES on, whose gates lay themselves
  out. **TIDY on a drawn circuit** (`Tidying`) lays its parts out as the drawing of code
  is, unturned, its wires' bends dropped and the whole kept where it was -- layout only,
  one step; its button stands left of the padlock while the circuit is not laid out so,
  and the canvas's menu offers it on a right-click anywhere. **A part the drawing makes up says what it does** (Part 15h-4e, asked
  2026-10-29) -- an operator, a mux, a case's choice, a slice, a join, a match, a
  constant -- in the inspector's line, in its operands' own names ("(a & b) OR c, bit by
  bit: 2 bits."), and goes by what it is, never `$p4`; a wire out of one no signal holds
  goes by its expression, "a & b [1:0] from the & to the |'s a" (`RtlDrawing.Describe`,
  `ExpressionOf`). **Every part of it comes from a line of the code** (Part 15h-5,
  Ahmad's choice, 2026-11-01), which its inspector line ends by naming ("From line 8."):
  a port's declaration, an instance's, an assign's or a gate's, a statement's inside a
  process -- the if's or the case's for the mux joining its ways -- and a register's
  always (`RtlCell.At`, `RtlDrawing.LineOf`, `PartsOn`). **And it says first what the code
  says of it** (M3, asked 2026-11-02): the `//` comment at the end of that line, else the
  `//` lines right above it -- a blank line, code, a banner or a line of code commented out
  ends them, and an import's `include` right over the line, with the blank line after it,
  is passed over -- its first sentence before the rest, since the bar is one line; a block
  with none of its own takes the comment over its module
  (`CodeComments`, `WorkbenchSession.CommentOfPart`). Every block, register, port and mux
  of the three processors has one (`ProcessorDescriptionTests`). An edit made on a
  drawing writes its module's code again and keeps only the comment over the module, so
  its line comments go with it. **A generate loop of one module's instances is one array block**
  (`slice × 8`, `RtlArrays.cs`), a double-click stepping into its first copy; constants
  stand beside what reads them, and a case arm of several labels is one input of its
  mux. A double-click on a block in it, or its row in DESIGN, steps into the instance the
  code makes, its module read again from the code whenever it is drawn again
  (`WorkbenchSession.CodeSteps`). **GATES beside it opens the small ones** --
  every operator and mux of four bits or fewer, and a sum or a difference of 4-bit
  numbers with its carry, `{cout, s} = a + b` (`RtlGates.OpenWithCarry`) -- into
  their gates in their place, BOXES closing them again, kept for each module; a
  wire inside is valued by the engine's own gates (`RtlDrawing.Inner`), and a number
  is split into its bits once, however many openings read it. **In a browser GATES
  opens no more than 800 parts** (`WorkbenchSession.GatesCap`, Part 15h-6, Ahmad's
  choice): past that the boxes stay, and it says the desktop version opens them all --
  the multicycle controller's 1,004, a third of a second to make and route on the
  desktop -- held wherever the drawing is made with GATES on, not only at the press.
  **A controller is shown as the textbook draws one** (Part 15h-6e, Ahmad's choice,
  2026-11-01, on seeing the multicycle controller's 224 parts drawn literally): a state
  machine written in code -- a register, of an enum or of localparam states, with a
  case on it -- as its state diagram, each state a box of what it sets and every arrow
  labelled in the code's own words, an output set on only some ways after a slash on
  its arrow; a decoder -- a case that sets outputs to values -- as its control table
  (`ControllerReader`, `ControllerLayout`, `ControllerView`). The state the run is in,
  the arrow it came by and each table's row are lit as it plays; the wheel zooms it, a
  drag or + and - move it, FIT fits it. DIAGRAM (TABLE for one with no states) or
  DRAWING beside CODE turns between it and the drawing, kept for each module; a module
  the reading is the whole of opens on it where it is a state machine or a table of
  several signals -- a one-output case, the seven-segment sample, stays its
  drawing -- and on it every edit is refused saying to press DRAWING. **Every reading
  is held to the engine before it is shown** (`ControllerCheck`): for every state and
  every value of each input of eight bits or fewer, the others random -- and, for a state
  with an arrow not yet taken, a second look at each pair of inputs together and a wide
  one at the constants its logic compares with (the 16d sweep) -- the next state
  the instance's netlist works out must be one the diagram has an arrow to, every
  arrow must be taken and every constant shown must be the circuit's -- every row of a
  table at its labels or, for its default, at values no label takes, its subject held
  there even where the module works it out itself; a case on an expression and a row of
  worked-out labels are not tried -- and a reading it does not bear out is not
  shown. The words are the code's own, an else's and a
  default's failed test included ("done and !err"), and not themselves checked. **A big
  drawing stays smooth** (Part 15g): the session routes a document once for the
  drawing, the hit map and a wire being drawn (`WorkbenchSession.RoutesOf`), which
  each routed it for themselves; a drag routes only the wires on the parts it moves,
  round the rest where they lie (`WireGeometry.Following`, a preview the drop routes
  whole) -- and, past 400 wires following it, carries along unrouted, with no bends, a
  wire between two of them (`CanvasEditor.RoutedUpTo`; the 16d sweep: all of a
  thousand-part drawing dragged was half a second a step), never on a drag of a few,
  where a wire carried ran through what it passed over; and a route tried asks only the squares of the board it crosses
  (`WireObstacles`) -- every route as it was, held to the character on fifty
  drawings. In a browser, a drawing slow to draw again says once a run that the
  desktop version is faster (`DesktopHint`). **A double-click on a part or a wire, or Enter
  with one selected, puts the keyboard in its inspector** (Part 10c), docked since
  11b-2: the bar under the toolbar holds every field and the side panel the quick
  ones with the part's line under them (a gate's rise is Tpdlh, its fall Tpdhl),
  both following the selection, and a focused field takes Escape as RUN TO
  does; a double-click is two plain clicks, never a click that placed a
  part and then a press to drag it. **A double-click on a block steps inside it**
  (Part 12a), as STEP INSIDE in its quick panel and a double-click on an instance
  in DESIGN do: the canvas draws and edits the block's module with that
  instance's values, while the run and the waves' rows stay the whole design's,
  and an edit inside is the module's, so every instance of it changes. A crumb of
  the breadcrumb in the circuit window's corner, or **Alt+Up**, steps back out --
  Backspace stays a second DEL, and Up alone still scrolls the waves. **A written
  block opens its code there instead** (Part 12d, `CodeEditorView`): a click in it
  is the code's, never the canvas's, and its field holds Escape while it has the
  keyboard, so the press that lets the code go does nothing else too -- it closed the
  workbench until Part 15a, and would put a part in hand down now.
  **In a browser the code is CodeMirror** (Part 12f, `CodeMirrorBridge`), a box
  the page lays over the canvas and so over every popup: it hides while a shell
  popup is open (`ShellPopup.AnyOpen`).
  + CODE, the last cell of PARTS' MORE, asks for a name and puts a block of one in
  hand. **A right-click opens the canvas's menu**
  (Part 10d) -- align, spread, turn, mirror, route again, reset size, tidy (Part 15h-4f), copy, cut, paste,
  make block (Part 12b; it asks for the new module's name first), delete --
  and Ctrl+C, X and V copy, cut and paste; a minimap at the side
  panel's foot, up while some of the drawing is out of sight, moves the view on a
  click. A part in hand, a wire being drawn, or a drag
  takes Escape before the workbench does. **The parts are a panel of the shell**
  (Part 11b), and **the splitter over the dock is a shell control**, so a drag on
  it is never the circuit's; the wheel over the waves' names scrolls their rows.
  **A row's name dragged up or down moves the row** (5.1, asked 2026-11-11; `WavePointer`,
  `WaveList.DropGap` and `MoveTo`): past four pixels a press on a name is a drag, a line
  shows where it would go, kept to the row's level -- a signal in a group within its
  group, a header carrying its group among the top-level rows, never into a group -- the
  rows scroll at the window's ends, and Escape puts it back; a header opens or shuts on
  the release of a click on its name, so the same press can carry it. **Once a row is
  moved by hand the list keeps its order** (`WaveList.OrderedByHand`): an edit of the
  circuit adds a port's row at the end and takes one away, and no longer puts every
  port's row back in the layout's order (Part 10e), which would have undone the drag.
  **A right-click on a BLOCKS row in PARTS deletes its module** (Part 14g), after a
  question -- Ctrl+Z still brings it back -- and is refused, saying where, while a
  block of it is placed or other code makes one. Deleting a block leaves its module,
  the player's work, so the bar's line says so and how it goes: Ahmad deleted
  code1's block and found code1 still in CODE and PARTS, with nothing in the
  workbench to take it out (2026-10-27).
  **A double-click is counted on the game's clock**
  (`Time.timeAsDouble`), which a test drives, so a test's double-click is one
  however loaded the machine; nothing in the game changes the time scale.
- **`UiText` arbitrates the keyboard.** The 4.x game's first text field went
  into a game that bound N, M, Q, E, H, R, Space, Enter and Escape in seventeen
  places, so typing "Adder" would have muted the sound (it had some then); the
  workbench binds more.
  Every reader of a key takes its keyboard from `UiText.Keyboard`,
  which is null while a text field is selected -- and each already coped with
  there being no keyboard, so while a name is typed each simply sees none.
  `UiTextTests` refuses a script under View that reads `Keyboard.current`
  itself. **A field may read a key it answers itself** through `UiText.FieldKeys`
  -- the code editor's Ctrl+Enter, which TextMesh Pro would type as a new line
  (Part 12d) -- and since Part 14g its Ctrl+Z, Ctrl+S, Ctrl+/ and Tab, some of which
  the game binds too: it reads them from `UiText.Keyboard`, null while a field holds
  the keys, so the two never both answer one press. **The code editor answers the keys a
  writer reaches for, in both builds** (Part 14g, asked 2026-10-27: "is Ctrl+V
  taken?" -- it is the circuit's paste only while nothing is typed in): Ctrl+Z and
  Ctrl+Y through the typing (`CodeDocument.UndoTyping`), Tab and Shift+Tab indenting
  the lines by four spaces and Ctrl+/ commenting them (`CodeEdits`), Ctrl+S applying
  as Ctrl+Enter does. TextMesh Pro's field had only Ctrl+A, C, V and X -- Tab typed a
  tab and Ctrl+Z did nothing -- and the browser's CodeMirror had the rest but find;
  it takes `@codemirror/search` now (Ctrl+F, Ctrl+D), and the keyboard map lists the
  lot under IN THE CODE. **Code folds, in both builds alike** (5.1, Ahmad: "the +/- thing in code that expands and
  unexpands things"): a - or + in the margin by each line a block starts on -- a keyword's, `module` to `endmodule` and
  `begin` to `end` among them, a bracket's of more than one line, a `/* */` or a run of `//` lines (`CodeFolds`; the
  browser's `folds.js` held to the same answer by a fixture both builds' tests read) -- Ctrl+Shift+[ and ] folding and
  opening the block round the caret and Ctrl+Alt+[ and ] every block. A folded block shows on one line, `module ha ( …
  endmodule`. On the desktop the field shows a folded view of the code (`FoldedView`) and every edit is taken back to the
  code itself, so the document, its drafts and its undo never see a fold: text typed on a folded line lands where it
  shows, a deletion that takes a fold's mark takes the block it hides, a place sent to inside a fold opens it, and the
  margin keeps the code's own line numbers. CodeMirror's undo starts from the block's code
  (`CodeMirrorBridge.Load`): put in as a change, it was a step of that undo, and Ctrl+Z
  after the first thing typed left the editor empty (the 14g browser smoke test). **In a browser the page's code editor
  counts as typing too** (`CodeMirrorBridge.Focused`, asked once a frame) -- its box,
  find's fields included, not only its text -- and
  while it has the keyboard the canvas stops taking every key. Derived, never set, for the reason pointer ownership is: a "typing"
  flag that leaked would be a game whose keys do nothing. A field answers
  Enter and Escape itself while it holds the keys, and the
  frame it closes is covered by `UiModal.OpenOrJustClosed` like any panel's.
  **Every shell field draws its own caret** (`ShellTheme.Field`, Part 15g): UI
  Toolkit's took its colour from a deprecated property that a browser build
  ignored, and no field there showed where the next letter goes. The shell puts that
  one out and draws a bar in the text colour, blinking on `ViewTime` -- so the
  reference shots hold it still -- while the field has the keyboard and nothing in it
  is chosen. An empty field's is at its start, measured from nothing: in a browser an
  empty field showed none until something was typed (Ahmad, 2026-10-28), and the editor
  never showed the fault. **And in a computer's browser every shell field sets `hideSoftKeyboard`**:
  without it UI Toolkit edits a field as on a phone and lets it go a tenth of a second
  apart while no on-screen keyboard is up, and every field flickered -- found with a
  focus log in a browser build, since the editor has no such keyboard (2026-10-28).
  **A field that stays on screen has to let go itself**: TextMesh Pro leaves a
  finished field selected, and every other field let go only by closing with
  its panel. The workbench's RUN TO kept every workbench key dead after Enter
  until a click elsewhere (the 9b-3 browser smoke test). It is a shell field
  now (Part 11a), and the shell lets it go a frame after Enter or Escape -- not
  in the same frame, or the Escape that cancelled it would do the workbench's
  Escape too, which closed it until Part 15a (`ShellTheme.Field`) -- and on a press anywhere else, which UI Toolkit
  cannot see when it lands on uGUI (`ShellPanel`). **A field given the keyboard
  keeps it** until it is let go on purpose: in the browser UI Toolkit lets a
  field go, to nothing, after the press that gave it the keyboard -- a click
  into it, on + SIGNAL, a double-click on a part -- and the first keys typed
  went nowhere (the 11d smoke test), so the shell gives it back -- as it was, with
  what was chosen in it: it came back with the caret at its start, and + CODE's free
  name, chosen to be typed over, was typed in front of (the 14g smoke test). **A key
  that asks for a field gives it the keyboard the frame after**
  (`ShellPanel.FocusAfterTheKey`): the platform's half of the key can reach UI
  Toolkit after the game has read it, and A typed an "a" into the search it
  opened. So does the name field + CODE and MAKE BLOCK open under the pointer: the
  browser's game read their click's press after the field had opened on it, as a
  press off the field, and let it go. **A shell field is not the
  event system's selection**, so `UiText.Typing` asks the shell as well
  (`ShellPanel.Typing`). **No key moves the focus round the shell** (Part 14g): a
  click on it makes it the event system's selection, and an arrow after that was UI
  Toolkit's navigation, putting the keyboard in a field where every workbench key
  was the field's -- Left stepped no time back, Alt+Up stepped out of nothing. Its
  navigation is ignored (`FocusController.IgnoreEvent`). And UI Toolkit reads the platform's keys, not the Input
  System's: a test's simulated keyboard never reaches a shell field, so a test
  hands it the key as UI Toolkit would (`ShellPanel.SendKey`) in the frame it
  presses the same key on the simulated keyboard.
- **A panel's backdrop is nine-sliced, and a full-screen one is not a panel.**
  `ProceduralSprites.Panel` is a rounded rectangle cut with a ten-texel border,
  so every `Panel_` keeps ten-pixel corners and a solid middle at any size.
  Panels used to borrow the AND gate's squircle, which cannot be sliced -- it
  has no straight edge to repeat and stops short of its own texture -- so
  `Image.Type.Sliced` stretched the whole sprite and every panel faded out
  towards its rim. Over the 4.x board that left the help panel's title and hint
  on bare grid. Anything shorter than two corners has to replace the sprite.

  **Generated sprites are `HideAndDontSave`, and the cache checks what it hands
  out.** `ProceduralSprites` keeps every sprite it draws in a static dictionary,
  and none of them are assets -- so an unload of unused objects destroyed the
  textures and left the entries pointing at corpses, and a destroyed sprite on
  an `Image` is a blank panel rather than an error. It cannot happen in a
  player, where nothing unloads mid-session; in the editor a WebGL build was
  enough, and the tests that ran seventeen seconds later failed with a null
  sprite. The flag stops the sweep; `TryCached` refuses a dead entry anyway,
  because this is the kind of fault that is only visible once something is
  already drawn wrong.

  **`Mathf.SmoothStep` is not the shader smoothstep.** It eases between its
  first two arguments, so `SmoothStep(0.62f, 0.98f, d)` is never 0 or 1;
  `SmoothStep(0f, 1f, t)` is the one form that is the same either way, and the
  one `ProceduralSprites` uses. The 4.x board's socket ring and its dot had it
  the other way until 2026-10-06: an empty input socket was a faint disc that
  nobody could see under a wire's end. They went with the board in Part 16b, and
  the tests that read their textures with them; `ProceduralSprites` draws the
  panel's backdrop and nothing else.

  **A full-screen panel has the screen to itself**, and its backdrop is
  `UiTheme.Scrim`, a flat rectangle: a scrim wants no corners at all, not small
  ones. The workbench is one, and Settings and the credits are pages of it.

  **Escape closes what is on top, one thing a press.** What is on top may be a
  popup, a page, an exercise's card or a code field with the keyboard; each
  implements `IHoldsEscape` and joins `UiEscape`. A first-time hint is not one:
  its strip stands along the circuit window's foot while the student goes on
  working, and GOT IT takes it down, as SKIP stops the tutorial on the same strip
  (this said a hint closed on Escape until Part 16d's sweep found it never had). A holder answers true while a press would
  close it and for the rest of the frame once Escape has -- one press, one thing,
  whichever updates first. Among themselves **the last one opened takes the
  press** (`UiEscape.TryTake`), as undo undoes the last thing done: each tells
  `UiEscape.Opened` as it comes up, and a holder is refused while one opened
  after it still holds Escape. `UiEscapeTests` scans the view scripts and refuses
  one that reads Escape without being a full-screen panel or a member of the
  list. And **a panel never answers a key on the frame it opened**
  (`FullScreenPanel.OpenedThisFrame`): in 4.x the Escape that took down one card
  raised another in the same frame, which closed on Escape too.

  **A full-screen panel derives from `FullScreenPanel`**, which does the four
  things showing one always takes: activate it, bring it to the front, and tell
  `UiModal` when it opens and when it closes -- disabling included. Five panels
  each wrote those out, and they had drifted: the chapter card never brought
  itself to the front. What differs between them goes in `OnShown` and
  `OnHidden`. (Its HUD half, `CoversTheHud` and `UiModal.HudVisible`, was the
  4.x board's, and went in Part 16b.)

  **A panel fades in, and takes clicks while it does.** `UiFade` moves a
  `CanvasGroup`'s alpha and nothing else, so a panel is clickable from the first
  frame it is drawn -- one that refused clicks while fading would cover the board
  and swallow them. Hiding is instant: a panel lingering after it was closed is
  what a stuck one looks like. It counts on `Time.deltaTime`, so captures stay repeatable, and a capture waits on `UiFade.AnyMoving`
  rather than on a number of frames.

- **Nothing may depend on the order components update in.** Unity leaves it
  undefined for scripts with no execution order. When one component needs a
  fact to be true by the time another can see something, produce that fact in
  the call that changes the state, not in a second poll -- an event raised in
  the call, as `ProgressTracker.ProgressReset` is. The 4.x game recorded a solve
  by polling in an Update, and its win panel could show the previous record.
- **BitSorter has no sound** since Part 16b (Ahmad, 2026-11-03: "remove the music
  and anything related to it"): the generated tracks, the two recordings, the sound,
  music and volume settings and the N key are gone, and the player ships with audio
  disabled (`ProjectSettings/AudioManager.asset`). `NoSoundTests` (Repo.Tests) holds
  it so: the switch off, no clip or mixer under Assets, no audio component in the
  build's scene or a prefab, and no script using the audio API. The built-in audio
  module stays in the manifest, since the director, video and web-request audio
  modules depend on it.
- **Anything shown to the player is derived, never restated.** A truth table is
  the engine run through every combination (`LogicTable`), a K-map is laid out
  from it, a state diagram is the engine run from its start (`StateExplorer`),
  and a controller's reading is checked against its netlist before it is shown
  (`ControllerCheck`). A second copy of a fact is a second thing to drift.
  **One copy is written down on purpose**: the browser page's loading screen is drawn
  before BitSorter exists and cannot ask it, so it writes `SchematicPalette.Dark`'s
  colours out as CSS -- and `TheLoadingScreensColours_AreThePalettes` holds every one
  of them to the palette, so the two cannot drift apart unseen.

## Not yet
**5.0 was asked for by name** (2026-10-11), and its parts are listed in
`Docs/architecture-5.md`: building them in order is not building ahead. Do not
build ahead of me otherwise.

**Settings is fullscreen, vertical sync, a frame cap and a
frame-rate counter, the look, reduced motion and the shortcut keys, the data
switch, starting over, and the credits** (`SettingsPanel`), reached from the
workbench's SETTINGS; BACK goes back to what it was opened over, and a change of
look reloads the game and puts Settings back over the same (`OpenAgain`). Until
Part 16a THE 4.x GAME stood beside CREDITS.

**Settings is a page of the workbench** (Part 15g, `WorkbenchPanel.AddPage`): under
the header like the start page, in the style's own colours and type, its switches
and sliders the shell's. It was a uGUI screen in the 4.x board's colours whatever
the style, and Ahmad asked that it look like the start page and that a change of
style through it change everything (2026-10-28). **The credits roll on a page of
the workbench too** (`CreditsPanel`), in the style's colours.

**STYLE chooses the look, and the game loads again in it** (`LookSwitch`). A
palette is never changed in place, so the whole scene is built again under a cover
of the ground's colour, and Settings is put back up where the choice was made. The
choice is kept as the look's own number
(`Look.Code`), never its place in the list, so a look added later cannot move
anyone's choice, and it is put on before the first scene in a build, **never in
the editor**, as the frame cap is: every test and reference shot would otherwise
be drawn in whatever the developer last picked. **REDUCED MOTION leaves out the
workbench's glow and its gates' flashes** (`ReducedMotion`); a change still
travels its wire, because where it has got to is what the workbench shows. Read
where it is drawn, so it needs no reload.

**Its sections are two columns, and fit a 1080 screen** (Part 15h): DISPLAY
and STYLE on the left (AUDIO headed it until Part 16b); PRIVACY, PROGRESS and
ABOUT on the right. One column ran past the foot, and CREDITS, last, was found
only by scrolling (Ahmad, 2026-10-28);
`EverySetting_FitsA1080Screen_WithNothingToScroll` holds it.

**It scrolls in a shorter window** (Part 15g). The canvas scales halfway between
width and height, so a wide window gives less than the 1080 the sections are laid
out against -- a browser tab 1920 by 800 gives about 930. As a uGUI screen it had
no scroll, ran under BACK and off the bottom, and was made to shrink to fit
(`SettingsPanel.FitScale`, removed in Part 16b).
**Nothing in a shell `ScrollView` may give up its height** (`flexShrink = 0` on
every row): UI Toolkit shrinks a column's children to the page before it lets
the page scroll, and the first build of the page squashed every line into the
next. **Nor does its viewport clip by itself**: the default theme does that, the
shell has none, and the sections scrolled above the page were drawn over the
workbench's header until the viewport was given `overflow: hidden` by hand.

**The credits are `Credits`, and the roll is `CreditsPanel`.** Every role is
Ahmad Zoabi's, the company is ZADZ, the testers are thanked without names,
and the roll ends on what the game is built with -- all as asked, 2026-09-26,
and nothing else is credited. **The roles are the simulator's** (Ahmad's choice,
2026-11-03): the 4.x game's GAME DESIGN, LEVEL DESIGN, ART AND INTERFACE, WRITING
and TESTING became PRODUCT DESIGN, the exercises, INTERFACE AND VISUAL DESIGN,
DOCUMENTATION and VERIFICATION AND TESTING, and one came in for each thing it does
-- its engine, SystemVerilog, timing, logic analysis, drawing of code, waves and
RISC-V processors -- with the testers thanked as testers, not playtesters. What it is built with is Unity, and since Part 12f
CodeMirror, the browser build's code editor (asked 2026-10-26). **5.0 added four
things** (Part 15g, Ahmad's choice, 2026-10-28): his roles for it -- the
SystemVerilog language, the exercises and the workbench, beside the engine --
IBM Plex Sans and Mono under BUILT WITH with their licence, the SIL Open Font
License; Icarus Verilog under CHECKED AGAINST, the simulator the engine is held
to, and since 2026-11-03 ModelSim beside it, the course's, the second check; and a dedication to every student of Digital Systems and Computer Structure -- the
course's name in full, as Ahmad asked (2026-10-29) -- naming no university.
No line for the assistant that helped write it. The roll credits no music
and no sound role since Part 16b, and `CreditsTests` holds it so. It rises at
`CreditsPanel.Speed` and comes to rest on its farewell -- HAPPY SIMULATING over the version (Ahmad, 2026-11-03; it was
THANK YOU FOR PLAYING) -- rather than rolling off
into an empty screen, and any key or click goes back to Settings.

**Fullscreen is a desktop build's alone** (`DisplayRules.Offered`): in a
browser the page has its own button, and fullscreen there is the browser's to
grant. Leaving it sets the window the game opens at, never the mode alone --
the mode alone kept the fullscreen resolution, a window the size of the display
with its title bar off the top. `DisplayRules.PreferredWindow` restates the
player settings' default size, and `DisplayRulesTests` reads the project file
to hold the two together. Unity remembers the choice between launches itself.

**So are vertical sync and the frame cap, and with nothing set they are the
game as it shipped** (`FrameRate`): vertical sync on and no cap, so a 165 Hz
laptop draws 165 frames a second. VSYNC switches it, beside FULLSCREEN, and
FRAME CAP under it holds the game to 30 up to 240 frames a second, or none.
**The cap is greyed out and locked while vertical sync is on**, still saying
what it is set to, because Unity ignores a cap then -- a control that would
visibly do nothing is greyed out, not left live. The first version was one button,
SCREEN or 60, for a laptop on battery (2026-09-28); asked again, the cap became
the player's and vertical sync a switch of its own (2026-09-30). The cap is
stored as a rate, never as the slider's stop, so a stop added later cannot move
anyone's cap; one this build does not offer reads as 60, never as none, the one
reading that costs the player something. `FrameRateTests` reads the project's
quality settings to hold the default to the vertical sync the desktop build
starts with. Both are kept in `Preferences` and applied before the first scene,
**but never in the editor**, whose Game view has its own VSync switch and where
a cap would hold every Play Mode test to it. Nothing in the game runs better or
worse for the frame rate: the simulation keeps its own clock and every
animation counts time.

**While nothing happens, a desktop build draws one frame in ten** (5.1, Ahmad: "we
need a good optimization for that", at about 56% of the GPU sitting still;
`IdleRendering`, the view-model's, and `WorkbenchPanel.Idle.cs`, through Unity's
`OnDemandRendering`). Every frame is drawn while the student does anything -- the
pointer moved, a button or a key down, the wheel -- the window is resized, the run
plays or is still being made, LOGIC, STATES or TIMING is still working a slice a
frame, a panel fades or the credits rise, or the drawing was drawn again, and for a
second after the last of them; input is read every frame either way, so the first
frame of anything is drawn at once. **Never in the editor**, as the frame cap is,
so every test and reference shot is drawn every frame, unless a test turns it on
(`WorkbenchPanel.ThrottlesWhenIdle`, `WorkbenchIdlePlayTests`); and not in a
browser yet, where it has not been tried. The keys are read for it through
`UiText.AnyKeyDown`, which answers no key.

**FPS COUNTER puts a frame-rate counter in the top-right corner** (`FrameRateCounter`),
off until asked for, so what the cap does can be seen -- asked for with it,
"like the one Steam puts on games". **It is the one DISPLAY switch a browser
build has too** (`SettingsPanel.DisplaySwitches`): a browser keeps fullscreen and
the frame rate to itself, but what the frame rate is is worth seeing anywhere
(asked for after the 4.0.0 browser playtest, 2026-10-05). It sits in the margin every top-right
control keeps, so it is above BACK rather than on it at any window size, and over
every panel on a canvas of its own sorted above the interface's; it takes no
clicks. It writes in the workbench's text colour (`SchematicPalette.Current.Text`),
since the workbench is what it is over -- the interface's near-white could not be
seen on Schematic Light's paper (1.01:1). It counts the frames drawn over half a second of real time -- not those
run, which are ten for every one drawn while nothing happens (5.1) -- and hands TextMeshPro its digits in
a reused buffer (`SetCharArray`) rather than formatting a string, because it
redraws by design. `FrameRateCounterPlayTests` holds the counting frames to no
allocation and a redraw to two: in the editor, and only there, `SetCharArray` also
keeps a string copy of the text for the inspector, a char array and a string --
read in TextMeshPro's own source after the first version of that test, which asked
for none, failed on exactly those.

**A reset asks first, and a double-click cannot answer.** RESET PROGRESS puts
its question where the button was, exactly the button's height, with CANCEL
and YES, RESET below it; Enter answers nothing and Escape backs out of the
question before the screen.

**A reset starts the exercises over and keeps the circuits** (Part 15g,
Ahmad's choice, 2026-10-28; it emptied the whole save). It forgets every solved
mark, every answer kept under an exercise's id, the bests, the milestones and
every hint seen (`ProgressStore.ForgetTheExercises`); MY CIRCUIT and every named
circuit stay, and Settings says which is which. Data and display settings live in
PlayerPrefs, not the save, and stay as they were.

**A forgotten exercise is not a new student.** The workbench does not read the
save again, so `ProgressTracker.ProgressReset` is for anything that keeps its own
copy of something the save said: the workbench lets an exercise open go -- MY
CIRCUIT in its place at once, under Settings, and the start page when Settings
comes down. Left on the stage until then, the exercise's circuit was MY CIRCUIT's
to keep (the 15g-2 review).

**Analytics is the one thing that sends data anywhere.** `GameAnalytics`
reports exactly two events, `levelStarted` and `levelSolved`, each carrying an
exercise's id under the parameter the 4.x game sent a level's file name in, to
answer one question: which exercise people stop at. The thirty share their ids
with the 4.x levels, so a level and its exercise count as one place. The
README's "What it collects" section is the canonical list — do not restate it
elsewhere. Nothing about the student's circuit, their bests or their progress
file is ever sent, and a reporting failure must never interrupt them. Adding a
third event, or a new parameter, is a change to what players were told is
collected, so ask first.

**Each event fires once per exercise per session.** Counting every open meant a
student who reopened a hard one four times before giving up read as four people
quitting -- and exercises get reopened in proportion to how hard they are, so
the apparent drop-off was inflated most at exactly the ones this measures.
Solves are deduplicated too, or one solved twice in a session would report more
solves than starts. `AnalyticsRules.ShouldReport` takes the answer;
`GameAnalytics` keeps the set, cleared on boot and never persisted.

**Reporting is on by default and the player can turn it off**, from
Settings, the PRIVACY section's DATA switch. It was a row
of the 4.x main menu itself until Settings existed; one click down, it gained a
line saying what it switches, which the row never had room for. Consent goes through `EndUserConsent`, not the
deprecated `StartDataCollection`; the two flows cannot be mixed, so no
call to the old one may come back. The consent framework does not persist
anything, so the answer lives in `PlayerPrefs` beside the frame cap and
is re-applied every launch. Removing the off switch, or defaulting it to
off, is a decision about what players were promised — ask first.

**The line is ranking, not measurement.** A number describing the student's
own circuit is fine, including one kept between sessions: its gate count, its
timing, the best kept beside an exercise, are facts about what they built
measured against what they built last time. Nobody else's number appears
anywhere.

**Still off limits until I ask by name:** anything that ranks a player
against other people or against an authored ideal — a star rating, a par
score, a leaderboard, a percentile, a grade. Those turn "here is your
circuit" into "here is how you compare", which is a different game.
