---
name: unity-test-loop
description: BitSorter's compile → test → capture loop, driven from a Claude session through the unity-mcp server. Use after editing anything under Assets/ (scripts, levels, the scene builder), before committing, or when asked to run the tests, take the reference shots, rebuild the scene, or check a visual change.
---

# The BitSorter test loop

Every change goes through the same loop: compile → check the DLL → Edit Mode → Play Mode → (visual change: capture and diff) → commit. CLAUDE.md is the authority on *why* each step exists; this is the *how*, with the exact calls. Read CLAUDE.md's "Working agreement" first if you have not.

All Unity calls go through `mcp__unity-mcp__Unity_RunCommand` (load it with ToolSearch). The code must be `internal class CommandScript : IRunCommand` with `public void Execute(ExecutionResult result)`. `result.Log` takes `{0}` placeholders without format specifiers. If a call fails with "Could not load file or assembly ... AssistantRunCommand", retry it once: that error is transient.

## 0. One editor, one driver

There is **one** Unity editor for this project, and every Claude session and workflow agent on this machine shares it. A compile or a `.cs` file written under `Assets/` during somebody else's test run kills that run (`NullReferenceException` in `PlayModeRunTask.cs`). Before you touch the editor:

- `ListAgents`: if another session is working on BitSorter, agree with it (`SendMessage`) who drives, and wait for its idle notice.
- **Never write a `.cs` file under `Assets/` while a test run is going**, your own included. Prepare edits in the scratchpad and apply them between runs.
- Subagents in a workflow must not drive the editor in parallel. Give editor work to one agent at a time, or keep it in the main loop.

## 1. Is the editor idle?

```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        result.Log("isPlaying=" + EditorApplication.isPlaying + " isCompiling=" + EditorApplication.isCompiling
            + " isUpdating=" + EditorApplication.isUpdating
            + " scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().path);
    }
}
```

You want `False False False` and `Assets/Scenes/HalfAdderDemo.unity`. If you see `isPlaying=True` and you started nothing, someone else is running something: stop and find out who (step 0). An `InitTestScene...` scene means a crashed run. Leftover `Assets/InitTestScene*.unity` files are git-ignored and harmless.

## 2. Compile, then check the DLL (not the test count)

```csharp
AssetDatabase.Refresh();
global::UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
```

Then wait until each assembly you touched is newer than the files you edited, and grep it for a symbol you just added:

```bash
cd "/c/Users/55556/Unity projects2/BitSorter"
until [ Library/ScriptAssemblies/BitSorter.View.dll -nt Assets/Scripts/View/SomeFile.cs ]; do sleep 3; done
grep -c "SomeNewMethodName" Library/ScriptAssemblies/BitSorter.View.dll
```

The assemblies are `BitSorter.LogicCore.dll`, `BitSorter.View.dll`, `BitSorter.View.Editor.dll`, `BitSorter.LogicCore.Tests.dll` (Edit Mode tests), `BitSorter.PlayMode.Tests.dll` and `BitSorter.TestTools.dll`. Method and type names are ASCII in the DLL. **String literals are UTF-16 and a plain grep will not find them**, so grep for a method name instead. If nothing rebuilds, reimport the `.asmdef` (see CLAUDE.md).

## 3. Run the tests: Edit Mode, then Play Mode

```csharp
if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating) { result.Log("busy"); return; }
result.Log("menu: " + EditorApplication.ExecuteMenuItem("BitSorter/Run Tests/Edit Mode"));   // or "Play Mode"
```

Then wait on the results file rather than on the clock:

```bash
f="/c/Users/55556/AppData/LocalLow/ZADZ/BitSorter/BitSorterTestRun.txt"; sleep 3
until ! grep -q RUNNING "$f"; do sleep 3; done; cat "$f"
```

(Give the Bash call a `timeout` of 400000–500000 ms. Play Mode takes about 2.5–3 minutes.)

- The file says `PASS`/`FAIL`, what ran, the tally, and every failure with its message. `REFUSED` means the editor was busy or the scene was dirty, and it says which.
- `ran=(started elsewhere)` means you did not start that run. Someone else did.
- The explicit `ReferenceShots` fixture shows as 21 skipped in a plain Play Mode run. That is expected.
- **Only ever the menu's PlayMode run.** The Test Runner's *Player* tab builds a player, cannot read project files, and proves nothing (see CLAUDE.md).
- Tests run under `SaveGuard`. **Never drive the real game against the real save** with ad-hoc RunCommand code, and never launch the Windows build for a smoke test. If you need a probe, write an `[Explicit]` Play Mode fixture that uses `SaveGuard`, run it by name, and delete it before committing.

Pure-LogicCore tests also run without Unity, the way CI runs them:

```bash
dotnet test "Tools/ci/LogicCore.Tests/LogicCore.Tests.csproj"
```

A new LogicCore test file has to be added to that `.csproj`'s list.

## 4. A visual change: capture and diff

Keep the previous set before you capture, because each capture overwrites `Captures/current`:

```bash
cd "/c/Users/55556/AppData/LocalLow/ZADZ/BitSorter/Captures" && rm -rf before-x && cp -r current before-x
```

```csharp
result.Log("menu: " + EditorApplication.ExecuteMenuItem("BitSorter/Capture Reference Shots"));
```

Wait on the results file as in step 3 (about 40 s, `ran=PlayMode BitSorter.PlayMode.Tests.ReferenceShots`), then compare:

```bash
python ".claude/skills/unity-test-loop/scripts/shotdiff.py" before-x current
```

It takes folder names under `Captures/`, not paths. There is no tolerance: two captures of the same code are identical to the pixel, so a change you did not intend is a real one. **Look at the shots you changed** with Read, cropping and enlarging with PIL where detail matters. A passing diff proves nothing about whether the thing looks right.

For Classic's shots, set the session key first and clear it after:

```csharp
SessionState.SetString("BitSorter.Capture.Look", "classic");   // ... capture ...
SessionState.EraseString("BitSorter.Capture.Look");
```

## 5. A scene change: only through the builder

```csharp
result.Log("menu: " + EditorApplication.ExecuteMenuItem("BitSorter/Build Play Scene"));
```

Then verify the saved file itself. Check that the new reference points somewhere, and that no serialized field is newly empty:

```bash
grep -n "_newField" Assets/Scenes/HalfAdderDemo.unity
echo "empty refs now: $(grep -cE '^\s+_[A-Za-z]+: \{fileID: 0\}' Assets/Scenes/HalfAdderDemo.unity) at HEAD: $(git show HEAD:Assets/Scenes/HalfAdderDemo.unity | grep -cE '^\s+_[A-Za-z]+: \{fileID: 0\}')"
```

The builder also re-saves `Assets/Settings/DemoBloomProfile.asset` in a different object order. Check that the values are unchanged, and commit it separately as a chore.

## 6. Commit

- Commit after each green run, with a short message ending in the `Co-Authored-By` line.
- A bug fix is two commits: a red one that compiles and fails on an assertion, then the fix.
- Unrelated changes go in separate commits. When one file holds two changes (usually CLAUDE.md), stage just the right hunks:

  ```bash
  python ".claude/skills/unity-test-loop/scripts/stage_hunks.py" CLAUDE.md "a phrase only in the hunks to leave out"
  ```

- Stage by explicit path, never `git add -A`. Another session's probe may be sitting untracked in `Assets/`.
- Correct any docs a change makes stale (CLAUDE.md, README, `Docs/`) in that same change.
