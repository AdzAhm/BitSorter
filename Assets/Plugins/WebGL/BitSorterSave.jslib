// The save in a browser with BitSorter open in two tabs (browser-1; Ahmad's decision, 2026-11-03: warn and reload).
// BrowserSave.cs calls these on a browser build. Each tab keeps the folder Unity gives Application.persistentDataPath in
// memory, read from IndexedDB once as the page loads and written back to it whole whenever a file in it changes -- so a tab
// that read the save before another wrote it puts its older save back over the newer one as soon as it writes anything, a
// setting included. Each save's stamp is told to every other tab, once the save is in IndexedDB, through localStorage,
// which every tab of the origin shares at once: a tab that hears of one takes IndexedDB's files into memory -- so what it
// writes after that carries the newer save, not its own -- and, where the save is of its own file, its ProgressStore writes
// no save over it until the page is loaded again. Nothing here calls into the game.
//
// Every BitSorter build on one origin -- the release and a candidate beside it on Pages -- keeps its files in Unity's one
// IndexedDB mount, /idbfs, a folder each, and every tab writes the whole mount back. So a save is told with its path: every
// tab of every build takes the files in, or it would put an older copy of another build's back over the newer, and only a
// tab of the same path is held by it (the browser-1 fix's review: a tab of the release was held whenever the candidate saved).
mergeInto(LibraryManager.library, {
  // The stamp this tab read its save with ('' for none), and the save's path. From the first call on, this tab hears of
  // every other tab's save.
  BitSorterSaveRead: function (stampPtr, pathPtr) {
    var save = Module.bitSorterSave || (Module.bitSorterSave = { own: '', path: '', elsewhere: false, hearing: false });
    save.own = UTF8ToString(stampPtr);
    save.path = UTF8ToString(pathPtr);
    save.elsewhere = false;
    if (save.hearing)
      return;
    save.hearing = true;

    // IndexedDB's files into this tab's memory, once its own last write has gone out to IndexedDB -- Unity's writing back of
    // the mount, and this page's own -- since taken in before it had, IndexedDB's older copy of what this tab just wrote
    // would be put back over it.
    var takeIn = function () {
      var root = Module.__unityIdbfsMount;
      if (save.telling || (root && root.mount && root.mount.idbPersistState)) {
        setTimeout(takeIn, 50);
        return;
      }
      FS.syncfs(true, function () {});
    };

    window.addEventListener('storage', function (e) {
      if (e.key !== 'BitSorter.save' || !e.newValue)
        return;
      var told = null;
      try {
        told = JSON.parse(e.newValue);
      } catch (x) {
        // Not a save told as this page tells one: still a write to IndexedDB, so taken in, and holding nothing.
      }
      if (told && told.path === save.path && told.stamp !== save.own)
        save.elsewhere = true;
      // What the other tab wrote, into this tab's memory: a file this tab writes from now on -- a setting -- puts the newer
      // save back into IndexedDB with it, never this tab's older one.
      takeIn();
    });
  },

  BitSorterSaveWrittenElsewhere: function () {
    var save = Module.bitSorterSave;
    return save && save.elsewhere ? 1 : 0;
  },

  // This tab's save written, at its path: told to the other tabs once IndexedDB has it, so a tab that takes it in takes this
  // one. One write to IndexedDB at a time: saves come in runs -- a keep, then what is open -- and two in flight at once is
  // only extra work, which Emscripten warns of; a save made while one is told is told after it, the latest stamp only.
  BitSorterSaveWritten: function (stampPtr, pathPtr) {
    var save = Module.bitSorterSave || (Module.bitSorterSave = { own: '', path: '', elsewhere: false, hearing: false });
    save.own = UTF8ToString(stampPtr);
    save.path = UTF8ToString(pathPtr);
    if (save.telling) {
      save.again = true;
      return;
    }
    var tell = function () {
      var stamp = save.own;
      save.telling = true;
      save.again = false;
      FS.syncfs(false, function () {
        try {
          window.localStorage.setItem('BitSorter.save', JSON.stringify({ path: save.path, stamp: stamp }));
        } catch (e) {
          // No localStorage -- a private window may refuse it -- and so no other tab hears: as it was before.
        }
        if (save.again)
          tell();
        else
          save.telling = false;
      });
    };
    tell();
  },

  // RELOAD: the page loaded again, which reads the save from IndexedDB as the other tab left it.
  BitSorterSaveReload: function () {
    window.location.reload();
  }
});
