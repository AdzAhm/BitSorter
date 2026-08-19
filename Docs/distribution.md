# Distribution

How to publish BitSorter, and what each place needs from you.

Two builds, both from menu items:

| Menu item | Output |
|---|---|
| **BitSorter → Build Windows Player** | `Build/Windows/` |
| **BitSorter → Build WebGL Player** | `Build/WebGL/` |

---

## What breaks in a browser

Found by reading Unity's WebGL runtime and inspecting the generated build, not by
guessing. Each of these is either fixed or listed as a known limitation.

### Saved progress — was broken, now fixed

**This is the one that would have cost players their progress silently.**

`ProgressStore` writes `progress.json` to `Application.persistentDataPath`. In a
browser that path is `/idbfs/<hash>/`, an Emscripten in-memory filesystem that is
only copied into IndexedDB when something calls `syncfs`.

Unity's runtime mounts it like this (`BuildTools/prejs/IdbFs.js`):

```js
Module.__unityIdbfsMount = FS.mount(IDBFS,
    { autoPersist: !!Module['autoSyncPersistentDataPath'] }, '/idbfs');
```

So automatic persistence is conditional on a flag the **stock template leaves
commented out**. Unity's own comment in the generated `index.html` says so:

> This autosyncing is currently not the default behavior to avoid regressing
> existing user projects…

With the flag off, every write succeeds, nothing errors, and the whole lot is
discarded when the tab closes. Solved levels, saved boards, personal bests.

**Fix:** `Assets/WebGLTemplates/BitSorter/` is a copy of Unity's Default template
with one line uncommented:

```js
config.autoSyncPersistentDataPath = true;
```

`WebGLBuild` selects it with `PlayerSettings.WebGL.template = "PROJECT:BitSorter"`.
That template exists for that one line — if it is ever regenerated from Unity's
stock copy, this must be re-applied.

**Still worth testing by hand**, because it depends on the browser: solve level 1,
reload the page, and check the tick is still in the level list. Private/incognito
windows and "block third-party cookies" settings can disable IndexedDB entirely,
in which case the console logs *"IndexedDB is not available. Data will not
persist"* and the game runs fine but forgets. That is a browser policy, not
something the build can fix.

### Audio — behaves differently, not broken

Browsers refuse to start audio until the user interacts with the page. The music
starts in `GameAudio.Start()`, which runs before any click, so the audio context
begins suspended and resumes on the first interaction.

In practice this is harmless here: the game opens on the main menu, and the first
thing anyone does is click it. The effect is that music begins at the first click
rather than at load.

### Music generation stalls the first frame

`ProceduralAudio` builds every clip sample by sample at startup. Measured in the
editor:

| Cue | Time | Length |
|---|---|---|
| Tick | 0.2 ms | 0.04 s |
| Gate | 0.2 ms | 0.05 s |
| Land | 0.7 ms | 0.16 s |
| Collide | 1.1 ms | 0.22 s |
| Win | 7.7 ms | 0.85 s |
| **Music** | **532 ms** | **32 s** |

532 ms of that is the music: 1.4 million samples, each summing several sine
voices. It runs synchronously in `Start()`, so it is a hard stall on the first
frame, and WebAssembly is slower than the editor's Mono at this kind of tight
float loop — expect it to be worse in a browser.

**Not fixed, deliberately.** It happens once, behind the main menu, on a screen
where nothing is moving yet. If it turns out to be visible, the fix is to
generate the music lazily on the first unmuted frame, or to build it in slices
across several frames.

### Quit — fixed

`Application.Quit()` does nothing in a browser; a tab is closed by the browser,
not by the page. The Quit item is compiled out of WebGL builds rather than left
sitting in the menu doing nothing.

### Not affected

- **LogicCore** is pure deterministic C# with no threading, no IO and no engine
  types. It behaves identically.
- **Keyboard and mouse** work through the Input System as they do on desktop.
- **Procedural sprites and audio** are generated with plain maths and
  `Texture2D` / `AudioClip.Create`, all supported.
- **Mute setting** uses `PlayerPrefs`, which Unity backs with the same IndexedDB
  store — and which the persistence fix above also covers.

---

## Compression, and why these settings

Set in `WebGLBuild.cs`, not in the dialog, so a build cannot inherit whatever was
last clicked:

| Setting | Value | Why |
|---|---|---|
| Compression format | **Brotli** | Smallest download by a wide margin |
| Decompression fallback | **On** | See below |
| Data caching | **On** | Returning players do not re-download the data file |
| Exception support | Explicitly thrown only | Full support costs size and speed; nothing here catches engine faults |

**Decompression fallback is the setting that matters.** A Brotli-compressed Unity
build only loads if the server answers with `Content-Encoding: br`. A host that
serves the `.br` files as plain bytes makes the loader fail, and the failure is
not graceful — a console error and a blank page.

The fallback ships a decompressor inside the loader so the build works whether or
not the host cooperates. It costs a slightly larger loader and a little CPU at
startup. One build that runs everywhere beats two builds that each run in one
place, so it is on.

If you ever host somewhere you control the headers, turning the fallback off and
serving `.br` with the right `Content-Encoding` gives a faster start.

---

## Analytics

The game reports to Unity Analytics. **The README's "What it collects" section is
the canonical list of what is sent** — this section covers only what publishing it
requires.

`GameAnalytics` initialises Unity Services and calls `StartDataCollection()`, then
reports `levelStarted` and `levelSolved`. It hangs off
`[RuntimeInitializeOnLoadMethod]` rather than a scene component, because
`HalfAdderDemoSceneBuilder` owns scene contents and a component would mean
regenerating and re-verifying the scene for something that needs no inspector
fields. The game ships one scene, so the hook fires exactly once.

Three things to know before trusting the numbers:

- **Custom events must be registered in the Unity Cloud dashboard before they are
  accepted.** Until `levelStarted` and `levelSolved` exist there, with a string
  parameter named `levelName`, the service rejects them and the only sign is a
  warning in the player log. The events being in the build is not enough.
- **The project must stay linked.** Collection depends on the `cloudProjectId` in
  `ProjectSettings.asset`. Unlinking the project silently stops reporting.
- **There is no opt-out.** `StartDataCollection()` is called unconditionally at
  startup, which treats launching the game as consent. That is worth revisiting
  before showing this to anyone in a jurisdiction that disagrees, and it is why the
  README says so plainly rather than burying it.

Reporting failures never interrupt play. A missing project, a blocked request or a
rejected event warns and is otherwise ignored, because nothing about analytics is
worth costing a player their run.

Storefronts ask about data collection. itch.io and Unity Play both have a field for
it, and the answer is now yes rather than no.

---

## itch.io

**Upload:** zip the *contents* of `Build/WebGL/` — `index.html` must be at the top
level of the zip, not inside a folder.

```
bitsorter-web.zip
├── index.html
├── Build/
└── TemplateData/
```

Then on the project page:

1. **Kind of project** → *HTML*.
2. Upload the zip, tick **"This file will be played in the browser"**.
3. **Viewport dimensions** → `960 × 600` or larger. The game targets 16:9 and
   `CameraFit` copes with other shapes, so anything in that region is fine.
4. **Mobile friendly** → leave **off**. There is no touch input, by decision.
5. **Fullscreen button** → enable. The board benefits from the space.

Two things to know:

- itch serves static files without custom headers, which is exactly the case the
  decompression fallback exists for. With it on, the build works there.
- itch pages are `https`. Nothing here makes network calls, so there is nothing
  to break.

**What only you can provide:** a cover image (630 × 500), screenshots, the
description, a price or "no payments", and whether the page is public or
restricted. The GIF in the README works as a screenshot.

## Unity Play

**Upload:** the same zip. Unity Play is built for Unity WebGL specifically and
sets compression headers correctly, so it works with or without the fallback.

1. Create a project at play.unity.com and upload the WebGL zip.
2. Set the title, description and a thumbnail.
3. Choose public or unlisted.

**What only you can provide:** a Unity account, the description and thumbnail,
and agreement to their content terms.

Unity Play enforces an upload size limit that has changed over time — check the
current figure when you upload. For reference this build is around 14 MB of
compressed payload, which is small by their standards.

## GitHub Pages

Live at <https://adzahm.github.io/BitSorter/>, served from the `gh-pages` branch.

**Publish with BitSorter → Publish WebGL to GitHub Pages**, or run
`Tools/publish-pages.ps1` directly. Build first; the script warns if anything under
`Assets/` is newer than the build it is about to push, but it will not stop you.

One-time setup, needed once before the URL resolves: **Settings → Pages → Source:
Deploy from a branch → `gh-pages` / `(root)`**. Pushing the branch does not enable
Pages by itself.

**The repository has to be public first.** On the free plan GitHub Pages serves
public repositories only; in a private one the Pages settings offer to make it
public or to upgrade, and until then the branch sits there unserved. The same
applies to release downloads, which are only reachable by accounts with access to
the repository. Of the three links in the README, only Unity Play works from a
private repo, because Unity hosts that build rather than GitHub.

### The fallback is what makes this work

Pages cannot send `Content-Encoding: br`, and there is no way to make it. It offers
no custom-header configuration at all — `_headers` files are a Netlify and
Cloudflare Pages feature, not GitHub's.

That would normally be fatal to a Brotli build. It isn't here, because the
extension tells you which mode the build is in:

| Decompression fallback | Files named | Who decompresses |
|---|---|---|
| **On** (ours) | `.unityweb` | The loader, in JavaScript |
| Off | `.br` | The server, via `Content-Encoding` |

Ours are `.unityweb`. Pages serves them as `application/octet-stream` with no
`Content-Encoding` header — it has no way to know the bytes are Brotli — and the
loader recognises the stream and inflates it itself. Confirmed on the real files:
they start `6b 8d 00 55`, so Brotli, since gzip would start `1f 8b`.

**So the fallback must stay on for Pages.** Turn it off and the files become `.br`,
Pages still won't send the header, and the loader aborts to a blank page with a
console error. Unlike itch, there is no host setting that can rescue it.

The subpath works without editing the generated HTML, which is the other thing that
usually breaks a project Pages site. Unity emits `var buildUrl = "Build"` and
`href="TemplateData/…"`, all relative with no leading slash, so being served from
`/BitSorter/` rather than a domain root is fine.

### Repo layout

The site is the branch root — `index.html` at top level, `Build/` and
`TemplateData/` beside it, exactly as Unity emits them, plus `.nojekyll`.

`.nojekyll` is not strictly required for this file set, since nothing here starts
with an underscore for Jekyll to skip. It disables the Jekyll step entirely, which
removes a class of surprise and a little deploy time.

**Do not use the `/docs` folder option instead.** Pages wants a folder named
exactly `docs`, and this repo already has `Docs/`. Git would treat those as two
paths; Windows cannot represent both at once, so the result is a repo that will not
check out cleanly on the machine it was authored on.

### Why it is force-pushed every time

`gh-pages` is an orphan branch, rebuilt from nothing on each publish and
force-pushed. A 14 MB WebGL build barely deltas against the previous one, so
appending commits would add most of a build to the repository on every republish.
Replacing the branch keeps it at one commit forever.

The script assembles it with `git init` in a scratch repo under `%TEMP%`, not with
`git clone`. A fresh repository's first commit has no parent, which is already what
an orphan branch is — so there is no working tree to empty, `main`'s `/[Bb]uild/`
ignore rule is not present to fight, and nothing runs `git rm` inside the project
folder while Unity is watching it.

It also sets `core.autocrlf false` in that scratch repo. This machine has
`core.autocrlf=true` globally, which would rewrite line endings in `index.html` and
`style.css` on commit. It happens to be a no-op because Unity emits LF, but a
publish should push the bytes that were built rather than rely on that. The four
compressed payloads were never at risk — git detects them as binary and leaves them
alone, verified by hashing them against the branch after the first deploy.

The commit message records which source the build came from, `+dirty` if the working
tree had uncommitted changes at the time — worth knowing, since a WebGL build can
easily contain code that is not in any commit.

---

## Desktop

`Build/Windows/` is ready to zip and send as it stands. The build script deletes
the `_DoNotShip` folder Unity leaves behind, so the folder can go out as it is.

- **Window title** is `productName`, which is `BitSorter`.
- **Icon** is generated by **BitSorter → Generate App Icon**, which draws
  `Assets/Icon/BitSorterIcon.png` and assigns it to every standalone size.
- **Company / product** are `ZADZ` / `BitSorter`. These set the save location, so
  changing either one moves where progress lives:
  `%USERPROFILE%\AppData\LocalLow\ZADZ\BitSorter\progress.json`.

  **On desktop, renaming abandons existing saves rather than migrating them.** A
  renamed build looks in a directory that does not exist yet and starts the player
  from level 1, with their old file still on disk under the previous name. Nothing
  errors and nothing warns. `ProgressStore` takes a path rather than deriving one,
  so a migration that reads the old location when the new one is missing would be a
  small change, if it ever matters.

  **In a browser it does not, because the two are keyed differently.** Unity
  documents the browser path as `/idbfs/<hash>`, where the hash is an MD5 of the URL
  of the directory containing the page — not of the company or product name. The
  build bears this out: the IDBFS mount is a fixed `FS.mount(IDBFS, …, "/idbfs")`,
  and the company name never reaches it. So a rename leaves browser progress intact,
  and what actually separates browser saves is the address they were made at:
  Unity Play and Pages are different URLs and have always had independent progress,
  and moving a build to a new URL strands the saves at the old one.

  What a rename *does* cost in a browser is the asset cache. The loader names it
  `caches.open(<name>_<companyName>_<productName>)`, so a renamed build opens a
  fresh one and returning players re-download the payload once. A slower first load
  after a rename, not lost progress.

The build is **unsigned**, so Windows SmartScreen warns that the publisher is not
recognised. That is expected for an unsigned binary and is mentioned in the
README, but tell testers directly or they will assume it is broken.

---

## GitHub Releases

The Windows download in the README points at
<https://github.com/AdzAhm/BitSorter/releases/latest>, which resolves to whichever
release is marked latest. The link does not change when you publish a new one, so
the README does not need editing per release — but it only resolves at all once a
release exists and is marked latest.

### The zip

Zip `Build/Windows/` **inside a containing folder**, so unzipping produces one
`BitSorter/` folder rather than 185 loose files in someone's Downloads:

```text
BitSorter-1.0.0-Windows.zip
└── BitSorter/
    ├── BitSorter.exe
    ├── BitSorter_Data/
    ├── MonoBleedingEdge/
    ├── D3D12/
    ├── UnityCrashHandler64.exe
    └── UnityPlayer.dll
```

**This is the opposite of what itch.io wants**, above, where `index.html` has to be
at the archive root with no containing folder. Same project, two archives, opposite
rule. Around 37 MB compressed, from 100 MB on disk.

`Build/` is gitignored, so the zip can sit next to the build without any risk of
being committed.

Check for a `_DoNotShip` folder before zipping. `WindowsBuild` deletes it, so it
should not be there — but it carries IL2CPP symbols, and shipping it is the kind of
mistake nobody notices until the download is twice the size it should be.

### Cutting one

A release is three things bolted together: **a tag** naming one commit forever,
**notes**, and **attached files**. The tag is the part that gets skipped.

```powershell
git tag -a v1.0.0 -m "First release: nine levels, Windows and browser"
git push origin v1.0.0
```

`-a` makes it annotated, so it stores an author and a date as a real object; a bare
`git tag v1.0.0` is a bookmark with neither. Note that `git push` on its own does
**not** push tags — the second line is required.

Then **Releases → Draft a new release** on GitHub, choose the existing tag from the
dropdown, paste the notes, drag the zip onto the attachment area, and publish.

Two things to get right:

- **Wait for the attachment to finish uploading.** The publish button stays live
  while it is still going, and a release with no binary is a 404 for everyone who
  followed the README.
- **Leave "Set as the latest release" ticked.** That is what makes the README link
  resolve.

Avoid typing a new tag name into the release form. GitHub will create it on publish,
but only on the remote, so your local repo does not have it until the next
`git fetch --tags` — which is how local and remote tags quietly diverge.

`gh release create` does the same job in one command if the GitHub CLI is ever
installed. It is not, as of this writing.

**What only you can provide:** the version number, the notes, and the decision that
a given commit is worth releasing.
