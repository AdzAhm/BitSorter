// The save in a browser with BitSorter open in two tabs (browser-1; Ahmad's decision, 2026-11-03: warn and reload).
// BrowserSave.cs calls these on a browser build. Each tab keeps the folder Unity gives Application.persistentDataPath in
// memory, read from IndexedDB once as the page loads and written back to it whole whenever a file in it changes -- so a tab
// that read the save before another wrote it puts its older save back over the newer one as soon as it writes anything, a
// setting included. Each save's stamp is told to every other tab, once the save is in IndexedDB, through localStorage,
// which every tab of the page shares at once: a tab that hears of a stamp not its own takes IndexedDB's files into memory
// -- so what it writes after that carries the newer save, not its own -- and its ProgressStore writes no save over it until
// the page is loaded again. Nothing here calls into the game.
mergeInto(LibraryManager.library, {
  // The stamp this tab read its save with ('' for none). From the first call on, this tab hears of every other tab's save.
  BitSorterSaveRead: function (stampPtr) {
    var save = Module.bitSorterSave || (Module.bitSorterSave = { own: '', elsewhere: false, hearing: false });
    save.own = UTF8ToString(stampPtr);
    save.elsewhere = false;
    if (save.hearing)
      return;
    save.hearing = true;
    window.addEventListener('storage', function (e) {
      if (e.key !== 'BitSorter.save' || !e.newValue || e.newValue === save.own)
        return;
      save.elsewhere = true;
      // What the other tab wrote, into this tab's memory: a file this tab writes from now on -- a setting -- puts the newer
      // save back into IndexedDB with it, never this tab's older one.
      FS.syncfs(true, function () {});
    });
  },

  BitSorterSaveWrittenElsewhere: function () {
    var save = Module.bitSorterSave;
    return save && save.elsewhere ? 1 : 0;
  },

  // This tab's save written: told to the other tabs once IndexedDB has it, so a tab that takes it in takes this one. One
  // write to IndexedDB at a time: saves come in runs -- a keep, then what is open -- and two in flight at once is only
  // extra work, which Emscripten warns of; a save made while one is told is told after it, the latest stamp only.
  BitSorterSaveWritten: function (stampPtr) {
    var save = Module.bitSorterSave || (Module.bitSorterSave = { own: '', elsewhere: false, hearing: false });
    save.own = UTF8ToString(stampPtr);
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
          window.localStorage.setItem('BitSorter.save', stamp);
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
