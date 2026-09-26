# The `LevelLoaded` pipeline

**Status: written up, not built** -- still true at 3.0.2. This is the design note
asked for before committing to the work. Nothing here is implemented, and the
table below was brought up to date on 2026-09-26: a tenth subscriber has joined
since it was written, which is the drift this note warns about.

`LevelSession.LevelLoaded` started as a notification — "a new level is on the
board, redraw yourself" — and has become an initialisation pipeline with ten
stages, an order that matters, and no declaration of what that order is. Two
shipped bugs came out of it. This is what it does today, what it needs, and what
fixing it would look like.

---

## What it is now

Ten subscribers, all subscribing in `OnEnable`:

| # | Component | Handler | What it does on load |
|---|---|---|---|
| 1 | `PlacementController` | `SelectFirstOffered` | Puts the palette selection on the level's **first budget row** |
| 2 | `WiringController` | `OnLevelLoaded` | Cancels any drag in progress |
| 3 | `GatePaletteView` | `Rebuild` | Rebuilds the palette rows from the new budget |
| 4 | `ProgressTracker` | `RestoreBoard` | **Puts the player's saved circuit back on the board** |
| 5 | `FirstTimeHints` | `OnLevelLoaded` | Clears the stall clock, hides the banner |
| 6 | `HelpPanel` | `OnLevelLoaded` | Rebuilds the truth table and hint |
| 7 | `TutorialDirector` | `OnLevelLoaded` | Stops the tutorial if the board is no longer its own |
| 8 | `GameAudio` | `OnLevelLoaded` | Advances the music track if the level actually changed |
| 9 | `GameAnalytics` | `OnLevelLoaded` | Reports `levelStarted` |
| 10 | `ChapterCard` | `OnLevelLoaded` | Decides whether the chapter card is owed on this level |

### The order is real, and it is an accident

C# invokes a multicast delegate in **subscription** order. Subscription happens
in `OnEnable`. `OnEnable` runs in **component order** on the GameObject. So the
table above is dictated by the order `HalfAdderDemoSceneBuilder` happens to call
`AddComponent`, and by nothing else. `GameAnalytics` is last only because it is a
static bootstrap that subscribes after the scene has loaded.

Nothing in the builder says so. Nothing fails if it changes. Reordering two
unrelated `AddComponent` lines is a behavioural change with no diff that looks
like one.

### What has already gone wrong because of it

**Stage 1 before stage 4 made the tutorial's first step free.** `SelectFirstOffered`
sets the selection before `RestoreBoard` has put anything on the board. The
tutorial's opening step is "click the NOT gate to pick one up", expressed as a
predicate over board state — and the selection was already sitting on the only
gate in the budget. The step was complete before the player arrived. The fix was
to stock a decoy gate first, in `TutorialLevel`, which works but fixes it from
the far end: the real cause is that something mutates player-visible state at
stage 1 and nothing declares that it may.

**Stage 4 nearly satisfied all six steps at once.** `RestoreBoard` restores a
saved circuit. A restored tutorial board would have completed every step the
instant the level loaded. The fix was to refuse to save or restore a board for
the tutorial at all, guarded on both sides.

Both were found in play mode. Neither is visible from any single script, and both
read as tutorial bugs when neither was.

### Two more things the order implies, unverified

- **Stage 3 rebuilds the palette before stage 4 puts gates on the board.** The
  palette shows remaining budget, so at the moment it rebuilds, the budget is
  whatever an empty board implies. `GatePaletteView` polls in `Update`, so this is
  probably corrected on the next frame rather than wrong forever — worth
  confirming rather than assuming.
- **Stage 8 reads `_session.LevelName`** to decide whether the level changed.
  That is set before the event fires, so it is correct today; it is correct by
  luck rather than by contract.

### And every subscriber can be called before its own `Start`

`LevelSession` loads the first level in `Start`. Subscribers subscribe in
`OnEnable`, which runs before every `Start`. So the first `LevelLoaded` of a
session can reach a component that has not built its own UI yet. Each of the ten
handles that differently:

- `GameAudio` is safe because `MusicRules.ChangesTrack` returns false when nothing
  was playing under a level yet — safe by rule, deliberately.
- `FirstTimeHints` null-checks the banner.
- `HelpPanel`, `GatePaletteView` and others rely on their own null guards.
- Nothing states that this is a requirement, so the next subscriber added will
  find out the hard way.

---

## What it should be

The event is doing two different jobs and they should be separated.

**Job one is a sequence.** Stages 1–4 build the board: select a part, cancel
stale input, rebuild the palette, restore the saved circuit. These have a correct
order and the game is wrong if they run in a different one.

**Job two is a notification.** Stages 5–9 react to a board that is already
finished: clear a hint clock, redraw a panel, stop a tutorial, change a track,
report a start. These genuinely do not care about each other and never will.

### Sketch: two events, one ordered, one not

```csharp
// LevelSession
public event Action<LevelDefinition> LevelBuilding;   // ordered, few subscribers
public event Action<LevelDefinition> LevelLoaded;     // unordered, many
```

`LevelBuilding` fires first, for the board-construction stages, and its order is
declared rather than inherited from the scene builder — an explicit list rather
than a delegate, so it can be read:

```csharp
private readonly List<ILevelStage> _stages = new List<ILevelStage>
{
    /* cancel input */, /* rebuild palette */, /* restore board */, /* select part */,
};
```

Note the reordering that falls out of writing it down: **select the part last**,
after the board is restored, because "which part is selected" is a decision about
a board that now exists. That alone would have prevented the tutorial bug without
a decoy gate.

`LevelLoaded` then fires once the board is final, and genuinely does not care who
listens or when.

### What that costs

- Five components move from one event to the other. Small, mechanical.
- `PlacementController.SelectFirstOffered` moves to the end of the sequence,
  which changes behaviour on every level, not just the tutorial. Needs a play
  test, and `TutorialLevel`'s decoy gate can probably then be removed — worth
  keeping until it is proven redundant.
- `ILevelStage` is a new interface for four implementers. Justifiable only because
  the list becomes readable; if it stays a delegate, nothing has improved.
- The PlayMode ordering test extends from three pairs to the declared list, and
  becomes an assertion about `_stages` rather than about `GetComponents` order —
  which is the point: the order stops being a property of the scene.

### Cheaper alternative, if the above is too much

Keep one event. Write the required order down in `HalfAdderDemoSceneBuilder` as a
comment block, and extend the PlayMode test to assert all ten subscribers appear
in that order rather than the three pairs it pins today. That does not stop the
order being an accident — it just makes changing it fail loudly. Perhaps a
quarter of the work, and most of the protection.

---

## What this does not touch

The `OnEnable`-before-`Start` hazard is separate and survives either fix. A
subscriber can still be called before it has built itself. The honest answer
there is a sentence in `CLAUDE.md` — *a `LevelLoaded` handler must be safe to
call before its own `Start`* — rather than machinery.
