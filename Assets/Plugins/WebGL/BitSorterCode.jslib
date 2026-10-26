// BitSorter's code editor in a browser (Part 12f): CodeMirror, in a box laid over the canvas where the circuit's window is.
// The editor itself is window.BitSorterCode, bundled into codemirror.jspre (Tools/codemirror); this only passes the
// game's calls to it. CodeEditorView.cs calls these on a browser build, and keeps its TextMesh Pro field everywhere else.
mergeInto(LibraryManager.library, {
  BitSorterCodeCreate: function (coloursPtr) {
    if (window.BitSorterCode)
      window.BitSorterCode.create(UTF8ToString(coloursPtr));
  },

  // x and y from the canvas's top-left, in the canvas's own pixels (the game turns its bottom-left screen into these).
  BitSorterCodePlace: function (x, y, width, height, font) {
    if (window.BitSorterCode)
      window.BitSorterCode.place(Module['canvas'], x, y, width, height, font);
  },

  BitSorterCodeShow: function (shown) {
    if (window.BitSorterCode)
      window.BitSorterCode.show(!!shown);
  },

  BitSorterCodeSetText: function (textPtr) {
    if (window.BitSorterCode)
      window.BitSorterCode.setText(UTF8ToString(textPtr));
  },

  // The text, in memory from _malloc: the C# side declares it a string, and the runtime copies it out and frees it.
  BitSorterCodeGetText: function () {
    var text = window.BitSorterCode ? window.BitSorterCode.getText() : '';
    var size = lengthBytesUTF8(text) + 1;
    var buffer = _malloc(size);
    stringToUTF8(text, buffer, size);
    return buffer;
  },

  BitSorterCodeTakeChanged: function () {
    return window.BitSorterCode && window.BitSorterCode.takeChanged() ? 1 : 0;
  },

  BitSorterCodeTakeApply: function () {
    return window.BitSorterCode && window.BitSorterCode.takeApply() ? 1 : 0;
  },

  BitSorterCodeTakeEscape: function () {
    return window.BitSorterCode && window.BitSorterCode.takeEscape() ? 1 : 0;
  },

  BitSorterCodeSetMarks: function (marksPtr) {
    if (window.BitSorterCode)
      window.BitSorterCode.setMarks(UTF8ToString(marksPtr));
  },

  BitSorterCodeGoTo: function (line, column) {
    if (window.BitSorterCode)
      window.BitSorterCode.goTo(line, column);
  },

  BitSorterCodeBlur: function () {
    if (window.BitSorterCode)
      window.BitSorterCode.blur();
  },

  BitSorterCodeFocused: function () {
    return window.BitSorterCode && window.BitSorterCode.focused() ? 1 : 0;
  }
});
