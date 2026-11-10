# Distribution

How to publish BitSorter, and what each place needs from you.

## Where each version lives

The current version is **5.0.0**, the SystemVerilog simulator. Its one source is
`bundleVersion` in `ProjectSettings/ProjectSettings.asset`: the start page's version line,
the credits and the browser template's `productVersion` all read it, and a test holds the
README and this file to it.

| Version | Where |
|---|---|
| 5.0.0, in a browser | GitHub Pages, <https://adzahm.github.io/BitSorter/> |
| 5.0.0, on Windows | A zip attached to its GitHub release |
| 4.0.0, the puzzle game | Unity Play, as it was released. It is not republished; its source is the `v4.0.0` tag |

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
discarded when the tab closes. Solved Exercises, saved circuits, personal bests.

**Fix:** `Assets/WebGLTemplates/BitSorter/` began as a copy of Unity's Default
template with one line uncommented:

```js
config.autoSyncPersistentDataPath = true;
```

`WebGLBuild` selects it with `PlayerSettings.WebGL.template = "PROJECT:BitSorter"`.
The template was made for that line, and does four more things the stock one
does not: it fits the canvas to the window as the window changes, it caps the
device pixel ratio at 1.5, it loads under BitSorter's own loading screen
instead of Unity's logo and loading bar, in the default look's colours
(`Docs/architecture-5.md`, "As built in Part 16: the loading screen"), and it says
what BitSorter is wherever a link to it is shared: a description, Open Graph and
Twitter tags, and `TemplateData/preview.png`, 1200 by 630, the workbench on the
reference circuit (`WebTemplateTests.ALinkToThePage_SaysWhatItIs_AndShowsIt`). If it
is ever regenerated from Unity's stock copy, all five must be re-applied.

**Still worth testing by hand**, because it depends on the browser: solve the first
Exercise, reload the page, and check EXERCISES still marks it solved. Private/incognito
windows and "block third-party cookies" settings can disable IndexedDB entirely,
in which case the console logs *"IndexedDB is not available. Data will not
persist"* and BitSorter runs fine but forgets. That is a browser policy, not
something the build can fix.

### Two tabs — handled

Each tab keeps that in-memory filesystem of its own, read from IndexedDB once as
the page loads and written back whole whenever a file in it changes, so a tab that
read the save before another tab wrote it would put its older save back over the
newer one -- and not only when it saved, but whenever it wrote anything, a
setting included. Every save carries a stamp, and `BitSorterSave.jslib` tells the
other tabs each one, with the save's path, through localStorage once IndexedDB
has it: a tab that hears of one takes IndexedDB's files into memory, once its own
last write has gone out, and a tab of the same save that hears a stamp not its
own writes no save until the page is loaded again, which the chip offers (FILE,
RELOAD THE SAVE). Two tabs saving within the same moment can still lose the later
of the two changes; nothing short of a lock across tabs closes that.

**Two builds on one origin** -- the release and a candidate beside it on Pages --
share more than it looks: Unity keeps every build's files in one IndexedDB mount,
`/idbfs`, a folder each, and every tab writes the whole mount back. So every tab
takes in every build's saves, or it would put an older copy of the other build's
files back over the newer; only a save of its own build's file holds it.

**Worth testing by hand**: open the build in two tabs, solve an Exercise in one,
edit MY CIRCUIT in the other -- the chip says another window saved and asks to
reload -- reload, and check the solve is there.

### Audio — gone

Browsers refuse to start audio until the page has been interacted with, and
generating the 4.x game's music sample by sample stalled the first frame
(fixed then by building each track in slices). Neither applies any more:
BitSorter has had no sound since Part 16b, and the player ships with audio
disabled.

### Quit — gone

`Application.Quit()` does nothing in a browser; a tab is closed by the browser,
not by the page. The 4.x game's main menu had a Quit item, compiled out of WebGL
builds; it went with that menu in Part 16a, and 5.0 has no Quit item and no
`Application.Quit` call anywhere. The Windows build is closed by its window's own
close button -- Escape closes what is on top and never the workbench.

The circuit open is kept half a second after its last edit, and at once as
BitSorter loses the focus (`WorkbenchPanel.OnApplicationFocus`). The Windows
build keeps it as it quits too (`OnApplicationQuit`), with code typed inside the
code editor's pause applied first. A browser runs nothing as its tab closes:
`OnApplicationQuit` does not run there, and the page sets no `beforeunload`,
`pagehide` or `visibilitychange` hook. So a tab closed within half a second of
the last edit, or inside the pause after typing, can lose that edit.

### Not affected

- **The four pure layers** (the language, the engine, the document and the view-model)
  are deterministic C# with no threading, no file access and no engine types. They
  behave identically.
- **Keyboard and mouse** work through the Input System as they do on desktop.
- **Procedural sprites** are generated with plain maths and `Texture2D`, all
  supported.
- **The display and data settings** use `PlayerPrefs`, which Unity backs with the
  same IndexedDB store — and which the persistence fix above also covers.

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
| Code optimization | **Disk size, with LTO** | The wasm is half the download. Lives in `Library/`, per machine, which is why the script sets it |
| IL2CPP code generation | Optimize size | Smaller wasm, at a small cost in speed BitSorter does not need |
| Managed stripping | Medium | Removes code nothing reaches; at Minimal every package went in whole. It was Low, the conservative level, until 2026-10-15: no runtime script uses reflection, and the browser build was smoke-tested at Medium before it was kept, since the risks are in packages (Unity Services, the Input System, `JsonUtility`) |
| WebAssembly 2023 | **On** | Native exceptions, bulk memory, BigInt and SIMD where the original feature set used JavaScript trampolines. Needs Chrome 91, Firefox 89 or Safari 16.4 |
| Strip engine code | **On** | Engine modules no code calls stay out of the wasm |
| Debug symbols | Off | Symbols are for a development build's stack traces, and would ride in the wasm or beside it |
| Show diagnostics | Off | A development overlay; off, the page carries none of it |
| The built-in pipeline's seven shaders (graphics settings) | **No support** | Deferred shading and reflections, screen-space shadows, depth-normals, motion vectors, halo and flare: URP draws with none of them, and at "built-in" they were about 25 KB of the download. Also in `GraphicsSettings.asset`, for every build |
| Always included shaders (graphics settings) | Without Legacy Diffuse and UI/DefaultETC1 | Nothing draws with either -- Diffuse is the built-in pipeline's, and ETC1's split alpha is Android's -- and they were about 12 KB. UI/Default, Sprites/Default and the three cubemap shaders stay. Also in `GraphicsSettings.asset` |

URP's post-processing is left out of the build whole: nothing BitSorter draws is
post-processed, so the 2D renderer carries no post-processing data, and the sixteen shaders
and the textures that data names do not go in (Part 16's URP pass, `Docs/architecture-5.md`).
Before that the renderer pointed at a copy of URP's data, `Assets/Settings/PostProcessData.asset`,
with its film-grain list emptied: no look uses film grain, and noise barely compresses, so the
package's own copy put about 2.5 MB of the download in for nothing. The copy is kept for that
reason, though nothing points at it now: post-processing turned on again points at it, not at
the package's.

**What the browser build weighs**, measured from the release build's `Build/` folder, Brotli-compressed as
served: the download is the four files under `Build/` -- the wasm, the data, the framework and the loader, which
is not compressed. 4.0.0's was 16,931,273 bytes; Part 16 records each cut it makes here against these.

| Build | wasm | data | Download |
|---|---|---|---|
| 5.0 before Part 16a (v5 `b278b2b`, 2026-11-02) | 7,990,347 | 7,177,312 | 15,462,551 |
| After Part 16a deleted the 4.x game (2026-11-02) | 7,688,213 | 7,063,474 | 15,046,579 |
| Before Part 16b's optimisation skills (2026-11-03) | not recorded | not recorded | 11,380,796 |
| After them, before the `optimize-web` pass (v5 `332b89a`, 2026-11-03) | 7,506,410 | 3,273,372 | 11,070,792 |
| After the `optimize-web` pass (2026-11-03) | 7,501,966 | 3,255,925 | 11,048,901 |
| The 5.0.0 release candidate: the 16d sweep's fixes and the folding panels (v5 `72d5c43f`, 2026-11-09) | 7,548,635 | 3,257,449 | 11,098,056 |
| **5.0.0 as released** (`v5.0.0`, `824a969d`, 2026-11-10) | 7,544,318 | 3,306,420 | 11,142,710 |

Deleting the game took 302,134 bytes off the wasm and 113,838 off the data, 2.7% of the download. Part 16b then
took the sound out, and the two menu recordings with it, about 3.6 MB of the data; its skills took a further
310,004 bytes, and the `optimize-web` pass 21,891 more -- 17,447 off the data, where the shaders went, and 4,444 off
the wasm: less than the 37 KB its pieces measured alone, since Brotli had shared much of their bytes with what stays.
The framework is 172,878 bytes of that build and the loader 118,132. From 4.0.0's 16,931,273, the download was
5,882,372 bytes smaller, 34.7%.

The release candidate is 49,155 bytes over that -- 46,669 on the wasm, the code of the sweep's 183 fixes and the
folding panels, 1,524 on the data and 962 on the framework, the loader unchanged at 118,132 -- and 5,833,217 bytes
under 4.0.0, 34.5%.

5.0.0 as released is 11,142,710 bytes, 5,788,563 under 4.0.0, 34.2%. Its data file is 48,971 bytes over the candidate's
with nothing added: inside it the assets are byte for byte the same and the IL2CPP metadata 16 bytes longer, but IL2CPP
writes the metadata in another order on every build -- 5.6 MB of its 9 MB differ in place -- and the order decides how
well Brotli compresses the data file, so the same code measures some 50 KB apart from one build to the next. Compare
builds by what is inside the data file, not by its size alone.

**Build WebGL from an editor on WebGL.** `WebGLBuild` switches the editor's platform to WebGL first, since 2026-11-10: a
WebGL build made while the editor was on Windows -- as `WindowsBuild` leaves it -- failed to compile, `WebGLInput` not
being found.

**Where the last of those bytes are** (`v5 332b89a`, measured by unpacking the build: the data file is a list of
files, its `data.unity3d` a Unity asset bundle, and each piece was Brotli-compressed on its own at quality 11, so
the parts add up to within a few kilobytes of the file):

| Piece | Raw | Brotli |
|---|---|---|
| wasm: the engine and every assembly's code, compiled | 28,304,609 | 7,506,410 |
| data: `global-metadata.dat`, IL2CPP's names, types and string literals | 8,942,676 | 2,017,844 |
| data: `data.unity3d`, the assets | 1,475,181 | 1,128,789 |
| data: `unity default resources` | 1,632,004 | 129,081 |
| framework.js | 702,653 | 172,878 |

Inside `data.unity3d`, the two Plex atlases are about 240 KB of it, Liberation Sans (TextMesh Pro's fallback for
a character Plex lacks, typed into a name or a comment) 171 KB, the three Plex faces UI Toolkit draws the shell
with 222 KB, the built-in shaders 100 KB, URP's own shaders 51 KB (the 2D lights' 35 KB of that), and the
Rendering Debugger's prefabs and font, in a `Resources` folder of URP's core package, about 35 KB. Unity stores
`data.unity3d` LZ4HC-compressed inside the data file, so Brotli finds less to take: compressed whole from its
4,773,376 raw bytes it would be 913,553, not 1,128,789. The browser build chooses that itself -- it is not
`BuildOptions`' LZ4, which `WebGLBuild` does not ask for -- and no player setting of 6000.3 was found that turns
it off.

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

## Describing it

Store pages ask what is in it. BitSorter 5.0 is a SystemVerilog simulator for the
Digital Systems and Computer Structure course, not a puzzle game: circuits drawn or
written, run on a 4-state event-driven engine, waves, timing, import and export
for ModelSim, and thirty-five Exercises checked by CHECK. Say so first, and
mention that the puzzle game it began as is 4.0.0.

It ships as one build and needs nothing from any host. The Exercises are JSON under
`Assets/Resources/Exercises/`, so they add no file to package.

---

## Analytics

BitSorter reports to Unity Analytics. **The README's "What it collects" section is
the canonical list of what is sent** — this section covers only what publishing it
requires.

`GameAnalytics` initialises Unity Services, applies the user's consent through
`EndUserConsent`, then reports `levelStarted` and `levelSolved`. It hangs off
`[RuntimeInitializeOnLoadMethod]` rather than a scene component, because
`HalfAdderDemoSceneBuilder` owns scene contents and a component would mean
regenerating and re-verifying the scene for something that needs no inspector
fields. BitSorter ships one scene, so the hook fires exactly once.

Three things to know before trusting the numbers:

- **Custom events must be registered in the Unity Cloud dashboard before they are
  accepted.** Until `levelStarted` and `levelSolved` exist there, with a string
  parameter named `levelName`, the service rejects them and the only sign is a
  warning in the player log. The events being in the build is not enough.
- **The project must stay linked.** Collection depends on the `cloudProjectId` in
  `ProjectSettings.asset`. Unlinking the project silently stops reporting.
- **Reporting is on by default and the player can turn it off**, from the
  workbench's SETTINGS. `GameAnalytics` sets Unity's consent state rather than calling
  the deprecated `StartDataCollection`, so denying really stops collection instead
  of hiding it. The two flows cannot be mixed: once consent is set this way, the
  SDK throws if the old calls are used, which is why none remain.
- **The choice is remembered in `PlayerPrefs`, not by the consent framework.**
  `UnityEngine.UnityConsentModule` has no save, load or clear, so consent is
  per-session and BitSorter re-applies the stored answer at every launch. Clearing
  site data in a browser therefore resets it to on, exactly as it resets the frame
  cap.

Reporting failures never interrupt the user. A missing project, a blocked request or a
rejected event warns and is otherwise ignored, because nothing about analytics is
worth costing anyone their work.

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
3. **Viewport dimensions** → `960 × 600` or larger. The interface targets 16:9 and
   scales with the canvas, so anything in that region is fine.
4. **Mobile friendly** → leave **off**. There is no touch input, by decision.
5. **Fullscreen button** → enable. The circuit and the waves benefit from the space.

Two things to know:

- itch serves static files without custom headers, which is exactly the case the
  decompression fallback exists for. With it on, the build works there.
- itch pages are `https`. The one network call BitSorter makes is analytics, to
  Unity Services over `https`, so mixed content has nothing to break.

**What only you can provide:** a cover image (630 × 500), screenshots, the
description, a price or "no payments", and whether the page is public or
restricted. The README's GIFs work as screenshots.

## Unity Play

BitSorter 5.0 is not published here; 4.0.0 stays on Unity Play as it was released.
What follows is how a build gets there, should one ever again.

**Upload:** the same zip. Unity Play is built for Unity WebGL specifically and
sets compression headers correctly, so it works with or without the fallback.

1. Create a project at play.unity.com and upload the WebGL zip.
2. Set the title, description and a thumbnail.
3. Choose public or unlisted.

**What only you can provide:** a Unity account, the description and thumbnail,
and agreement to their content terms.

Unity Play enforces an upload size limit that has changed over time — check the
current figure when you upload. For reference the 4.0.0 build is about 16 MB of
compressed payload (16.8 MB zipped), which is small by their standards; 3.0.1's
was about 20 MB, before the size settings above. 5.0's is in the table under
"Compression, and why these settings".

## GitHub Pages

Live at <https://adzahm.github.io/BitSorter/>, served from the `gh-pages` branch. It
serves the current version, 5.0.0.

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
the repository. Of the links in the README, only Unity Play works from a private
repo, because Unity hosts that build rather than GitHub.

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
force-pushed. A 16 MB WebGL build barely deltas against the previous one, so
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
  renamed build looks in a directory that does not exist yet and starts the user
  with nothing saved, with their old file still on disk under the previous name. Nothing
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
BitSorter-5.0.0-Windows.zip
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
rule. Around 40 MB compressed, from 104 MB on disk (4.0.0). The 5.0.0 release candidate (v5 `72d5c43f`,
2026-11-09) is 38,155,881 bytes zipped, from 99,282,083 on disk in 190 files; 5.0.0 as released, 38,156,003.

`Build/` is gitignored, so the zip can sit next to the build without any risk of
being committed.

Check for a `_DoNotShip` folder before zipping. `WindowsBuild` deletes it, so it
should not be there — but it carries IL2CPP symbols, and shipping it is the kind of
mistake nobody notices until the download is twice the size it should be.

### Cutting one

A release is three things bolted together: **a tag** naming one commit forever,
**notes**, and **attached files**. The tag is the part that gets skipped.

```powershell
git tag -a v5.0.0 -m "BitSorter 5.0.0: a SystemVerilog simulator for the course"
git push origin v5.0.0
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

`gh release create` does the same job in one command, and the GitHub CLI is
installed now: v1.3.2 was cut with `gh release create v1.3.2 <zip> --verify-tag
--latest --notes-file <notes>`. `--verify-tag` refuses to run unless the tag is
already on GitHub, which rules out the remote-only tag described above, and
`--latest` is the checkbox.

**What only you can provide:** the version number, the notes, and the decision that
a given commit is worth releasing.
