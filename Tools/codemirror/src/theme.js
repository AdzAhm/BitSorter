// The code editor's look (Part 12f), kept out of bitsorter-code.js, which needs a page, so that test/theme.test.mjs
// can build it under Node (Part 14g).

import { EditorView } from "@codemirror/view";
import { syntaxHighlighting, HighlightStyle } from "@codemirror/language";
import { tags } from "@lezer/highlight";

/** The editor's look from the game's palette: every colour the game's, none of CodeMirror's own. */
export function themeOf(c) {
  const look = EditorView.theme({
    "&": { color: c.text, backgroundColor: c.ground, height: "100%" },
    ".cm-scroller": { fontFamily: "'IBM Plex Mono', ui-monospace, Consolas, Menlo, monospace", lineHeight: "1.4" },
    ".cm-content": { caretColor: c.text },
    "&.cm-focused .cm-cursor": { borderLeftColor: c.text, borderLeftWidth: "2px" },
    "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, ::selection": { backgroundColor: c.selection + " !important" },
    ".cm-gutters": { backgroundColor: c.ground, color: c.muted, border: "none" },
    ".cm-activeLine": { backgroundColor: "transparent" },
    ".cm-activeLineGutter": { backgroundColor: "transparent", color: c.text },
    "&.cm-focused": { outline: "none" },
    ".cm-lintRange-error": { backgroundImage: "none", textDecoration: "underline wavy " + c.error },
    ".cm-lintRange-warning": { backgroundImage: "none", textDecoration: "underline wavy " + c.warning },
    ".cm-tooltip": { backgroundColor: c.ground, color: c.text, border: "1px solid " + c.muted },
    // Find and replace (Part 14g), in the game's colours like the rest.
    ".cm-panels": { backgroundColor: c.ground, color: c.text },
    ".cm-panels.cm-panels-top": { borderBottom: "1px solid " + c.muted },
    ".cm-textfield": { backgroundColor: c.ground, color: c.text, border: "1px solid " + c.muted },
    ".cm-button": { backgroundImage: "none", backgroundColor: c.ground, color: c.text, border: "1px solid " + c.muted },
    ".cm-panel.cm-search label, .cm-panel.cm-search [name=close]": { color: c.text },
    // Its checkboxes drawn here too, not the browser's own white boxes (the Part 14g browser smoke test): on the ground,
    // edged as the fields are, filled with the accent round a ring of the ground when ticked.
    ".cm-panel.cm-search input[type=checkbox]": {
      appearance: "none", width: "1em", height: "1em", margin: "0 0.35em 0 0", verticalAlign: "-0.15em",
      backgroundColor: c.ground, border: "1px solid " + c.muted, borderRadius: "2px", cursor: "pointer",
    },
    ".cm-panel.cm-search input[type=checkbox]:checked": {
      backgroundColor: c.accent, borderColor: c.accent, boxShadow: "inset 0 0 0 2px " + c.ground,
    },
    ".cm-panel.cm-search input[type=checkbox]:focus-visible": { outline: "1px solid " + c.accent, outlineOffset: "1px" },
    ".cm-searchMatch": { backgroundColor: c.selection, outline: "1px solid " + c.muted },
    ".cm-searchMatch.cm-searchMatch-selected": { backgroundColor: c.selection, outline: "1px solid " + c.accent },
    // Folding (5.1): the margin's - and +, and a folded block's mark, in the game's colours.
    ".cm-foldGutter .cm-gutterElement": { color: c.muted, cursor: "pointer", padding: "0 4px" },
    ".cm-foldGutter .cm-gutterElement:hover": { color: c.text },
    ".cm-foldPlaceholder": { backgroundColor: "transparent", border: "1px solid " + c.muted, color: c.text, padding: "0 4px" },
  }, { dark: c.dark });

  return [look, syntaxHighlighting(codeWords(c))];
}

/**
 * The code's colours from the game's palette <c> (5.1): its own inks for code -- keyword, number, comment, system -- each
 * legible on the ground as every word is, the same the desktop's editor writes code in; the look's colours where a page
 * gives none.
 */
export function codeWords(c) {
  const keyword = c.keyword || c.accent, number = c.number || c.one, comment = c.comment || c.muted, system = c.system || c.warning;
  return HighlightStyle.define([
    { tag: tags.keyword, color: keyword },
    { tag: [tags.typeName, tags.standard(tags.name)], color: keyword },
    { tag: [tags.number, tags.literal, tags.string], color: number },
    { tag: tags.comment, color: comment, fontStyle: "italic" },
    { tag: [tags.meta, tags.processingInstruction], color: system },
  ]);
}

/** The editor's look in <compartment>, from the game's palette <c>, so another look's can be put in its place. */
export function lookIn(compartment, c) {
  return compartment.of(themeOf(c));
}

/**
 * The change that puts the look of the palette <c> in <compartment>, in place of the last: STYLE loads the game again,
 * never the page, so the page's editor outlives a change of style and takes the new look's colours (browser-3).
 */
export function restyled(compartment, c) {
  return { effects: compartment.reconfigure(themeOf(c)) };
}
