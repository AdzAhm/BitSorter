// BitSorter's exports in a browser (Part 9b-4): the bytes made a Blob, and a link with a download
// name clicked, as a page's own "save as" does. A browser build cannot write a file the player can
// find, so FileSaver.cs hands its bytes here instead.
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
  }
});
