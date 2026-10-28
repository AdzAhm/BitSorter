# BitSorter

[![LogicCore tests](https://github.com/AdzAhm/BitSorter/actions/workflows/logic-core.yml/badge.svg)](https://github.com/AdzAhm/BitSorter/actions/workflows/logic-core.yml)

Bits fall through logic. Sort them.

![A half adder running: bits leave the two sources, cross into an XOR and an AND,
and land in the SUM and CARRY bins](Docs/half-adder.gif)

A puzzle game about building digital circuits. Sources emit a stream of 0s and
1s, bins want particular values, and you have a box of gates. Wire them together
so every bin gets what it asked for.

Above is the half adder: `A` and `B` each feed both gates, XOR produces
the sum and AND produces the carry. Every bit is drawn as the digit it carries: a
magenta 1, an indigo 0.

Thirty levels, from routing a single bit to a state machine that adds two
numbers a column at a time. Built in Unity
as a way of working through a computer engineering digital systems course from
the inside.

## Play it

| Where | |
| --- | --- |
| **In a browser** | [Unity Play](https://play.unity.com/en/games/c22f4580-98a3-4fcd-a844-e9d731257c83/bitsorter), or [GitHub Pages](https://adzahm.github.io/BitSorter/) |
| **Windows** | [Download the latest release](https://github.com/AdzAhm/BitSorter/releases/latest) — 40 MB zip |

The two browser links are the same build, hosted twice so neither one going down
takes the game with it. Nothing to install, and no account needed for either.

**Want to help?** Bug reports, level ideas and code are all welcome.
[CONTRIBUTING.md](CONTRIBUTING.md) explains how to open an issue, fork the
project and send a pull request.

---

## Playing

**In a browser**, use either link above. Progress is saved by the browser itself,
so it survives a reload but is per-browser and per-device — and a private window
or blocked third-party storage will make the game forget between sessions. In
the browser's fullscreen, the first `Esc` leaves fullscreen — the browser keeps
that key for itself — so the MENU button is the way to the menu there. The same
simulator runs faster as the desktop build, which a drawing of hundreds of parts
notices; the browser says so, once, if it is ever slow to draw yours again.

**On Windows**, unzip the release anywhere and run `BitSorter.exe`. Nothing is
installed and nothing is written outside your own user folder.

Windows will probably warn that it does not recognise the publisher — the build
is unsigned, which is all that warning means. "More info", then "Run anyway".

**Settings** -- on the main menu, or in 5.0 the workbench's SETTINGS -- has the
sound -- one switch for everything, and one each for the music (off until you
switch it on, in 5.0) and the effects -- and its volume, fullscreen on Windows
(`Alt`+`Enter` works too), the data switch, a way to start over, and the credits.
On Windows it also has vertical sync, on by default so the game keeps to your
screen's refresh rate, and with it off a frame cap from 30 to 240 frames a second,
or none. A lower cap uses less power on a laptop. FPS COUNTER, on Windows and in
the browser, puts a small frame-rate counter in the top-right corner. In 5.0
Settings and the credits are pages of the workbench, drawn in the style you chose,
and Settings scrolls in a short window.

The bottom-right corner of the main menu says which version you are running (in
5.0, the foot of the start page), which is worth including if you report a bug.

[A guided tutorial](#the-tutorial) covers the controls, thirty levels teach the
ideas in order, and [a sandbox](#the-sandbox) is there for when you would rather
build something without being marked on it.

### Two rules explain almost everything

**A gate fires when all of its inputs are full, and consumes them.** It cannot
fire on one input and wait for the other. Whatever arrives first sits in the port
until its partner turns up.

**Every wire takes at least one tick.** A longer path costs more ticks, so two
paths into the same gate can arrive at different times.

Put those together and you get the mistake the middle levels are built around. If
one input to a gate arrives a tick before the other, the early bit waits in its
port — and the *next* bit down that wire arrives to find it still there. They
collide, and both are destroyed.

A red scorch mark appears on the port where that happened. It is the most useful
thing on the screen: it names the junction whose paths are unbalanced. Fix it by
making both paths take the same number of ticks, either by routing differently or
by scrolling a wire to lengthen it.

An unbalanced circuit does not run slower. It loses bits.

### Reading the board

Every input port is drawn on the gate itself: a hollow ring when it is empty, a
filled disc with the bit's digit cut out of it when it is holding one. A gate
holding a bit it cannot use yet — waiting on its other input — dims and breathes
slowly amber.
A paused board therefore says which gates are stuck, and which port is the reason.

When a bit is waiting and another is one tick from arriving on the same wire, the
port, the wire and the incoming bit all pulse together: amber when only the
arrival will be destroyed, red when the waiting bit dies with it — and then the
waiting bit is crossed out as well, so the difference does not rest on telling
amber from red. That warning is exact rather than a guess. Delivery happens before evaluation, so nothing can
empty the port in between — if it is lit, the collision is already unavoidable.

### The tutorial

A short guided level, reached from the level list, which walks through picking a
part, placing it, wiring it up, lengthening a wire and running it. Seven steps,
each waiting for you to actually do the thing rather than for you to click Next.

It never takes the controls away. Every other action stays legal throughout, and
a step quietly un-finishes itself if you delete what it asked for — undo
included. Skippable at any point, and replayable from the level list afterwards.
On a fresh save it runs once by itself. Put a part on the wrong square and it
says which one, and how to take it off.

Past that, single-line hints appear the first time you meet something that wants
explaining: a gate stalling, a collision, the fact that a wire's delay can be
scrolled at all, and what the bit inside a register means. Each appears once —
until you reset your progress — and none of them pause the game.

### Controls

| Action | Effect |
| --- | --- |
| Click a gate in the palette, then click the board | Place it |
| `1` – `7` | Pick a part: NOT, AND, OR, XOR, NAND, NOR, register |
| Drag from one port to another | Wire them |
| Right click | Delete a gate or a wire |
| Scroll on a wire, or `[` / `]` | Change its delay |
| `Ctrl`+`Z`, or the UNDO button | Undo the last edit |
| `Ctrl`+`Y` / `Ctrl`+`Shift`+`Z` | Redo it |
| `Enter`, or the RUN button | Run |
| `R`, or the RESET button | Stop a run, or clear what a finished one left on the board. There is no need to press it before editing: after a run, the first edit puts the board back by itself |
| `Shift`+`R` | Clear everything you built. On a level that opens on a circuit this is START OVER, which puts that circuit back |
| `Space` | Pause a run |
| `→` while paused | Step one tick |
| `H`, or the `?` button | This level's truth table, its Karnaugh map for each bin where it has one, and a hint |
| `Esc`, or the MENU button | The main menu. `Esc` closes whatever is on top first |
| `M` | The level list, and the way into the sandbox |
| `N` | Mute the game; the volume is in Settings |
| `Alt`+`Enter` | Fullscreen, on Windows; also in Settings |
| `Q` / `E` | Previous / next level — they stop at the first and the last. From the tutorial, `E` goes to level 1; free play is not a level, so they point to `M` there |
| `F8`, or the square-wave button | The timing diagram: what every source sent and every bin got, tick by tick, and the clock on levels that have one. F8 because browsers keep most of the other function keys for themselves (F3 is find-in-page, F5 reloads) |
| `F2` | The clock as a square wave, on levels that have a clock, and some developer numbers |
| `Alt`+click a wire | Add that wire to the timing diagram, W1 to W4, to see what arrives at its end each tick. Alt+click it again to take it out |
| Scroll on the timing diagram | Zoom in on the tick under the cursor, or back out to the whole run. With `Shift`, or scrolling sideways, move through the run |

### What it teaches

Thirty levels, in this order. Each one is a topic from a digital systems course,
arranged so that a mechanic is always taught before it is required.

| | Level | The idea |
|---|---|---|
| 1 | Route the bit | Sources, bins, wires. A bin that must stay empty |
| 2 | The long way round | Boolean algebra and De Morgan — build a NAND without one |
| 3 | Balance the paths | Propagation delay, and the collision an unbalanced path causes |
| 4 | Four corners | K-map minimisation. Two different minimal covers both pass |
| 5 | Don't care | K-map minimisation with don't-cares: six rows that never happen make the circuit smaller |
| 6 | Nothing but NAND | Functional completeness — XOR out of NANDs alone |
| 7 | The slow lane | Delay arithmetic across a deep circuit |
| 8 | Out of step | Debugging: every gate is right and the timing is not. The board opens on the broken circuit; find the collision and re-time within the budget |
| 9 | Odd one out | Parity and the critical path: three gates in a chain are too slow; as a tree they are not |
| 10 | One of four | A decoder: each output is one row of the table |
| 11 | Pick a lane | A multiplexer, and fan-out to two places at once |
| 12 | Four lanes | A 4:1 multiplexer built from three 2:1 ones, sharing one inverted select between two of them |
| 13 | Highest wins | A priority encoder, with a valid output, and the first answer that doesn't matter on every row |
| 14 | Which is bigger | A 1-bit comparator: less, equal, greater |
| 15 | Half adder | Two outputs from one circuit: sum and carry |
| 16 | Carry the one | A full adder, joining two half adders and their carries |
| 17 | Wrong part | Debugging: a full adder with one wrong gate, found from the rows where its output goes wrong |
| 18 | Pass it on | A 2-bit ripple-carry adder: the carry travels from one column to the next |
| 19 | Carry it further | A 3-bit ripple-carry adder: the carry reaches each column later, and everything meeting it waits |
| 20 | Four at once | A 4-bit ripple-carry adder from four full adders in boxes: the first [block](#blocks) |
| 21 | Look ahead | Carry-lookahead: the same sum two ticks sooner, by working out the carry into the top half without waiting for it |

Then the circuits start to remember. A register keeps one bit and hands it on a
clock later, and because a kept bit has to travel back round to meet the next
one, these levels space their vectors out on a clock.

| | Level | The idea |
|---|---|---|
| 22 | One clock late | The register itself: it starts holding 0, and delays a stream by a clock |
| 23 | Rising edge | This bit against the one before it — and a register hands its bit on early |
| 24 | Flip on one | The first loop, and the clock a loop has to close inside |
| 25 | Hold when told | A register with an enable: load, or keep what you have |
| 26 | Count the ones | Two bits of state, and the carry between them |
| 27 | Miscount | Debugging: a counter that never counts past one, with one wire in the wrong place |
| 28 | Spot the pattern | A machine watching for 1-0-1, overlaps included, in two registers |
| 29 | One clock behind | The same answer reported a clock later: Moore against Mealy |
| 30 | Add as you go | A serial adder, one column per clock, its carry kept in a register |

Deliberately out of scope: assembly, datapaths, memory addressing and number
representation. Static and dynamic hazards are out too, and cannot be expressed —
a hazard needs a continuous signal model, and bits here are discrete tokens with
nowhere for a glitch to live. Unbalanced-path corruption is the lesson that
replaces it.

Clock skew and hold time are out for the same kind of reason: the clock here is
the spacing between vectors, global and exact, so nothing can arrive late
relative to it. What survives is the constraint setup time exists for —
everything must settle within one clock period — and the sequential levels are
built on it.

### Blocks

A block is a small circuit in a box, placed from the parts list and wired like a
gate: its inputs down the left of the box, its outputs down the right, each with
its name beside it. What is inside is not drawn, only the box. Four at once
hands you a full adder as one, `FA`, and Look ahead adds a lookahead block, `LA`.

**A block costs exactly what is inside it.** The wire you draw to a block's input
and the block's own wire from that input are one wire, so going in and out of a
box adds no tick: a block is as fast as the same gates placed by hand, and the
solved card counts the gates inside it. That matters most in Look ahead, which is
about how long the carry takes.

**A block's timing is the timing of what is inside it**, so what the goal says
about it is worth reading. Both of the levels' blocks take their carry a tick
after their other inputs, because inside, the carry meets the other bits one gate
later. A carry that arrives early waits in its port, and the next one runs into it.

In free play you can make your own: **Make block** turns the board on screen into a
block, its sources becoming the inputs and its sinks the outputs, and it joins
free play's parts list. The library holds four, and a board that uses a block
keeps its own copy, so deleting one from the library never breaks a saved board.

### The sandbox

Free play, from the main menu or the last row of the level list. Every part, as many
as you like, no delay budget, and nothing to pass or fail. Registers are there
too, with a clock setting beside the vector count — a state machine needs it,
since a loop cannot keep up with a vector every tick.

The setup is a panel docked down the right-hand side, beside the board rather
than over it, and it collapses to a tab when you want the width back. You set up
the inputs yourself: how many sources, what each one emits, how many sinks, and
how many test vectors they all run for. Click a bit to flip it between 0 and 1,
or fill every combination at once with **Truth table** — up to three sources,
with A as the most significant bit, the way the levels write theirs.

**Under the inputs, each sink shows what it actually caught**, in arrival order,
in the same columns the streams above it use, while the run happens and after it
stops. That is the point of the mode. In a level you already know what you wanted
and the verdict tells you whether you got it; here there is no intended answer,
so what came out is the only result there is. A `·` is a column nothing arrived
in, and `+2` means two more bits arrived than there were columns for.

**Speed** runs the clock at 1x, 2x or 4x, for a circuit of your own that takes a
while to say what it does. Levels always run at the authored rate.

Everything else behaves normally. Bits still collide on unbalanced paths, the
bits-lost meter still fires, and the scorch mark still names the junction — you
just do not get marked on any of it. The board is saved like any other, so a
sandbox circuit and its setup are still there next time.

**You can keep up to eight boards**, each with a name, from the **Board** section
at the top of the setup panel: the arrows step between them, **New** starts an
empty one, **Copy** duplicates the board on screen, **Rename** asks for a name,
and **Delete** asks before it deletes anything. Deleting your last board empties
it instead. Free play is 13 cells by 7, so a column holds seven sources.

Sources and sinks keep their own slots, counted from the top, so adding a third
source never moves the first two and never moves the wires you drew from them.
Both edge columns are kept for them, which is why a gate will not go there. Take
a source away and the wires drawn from it stay where they are, connected to
nothing, and come back when it does. Changing the setup never disturbs your
circuit, the part in your hand or what Ctrl+Z will undo.

### Your progress

Solved levels, the circuits you built, and your best gate count and tick count
save automatically, per level, along with which hints you have seen, whether you
have been through the tutorial, and the sandbox's setup. On Windows they go to a
file:

```text
%USERPROFILE%\AppData\LocalLow\ZADZ\BitSorter\progress.json
```

**To start over:** the workbench's **SETTINGS** → **Reset progress**. It asks first,
and nothing is forgotten unless you answer yes. A reset starts the exercises over:
every solved exercise, its saved answer and its best, the tutorial and the first-time
hints are forgotten -- and the 4.x game's levels with them, which share the exercises'
names. **Your circuits are kept**: MY CIRCUIT, every circuit you named, and free play's
boards and blocks. Sound, music, effects, volume, display and data settings are not
progress, so they stay as they are.

In a browser the same data lives in the browser's own storage for the address
you played at. Progress does not travel between the desktop build and a browser,
and because it is keyed to the address, the two browser links above each keep
their own.

If the file is ever damaged, say by a crash part way through saving, the game
starts fresh rather than refusing to open, and keeps a copy of the damaged file
beside it as `progress.json.unreadable`.

Your progress file itself is never uploaded. Nothing is compared against anyone
else either — every number the game shows is about the circuit in front of you, or
the one you built last time.

### Where 5.0 opens (5.0 preview)

5.0 opens as a simulator rather than a game: on whatever you had open last -- a
circuit of yours, an exercise or a sample -- and with nothing, on a **start
page**: NEW CIRCUIT, OPEN, IMPORT, your recent circuits, the samples, the
exercises, the keyboard map and Settings. **HOME** goes back to it. The
workbench's header has the app's menus -- HOME, FILE, EXERCISES, HELP (the
keyboard map, the start page, the tutorial, and ABOUT BITSORTER: what the simulator
does, with the credits at its foot) and SETTINGS -- and no BACK:
`Esc` closes whatever is on top and never leaves the workbench. The music is off
until you switch it on in Settings.

Every pin nothing is wired to yet has a short lead with an open circle at its end:
point at one and it lights, and a drag from there draws a wire. While you draw one,
the pins it can end on show their circles.

**On the waves**, a click puts a cursor there and a drag moves it through the run,
reading any moment: the values beside the rows are the ones there. `Shift` and a
click moves the playhead, the moment the circuit shows. The time between the two is
written under the rows. The wheel zooms, out
past the end of the run as far as a quarter of the strip.

**INPUTS**, beside WAVES, is what the inputs are given, as a table you type in: a
column per input -- a bus is one column, in binary, hex or decimal -- and a row per
moment something changes. Click columns' names to choose them, then **ALL
COMBINATIONS** fills in every combination of them, **COUNT** counts a bus up, and
**CLOCK** makes one a clock; **+ ROW**, **STEP** and **END** do the rest. `Ctrl`+`Z`
takes any of it back. A design imported without its testbench says which inputs
nothing drives yet. While a testbench of your own drives them, the table shows what
it applies, read only, with **CONVERT** beside it to turn them into rows you can edit.

**LOGIC**, beside TIMING, shows what a circuit without registers does: its truth
table, found by running it through every combination of its inputs (up to ten
bits); for eight inputs or fewer, each output as a minimal sum of products -- the
textbook's way, `a'b + ab'`, and SystemVerilog's -- marked where the search had to
stop short of proving it the smallest; and for four inputs or fewer, each output's
K-map.

**STATES**, beside LOGIC, draws a clocked circuit's state diagram: every state it
can reach from its reset, found by running it with every input from every state,
each a circle with an arrow for each way on, and its next-state table beside it.
It says whether the machine is Moore -- its outputs written under a bar in each
state -- or Mealy, its outputs on the arrows after the inputs, `in/out`. Up to six
register bits, four inputs besides the clock and reset, and sixteen states.

### Exercises (5.0 preview)

The thirty levels of 4.x come back as **exercises**, with five new ones on
tri-state buses, resets, critical paths and pipelines -- thirty-five, in the
course's order. **EXERCISES** lists them chapter by chapter, the ones you have
solved and the next one marked, and the start page carries on from where you
are. An exercise opens with its brief: what to build, and a hint if you ask for
it (**GOAL** brings it back). Its own test drives the inputs and only its parts
are in PARTS, each with how many are left. **CHECK** grades it: each check that failed is a line in the
TRANSCRIPT that takes the run to its moment, and once it is solved a card says
how many gates it took and how fast it settles, beside your best, and offers the
next. A board you left in a 4.x level opens in its exercise.

New to it? **TUTORIAL**, on the start page and in HELP, walks you through the
first exercise one click at a time, and the simulator explains a few things the
first time you meet them -- a gate's delay, a clock, a glitch, z and x.

**WRITE IT** answers an exercise in SystemVerilog instead: your drawing becomes
code, gate for gate, and you carry on typing. It is held to the same parts as a
drawing -- the course's gates, no operators, no more of each than the exercise
gives -- and graded the same way. **DRAW IT** goes back to drawing, and draws your code
as its gates.

The 4.x game is still there until 5.0 replaces it: Settings, **THE 4.x GAME**,
beside the credits. **BACK TO THE SIMULATOR**, first on its title screen, brings you back,
and so do **SIMULATOR** beside MENU on a 4.x board and Escape on the title screen.

### Your own SystemVerilog, in and out (5.0 preview)

The workbench's **FILE** menu keeps your circuits, and takes a project's files in and
gives them back out:

- **Your circuits.** MY CIRCUIT is always there. NEW CIRCUIT makes another, named,
  and OPEN A CIRCUIT lists them all, the one you opened last first; RENAME and DELETE
  work on any but MY CIRCUIT, and DELETE asks first, since a deleted circuit cannot
  be brought back. Each is kept as you leave it, and each exports under its own name.
- **Import.** On a desktop, put a project's files — its `.sv` files, any files they
  `` `include ``, and the memory images `$readmemh` reads — in the Import folder
  (FILE, OPEN THE IMPORT FOLDER), then IMPORT. In a browser, IMPORT picks the files
  or a folder. The project opens as a circuit of its own, named after it, and
  nothing you already have is replaced. A module made only of gates is drawn; any other module is kept as
  the code you wrote. Your testbench runs as written, `$display` and all. Anything
  the import could not read is listed in the TRANSCRIPT with its file and line.
  An import takes up to 200 files, 4 MB a file and 16 MB in all, which is far
  more than a course project needs; a larger file is left out and named, and so
  is a file no module uses, such as a handout or the simulator's own logs.
- **Convert.** While your testbench drives the inputs, they cannot be set on the
  waves. CONVERT, in the FILE menu or on the INPUTS tab, records what the testbench drove and makes it steps you can
  edit; it says first what it will not keep (what the testbench printed or
  checked), and Ctrl+Z undoes it.
- **Export.** EXPORT PROJECT saves a zip of every file the project is, with a
  `run.do`: unzip it, and in ModelSim type `do run.do`.
- **Hand in.** Import a homework file that says `// Put your code here`, and its
  module keeps the file's markers: write your code between them. FILE, EXPORT FOR
  SUBMISSION then saves the file exactly as the course gave it, with your code
  between its markers. It refuses, and says why, if the ports, the markers or
  anything outside them (comments included) are no longer the course's, so nothing
  you wrote is left out without a word.

Nothing you import or export is sent anywhere: it stays on your machine, or in
your browser's tab.

**Writing code.** `+ CODE` in PARTS makes a block you write rather than draw, and a
double-click on one opens its code in the circuit's window. It applies itself when
you pause typing; **Ctrl+Enter** or **Ctrl+S** applies it at once. **Ctrl+Z** and
**Ctrl+Y** undo and redo the typing, **Tab** and **Shift+Tab** indent and outdent
the lines by four spaces, and **Ctrl+/** comments them out or back in. In a browser
**Ctrl+F** finds and replaces, **Ctrl+D** selects the next match and **Alt+Up** or
**Alt+Down** moves the lines. While you type, none of the workbench's own keys act:
Ctrl+V pastes text in the code, and pastes parts only on the circuit. `?` lists
every key. Deleting a block leaves its module, since that is your code; to delete
the module itself, right-click its row under BLOCKS in PARTS. It asks first, and
Ctrl+Z brings it back.

### The course's tools (5.0 preview)

Once a circuit works, the workbench answers the questions the course asks of it:

- **TIMING**, a tab in the dock beside CODE. It lists every path from an input to
  an output, worked out by the textbook rule: each gate adds its Tpdlh where its
  output rises and its Tpdhl where it falls. Paths through the same gates are
  grouped, and the slowest comes first. The critical path is lit on the circuit,
  and clicking another path lights that one. A long list folds away under the arrow
  beside PATHS, and the arrow beside a path too long for its row shows all of it.
  For a circuit with registers, give
  each its clock-to-Q and setup in its inspector, and the tab gives the minimum
  clock period and the frequency that allows. The tab also counts the gates.
- **The measured worst.** For a circuit of up to ten input bits and no registers,
  the TIMING tab steps every input up and down from every value of the others.
  It shows the slowest each output really changed, and the input values that
  caused it. A computed path that no step ever fires is marked "never fires".
  **STEP TEST** turns your stimulus into a step test, one input at a time, so you
  can read each delay off the waves with the cursors. Ctrl+Z undoes it.
- **LIBRARY**, on the TIMING tab, holds a design to a gate library. **COURSE
  CELLS** allows only the course's cells, and PARTS then offers only those. Every
  gate of a cell keeps that cell's delays: type a delay on any one of them and
  the cell, and every gate of it, follows. You can also take a kind of gate out,
  refuse constants, and set a gate budget, which the gate count is held against;
  over it, the TRANSCRIPT warns you too. **Code you write is held to it as well**:
  every gate one of the library's cells at its delays (write `NAND2 g1 (...)` and
  it takes the library's), no logic written with operators (`&`, `|`, `+`, `?:`) --
  not in an assignment, a gate's input or a connection -- and a register only as an
  `always_ff` of plain writes, with at most a reset. Anything else is marked at its
  line, as any mistake in the code is. The testbench your project runs is not held
  to it.
- **Warnings, as ModelSim's `vlog` gives them.** The TRANSCRIPT warns about a
  signal read that nothing drives, one set that nothing reads, two drivers on a
  wire that cannot both let go, and a value cut short by a narrower target. They
  never stop a run.
- **Buses.** Several tri-state gates (BUFIF, NOTIF) may drive one wire; where two
  drive at once it reads x on the waves. Written code may use `inout` ports, and a
  block of it shows an inout pin you can wire onto a bus beside tri-state gates and
  other devices' inout pins.

### What it collects

The game uses Unity Analytics, which sends anonymous usage data to Unity.

Two events come from the game itself, and nothing else does:

| Event | Sent when | Data |
| --- | --- | --- |
| `levelStarted` | The first time a level, or an exercise, is opened in a session | `levelName`, the level's file name, which its exercise keeps |
| `levelSolved` | The first time a level, or an exercise, is solved in a session | `levelName`, the level's file name, which its exercise keeps |

`levelName` is the identifier, like `half-adder` — not a display title and not
anything you typed. The pair exists to answer one question: which level people
stop at.

Each is sent at most once per level per session. Reopening a level, or
solving it again, is not counted a second time.

Alongside those, Unity's SDK collects its own standard session data: a random
installation identifier, session start and end, app version, platform and
operating system, device model, language, and an approximate region derived from
your IP address. That set is Unity's, not mine, and
[Unity documents it](https://docs.unity.com/analytics/en/manual/UnityAnalyticsData).

**What is never sent:** your progress file, the circuits you build, your gate or
tick counts, your personal bests, or any account, name or email. There is no
login, and the game asks for nothing.

**To turn it off:** main menu → **Settings** → **Data** (in 5.0, the workbench's
**SETTINGS**). It toggles between
`DATA ON` and `DATA OFF`, takes effect immediately, and is remembered on that
machine. Turning it off stops collection rather than merely hiding it — the game
tells Unity's consent framework that consent is denied, and nothing is queued or
sent while it is off.

Reporting is on unless you turn it off, so if you would rather it never ran at
all, that is the first thing to change.

---

## Developing

Unity 6.3 LTS (6000.3.11f1).

- **BitSorter → Build Windows Player** writes a player to `Build/Windows/`.
- **BitSorter → Build WebGL Player** writes a browser build to `Build/WebGL/`.
  See [Docs/distribution.md](Docs/distribution.md) for what differs in a browser,
  and what Pages, itch.io and Unity Play each need.
- **BitSorter → Publish WebGL to GitHub Pages** force-pushes `Build/WebGL/` to the
  `gh-pages` branch, via [Tools/publish-pages.ps1](Tools/publish-pages.ps1). The
  script runs standalone too, and warns if the build is older than the scripts.
- **BitSorter → Generate App Icon** redraws `Assets/Icon/BitSorterIcon.png` and
  assigns it to every standalone icon size.
- **BitSorter → Build Play Scene** regenerates the play scene from code. The
  scene is generated rather than authored, so anything added by hand is discarded
  the next time that runs.
- Tests: Window → General → Test Runner, or **BitSorter → Run Tests**. Roughly
  1,280 EditMode cases and 220 PlayMode at present (4.0.0), the PlayMode ones
  across twenty-one fixtures — pointer arbitration, audio and the menu's music,
  scene composition, the tutorial's opening, the frame a run ends on, free play's
  setup, its named boards and its blocks, a block on a level's board, a level that
  opens on a circuit, full-screen panels and the keys that open and close them,
  framing the board clear of the interface, what the board draws while a run
  moves and how it renders, the timing diagram and the wires picked for it, the
  wire-delay hint, the level list, the help panel's K-map, the HUD allocating
  nothing on a quiet frame, the settings, their reset and the credits, and the
  guard that keeps every test off your own save file. Those need a live
  scene, but not a focused window: nothing in the suite waits on wall-clock time
  any more. If you ever script that run, read the results from
  `TestResults.xml` in the save directory rather than from a `TestRunnerApi`
  callback, which does not survive the domain reload that entering play mode
  causes.
- **BitSorter → Capture Reference Shots** screenshots forty-six states of the
  real game — menu, board, a run, both kinds of collision, the cards, free play,
  settings, the credits, the wider boards, the three levels that open on a broken
  circuit, the timing diagram mid-run, a four-bit adder built from blocks, and the
  5.0 workbench in its docked shell: the circuit edited, the waves, the inspector,
  the menus, the transcript, the keyboard map, a block stepped inside, one being made, an array of four, a
  block's written code, the code panel and the FILE menu — into
  the save directory. Two captures of the same code are identical
  to the pixel, so a change that claims to leave the look alone can be held to it.
  It needs the Game view.
- To compile without the editor at all, Bee leaves the exact compiler invocation
  in `Library/Bee/artifacts/*/BitSorter.*.rsp`; redirect `-out` and run it
  through `Editor/Data/DotNetSdkRoslyn/csc.dll`. Two things to watch: gate on
  csc's exit code rather than grepping for `error`, because a missing reference
  reports with no `path(line,col):` prefix, and append any source file added
  since Bee wrote the rsp, or it simply is not compiled.
- **CI** runs everything that needs no Unity on every push: `Tools/ci/` builds
  `Assets/Scripts/LogicCore` and 5.0's new pure layers for netstandard2.1, runs
  the simulator's own tests and the repository's rules, and runs the oracle
  designs under Icarus Verilog, which it installs. Locally, the same run is
  `dotnet test Tools/ci/BitSorter.Ci.slnx`.

### Why it isn't a physics game

The obvious implementation is Rigidbody2D marbles bouncing off ramps. That fails:
floating-point variation means the same circuit can produce different results on
different runs, so a correct solution can fail and levels cannot be validated
automatically.

Instead the game is a deterministic tick-based simulator, and Unity is only a
view. Bits are tokens on a graph, not rigid bodies. The physical look — sparks,
bloom, debris — is animation driven by simulation state, and never feeds back
into it.

### Architecture

```text
Assets/Scripts/LogicCore/   pure C#, no UnityEngine reference
Assets/Scripts/View/        Unity rendering, input and interface
Assets/Resources/Levels/    one JSON file per level
Assets/Tests/EditMode/      NUnit tests, no scene
Assets/Tests/PlayMode/      tests against the real scene, on a scratch save
```

`LogicCore` is its own assembly with `noEngineReferences: true`, so a stray
`using UnityEngine;` fails to compile rather than silently coupling the simulator
to the engine.

State flows LogicCore → View only. The view never mutates the simulation.

The interface is a Canvas built in code, for the same reason the scene is: an
authored hierarchy would be dozens of RectTransforms for the builder to reproduce
and get subtly wrong. Sprites, sound effects and the level music are all
generated at runtime, so there are no art files and no licences for any of them.
The only audio files are the two main-menu tracks, credited under
[Music credits](#music-credits).

### Simulation model

Time is an integer tick. Each `Tick()` runs three phases in a fixed order:
advance bits in transit, deliver arrivals into input ports, evaluate nodes.

- An input port holds at most one bit (`Bit?`, where null is empty).
- A node evaluates only when **all** its input ports are filled, then consumes
  them. That is what makes two-input gates work with serially arriving bits, and
  it makes timing part of the puzzle.
- Every edge has an integer delay of at least 1. Zero-delay edges are rejected at
  construction because they would force node evaluation into topological order.
- **Node evaluation order within a tick cannot affect the result.** This is a
  tested invariant: the same graph built with nodes and edges registered in
  reverse order produces identical output.
- A bit delivered to an occupied port is a collision. Matching values destroy the
  arrival; differing values destroy both and poison the port for the rest of that
  delivery phase, so no ordering leaves a survivor. `CorruptedCount` counts
  destroyed bits, not events, and `CorruptionSites` says which ports they were
  destroyed at.

#### The consequence that shapes the game

Because delay is real and ports latch, a circuit with a correct truth table can
still fail if its paths are unbalanced. A second-stage gate receiving its inputs
on different ticks holds the first, waits, and corrupts when the next arrives.
The failure is corruption, not a wrong answer.

That is the same problem timing closure solves in real hardware, and it is the
intended difficulty of the adder chapters.

There is also no backpressure — a source has no inputs, so it emits every tick
regardless of what is stalled downstream. This is why throughput is not scored:
a balanced circuit sustains exactly one vector per tick whatever its depth, and
an unbalanced one fails outright, so there is nothing in between to rank.

### Not built

- Latches built from gates. Memory cannot emerge from gate feedback under consume
  semantics — a cross-coupled NOR latch deadlocks at startup and stalls after one
  firing — so the register is a primitive (`RegisterNode`) rather than something
  the player builds. A deliberate decision, not an oversight.
- Anything that ranks a player against other people or an authored ideal. Star
  ratings, par scores and leaderboards are out by design.

`CLAUDE.md` carries the full set of decisions and the reasoning behind them.

---

## Contributing

Bug reports, level ideas, new levels and code are all welcome.
[CONTRIBUTING.md](CONTRIBUTING.md) covers opening an issue (there are forms for
bugs, features and level ideas), what each label means, the level file format and
what a new level needs, setting up the project, the rules the code keeps, tests,
and forking the repository and sending a pull request.

---

## Licence

MIT: see [LICENSE](LICENSE). That covers the code, the levels and the docs.

The TextMesh Pro files Unity bundles under `Assets/TextMesh Pro/` keep their own
terms: the Unity Companion License, the SIL Open Font License for Liberation Sans,
and EmojiOne's for its sprites. The details are in `LICENSE`.

The game's font is **IBM Plex Sans**, © IBM Corp., from
[github.com/IBM/plex](https://github.com/IBM/plex), under the
[SIL Open Font License 1.1](Assets/Fonts/IBMPlexSans/LICENSE.txt), which travels
with it in `Assets/Fonts/IBMPlexSans/`. Liberation Sans stays on as the fallback for
any character Plex does not have. Code is set in **IBM Plex Mono**, from the same
family and under the [same licence](Assets/Fonts/IBMPlexMono/LICENSE.txt), in
`Assets/Fonts/IBMPlexMono/`. Both are named in the game's credits, as is Icarus
Verilog, the simulator BitSorter's engine is checked against.

The browser build's code editor is **[CodeMirror](https://codemirror.net)** 6,
© Marijn Haverbeke and others, under the MIT licence: bundled into
`Assets/Plugins/WebGL/codemirror.jspre` by `Tools/codemirror/build.ps1` from the
versions `Tools/codemirror/package-lock.json` pins, and never fetched while the game
runs. The file's head names every package in it, its version and its copyright.

### Music credits

Every level track and every sound effect is generated in code
(`ProceduralAudio`), and all of them are original. The main menu plays two
recorded tracks by other composers, under their own licences rather than the
MIT licence above. Both are also credited in the game, in the credits under
Settings:

- **"Dream"** by **jkjkke**, from OpenGameArt's
  ["MainMenu Music"](https://opengameart.org/content/mainmenu-music). Licensed
  [CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/), no rights
  reserved. `Assets/Audio/Music/jkjkke-dream.mp3`.
- **"Woodland Fantasy"** by **Matthew Pablo**
  ([matthewpablo.com](http://www.matthewpablo.com)), from
  [OpenGameArt](https://opengameart.org/content/woodland-fantasy). Licensed
  [CC BY 3.0](https://creativecommons.org/licenses/by/3.0/). Changed:
  converted to mono and re-encoded at a lower quality for the game builds.
  `Assets/Audio/Music/matthew-pablo-woodland-fantasy.mp3`.
