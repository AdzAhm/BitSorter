// BitSorter's files in a browser. Exports (Part 9b-4): the bytes made a Blob, and a link with a download
// name clicked, as a page's own "save as" does -- a browser build cannot write a file the player can
// find, so FileSaver.cs hands its bytes here instead. Imports (Part 13d): the page's own file picker,
// below, since a browser build cannot read the player's disk either.
mergeInto(LibraryManager.library, {
  BitSorterDownload: function (namePtr, bytesPtr, length, mimePtr) {
    var name = UTF8ToString(namePtr);
    var mime = UTF8ToString(mimePtr);
    // A copy, since the heap the pointer points into can move once this returns.
    var bytes = HEAPU8.slice(bytesPtr, bytesPtr + length);
    var url = URL.createObjectURL(new Blob([bytes], { type: mime }));
    var link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    // Revoked a moment later: some browsers start the download only after the click has returned.
    setTimeout(function () { URL.revokeObjectURL(url); }, 1000);
  },

  // An import in a browser (Part 13d): the page's own file picker -- several files, or a folder's -- each file read as
  // text, and the lot kept as JSON until FileImport.cs takes it. Nothing here calls into the game.
  BitSorterPickFiles: function (folder) {
    var input = document.createElement('input');
    input.type = 'file';
    input.multiple = true;
    if (folder) {
      input.webkitdirectory = true;
      input.setAttribute('webkitdirectory', '');
    }
    input.style.display = 'none';
    document.body.appendChild(input);
    var gone = function () {
      if (input.parentNode)
        input.parentNode.removeChild(input);
    };
    // A picker closed with nothing picked fires cancel rather than change: the input goes either way.
    input.addEventListener('cancel', gone);
    input.onchange = function () {
      var picked = Array.prototype.slice.call(input.files || []);
      gone();
      var files = new Array(picked.length);
      var left = picked.length;
      var done = function () { Module.bitSorterPicked = JSON.stringify({ files: files }); };
      if (left === 0) {
        done();
        return;
      }
      picked.forEach(function (file, i) {
        var reader = new FileReader();
        var path = file.webkitRelativePath || file.name;
        reader.onload = function () { files[i] = { path: path, text: String(reader.result) }; if (--left === 0) done(); };
        reader.onerror = function () { files[i] = { path: path, text: '' }; if (--left === 0) done(); };
        reader.readAsText(file);
      });
    };
    input.click();
  },

  BitSorterTakeFiles: function () {
    var json = Module.bitSorterPicked;
    if (!json)
      return null;
    Module.bitSorterPicked = null;
    var size = lengthBytesUTF8(json) + 1;
    var buffer = _malloc(size);
    stringToUTF8(json, buffer, size);
    return buffer;
  }
});
