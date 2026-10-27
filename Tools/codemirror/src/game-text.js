// The game's text in the code editor (Part 14g). Kept out of bitsorter-code.js, which needs a page, so that
// test/game-text.test.mjs can run it under Node.

import { EditorState } from "@codemirror/state";

/** The editor's state showing <text>, a block's code as the game gives it when the block is stepped into. */
export function blockState(text, extensions) {
  const empty = EditorState.create({ doc: "", extensions });
  return empty.update({ changes: { from: 0, insert: text } }).state;
}

/** The change that puts <text> in over <state>'s document as the game's -- REVERT -- a step Ctrl+Z takes back. */
export function gameText(state, text) {
  return { changes: { from: 0, to: state.doc.length, insert: text } };
}
