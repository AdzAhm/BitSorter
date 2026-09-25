# BitSorter

2D Unity puzzle game. Bits (0/1) fall through logic components into bins.
Teaching digital logic: gates, flip-flops, adders, FSMs.

## This document
This file is authoritative, and it drifts silently — nothing recompiles
when it goes stale, and no test turns red. When you hit a section that no
longer matches the code, say so and correct it in the same change. Do not
quietly work around a stale paragraph, and do not treat one as binding
just because it is written down here.

## Architecture rule (do not break this)
- Assets/Scripts/LogicCore/ = pure C#, NO UnityEngine imports.
  Deterministic tick-based simulator. Has its own asmdef.
- Assets/Scripts/View/ = Unity MonoBehaviours. Reads sim state, renders it.
- State flows LogicCore -> View only. The view never mutates the sim.
- No Rigidbody2D for bits. Physics is decoration only (sparks, debris).

## Conventions
- Every LogicCore component gets a unit test with its truth table.
- Levels are JSON in Assets/Resources/Levels/, and each one names its own
  `order` — play order is authored, not the ordinal sort of file names.
  `LevelCatalog` refuses two levels claiming the same place.
- **`goal` states the objective and may name gates outright. `hint` may
  not.** The no-giveaway rules in `CurriculumTests` apply to the hint
  alone. Before `goal` existed the hint had to carry both jobs, which is
  how the half adder ended up with a hint that named both its gates and
  said which output each produced.
- A mechanic is taught before it is required. `CurriculumTests` enforces
  that the delay tutorial precedes every level that budgets delay.
- **There are four kinds of teaching, and they must stay apart.** `goal` states
  the objective, `hint` nudges towards *this level's* answer, and a hint in
  `HintRules` explains a *mechanic*, once ever, the first time the player
  meets it. Four of them: a gate stalling, a collision, the fact that a
  wire's delay can be scrolled at all, and what the bit inside a register
  means. They are fired by what happens, not by which level is loaded, and
  `hintsSeen` in the save remembers them.

  The jobs must stay apart. `balance-the-paths`' hint already covers stalling
  and collision *for that level*, so a first-time hint reaching for the same
  words would be a second copy the player also has to read twice.
  `CurriculumTests` refuses any four-word run shared between the two.

  The fourth is the **guided tutorial**, which teaches *which input does what*
  and nothing else. It may say a wire can be scrolled — that is an input — but
  not what a longer wire does to arrival order, which is `wireDelay`'s job on
  the level where it matters. Finishing it marks no hint as seen, and
  `CurriculumTests` holds tutorial text to the same four-word rule.

  **The level's `hint` is shown in one place: the help panel, behind the `?`
  badge.** It used to be on the status banner *as well*, so the same sentence was
  on screen twice at once, in the same size and colour -- and a player who pressed
  `?` to "see the hint" was handed a line already in front of them, which teaches
  that the button is not worth pressing. The banner carries the title and the goal
  and nothing else. Asking for a nudge is a decision; the brief is not.

  There is deliberately **no hint for a bin that must stay empty**:
  `route-the-bit`'s goal says it on the first level, and the sink readout and
  the fail verdict both name it afterwards.

  **The stall hint fires on a duration, never on a port filling.** A gate is
  never seen holding one input mid-delivery — see the tick-order decision
  below — but a one-tick imbalance is a real stall on a circuit that works,
  and explaining it would teach the player about a mistake they did not make.
  It fires when the run settles with a gate still stalled, or after four
  consecutive ticks.

## My level
I'm a 3rd-semester CE student, new to Unity and git.
Explain things as you go and tell me when I'm about to do something dumb.

## Syllabus scope

What this game teaches, and what it deliberately leaves alone.

**In scope:** boolean algebra, K-map minimization, functional
completeness, propagation delay, combinational components, adders,
flip-flops, FSMs, critical path, pipelining.

**Out of scope:** assembly, the RISC-V datapath, memory addressing, and
number representation. Those are a different game — don't reach for them
even when a level looks like it could stretch that way.

**Hazards are not expressible.** Static and dynamic hazards need a
continuous signal model; bits here are discrete tokens, so a glitch has
nowhere to live. The related lesson this game *can* teach is
unbalanced-path corruption: two paths of different total delay into one
gate, where the early bit waits and the next arrival collides with it.
Reach for that whenever the subject would otherwise be hazards.

**Setup time has an honest substitute; the rest of clock timing does
not.** The clock here is still not a wire — it is the spacing between
vectors, global and exact, so skew and hold time have nothing to be
measured against and a latch and a flip-flop remain the same object. What
*is* expressible, and is what the sequential levels are built on, is the
constraint setup time exists to express: **everything must settle within
one clock period**, and a loop that takes longer is not slow but broken.
Reach for the period whenever the subject would otherwise be clock
timing — and note that a level's clock is chosen to be exactly its
intended loop, so the constraint bites.

**Throughput is not a meaningful score.** A balanced circuit sustains
exactly one vector per tick whatever its depth. Every gate consumes its
inputs, emits, and hands the result to an edge of at least one tick, so
every circuit is already pipelined at gate granularity, and depth buys
latency rather than throughput. An unbalanced circuit does not run slower
— it loses bits: sources have no input ports, so they emit every tick
regardless of what is stalled downstream. There is no backpressure, so
the next bit arrives into a port that is still occupied and the collision
destroys it. Throughput therefore has two states, one vector per tick or
a failed run, with nothing in between to rank. **Score gate count and
latency only.** This also means the classical latency-versus-throughput
pipelining trade has nowhere to live here; the lesson that survives is
stage balancing, which is the same requirement arrived at from the
failure side.

## Core decisions

- **Bit** is `enum Bit { Zero, One }`, never a raw int. Port state is `Bit?`,
  where null means empty. One field expresses zero / one / empty.

- **Edge delay must be >= 1.** The constructor throws
  ArgumentOutOfRangeException below that. Zero-delay edges would force
  node evaluation into topological order, which breaks the rule that
  evaluation order within a tick cannot affect the result. Feedback loops
  also need a real time step to resolve — but note that latches come from
  RegisterNode, not from cross-coupled gates. See the sequential-logic
  decision below.

- **Order-independence is a hard invariant.** Nodes within a single tick
  must be evaluatable in any order with the same outcome. Do not add
  anything that breaks this.

- **SourceNode plays a scripted sequence from tick 0**, then goes silent.
  It needs no special casing: a source has no inputs, so the
  "all inputs filled" rule is vacuously true and it fires every tick.
  The sequence is `Bit?[]`, exactly as this entry said it would be if sparse
  streams were ever needed, and a null is a tick with no bit on it. What
  needed them is the **clock**: a level spaces its vectors out with
  `clockPeriod`, `CircuitBuilder` puts the gaps in, and no stream may
  ask for one by hand — every source has to keep the same beat, and a gap
  written into one of them would put that source out of step silently.
  Gaps go between vectors, never after the last: a source is exhausted
  only once its whole sequence has played, and the grader waits for that
  before it calls a run settled.

- **Collisions never throw.** A bit delivered to an occupied input port is
  destroyed, not thrown on. If the two values match, the port keeps its
  value and only the arrival is destroyed. If they differ the result is
  ambiguous, so neither bit survives: the port is cleared and stays
  poisoned for the rest of that tick's delivery phase, so a later arrival
  in the same tick cannot refill it. A mixed-value collision never leaves
  a survivor.

- **CorruptedCount counts destroyed bits, not collision events.** A
  matching-value collision adds 1. A mixed-value collision adds 2, since
  both bits are destroyed.

- **Between ticks, an occupied input port always belongs to a stalled
  gate.** `Tick` runs advance, deliver, evaluate in that order, so a node
  whose ports are all full is evaluated in the tick that filled them and
  never survives to the next frame holding them. "Holding a bit" and "could
  not fire" are therefore the same observable condition, which is why the
  view needs no separate stall computation. Mid-tick is the exception the
  code still has to handle: a node can be momentarily ready and about to
  fire, and that must not read as stuck. `PortState.IsStalled` is the one
  place this is decided.

- **An imminent collision is predictable exactly, not heuristically.**
  Delivery is phase 2 and evaluation is phase 3, so nothing can empty a
  port between now and an arrival one tick out: if the port is occupied and
  a bit has one tick left, they meet. Comparing that bit's value to
  `Pending` says in advance which outcome `Deliver` will pick. This is what
  lets the board warn before the bang rather than only mark the damage
  after, and it is why the warning can never be a false alarm.

- **A bit is identified by `(Edge.Id, BitInTransit.Serial)`, never by
  `TicksRemaining`.** The serial is assigned in `Edge.Accept` and never
  reused. Deterministic despite being assigned as bits are emitted: an edge
  has one source port and a node evaluates at most once per tick, so an
  edge accepts at most one bit per tick and the sequence is tick order
  whatever order the nodes were visited in.

  `TicksRemaining` separates the bits on one edge *at one instant*, which
  is all a single frame's drawing needs, and `BitInTransit` used to
  recommend it for following a bit *between* frames as well. Those are
  different claims. Across a tick boundary the count a departing bit
  vacates is taken by the bit behind it, so on a delay-1 edge — every wire
  until the player lengthens one — a whole stream reads as one bit that
  never arrives. `BitRenderer` diffed frames on it, so no spark fired after
  the first and the gate and landing cues stopped with them.

  **Two collision facts, and they are not the same.**
  `InputPort.LastCorruptedTick` is the poison flag: simulation state, read
  by the tick loop to refuse later arrivals, and set only where a port is
  actually emptied. `LastCollisionTick` is observational, set wherever a bit
  is destroyed, and nothing in the loop reads it. A matching-value
  collision loses a bit without emptying the port, so it sets the second
  and not the first — and widening the first to cover it would poison a
  port that still legitimately holds its value, changing both
  `CorruptedCount` and the port's contents.

- **NodeCount and EdgeCount are id bounds, not populations.** Removing
  leaves a tombstone: the slot becomes null and the id is retired, never
  reissued, so every surviving id keeps meaning the same node. Use
  LiveNodeCount / LiveEdgeCount for the population, and null-check
  anything GetNode / GetEdge returns — the id range is not dense. A
  removed node or edge reports Id -1, so capture an id before removing
  rather than reading it back off the object. Bits lost to a removal are
  not corruption and must never touch CorruptedCount.

- **Undo is a stack of whole-board snapshots, not inverse operations.**
  `BlueprintSnapshot` copies the blueprint's two lists; both hold only
  readonly structs, so a snapshot shares nothing with the live board and
  is correct by construction rather than by argument. Inverses were
  rejected because two of the six edits — removing a gate, and CLEAR ALL —
  take wires with them, so their inverses are subgraph snapshots anyway.
  The undo unit is one committed edit, recorded after validation so a
  refused edit leaves no step behind. The exception is wire delay: a run
  of scroll notches on **one** wire coalesces into a single step, because
  scrolling 1→4 and pressing Ctrl+Z should land on 1, not 3. History is
  capped, gated on `CanEdit`, and cleared on every level load — before
  `LevelLoaded`, so the board `ProgressTracker` restores is the baseline
  rather than a step the player can reverse past.

- **Sequential logic uses a stateful RegisterNode, not gate-built latches.**
  Consume semantics destroys a value on use, so a cross-coupled NOR latch
  deadlocks at startup (each gate waits on the other's first output) and
  stalls after one firing (its external input port is never refilled).
  Memory cannot emerge from gate feedback here, which is why the register
  is a primitive. **Built**, with the tick loop unchanged.

  **It emits before it is given anything**, on the first tick, and hands on
  every bit it receives in the tick it arrives. That first bit is what
  breaks the startup deadlock: a loop then has one bit circulating in it
  from the beginning, and that bit is the machine's state. This entry used
  to call for "edges that start with bits already in transit" instead —
  same arithmetic, worse game, because a bit sitting on a wire belongs to
  nothing the player can point at. Seeded edges were never built and are
  not needed.

  **Every register starts at 0**, like a reset, and nothing can author it
  otherwise. A machine that wants to start elsewhere encodes its states so
  the reset state is the zero one, which keeps the level format, the save
  file and the palette out of it entirely.

  **A register costs no time of its own, and shifts the stream.** Its *k*-th
  output is the bit it was given on cycle *k-1*, so it hands that bit on one
  clock **earlier** than a plain wire would. That is the whole of the
  arithmetic: a circuit comparing a bit with the one before it balances with
  no padding at all, and the direct path around a register is the one that
  needs lengthening.

- **A loop needs a clock, and this is not a tuning choice.** A state machine
  is a loop, the shortest loop is two wires, and a dense source sends a bit
  every tick — so the next input lands before the state is back, waits in a
  port, and the one after it collides. Sequential levels therefore set
  `clockPeriod`, and the rule the player learns is the real one: everything
  must settle inside one period. Two consequences:

  **Balancing relaxes exactly as clocked design does.** Two paths into a
  gate no longer have to arrive together, only within one period of each
  other, because the next vector is a period away. Skew of a period or more
  still collides.

  **A loop must close within one period.** Longer and the state comes back
  late, the inputs queue, and the run corrupts — this game's own way of
  saying a circuit missed its clock.

## Working agreement
- Use Plan mode for anything touching more than one file.
- Every new LogicCore component ships with its Edit Mode tests in the
  same change. No component without a truth-table test.
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
  and `TestScene.Clear` are the only way in and out. A fixture that loads the game
  and simply finishes leaves it loaded for whatever runs next, and NUnit orders
  fixtures alphabetically -- `AudioPlayTests` ran first and handed a fully built
  game to `PointerArbitrationPlayTests`, which builds its own canvas and assumes
  nothing else is on screen. Two of its four tests went red with none of its own
  code changed, because the main menu is a full-screen panel and the pointer was
  therefore over UI at every coordinate. A test leaking into another test is worse
  than either failing: the red lands in the fixture that is still correct.

  **They would also touch the player's own files.** `SaveGuard.Redirect` points
  the save and the PlayerPrefs settings at scratch copies before the scene loads
  (see "Never move the player's save" below), and every fixture then turns
  analytics off -- `GameAnalytics.Boot` runs on AfterSceneLoad and would otherwise
  post real `levelStarted` events from a test run, into the one measurement the
  game collects. This paragraph used to say `SaveGuard` moved the real file aside
  and put it back, the design the paragraph below explains was replaced.

  **Drive the clock, do not wait for it.** `SimulationRunner.StepOneTick` advances
  exactly one tick. The first version of the cue tests called `Run()` and waited
  ninety frames for the two-per-second clock to produce something; it produced
  nothing, with no explanation, and the muted half of the pair *passed* -- a test
  asserting no sound was made is trivially satisfied when nothing would have made
  one. Assertions about absence need a paired test proving the thing is possible.

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
  far fewer frames a second, `GameAudio` builds the next track at 2 ms *a
  frame*, and `AudioPlayTests.WaitForTheFade` waited five seconds of *wall
  clock* -- too few frames fit in those five seconds, the track never finished,
  and seven audio tests failed, every one of them saying the level track never
  replaced the menu track. Reproducible, so it did not read as a flake, and the
  messages pointed straight at the music. It cost a bisect on 2026-09-20 before
  anyone thought to check focus.

  The fix was to wait on `GameAudio.IsSettled` instead, which is the cue tests'
  rule applied to the music: drive the clock, do not wait for it. The suite now
  passes with the editor behind something else, and on the day ran in 42 seconds
  rather than 102 (twice the tests later, about 85). **A `yield` loop counting seconds is the bug, not the frame rate** --
  anything here that needs to wait waits on the condition, with a generous cap
  that fails loudly rather than returning quietly.

  `IsSettled` says the music has stopped moving and nothing more. Keep it that
  way: a helper that waits for the track a test is about to assert would make
  every one of those assertions pass by construction.

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
  this machine can still be broken for a fresh clone.
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
- **The interface is a Canvas, built in code.** `UiTheme` holds the shared
  builders; each panel constructs its own hierarchy at runtime the way
  `PlacementGrid` builds its dots. The scene is generated, so an authored
  hierarchy would be dozens of RectTransforms for the builder to reproduce
  and get subtly wrong.
- **Every colour comes from `Palette`**, board and interface alike -- no colour
  literal, and no serialized colour field on a renderer. They used to live
  wherever each was first needed, and nothing could compare them: the interface
  took its accents from the node colours on purpose, and the side effect nobody
  weighed was that every source was `Good`, every sink `Bad` and the NAND gate
  `Accent`, to the last digit. **Never change a palette in place** -- derive a
  new one -- and choose it before the scene loads, because renderers take their
  colours when they build. The board tile bakes its colours in, so it is cached
  per palette.

  **The game is drawn in `Looks.NeonBoard`**, `Look.Default`, chosen out of three
  directions rendered side by side. `Look.Classic` is 2.0's look, kept so a change
  that claims to touch no look can be held to Classic's reference shots, pixel
  for pixel. A look's bloom lives in the scene's volume profile, which holds
  Classic's values, so `LookBoot` puts the current look's on every scene as it
  loads. `LookBriefTests` holds every look but Classic to the brief it was drawn
  against: no state colour on the board, a 1 in a colour of its own, a 0 that
  glows in its own colour rather than white or the wire's, panels that cover,
  text at 4.5:1 on every kind of button as well as every panel, and a chosen
  button that stands out.

  **Bloom reaches a bit in flight and nothing else**, in every look. The
  threshold is 1 and only a travelling bit is lifted over it
  (`BitVisuals.Emission`); gates, fixtures, held bits and the interface are all
  drawn in plain colour. Several rules below were first written when bloom
  reached the gates too, and gave it as their reason. They were rendered again
  under Neon Board on 2026-09-24 -- a stalled gate drawn both ways, bloom on and
  off -- and bloom changed no pixel of either. The rules stood anyway, each for a
  reason that does not need bloom, and those are the reasons written below. A
  rule whose only stated reason is bloom is worth suspecting.

  **A button says what it is for** -- `ButtonRole`, and `UiTheme.FillOf` is the
  one place a role becomes a colour. The primary is the one thing a screen asks
  for next, and the only solid button, so an outlined look tells it apart by shape
  as well as colour. Quiet is quieter by its edge, never its caption: a dim
  caption is how a button says it cannot be pressed.

  **A bit says its value by its shape.** In Neon Board a bit in flight is its own
  digit (`BitStyle.Digit`), stroked like a neon tube and kept upright on every
  wire, with its arrival squash laid onto the screen's axes by
  `BitVisuals.Upright`. Colour could not carry it: a magenta 1 and an indigo 0 are
  neighbours, bloom lifted the 0 to a bright violet, and a playtester could not
  tell them apart. A digit wants a quieter halo and a narrower trail than a dot,
  which are the look's `BitGlow` and `TrailWidth`. A bit *held* -- in a socket,
  or inside a register -- is a disc with its digit cut out of it
  (`ProceduralSprites.HeldBit`), not the stroked digit: hollow already means an
  empty socket, so a held bit stays filled and says its value in the cut. The
  disc is exactly the register's circle, so `PortGeometry` did not move.
- **Ambient animation keeps one clock, `ViewTime`; an event counts from its own
  start.** A collision warning throbs in step across port, wire and bit, stalled
  gates breathe together and the grid shimmers as one, so those read
  `ViewTime.Now`. A win, a scorch or a wire just re-timed marks a moment and
  counts from it -- the win celebration once pulsed on the game clock and a solve
  could land at the bottom of its swell.
- **Visual changes are checked against the reference shots.**
  `BitSorter/Capture Reference Shots` captures seventeen states (menu, a built
  board, bits in flight, a collision one tick out and one that takes both bits,
  the solved card, the level list, free play, the help panel, the chapter card, a
  clocked level, the tutorial's intro and its closing card, a first-time hint, a
  register holding a bit, the tutorial ringing a button, the settings asking
  whether to reset) as an Explicit Play
  Mode fixture, so it runs under `SaveGuard` --
  driving the real game for a screenshot by hand once marked a level solved on
  the developer's own save. Frame time is fixed, particle systems are seeded,
  `ViewTime` is pinned and the real mouse and keyboard are swapped for the
  fixture's own -- a click on the Game view mid-capture once put a refusal toast
  into two shots -- so **two captures of the same code are identical to the
  pixel**; a refactor that claims to change nothing is held to that, with no
  tolerance. It needs the Game view, so not batch mode.

  **Two things that can overlap at one sorting order need different depths.**
  Unity breaks a tie in sorting order by depth, and leaves a tie in depth to
  chance. Every bit used to sit at depth zero, so where the half adder's wires
  cross, the two bits' trails drew in either order and about one capture in
  three differed there -- forcing that one order reproduced the odd capture to
  the pixel. `BitRenderer.DepthOf` now gives each bit a depth from its identity,
  in the order Unity had usually chosen, so the fix moved no pixel of the
  reference shots. A difference in a capture is a real one.

  **A capture can be of any look**, named in the `BitSorter.Capture.Look`
  session key (or `-captureLook` on the command line) and saved under a folder
  of that name. The fixture chooses it before the scene loads and fails on a
  name that matches nothing, rather than capturing the game as it is into a
  folder that says otherwise.
- **Where a piece of the HUD sits is `UiRows`, worked out once.** Three
  `UiStack`s -- along the top, along the bottom, down the right -- each place a
  row clear of the one before, so no two rows of one stack can overlap. They
  replaced a dozen constants each written as "the row before, plus its height,
  plus a gap", which held until somebody forgot: the toast went over the
  controls line, the verdict over the first-time hint, the clock over the
  verdict, the help panel over the setup panel -- and each was fixed with one
  more constant and one more test for that one pair. A component reads its row
  and never works out its own clearance; `UiStackTests` checks that every row
  came out of a stack.
- **Text is sized by what it is for, never by a number.** `UiTheme.Label` takes a
  `UiType` -- Micro, Caption, Label, Body, Lead, Numeral, Heading, Title, Display --
  and `UiTheme.SizeOf` is the one place those become sizes. Seventeen sizes from
  10 to 54 had grown a label at a time, so full-screen titles alone came in three
  and two things of one kind drifted a point apart. Nothing is under twelve:
  the music credit, the one line a licence requires be shown, was eleven.
  `UiTypeTests` refuses a size set by number.
- **`HalfAdderDemoSceneBuilder` is the only authority on scene contents.**
  Anything added by hand is wiped by `BitSorter/Build Play Scene`.
- **`PointerGate` arbitrates the mouse.** Every component that reads a
  click asks it first. Ownership is *derived* from what is happening, never
  claimed and released — a claim that leaks disables input silently, with
  no error and no way for the player to recover. `WiringController` runs at
  `DefaultExecutionOrder(-100)` because a press that grabs a port and a
  press that places a gate are the same press. `PointerGate` itself runs at
  `-30000` and samples "over the interface" before anything reacts to the
  frame's input: a tap puts press and release in one frame, the event system
  handles the whole click, and a button that closes its panel would otherwise
  leave nothing under the pointer by the time placement asks.
- **A panel's backdrop is nine-sliced, and a full-screen one is not a panel.**
  `ProceduralSprites.Panel` is a rounded rectangle cut with a ten-texel border,
  so every `Panel_` keeps ten-pixel corners and a solid middle at any size.
  Panels used to borrow the AND gate's squircle, which cannot be sliced -- it
  has no straight edge to repeat and stops short of its own texture -- so
  `Image.Type.Sliced` stretched the whole sprite and every panel faded out
  towards its rim. Over the board that left the help panel's title and hint on
  bare grid. Anything shorter than two corners replaces the sprite, as the help
  badge and the clock's pips do with a circle.

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

  **A full-screen panel has the screen to itself**, and its backdrop is still
  `UiTheme.Scrim`, a flat rectangle: a scrim wants no corners at all, not small
  ones. Before the slicing was fixed this was a workaround for the fade, and it
  left the screen's edges, where the HUD lives, undimmed. The HUD --
  banner, run buttons, controls line, parts list, help badge, bits-lost meter --
  hides while `UiModal.HudVisible` is false, or the panels' titles and help
  lines print over it. Anything that acts on a key asks
  `UiModal.OpenOrJustClosed`, so the press that closed one panel cannot open
  another in the same frame -- nor reach the board: the board's keys asked
  `AnyOpen`, and the Enter that dismissed the chapter card could also run an
  empty board, whenever the card happened to update first.

  **Escape is the main menu and M is the level list.** They were the other
  way round until a playtest, 2026-09-26: Escape is the key players reach for
  to get to a menu. **Escape closes what is on top, and only with nothing on
  top does it open the main menu**; M opens and closes the list, and only
  opens it with nothing open, so it never stacks on the menu.

  On a board, what is on top may be the solved card, a first-time hint or
  the open help panel. None of them is a modal, so the main menu cannot hear
  about them from `UiModal` and asks each one's `HoldsEscape`, which stays
  true for the rest of the frame once Escape has taken it down -- one press,
  one thing, whichever updates first. A new thing that sits on the board and
  closes on Escape needs the same, or the press also opens the menu. And **a panel never answers a key on the
  frame it opened** (`FullScreenPanel.OpenedThisFrame`): the Escape that
  takes down the tutorial's solved card raises the tutorial's own card in
  the same frame, and that card closes on Escape; the Escape that leaves
  Settings reopens the main menu in the same frame, and the menu closes on
  Escape.

  **A full-screen panel derives from `FullScreenPanel`**, which does the four
  things showing one always takes: activate it, bring it to the front, and tell
  `UiModal` when it opens and when it closes -- disabling included. Five panels
  each wrote those out, and they had drifted: the chapter card never brought
  itself to the front. What differs between them goes in `OnShown` and
  `OnHidden`.

  **A panel fades in, and takes clicks while it does.** `UiFade` moves a
  `CanvasGroup`'s alpha and nothing else, so a panel is clickable from the first
  frame it is drawn -- one that refused clicks while fading would cover the board
  and swallow them. Hiding is instant: a panel lingering after it was closed is
  what a stuck one looks like. It counts on `Time.deltaTime`, like the bits-lost
  punch, so captures stay repeatable, and a capture waits on `UiFade.AnyMoving`
  rather than on a number of frames.

  **The HUD stays under a panel until the panel has covered it.** "A panel is up"
  for `UiModal.HudVisible` is `FullScreenPanel.CoversTheHud`: faded in, or at once
  if the HUD was already hidden when it opened. It used to vanish in the frame the
  panel opened, while the panel was still nearly transparent, and the board
  showed bare for a moment. "Already hidden" counts a panel closed earlier in the
  same frame, because the menu closes itself before opening the level list and
  the HUD would otherwise flash up between them. A test that asserts the HUD has
  gone waits for the fade, then one frame for each piece to step aside.
- **The board is framed in what the interface leaves free.** `CameraFraming
  .Fit` centres it between the pixels taken on the left and the right, and
  `CameraFit` reads those from the parts list and free play's setup panel.
  Fitting to the whole screen put the outermost column under the parts list,
  where four shipped levels keep a source — in Carry the one, source B sat
  under the AND with "DELAY 0 of 5" across its label.
- **Nothing may depend on the order components update in.** Unity leaves it
  undefined for scripts with no execution order. When one component needs a
  fact to be true by the time another can see something, produce that fact in
  the call that changes the state, not in a second poll. `LevelSession.RunEnded`
  is how a solve is recorded before any panel can see the pass. It used to be
  recorded by `ProgressTracker` polling in its own Update, and the win panel
  could show the previous record.
- **Sound is procedural, except the main menu's music.** Every cue and every
  level track is generated by `ProceduralAudio`, exactly as `ProceduralSprites`
  generates sprites. The main menu plays two recorded tracks by other composers,
  under free licences: "Dream" by jkjkke (CC0) and "Woodland Fantasy" by Matthew
  Pablo (CC BY 3.0), in `Assets/Audio/Music/`. **Any imported audio needs its
  licence read on its source page before download, an entry in the README's
  Music credits, and -- for CC BY -- the in-game credit**, which is
  `GameAudio.MenuMusicCredit` on the main menu. Woodland Fantasy's author's own
  attribution page is gone, so the credit follows the CC BY 3.0 terms: author,
  title, licence, the change made (converted to mono), and the licence's address
  -- a browser build is a copy that ships without the README, which is the only
  other place the address appeared. **It also needs its loudness measured**; see
  the menu's music below.

  **Never a real game's soundtrack.** Minecraft's tracks -- C418's, Lena
  Raine's, "Sweden", "Infinite Amethyst" -- were asked for by name and declined.
  They are copyrighted, and Mojang's usage guidelines allow approved tracks in
  videos and streams, not copied into another game. The crystal and felt-piano
  tracks reach for those moods with original melodies; keep it that way.
- **There are twenty-two background tracks and they are one piece of music.**
  `ProceduralAudio.MusicTracks` is the count. Same five notes (A, C, D, E, G),
  same tempo, same four-bar shape; what differs is density, ring, register,
  which way the figure moves on its alternate pass, where the chords go, whether
  soft chords swell under it, and which `Voice` plays it. `MusicTests` pins the
  scale -- chord tones included -- and the sparseness, because a mistyped
  semitone is invisible to read and obvious to hear. A rest is `Rest`, not
  "negative" -- some tracks are written an octave down, so `semi < 0` would
  silence them.

  **Many moods, one scale, and no one mood more than a third.** Tracks root that
  collection on A, C, F, B-flat or E: minor, major, open or modal. Eleven of
  twenty-one used to come home to A -- seven instruments cannot disguise one
  mood, and the thing that varies most between tracks was varying least, which
  is audible as the set sounding smaller than it is. Two were re-rooted without
  a note moving, which is the whole point of the trick below. A minor
  pentatonic and C major pentatonic are the same five pitches -- only the bass
  decides which -- and B-flat or F under them gives the lush major-seventh and
  Lydian colours of the crystal tracks without a sixth note. That is what lets
  the set carry many moods without the scale rule bending, and why any track
  can follow any other without the switch sounding like a key change. `Voice`
  says what plays a track: plucked, keys, mallets, felt piano, crystal, music
  box or choir; a `Pad` plays the chord layer. Reverb is one pass over the
  finished buffer, so it costs nothing at runtime.

  **Two tracks are unlike any of the others, and deliberately only two.** One
  plays a counter-melody -- a second line on a second voice, answering the first
  in its gaps -- where every other track is one instrument over a bass and
  sometimes a chord. The other is rooted on E, which gives the same five notes
  an Em7-with-an-eleventh colour that never resolves, and is the only track that
  pulses rather than plays. Both earn their place by being the exception; a set
  of exceptions is just a set.

  A counter-melody obeys every rule the melody does -- same scale, same sixteen
  steps, same lift -- and `MusicTests` asks it for all of them, `HighestPartialHz`
  included, because its voice is its own. Sparseness is checked per line with a
  stated bound on the pair: "half the grid empty" was written when a track could
  only have one line.

  **The plucked voice is down to three tracks, from six.** The first six were
  written before the set had any other voice, and six variations on one pluck
  read as the same piece coming round again rather than as six pieces. What is
  left is the three that actually differ in gesture -- the original, the sparse
  one that drops an octave where the original climbs, and the busiest with its
  paired notes. `MusicTests` states the total, so removing one is a deliberate
  act rather than a drift. **A new voice declares its
  brightest partial in `TopPartial`**, and `HighestPartialHz` holds every track
  under 4.4 kHz -- a bright voice written high would read as hiss above 8 kHz.

  **Nothing under the music, and nothing in it changes value in one sample.**
  Every track once had white noise mixed in "so the quiet parts are not
  digitally dead"; baked into the clip, it rose and fell with the music and was
  reported as a static hiss, plainer on speakers. And three things jumped between
  two samples: the bass restarting on every bar line (a click exactly every
  eight seconds), plucked notes going to full volume in one sample, and notes
  dropped mid-cycle after a fixed five steps. A jump is energy at every
  frequency up to the top of a 22 kHz render, which is where playback
  resampling turns it into a sizzle. So notes start at a zero crossing and rise
  over a millisecond, each bar strikes its own bass note and chord while the
  last bar's fall away under them, and a note is only dropped once it is below
  -60 dB. `MusicTests` looks above 8 kHz, where no note reaches, and holds hiss
  and clicks to -66 and -54 dBFS.

  **The track changes only when the level does**, and `MusicRules` is the one
  place that decides. Never mid-level. Never on a retry, or the music would
  change every time a player failed the level they are already stuck on. Never
  on the first load of a session, or the opening would cross-fade a second after
  the player first heard it. **Which track comes next is `MusicBag`'s answer**:
  a shuffle that deals every track once per bag and never the same track twice
  running, seeded once in `GameAudio.Awake` -- the seed is the only random part,
  so a test can build the same bag and say which track should be playing.

  **The main menu has its own music.** Which of its two tracks it opens on is
  drawn from the same session seed (`MusicRules.FirstMenuTrack`), and every start
  after that is the other one: when a track ends -- they are not built to loop --
  and every time the menu is opened again. It used to open on "Dream" every
  launch and restart it on every visit, so "Woodland Fantasy" was heard only if
  the menu stayed up for the whole two and a half minutes of Dream. Leaving the
  menu fades to the level's track, and coming back to the level resumes the same
  one, because opening the menu is not a level change. The menu track not
  playing has its audio unloaded.

  **The recordings play at the level music's loudness.** They were mastered 6 to
  11 dB louder than the generated tracks, and at the same volume the music
  dropped away every time a level started. `ProceduralAudio.MusicLoudnessDb` is
  the set's loudness, which `MusicTests` holds every generated track to within
  3 dB of; each menu track carries its own loudness beside its path in the scene
  builder, and `GameAudio` turns it to the set's. That figure is measured, not
  chosen -- the file's RMS once imported, mixed to mono and normalised -- and
  `AudioPlayTests` decodes the files to check what the menu then sounds like
  against a level. **A recording added or re-exported needs measuring before it
  goes in**, or that test fails.

  **A track is built before it is needed, and a switch waits for it.**
  `MusicBake` renders a track a slice at a time, bit-identical to rendering it
  in one go, and `GameAudio` builds the next one -- the shuffle's `PeekNext`, or
  the track a change is heading to -- at 2 ms a frame. Building on the spot used
  to freeze a frame for 260-550 ms; the chord tracks take up to 0.9 s. If a
  change arrives before its track is built, the music already playing carries on
  while the build finishes at 8 ms a frame, and the switch comes a moment late.

  **Mute is one switch for the whole game**, music and cues alike. Every cue has
  something on screen saying the same thing, so silence costs no information,
  and a second setting would be four states to reason about for five cues and
  one loop. It used to silence only the music while the clock carried on
  ticking, which is the sound somebody reaching for mute most wants gone. The
  PlayerPrefs key still says music, deliberately: renaming it would reset the
  preference of anyone who had already turned the sound off.

  Music is rendered at half the sample rate the cues are. Nothing in it comes
  near that Nyquist limit, and it halves what each long uncompressed clip costs.
  **At most three tracks are held at once** -- the one playing, the next one
  ready, and the buffer the one after is being built in -- about 8.5 MB, in a
  game whose whole browser build downloads about 21 MB (measured 2026-09-23; it
  said 16 here, written before the menu's recordings). Every track built used to stay for
  the session; at twenty-two tracks that would have been over 55 MB. A clip
  belongs to whoever built it, and `GameAudio` destroys the one it leaves.

  **That figure is `ProceduralAudio.MusicResidentBytes` and `MusicTests`
  asserts against it.** It used to be the whole set, written in a comment when
  there were six tracks, and it still said six and 34 MB after three more were
  added -- the set cost half again as much as the only place that explained the
  decision claimed. The number of tracks no longer moves it at all.
- **AND and NAND are the textbook D.** A flat back, straight top and bottom, a
  round front -- the symbol a student reads in every lecture. They were a rounded
  square, a shape the game had made up. As wide as tall like every gate, so the
  aspect-ratio rule below still holds; square back corners and a round front keep
  it apart from the OR family's pointed front at a glance.
- **A register is drawn as what it is, not as another gate.** Its
  silhouette is a tall box with the clock's notch cut out of the left edge,
  and it is the only shape taller than it is wide — aspect ratio is the cue
  that reads before colour does, the same reasoning that made sources a wide
  capsule. Inside it sits **the bit it is holding**, in that bit's own colour,
  which swells for a moment when it changes: the state of a machine has to be
  readable on the board while it runs. The body is the palest, least
  saturated thing on the board on purpose. Near-white was tried and was
  wrong the way the stalled-gate glow was wrong — a bright slab with the held
  bit lost inside it. That was blamed on bloom, which no longer reaches a
  body; the rule stands for the stall's reason below, that the largest bright
  area on a node outshouts the small thing inside it that carries the meaning.

  **The capture is a swell, not a brightening**, and that too was first
  argued from bloom -- "bloom is already brightest at the middle of a node" --
  which stopped being true once held bits were drawn in plain colour. It
  stands because a plain colour made brighter can only move towards white,
  the one direction that erases which value it was, where a change of size
  keeps the colour and the digit and does not depend on colour at all.

  **The held bit is measured against the body, in `PortGeometry`, and sits
  right of centre.** It shipped drawn from a size on `NodeRenderer` while the
  outline lived in `ProceduralSprites`, with nothing comparing the two: at rest
  the disc covered the notch, and a capture swelled it wider than the box it
  was inside, so the state changing read as the register bursting. Offset, it
  leaves the notch showing and puts the state on the Q side.
- **The clock is on screen when there is one.** `ClockReadout` draws one pip
  per tick of the period under the banner, lit in turn, and only on levels
  that have a clock. Every other rule of this chapter is visible on the
  board; "a vector every third tick, and your loop has that long" is not,
  until something collides. It sits on `UiRows.Clock`, under the
  verdict — worked out from the banner instead, it landed on top of the
  verdict, which is the third time two things in that file each owned half
  the arithmetic.

  **`ClockDiagram` is the same fact in the course's notation**, behind F3:
  a square wave over three cycles with a playhead on the current tick, in
  the bottom-left corner. It is the pair to the readout, not a replacement
  — the pips explain the beat to someone who has never seen a timing
  diagram, and this is for someone who has. Behind F3 and absent from the
  controls line, so it costs a player who does not want it nothing -- but
  named, quietly, at the end of the clock strip, which is on screen exactly
  on the levels where it means anything, and on the tutorial's card. It was
  once named nowhere, which is not "costs nothing" but "does not exist".

  **The two F3 readouts take opposite bottom corners**, and both hide while
  `UiModal.HudVisible` is false. They come up on one key and are anchored
  the same way at the same width, so a shared corner would put them in one
  rectangle — which the catch readout and diagnostics already did once.
- **Anything shown to the player is derived, never restated.** The truth
  table comes from the level's own streams and expectations; node labels
  come from `Node.Name`. A second copy of a fact is a second thing to
  drift. The table draws a row per **clock cycle**, not per vector: a sink
  fed through a register takes a bit after the streams have run out, and a
  table that stopped at the vectors disagreed with the level it described.

## Not yet
Do not build ahead of me. The logic core, the view layer, the level
format, the seventeen levels, the canvas interface, sound, level select,
saved progress, analytics, the sandbox, board undo and the settings screen
are all in.

**Seventeen: nine combinational, eight sequential.** The sequential
chapter is the register, the rising edge, the toggle, the enabled
register, the two-bit counter, the 1-0-1 detector, the Moore reading of it
and the serial adder — orders 100 to 170, in tens like the rest. Its card
(`ChapterCard`) is shown once, on the first level whose parts list holds a
register, and is a milestone in the save beside the tutorial.

**Where the chapters divide is `LevelCatalog.IsSequential`, and it is the
only place that knows.** The card fires on it and the level list draws its
headings from it — `CIRCUITS THAT FORGET` and `CIRCUITS THAT REMEMBER`,
both constants there, the second being the card's own title. Deciding by
what a level *stocks* rather than by its number means inserting or
reordering levels cannot put the boundary in the wrong place, and
`CurriculumTests` refuses a run whose two chapters interleave — which is
the only way a level could end up under the wrong heading.

**The banner reserves three lines for the goal and shrinks to what it
uses.** A goal is centred and wrapping, so before this it overflowed a
28-pixel box in both directions and printed over the level title; six of
the seventeen goals are longer than one line. `UiTheme.GoalHeight`
measures one with the label that will draw it, and is what both the
banner and `UiThemeTests` ask — a level whose goal will not fit is a
failing test rather than a smudge on the title. Everything below the
banner is still placed from its full row, `UiRows.Banner`, so a short banner
leaves a wider gap and never a collision.

**The sandbox is built in code, not authored as JSON.** That is a
decision, not a shortcut: `LevelLoader.Validate` refuses a level with no
expectations and refuses a sink nothing grades — both correct for a
taught level and both fatal to free play — and `CurriculumTests` then
demands a hint and a goal from every file in `Resources/Levels`. Building
it in `SandboxLevel` leaves all of those rules exactly as strict as they
were rather than carving an exception through them. It also means the
sandbox is not in `LevelCatalog`, so it is not a level in the run: it has no
order, no completion tick and no personal best, and it is reached by an
explicit entry in the main menu and at the foot of the level list.

Free play is ungraded via `RunState.Finished`, which is deliberately not
`Passed` — every "did they win" check names `Passed`, so none of them
fire. Unlimited is spelled `-1`, the way `RemainingDelay` already spells
an absent budget, and the trap is that zero and unlimited are opposites
that both look falsy: test `== 0` for "not stocked", never `<= 0`.

**Its setup is a docked panel, not a modal.** `SandboxPanel` sits down the
right-hand edge beside the board, collapses to a tab, reads no keys and
never registers with `UiModal` — a panel that closed on Escape was racing
the level list for the same press. It is part of the HUD, so it steps
aside for a full-screen panel, and it reports its width to `CameraFit`,
which frames the board in what is left. Free play's catch readout lives in
it now, in the same columns as the streams above, rather than in the
bottom-right corner.

**A setup edit is `LevelSession.Reconfigure`, never `Adopt`.** Adopt is a
level *switch*: it empties the undo history, resets the part in hand,
cancels the run and makes `ProgressTracker` rewrite the save — which is
what one click on one bit used to cost. Reconfigure swaps the definition
under the board and raises `LevelChanged`, which is for anything derived
from the level rather than the board; the help panel's truth table is the
subscriber that matters, since free play's streams are the player's.
Entering free play is still an Adopt, and that is what saves and restores.

**Fixtures have fixed slots and both edge columns are reserved.** Slot *i*
sits at `halfExtents.y - i`, never re-centred: fixtures used to be centred
in their column, so adding a source moved every one of them, and wires are
stored by cell — a circuit wired to A silently became one wired to B.
`LevelDefinition.ReservedSlots` then keeps the whole of both columns clear
of gates, because the count is the player's to raise at any moment.
Wires may still end on a reserved slot, so a fixture counted away leaves
its wires in the blueprint, built by nothing, and they return with it.
Boards saved under the old layout are moved onto the new slots by
`SandboxLevel.MigrateLegacyBoard`, which `SandboxConfig.layout` gates —
a missing `layout` is zero, and zero means centred.

**Streams are stored at `MaxVectors` and the level takes the first few**,
so the vector count decides how much is emitted rather than how much
survives. It used to truncate, so stepping down and back up returned
zeros where the player's pattern had been.

**`SimulationRunner.Speed` is free play's alone.** It divides the tick
interval by 1, 2 or 4, and `LevelSession` puts it back to `DefaultSpeed`
whenever a level is installed, so a fast sandbox cannot follow the player
into a taught level.

**The guided tutorial is code-built, like the sandbox, and for the same
reasons.** `LevelLoader.Validate` and `CurriculumTests` stay exactly as strict
as they were rather than gaining an exception, and a level file cannot author
wires. It reaches the board through `LevelSession.Adopt`, so `ProgressTracker`
persistence works with neither side knowing the other exists.

It is **not in `LevelCatalog`**: it cannot disturb the run whose order
`CurriculumTests` pins, and it never appears in `AvailableLevels`, so Q and E
never step into it and the banner counts the seventeen levels without it. It is
reached by a row at the head of the level list — free play's row at the foot is
the same idea — and once by itself on a save with no `tutorial` milestone.

**Off the run is before its start.** From the tutorial E goes to the first
level and Q goes nowhere; from free play neither key goes anywhere, because
free play is not one of the levels. Q used to start at the far end from any
board off the run, so it went to level 17 from the tutorial -- one step before
the first level, where the fix for Q wrapping stopped. A fresh save starts in
the tutorial, so that was most players' first Q. Found in a playtest,
2026-09-26.

**The main menu's CONTINUE goes back to the tutorial** when it is running,
rather than to the furthest unsolved level, which is where it goes from
everywhere else. Escape opens the menu and is the key a new player presses
first; leaving the tutorial for level 1 lost it for the session. Unlike the sandbox it
*is* graded, so the last step ends on the ordinary win panel.

**No step ever blocks input.** Each step is a predicate over board state, so an
unsatisfied step simply does not advance and every other action stays legal —
and a step un-finishes by itself when the player deletes what it asked for,
Ctrl+Z included, with nothing tracking the undo.

**A part on the wrong square is named, and so is the way off.** Not refused --
nothing is -- but the step's own line would ask for a click the board then
refuses, so `TutorialScript.CorrectionText` says instead which part is where it
should not be and to right-click it: the decoy on the NOT's square, the NOT on
another square, or both. Found in a playtest, 2026-09-25, and the sweep after it
found the other side: the first step, "the NOT is in hand", is also met by the NOT
already on its square, because a drag from the parts list places a part without
selecting it.

**The intro is the one exception, and it holds the whole board.** Until the
player presses START or SKIP nothing on the board takes an edit; after either,
everything is theirs. That moment is the only one where a free board costs
something that cannot be got back: the tutorial stocks one NOT and one AND
decoy, and the step that wants the NOT wants it on one particular cell, so a
part spent before it is asked for leaves a step that cannot be met and nothing
in hand to meet it with. A playtester hit exactly that — the instructions asked
for a gate that was no longer there.

It is refused with a reason, never ignored: a click that does nothing and says
nothing teaches that the board is broken, and this is the first board anyone
sees. And it is **one gate, not three controllers**. Every board edit already
passes through `LevelRules.CanEdit`, which exists to say the board is not
editable and to say why, so the tutorial is another reason rather than a new
mechanism — this paragraph used to argue against gating precisely because it
imagined reaching into `PlacementController`, `WiringController` and
`PaletteDragSource`, and it does not.

`TutorialDirector.HoldingTheBoard` is **derived from the phase and never set**,
which is the rule pointer ownership follows and for the same reason: a hold that
can be claimed is a hold that can leak, and a leaked hold is a board nobody can
touch with nothing on screen explaining why. There is no state to leak — no
director, a destroyed one, or any phase but the intro, and the board is free.
The tests that matter are the two proving both ways out, not the one proving the
hold.

**It ends on its own card, after the win panel.** The run settles `Passed`, the
ordinary solved panel appears, and only once it is dismissed does the card take
the screen — a full-screen scrim built like `EndingPanel`, the controls in two
columns from `ControlsReference.Groups`, and one button into the first level.
Skipping is the small quiet button on the instruction strip and just stops, so
finishing and skipping do not look alike.

**The solved card leads to the ending card, not past it.** While the tutorial is
running its steps, the solved card's one button is CONTINUE, which dismisses it so
the ending card comes up (`TutorialDirector.EndsOnItsCard`, derived from the phase
like `HoldingTheBoard`). It used to offer PLAY THE FIRST LEVEL, which loaded the
level and skipped the card. A player who skipped the tutorial and solved its
board anyway gets no ending card, so for them the solved card still offers the
first level.

**Leaving it once its run has passed is finishing it, and it offers itself once
a session.** Recorded as walking away, leaving a solved tutorial left no
milestone on an empty first-level board -- a first-time player, to the
auto-start -- and a playtester who had just finished the tutorial was put
straight back in it. Opening the first level from the list mid-tutorial looped
the same way.

**Nothing outside the run may count itself as a level.** `LevelCatalog.
IsOffCatalogue` is the one place that knows free play and the tutorial are not
levels in the run, and `ProgressTracker` and `GameAnalytics` both ask it. The
tutorial being *graded* is what made this bite: free play never passes so it
never reached the code that records a solve, and without the guard the tutorial
marked itself complete, took a personal best and reported itself to analytics.
The board rule is narrower still — free play keeps its board on purpose, and only
the tutorial must always start empty, guarded on save *and* on restore because a
restored circuit would satisfy all six steps the instant it loaded.

Traps worth knowing, all found in play mode and none visible from the script.
`PlacementController` puts the selection on a level's **first** budget row on
every load, so the tutorial stocks a decoy first — otherwise the "pick a part"
step is already complete before the player touches anything. The main menu holds
`UiModal` at boot, so auto-launch waits for it to have been closed rather than
firing on startup. And `WinPanel` never registers with `UiModal`, so the director
asks the panel itself: the card goes up once `WinPanel.PresentedThisRun` and the
panel is no longer showing. "Not showing" alone cannot say the panel has had its
turn -- on the frame a run passes it may simply not have updated yet, and Unity
makes no promise about which updates first. This used to be the director
*watching* for the panel, "seen showing, then gone", which depended on that same
order: a card dismissed in the frame it appeared was never seen, and the ending
waited for a card that had already come and gone. `PresentedThisRun` is set in
the call that presents the card, so there is nothing to catch.

**Settings is sound, the data switch, and starting over.** `SettingsPanel` is
reached only from the main menu and goes back to it, and it plays the menu's
music. Sound and Data were rows of the menu until it existed.

**A reset asks first, and a double-click cannot answer.** RESET PROGRESS puts
its question where the button was, exactly the button's height, with CANCEL
and YES, RESET below it; Enter answers nothing and Escape backs out of the
question before the screen.

**A reset switches level first and empties the save after.**
`ProgressTracker.ResetProgress` loads the first level, then clears the store,
so whatever is written on the way out of the old level -- its board, or the
tutorial recording itself finished -- is emptied with the rest. Boards are
neither saved nor restored while it runs, or the first level would come back
with its old circuit. Sound and data live in PlayerPrefs, not the save, and
stay as they were.

**An empty file is not a new player.** The tutorial remembers it has run this
session and free play holds its setup in memory, and neither reads the save
again. `ProgressTracker.ProgressReset` is for anything that keeps its own copy
of something the save said; without it a reset brought back neither the
tutorial nor the opening setup until a restart.

**Analytics is the one thing that sends data anywhere.** `GameAnalytics`
reports exactly two events, `levelStarted` and `levelSolved`, each
carrying the level's file name, to answer one question: which level
people stop at. The README's "What it collects" section is the canonical
list — do not restate it elsewhere. Nothing about the player's circuit,
their bests or their progress file is ever sent, and a reporting failure
must never interrupt play. Adding a third event, or a new parameter, is a
change to what players were told is collected, so ask first.

**Each event fires once per level per session.** Re-entering a level from the
level list refires `LevelLoaded`, so counting every load meant a player who
reopened a hard level four times before giving up read as four people quitting
— and levels get reopened in proportion to how hard they are, so the apparent
drop-off was inflated most at exactly the levels this measures. The old number
was not merely noisy: `LevelLoaded` fires on opening a level but not on RESET,
so it counted neither attempts nor players, only which key somebody retried
with. Solves are deduplicated too, or a level solved twice in one session would
report more solves than starts. `AnalyticsRules.ShouldReport` takes the answer;
`GameAnalytics` keeps the set, cleared on boot and never persisted.

**Reporting is on by default and the player can turn it off**, from
Settings on the main menu -- the PRIVACY section's DATA switch. It was a row
of the main menu itself until Settings existed; one click down, it gained a
line saying what it switches, which the row never had room for. Consent goes through `EndUserConsent`, not the
deprecated `StartDataCollection`; the two flows cannot be mixed, so no
call to the old one may come back. The consent framework does not persist
anything, so the answer lives in `PlayerPrefs` beside the mute setting and
is re-applied every launch. Removing the off switch, or defaulting it to
off, is a decision about what players were promised — ask first.

**The line is ranking, not measurement.** A number describing the player's
own circuit is fine, including one kept between sessions: gate count and
latency on the win panel, and the personal best beside each level, are
facts about what they built measured against what they built last time.
Nobody else's number appears anywhere.

**Still off limits until I ask by name:** anything that ranks a player
against other people or against an authored ideal — a star rating, a par
score, a leaderboard, a percentile, a grade. Those turn "here is your
circuit" into "here is how you compare", which is a different game.

Whatever gets measured, **throughput must never be one of them** — see the
Syllabus scope note above for why it has only two states.

## Level ideas
Design notes only — not implementation work, and not a backlog. Nothing
here gets built, scaffolded or prepared for until I explicitly ask for it
by name. Treat this section as a place to park ideas, not as a to-do list.

- **Flip-flops and FSMs. Shipped**, as the eight sequential levels.
  `Docs/level-roadmap.md` listed three blockers — the register, a way to
  author its initial state, and a palette slot — and a fourth one nobody
  had noticed: a loop cannot keep up with a vector every tick. The clock
  answers the fourth, and the second dissolved rather than being built,
  because every register starting at 0 needs no authoring at all.

  What is left of that chapter's ideas: level- versus edge-triggering and
  clock skew stay out (see Syllabus scope), and a wider board is still
  what stands between this game and carry-lookahead.

- **NAND-only puzzle.** NAND and NOR are each functionally complete —
  every other gate, including NOT, can be built from either one alone.
  A level that hands the player nothing but NANDs and asks for XOR.

  **Shipped** as `nothing-but-nand.json`, level 5. This entry went on reading
  as an unbuilt idea after the level existed.

- **Unbalanced path delays.** Feeding a second-stage gate along paths of
  different total delay causes corruption, not wrong answers. The early
  bit latches in its port, and the next arrival collides with it. This is
  the core difficulty of the adder chapters, and the reason CorruptedCount
  exists as a game mechanic rather than just a diagnostic.

  **Shipped, and the timing-hazard chapter is unblocked.** Of the two
  candidate fixes once listed here — a locked `wires` array in the level
  JSON, or player-chosen wire delays — the second is the one that landed
  (2925472). Delay is a resource the player manages: wires carry a delay
  scrolled on the wire itself, `LevelRules.CanSetDelay` polices the floor
  of 1, and a level bounds it with `maxWireDelay` and `delayBudget`.
  `balance-the-paths.json` is the first level built on it.

  The road not taken is still not built: there is no fixture-wire array,
  so a level cannot author an unbalanced path of its own. Every wire on
  the board is the player's, which means every timing hazard is one they
  created and can therefore undo. Worth knowing when writing the adder
  chapters — a level can constrain the delay budget, but it cannot hand
  the player a pre-broken circuit to repair.

- **A waiting bit needs a stronger visual. Shipped.** This entry used to
  claim a held bit rendered as a small square inside the node. It never
  did: `BitRenderer` released the sprite the moment the bit left transit
  and nothing drew it again, so a waiting bit was drawn as *nothing* and a
  stalled board showed gates idling for no stated reason.

  Now an empty input socket is a hollow ring and a full one is a filled
  disc in that bit's own colour, carrying the glow it had on the wire. A
  stalled gate drains and dims with a slow amber breath — **darker, not
  brighter**: the first attempt raised its glow and the gate became one
  bright blob with the sockets lost inside it, which is backwards, because
  the sockets are what carry the meaning. That was put down to bloom, and
  re-rendered on 2026-09-24 with bloom off the brighter gate was still the
  blob: the halo and the body are the two largest things on a gate and the
  held bit the smallest, so dimming the first two is the only way to let the
  held bit stand out against them. A collision
  one tick away throbs on the port, the wire and the bit at once, amber
  when only the arrival dies and red when the waiting bit dies too -- and
  then the waiting bit is **crossed out**, because amber and red are the
  pair a red-green colour-blind player cannot separate. The cross says the
  one thing that differs, on the bit it is about, by shape. Not a faster
  throb: the warning already pulses above three times a second.

  **The aftermath flash fires once per collision, and every collision gets
  one.** Both halves of that were wrong. It was armed from "the port's last
  collision was the tick just executed", which is a standing fact rather
  than an event — so the view re-armed it on every frame, invisibly while
  the clock moved and permanently once it stopped. A run that settled on a
  collision tick left the port swollen and red until the next rebuild, and
  never repainted with what it was holding, because a port mid-flash is
  skipped by the resting pass. And it keyed on the poison flag, which only
  a mixed-value collision sets, so a matching-value collision scorched the
  board and moved the meter while the port itself did nothing.
  `CollisionWatch` owns the decision now and is reachable from Edit Mode;
  the renderer keeps the countdown.

  Still worth playtesting with someone unfamiliar with the game — that was
  the original point of this entry and no amount of design settles it.
