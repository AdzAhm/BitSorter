// The code editor's look (Part 14g), under Node's own test runner: `node --test "test/*.test.mjs"` from
// Tools/codemirror. build.ps1 runs it before every bundle.

import { test } from "node:test";
import assert from "node:assert/strict";
import { themeOf } from "../src/theme.js";

const colours = {
  text: "#e0e0e0", ground: "#101418", muted: "#808890", selection: "#334455",
  accent: "#ff3399", error: "#ff4444", warning: "#ffaa00", one: "#33ffcc", dark: true,
};

/** Every CSS rule <extension> puts on the page. */
function rulesOf(extension) {
  if (Array.isArray(extension))
    return extension.flatMap(rulesOf);
  const value = extension && extension.value;
  return value && typeof value.getRules === "function" ? value.getRules().split("\n") : [];
}

/** The rule whose selector ends in <selector>, or nothing. */
function rule(rules, selector) {
  return rules.find((r) => r.split("{")[0].trim().endsWith(selector));
}

test("find's checkboxes are drawn in the game's colours, not the browser's white", () => {
  // The Part 14g browser smoke test found them white, the one thing in the editor in the browser's own colours.
  const rules = rulesOf(themeOf(colours));
  const box = rule(rules, ".cm-panel.cm-search input[type=checkbox]");
  assert.ok(box, "a rule for find's checkboxes");
  assert.match(box, /appearance: none/, "the browser's own box left out");
  assert.match(box, new RegExp("background-color: " + colours.ground), "on the editor's ground");
  assert.match(box, new RegExp("border: 1px solid " + colours.muted), "edged as the fields beside it are");

  const ticked = rule(rules, ".cm-panel.cm-search input[type=checkbox]:checked");
  assert.ok(ticked, "a rule for a ticked one");
  assert.match(ticked, new RegExp("background-color: " + colours.accent), "filled with the accent when ticked");
});
