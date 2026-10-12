---
name: privacy-scan
description: Check that no student ID number and no copied course material is in BitSorter's public repository, its working tree or its git history. Use before every commit that touches Docs, the oracle corpus, tests, levels or anything written with the course in mind, and always before a push.
---

# The privacy scan

BitSorter 5.0 is built against a university course whose files hold student ID numbers (in handouts,
testbench operands, folder and archive names) and whose code belongs to the university. The repository
is public and git keeps everything, so the check runs **before** a commit. `Tools/ci/Repo.Tests/PrivacyScan.cs`
holds the rules and the reasons; this is how to run it.

## 1. The generic rule — every time

No hand-written file may hold a standalone nine-digit number, nor an eight-digit one that passes the
ID check digit. CI runs it too, but run it before committing rather than finding out from CI:

```bash
cd "/c/Users/55556/Unity projects2/BitSorter"
dotnet test Tools/ci/Repo.Tests/Repo.Tests.csproj -c Release --filter "FullyQualifiedName~PrivacyScanTests"
```

It reads tracked files **and** new files not yet added, so a file about to be committed is covered.

## 2. The private rule — before a push, and after writing anything course-shaped

Point `BITSORTER_PRIVATE_DIR` at the course folder. **Its path is in Claude's memory
(`course-files-private`), never in the repository** — this file is public. The test reads the real
numbers out of the folder as it runs, keeps them in memory, and looks for each one in every file in the
working tree and in the whole of git history; it also looks for runs of six or more meaningful lines
copied from the course's code.

```bash
cd "/c/Users/55556/Unity projects2/BitSorter"
BITSORTER_PRIVATE_DIR='<the course folder>' dotnet test Tools/ci/Repo.Tests/Repo.Tests.csproj -c Release \
  --filter "Name=ThePrivateCourseFolder_HasLeftNoTrace" --logger "console;verbosity=detailed"
```

Read the line `checked N private numbers against M files and all of history`. **N must be more than
0**, or nothing was checked: a wrong path fails the test, but a right path to the wrong folder would
pass with N = 0. On 2026-10-12 it was 2.

## 3. When it finds something

- **A file in the working tree:** take the number or the block out. Do not commit it. Rewrite a copied
  block from scratch, in your own words.
- **A commit:** it is already in history. **Stop, and tell Ahmad before any push.** Do not try to fix
  history on your own; rewriting pushed history needs his decision.
- **A harmless number the generic rule catches** (a seed, a constant): add it to
  `Tools/ci/Repo.Tests/privacy-allowlist.txt` with a reason, and only after checking where it came
  from. The private rule has no allowlist and never will.

## 4. Never

- Never print, paste, summarise or quote a private number or a course line — not in chat, a commit, a
  PR, a doc or memory. The test reports places and counts only; keep it that way.
- Never copy a course file into the repository, not even into a scratch folder inside it.

## What it does not see

- The handouts are PDFs and are not read; only their file names are. The generic rule still catches any
  nine-digit number that reaches the repository from one.
- In history, a number written with underscores between its digits (`123_456_...`) is not found by
  git's pickaxe. The working-tree check normalises underscores and does find it.
