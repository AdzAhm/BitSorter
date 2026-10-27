// The editor's undo and the game's text (Part 14g), under Node's own test runner: `node --test "test/*.test.mjs"` from
// Tools/codemirror. build.ps1 runs it before every bundle.

import { test } from "node:test";
import assert from "node:assert/strict";
import { history, undo, redo } from "@codemirror/commands";
import { blockState, gameText } from "../src/game-text.js";

/** <command> run on <state>, as its key would run it: the state it made, or <state> when it did nothing. */
function run(command, state) {
  let next = state;
  command({ state, dispatch: (transaction) => { next = transaction.state; } });
  return next;
}

const template = "module code1 (\n    input  logic a,\n    output logic y\n);\n    assign y = a;\nendmodule\n";
const device = "module code1 (\n    inout wire io\n);\nendmodule\n";

test("Ctrl+Z takes out what was typed, and never the block's code itself", () => {
  // In the browser smoke test the second Ctrl+Z took out the code the game had shown, and left nothing.
  let state = blockState(template, [history()]);
  state = state.update({ changes: { from: 0, to: state.doc.length, insert: device }, userEvent: "input.paste" }).state;

  state = run(undo, state);
  assert.equal(state.doc.toString(), template, "the paste taken out");
  state = run(undo, state);
  assert.equal(state.doc.toString(), template, "and nothing more: the block's code stays");
  state = run(redo, state);
  assert.equal(state.doc.toString(), device, "Ctrl+Y puts the paste back");
});

test("REVERT is a step Ctrl+Z takes back, as in the desktop build's editor", () => {
  let state = blockState(template, [history()]);
  state = state.update({ changes: { from: 0, insert: "// mine\n" }, userEvent: "input.type" }).state;
  state = state.update(gameText(state, template), { userEvent: "revert" }).state;
  assert.equal(state.doc.toString(), template, "sanity: reverted");

  state = run(undo, state);
  assert.equal(state.doc.toString(), "// mine\n" + template, "REVERT taken back");
});
