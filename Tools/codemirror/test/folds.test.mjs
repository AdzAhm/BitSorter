// Where SystemVerilog folds in the browser's editor (5.1), under Node's own test runner: the same fixture the desktop's
// CodeFolds is held to (Tools/ci/ViewModel.Tests/CodeFoldsTests.cs), so the two builds fold alike.

import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { EditorState } from "@codemirror/state";
import { foldable } from "@codemirror/language";
import { foldsOf, verilogFolds } from "../src/folds.js";

const fixture = JSON.parse(readFileSync(new URL("./folds.fixture.json", import.meta.url), "utf8"));

for (const file of fixture.files) {
  test("folds: " + file.name, () => {
    const found = foldsOf(file.code).map((f) => [f.firstLine + 1, f.lastLine + 1]);
    assert.deepEqual(found, file.folds);
  });
}

test("the editor folds a block from its first line's end to its last line's text, as the desktop shows it", () => {
  const code = fixture.files[0].code;
  const state = EditorState.create({ doc: code, extensions: [verilogFolds] });
  const begin = state.doc.line(7);   // "    always_ff @(posedge clk) begin"
  const range = foldable(state, begin.from, begin.to);
  assert.ok(range, "the begin folds");
  assert.equal(range.from, begin.to, "from the end of its first line");
  assert.equal(state.sliceDoc(range.to, range.to + 3), "end", "to its last line's text");
  assert.equal(foldable(state, state.doc.line(2).from, state.doc.line(2).to), null, "a line no block starts on folds nothing");
});
