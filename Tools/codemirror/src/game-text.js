// The game's text in the code editor (Part 14g). Kept out of bitsorter-code.js, which needs a page, so that
// test/game-text.test.mjs can run it under Node.

import { EditorState, Transaction } from "@codemirror/state";
import { isolateHistory } from "@codemirror/commands";

/**
 * The editor's state showing <text>, a block's code as the game gives it when the block is stepped into, with nothing to
 * undo: its code is where the editor's undo starts. Put in as a change, it was a step of that undo, and the Ctrl+Z after
 * the first thing typed took the code itself out and left nothing (the Part 14g browser smoke test).
 */
export function blockState(text, extensions) {
  return EditorState.create({ doc: text, extensions });
}

/**
 * The change that puts <text> in over <state>'s document as the game's -- REVERT -- a step Ctrl+Z takes back, and a step
 * of its own: unlabelled, CodeMirror joined it to typing within half a second of it, and one Ctrl+Z took back both.
 */
export function gameText(state, text) {
  return {
    changes: { from: 0, to: state.doc.length, insert: text },
    annotations: [Transaction.userEvent.of("revert"), isolateHistory.of("full")],
  };
}
