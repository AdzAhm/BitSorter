// The code editor BitSorter's browser build shows over a written block (Part 12f): CodeMirror 6 in a box laid over the
// game's canvas, where the circuit's window is. The game drives it through window.BitSorterCode, from its .jslib; it
// never reaches into the game. Bundled by build.mjs into Assets/Plugins/WebGL/codemirror.jspre.

import { EditorState, Compartment } from "@codemirror/state";
import { EditorView, keymap, lineNumbers, highlightActiveLine, highlightActiveLineGutter, drawSelection, highlightSpecialChars } from "@codemirror/view";
import { defaultKeymap, history, historyKeymap, indentWithTab, redo } from "@codemirror/commands";
import { StreamLanguage, bracketMatching, indentOnInput, indentUnit } from "@codemirror/language";
import { verilog } from "@codemirror/legacy-modes/mode/verilog";
import { setDiagnostics, lintGutter } from "@codemirror/lint";
import { search, searchKeymap, searchPanelOpen, closeSearchPanel } from "@codemirror/search";
import { blockState, gameText } from "./game-text.js";
import { themeOf } from "./theme.js";

const state = {
  host: null,         // the box over the canvas
  view: null,         // the editor
  extensions: null,   // what it is made of, for each block's code shown afresh
  theme: new Compartment(),
  changed: false,     // typed since the game last asked
  applyAsked: false,  // Ctrl+Enter or Ctrl+S since the game last asked
  escaped: false,     // Escape let the editor go since the game last asked
  setting: false,     // the game is setting the text: not typing
  colours: null,
};

const api = {
  /** Makes the editor, hidden, its colours the game's: a JSON object of CSS colours, and whether the ground is dark. */
  create(coloursJson) {
    if (state.view)
      return;
    state.colours = JSON.parse(coloursJson);

    const host = document.createElement("div");
    host.id = "bitsorter-code";
    host.style.position = "absolute";
    host.style.display = "none";
    host.style.zIndex = "10";
    host.style.overflow = "hidden";
    document.body.appendChild(host);
    state.host = host;

    // Ctrl+S applies as Ctrl+Enter does (Part 14g), and the browser's Save Page never opens over the code. Escape closes
    // find and replace first, and only then lets the editor go.
    const ours = keymap.of([
      { key: "Mod-Enter", run: () => { state.applyAsked = true; return true; } },
      // From find's fields too, or the browser's Save Page opens over the code (the 14g review).
      { key: "Mod-s", run: () => { state.applyAsked = true; return true; }, scope: "editor search-panel" },
      // Redo as the desktop build's editor does; CodeMirror binds it on a Mac and Linux, not on Windows.
      { key: "Mod-Shift-z", run: redo },
      {
        key: "Escape",
        run: (view) => {
          if (searchPanelOpen(view.state))
            return closeSearchPanel(view);
          state.escaped = true;
          view.contentDOM.blur();
          return true;
        },
      },
    ]);

    state.extensions = [
      ours,
      lineNumbers(), highlightActiveLineGutter(), highlightSpecialChars(), history(), drawSelection(),
      indentOnInput(), bracketMatching(), highlightActiveLine(), lintGutter(),
      indentUnit.of("    "), EditorState.tabSize.of(4),
      search({ top: true }),
      keymap.of([indentWithTab, ...searchKeymap, ...defaultKeymap, ...historyKeymap]),
      StreamLanguage.define(verilog),
      state.theme.of(themeOf(state.colours)),
      EditorView.updateListener.of((update) => {
        if (update.docChanged && !state.setting)
          state.changed = true;
      }),
    ];
    state.view = new EditorView({ parent: host, state: blockState("", state.extensions) });
  },

  /** Lays the editor over the canvas: x and y from the canvas's top-left, width, height and its letters' size, in the canvas's own pixels. */
  place(canvas, x, y, width, height, font) {
    if (!state.host || !canvas)
      return;
    const box = canvas.getBoundingClientRect();
    const sx = box.width / canvas.width, sy = box.height / canvas.height;
    state.host.style.left = (window.scrollX + box.left + x * sx) + "px";
    state.host.style.top = (window.scrollY + box.top + y * sy) + "px";
    state.host.style.width = Math.max(0, width * sx) + "px";
    state.host.style.height = Math.max(0, height * sy) + "px";
    state.host.style.fontSize = Math.max(1, font * sy) + "px";
  },

  show(shown) {
    if (!state.host)
      return;
    state.host.style.display = shown ? "block" : "none";
    if (!shown)
      api.blur();
  },

  /** A block's code shown, as the game gives it when the block is stepped into: the editor's undo starts from it (Part 14g). */
  load(text) {
    if (!state.view)
      return;
    state.view.setState(blockState(text, state.extensions));
    state.changed = false;
  },

  /** The game's text put in over what is shown, as the document has it now -- REVERT -- which is not typing. */
  setText(text) {
    if (!state.view || state.view.state.doc.toString() === text)
      return;
    state.setting = true;
    state.view.dispatch(gameText(state.view.state, text));
    state.setting = false;
  },

  getText() {
    return state.view ? state.view.state.doc.toString() : "";
  },

  /** Whether the text was typed into since the last ask; asking clears it. */
  takeChanged() {
    const was = state.changed;
    state.changed = false;
    return was;
  },

  /** Whether Ctrl+Enter was pressed since the last ask; asking clears it. */
  takeApply() {
    const was = state.applyAsked;
    state.applyAsked = false;
    return was;
  },

  /** Whether Escape let the editor go since the last ask; asking clears it. */
  takeEscape() {
    const was = state.escaped;
    state.escaped = false;
    return was;
  },

  /** The faults to mark: a JSON array of { line, column, message, error }, lines and columns from 1. */
  setMarks(marksJson) {
    if (!state.view)
      return;
    const doc = state.view.state.doc;
    const diagnostics = JSON.parse(marksJson).map((m) => {
      const line = doc.line(Math.min(Math.max(1, m.line), doc.lines));
      const from = Math.min(line.to, line.from + Math.max(0, m.column - 1));
      const to = from < line.to ? line.to : from;
      return { from, to, severity: m.error ? "error" : "warning", message: m.message };
    });
    state.view.dispatch(setDiagnostics(state.view.state, diagnostics));
  },

  /** The keyboard put in the editor, its caret at line and column (from 1), scrolled into sight. */
  goTo(line, column) {
    if (!state.view)
      return;
    const doc = state.view.state.doc;
    const at = doc.line(Math.min(Math.max(1, line), doc.lines));
    const pos = Math.min(at.to, at.from + Math.max(0, column - 1));
    state.view.dispatch({ selection: { anchor: pos }, scrollIntoView: true });
    state.view.focus();
  },

  focus() {
    if (state.view)
      state.view.focus();
  },

  /** The keyboard let go of: by the text, or by find and replace's fields, which are the editor's too. */
  blur() {
    const active = document.activeElement;
    if (state.host && active && state.host.contains(active))
      active.blur();
  },

  /**
   * Whether the editor has the keyboard: while it does, the game's keys are the editor's. The text or anything else in its
   * box -- find and replace's fields (Part 14g), where typing an S would otherwise have been the game's.
   */
  focused() {
    const active = document.activeElement;
    return !!(state.host && state.host.style.display !== "none" && active && state.host.contains(active));
  },
};

window.BitSorterCode = api;
