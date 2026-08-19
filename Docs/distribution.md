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

---

## Desktop

`Build/Windows/` is ready to zip and send as it stands. The build script deletes
the `_DoNotShip` folder Unity leaves behind, so the folder can go out as it is.

- **Window title** is `productName`, which is `BitSorter`.
- **Icon** is generated by **BitSorter → Generate App Icon**, which draws
  `Assets/Icon/BitSorterIcon.png` and assigns it to every standalone size.
- **Company / product** are `Ahmad` / `BitSorter`. These set the save location,
  so changing them moves where progress lives:
  `%USERPROFILE%\AppData\LocalLow\Ahmad\BitSorter\progress.json`.

The build is **unsigned**, so Windows SmartScreen warns that the publisher is not
recognised. That is expected for an unsigned binary and is mentioned in the
README, but tell testers directly or they will assume it is broken.
