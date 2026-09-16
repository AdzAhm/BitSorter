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
  meets it. Three of them: a gate stalling, a collision, and the fact that a
  wire's delay can be scrolled at all. They are fired by what happens, not by
  which level is loaded, and `hintsSeen` in the save remembers them.

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

- **SourceNode emits one bit per tick from tick 0**, then goes silent.
  It needs no special casing: a source has no inputs, so the
  "all inputs filled" rule is vacuously true and it fires every tick.
  If sparse streams are ever needed, make the sequence `Bit?[]` where
  null means emit nothing. Not needed yet — do not add it preemptively.

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

- **Sequential logic uses stateful RegisterNode primitives plus seedable
  edges, not gate-built latches.** Consume semantics destroys a value on
  use, so a cross-coupled NOR latch deadlocks at startup (each gate waits
  on the other's first output) and stalls after one firing (its external
  input port is never refilled). Memory cannot emerge from gate feedback
  here. A RegisterNode — emits the bit it holds, stores the one it just
  consumed — plus edges that start with bits already in transit gives full
  synchronous sequential power with no change to the tick loop.
  **Not yet implemented.**

## Working agreement
- Use Plan mode for anything touching more than one file.
- Every new LogicCore component ships with its Edit Mode tests in the
  same change. No component without a truth-table test.
- After editing scripts, remind me to focus the Unity window so it
  recompiles, then run EditMode tests before we commit.
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

  **They also touch the player's own files.** `SaveGuard` moves the real
  `progress.json` aside and puts it back, and every fixture turns analytics off in
  `OneTimeSetUp` and restores it after -- `GameAnalytics.Boot` runs on
  AfterSceneLoad and would otherwise post real `levelStarted` events from a test
  run, into the one measurement the game collects.

  **Drive the clock, do not wait for it.** `SimulationRunner.StepOneTick` advances
  exactly one tick. The first version of the cue tests called `Run()` and waited
  ninety frames for the two-per-second clock to produce something; it produced
  nothing, with no explanation, and the muted half of the pair *passed* -- a test
  asserting no sound was made is trivially satisfied when nothing would have made
  one. Assertions about absence need a paired test proving the thing is possible.

  **Never move the player's save.** `SaveGuard` points `ProgressStore.Redirected`
  at a scratch file and the real `progress.json` is not opened, copied, moved or
  deleted by any test. Two earlier designs moved it aside and moved it back, and
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

  **Unity must be focused**, or it does not tick and the run never enters play
  mode -- it sits there reporting nothing.

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
  colours and builders; each panel constructs its own hierarchy at runtime
  the way `PlacementGrid` builds its dots. The scene is generated, so an
  authored hierarchy would be dozens of RectTransforms for the builder to
  reproduce and get subtly wrong.
- **`HalfAdderDemoSceneBuilder` is the only authority on scene contents.**
  Anything added by hand is wiped by `BitSorter/Build Play Scene`.
- **`PointerGate` arbitrates the mouse.** Every component that reads a
  click asks it first. Ownership is *derived* from what is happening, never
  claimed and released — a claim that leaks disables input silently, with
  no error and no way for the player to recover. `WiringController` runs at
  `DefaultExecutionOrder(-100)` because a press that grabs a port and a
  press that places a gate are the same press.
- **Nothing may depend on the order components update in.** Unity leaves it
  undefined for scripts with no execution order. When one component needs a
  fact to be true by the time another can see something, produce that fact in
  the call that changes the state, not in a second poll. `LevelSession.RunEnded`
  is how a solve is recorded before any panel can see the pass. It used to be
  recorded by `ProgressTracker` polling in its own Update, and the win panel
  could show the previous record.
- **Sound is procedural**, generated by `ProceduralAudio` exactly as
  `ProceduralSprites` generates sprites. No audio files, no licences.
- **There are nine background tracks and they are one piece of music.** Same five
  notes (A, C, D, E, G), same tempo, same four-bar shape; what differs is
  density, ring, register, which way the figure moves on its alternate pass,
  and where the chords go. `MusicTests` pins the scale and the sparseness,
  because a mistyped semitone is invisible to read and obvious to hear. A rest
  is `Rest`, not "negative" -- some tracks are written an octave down, so
  `semi < 0` would silence them.

  **Two moods, one scale.** Six root that collection on A and read as minor;
  three root it on C, with a softer voice and a reverb tail, and read as major.
  A minor pentatonic and C major pentatonic are the same five pitches -- only
  the bass decides which. That is what lets the set carry two moods without the
  scale rule bending, and it is why a warm track can follow a sad one without
  the switch sounding like a key change. `Voice` says what plays a track;
  reverb is one pass over the finished buffer, so it costs nothing at runtime.

  **The track changes only when the level does**, and `MusicRules` is the one
  place that decides. Never mid-level. Never on a retry, or the music would
  change every time a player failed the level they are already stuck on. Never
  on the first load of a session, or the opening would cross-fade a second after
  the player first heard it. The only random part is where in the cycle a
  session starts, picked once in `GameAudio.Awake`; everything after that is
  deterministic so a test can say which track should be playing.

  **Mute is one switch for the whole game**, music and cues alike. Every cue has
  something on screen saying the same thing, so silence costs no information,
  and a second setting would be four states to reason about for five cues and
  one loop. It used to silence only the music while the clock carried on
  ticking, which is the sound somebody reaching for mute most wants gone. The
  PlayerPrefs key still says music, deliberately: renaming it would reset the
  preference of anyone who had already turned the sound off.

  Music is rendered at half the sample rate the cues are. Nothing in it comes
  near that Nyquist limit, and it is what keeps nine long uncompressed clips
  affordable -- 24 MiB of heap, against 48 at the cue rate, in a game whose whole
  browser build is 16 MB. Clips are built on first use, so a session only pays
  for the tracks it reaches.

  **That figure is `ProceduralAudio.MusicBytes` and `MusicTests` asserts against
  it.** It used to be a number in a comment, written when there were six tracks,
  and it still said six and 34 MB after three more were added -- the set cost
  half again as much as the only place that explained the decision claimed.
- **Anything shown to the player is derived, never restated.** The truth
  table comes from the level's own streams and expectations; node labels
  come from `Node.Name`. A second copy of a fact is a second thing to
  drift.

## Not yet
Do not build ahead of me. The logic core, the view layer, the level
format, the nine levels, the canvas interface, sound, level select,
saved progress, analytics, the sandbox and board undo are all in.

**The sandbox is built in code, not authored as JSON.** That is a
decision, not a shortcut: `LevelLoader.Validate` refuses a level with no
expectations and refuses a sink nothing grades — both correct for a
taught level and both fatal to free play — and `CurriculumTests` then
demands a hint and a goal from every file in `Resources/Levels`. Building
it in `SandboxLevel` leaves all of those rules exactly as strict as they
were rather than carving an exception through them. It also means the
sandbox is not in `LevelCatalog`, so it is not a tenth level: it has no
order, no completion tick and no personal best, and it is reached by an
explicit entry in the main menu and at the foot of the level list.

Free play is ungraded via `RunState.Finished`, which is deliberately not
`Passed` — every "did they win" check names `Passed`, so none of them
fire. Unlimited is spelled `-1`, the way `RemainingDelay` already spells
an absent budget, and the trap is that zero and unlimited are opposites
that both look falsy: test `== 0` for "not stocked", never `<= 0`.

**The guided tutorial is code-built, like the sandbox, and for the same
reasons.** `LevelLoader.Validate` and `CurriculumTests` stay exactly as strict
as they were rather than gaining an exception, and a level file cannot author
wires. It reaches the board through `LevelSession.Adopt`, so `ProgressTracker`
persistence works with neither side knowing the other exists.

It is **not in `LevelCatalog`**: it cannot disturb the nine-level run that
`CurriculumTests` pins, and it never appears in `AvailableLevels`, so Q and E do
not cycle into it and the banner still counts to nine. It is reached by a row at
the head of the level list — free play's row at the foot is the same idea — and
once by itself on a save with no `tutorial` milestone. Unlike the sandbox it
*is* graded, so the last step ends on the ordinary win panel.

**It never blocks input.** Each step is a predicate over board state, so an
unsatisfied step simply does not advance and every other action stays legal —
and a step un-finishes by itself when the player deletes what it asked for,
Ctrl+Z included, with nothing tracking the undo. Gating input would mean
reaching into `PlacementController`, `WiringController` and `PaletteDragSource`,
and pointer ownership is derived and never claimed precisely because a claim
that leaks disables the game with no way back.

**It ends on its own card, after the win panel.** The run settles `Passed`, the
ordinary solved panel appears, and only once it is dismissed does the card take
the screen — a full-screen scrim built like `EndingPanel`, the controls in two
columns from `ControlsReference.Groups`, and one button into the first level.
Skipping is the small quiet button on the instruction strip and just stops, so
finishing and skipping do not look alike.

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
watches its `IsShowing` instead, and puts the card up only once it has **seen**
the panel showing and then seen it gone. This paragraph used to say it worked
because the scene builder adds the director after `WinPanel`. Unity makes no such
promise — the order components are added in is not the order they update in — so
"not showing" on the frame a run passes could just mean the panel had not updated
yet.

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

**Reporting is on by default and the player can turn it off**, from the
main menu's Data item. Consent goes through `EndUserConsent`, not the
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
  brighter**: the first attempt raised its glow and under bloom the gate
  blew out into one bright blob with the sockets lost inside it, which is
  backwards, because the sockets are what carry the meaning. A collision
  one tick away throbs on the port, the wire and the bit at once, amber
  when only the arrival dies and red when the waiting bit dies too.

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
