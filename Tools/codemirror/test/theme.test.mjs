// The code editor's look (Part 14g), under Node's own test runner: `node --test "test/*.test.mjs"` from
// Tools/codemirror. build.ps1 runs it before every bundle.

import { test } from "node:test";
import assert from "node:assert/strict";
import { Compartment, EditorState } from "@codemirror/state";
import { lookIn, restyled, themeOf } from "../src/theme.js";

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

test("the editor made again takes the new look's colours, never the first look's (browser-3)", () => {
  // STYLE loads the game again, never the page: the editor outlived it, in the colours of the look the page loaded with.
  const light = { ...colours, text: "#202428", ground: "#fbfaf6", muted: "#5c6470", dark: false };
  const compartment = new Compartment();
  let state = EditorState.create({ extensions: [lookIn(compartment, colours)] });
  assert.ok(rulesOf(compartment.get(state)).some((r) => r.includes("background-color: " + colours.ground)), "sanity: the first look's ground");

  state = state.update(restyled(compartment, light)).state;
  const rules = rulesOf(compartment.get(state));
  assert.ok(rules.some((r) => r.includes("background-color: " + light.ground)), "the new look's ground");
  assert.ok(!rules.some((r) => r.includes(colours.ground)), "and nothing left of the first look's");
});
