// Where SystemVerilog folds (5.1), for the browser's editor: the desktop's rules (Assets/Scripts/ViewModel/CodeFolds.cs and
// CodeColouring.cs) written again for CodeMirror, and held to the same answer by test/folds.fixture.json, which both builds'
// tests read. Kept out of bitsorter-code.js, which needs a page, so test/folds.test.mjs can run it under Node.

import { foldService } from "@codemirror/language";

/** Each opening word and the words that end it. */
const ENDINGS = {
  module: ["endmodule"], macromodule: ["endmodule"], begin: ["end"], case: ["endcase"], casex: ["endcase"], casez: ["endcase"],
  function: ["endfunction"], task: ["endtask"], generate: ["endgenerate"], fork: ["join", "join_any", "join_none"],
  interface: ["endinterface"], package: ["endpackage"], class: ["endclass"],
};
const ENDERS = new Set(Object.values(ENDINGS).flat());

const isWord = (c) => /[A-Za-z0-9_$]/.test(c);

/** What each character is, as CodeColouring reads it, for folding: "c" comment, "s" string, "w" a word's, "n" number, " " the rest. */
function classify(text) {
  const kinds = new Array(text.length).fill(" ");
  const fill = (from, to, kind) => { for (let i = from; i < to; i++) kinds[i] = kind; return to; };
  let at = 0;
  while (at < text.length) {
    const c = text[at];
    if (c === "/" && (text[at + 1] === "/" || text[at + 1] === "*")) {
      let end;
      if (text[at + 1] === "/") { end = text.indexOf("\n", at); if (end < 0) end = text.length; }
      else { end = text.indexOf("*/", at + 2); end = end < 0 ? text.length : end + 2; }
      at = fill(at, end, "c");
      continue;
    }
    if (c === '"') {
      let end = at + 1, escaped = false;
      while (end < text.length) {
        const d = text[end];
        if (d === "\n" && !escaped) break;
        end++;
        if (d === '"' && !escaped) break;
        escaped = !escaped && d === "\\";
      }
      at = fill(at, end, "s");
      continue;
    }
    if ((c === "$" || c === "`") && at + 1 < text.length && isWord(text[at + 1])) {
      let end = at + 1;
      while (end < text.length && isWord(text[end])) end++;
      at = fill(at, end, " ");
      continue;
    }
    const number = numberEnd(text, at);
    if (number > at) { at = fill(at, number, "n"); continue; }
    if (isWord(c)) {
      let end = at;
      while (end < text.length && isWord(text[end])) end++;
      at = fill(at, end, "w");
      continue;
    }
    at++;
  }
  return kinds;
}

/** Where a number starting at <at> ends, or <at>: CodeColouring's rule, a based literal, a real or a decimal. */
function numberEnd(text, at) {
  const c = text[at];
  if (!(/[0-9]/.test(c) || c === "'")) return at;
  if (at > 0 && isWord(text[at - 1])) return at;
  let i = at;
  while (i < text.length && /[0-9_]/.test(text[i])) i++;
  let j = i;
  while (j < text.length && text[j] === " ") j++;
  if (text[j] === "'") {
    let k = j + 1;
    if (text[k] === "s" || text[k] === "S") k++;
    if (k < text.length && /[dDbBoOhH]/.test(text[k])) {
      const base = text[k].toLowerCase();
      k++;
      while (text[k] === " ") k++;
      const digits = k;
      while (k < text.length && digitOf(base, text[k])) k++;
      if (k > digits) return k;
    }
    return i > at ? i : at;
  }
  if (i === at) return at;
  let r = i;
  if (text[r] === "." && /[0-9]/.test(text[r + 1] || "")) { r += 2; while (r < text.length && /[0-9_]/.test(text[r])) r++; }
  if (text[r] === "e" || text[r] === "E") {
    let e = r + 1;
    if (text[e] === "-") e++;
    if (/[0-9]/.test(text[e] || "")) { while (e < text.length && /[0-9_]/.test(text[e])) e++; r = e; }
  }
  return r;
}

function digitOf(base, c) {
  const l = c.toLowerCase();
  if (l === "_" || l === "x" || l === "z") return true;
  if (base === "b") return l === "0" || l === "1";
  if (base === "o") return l >= "0" && l <= "7";
  if (base === "d") return l >= "0" && l <= "9";
  return /[0-9a-f?]/.test(l);
}

function lineStarts(text) {
  const starts = [0];
  for (let i = 0; i < text.length; i++) if (text[i] === "\n") starts.push(i + 1);
  return starts;
}

function lineOf(starts, at) {
  let lo = 0, hi = starts.length - 1;
  while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (starts[mid] <= at) lo = mid; else hi = mid - 1; }
  return lo;
}

/**
 * The folds of <text>, as CodeFolds finds them: [{ firstLine, lastLine, from, to }], lines from 0, one to a line -- the one
 * reaching furthest -- <from> where its first line ends and <to> its last line's first character that is not a space.
 */
export function foldsOf(text) {
  if (!text) return [];
  const kinds = classify(text);
  const starts = lineStarts(text);
  const endOfLine = (line) => (line + 1 < starts.length ? starts[line + 1] - 1 : text.length);
  const firstNonSpace = (line) => {
    let at = starts[line];
    const end = endOfLine(line);
    while (at < end && (text[at] === " " || text[at] === "\t" || text[at] === "\r")) at++;
    return at;
  };
  const best = new Map();
  const found = (open, close) => {
    const first = lineOf(starts, open), last = lineOf(starts, close);
    if (last <= first) return;
    const kept = best.get(first);
    if (!kept || last > kept.lastLine) best.set(first, { firstLine: first, lastLine: last, from: endOfLine(first), to: firstNonSpace(last) });
  };

  const words = [], brackets = [];
  let previous = null, at = 0;
  while (at < text.length) {
    const c = text[at], kind = kinds[at];
    if (kind === "c" && c === "/" && text[at + 1] === "*") {
      let end = at;
      while (end < text.length && kinds[end] === "c") end++;
      found(at, end - 1);
      at = end;
      continue;
    }
    if (kind === "c" || kind === "s") { at++; continue; }
    if (kind === "w") {
      let end = at;
      while (end < text.length && kinds[end] === "w") end++;
      const word = text.slice(at, end);
      if (ENDERS.has(word)) {
        for (let i = words.length - 1; i >= 0; i--) {
          if (!ENDINGS[words[i].word].includes(word)) continue;
          found(words[i].at, at);
          words.length = i;
          break;
        }
      } else if (ENDINGS[word] && !(word === "fork" && (previous === "wait" || previous === "disable"))
                 && !(word === "class" && (previous === "typedef" || previous === "interface"))) {
        words.push({ word, at });
      }
      if (/[A-Za-z_]/.test(word[0])) previous = word;
      at = end;
      continue;
    }
    if (c === "(" || c === "[" || c === "{") brackets.push({ bracket: c, at });
    else if (c === ")" || c === "]" || c === "}") {
      const opening = c === ")" ? "(" : c === "]" ? "[" : "{";
      for (let i = brackets.length - 1; i >= 0; i--) {
        if (brackets[i].bracket !== opening) continue;
        found(brackets[i].at, at);
        brackets.length = i;
        break;
      }
    }
    at++;
  }

  // Runs of two or more lines that are only // comments.
  const lineComment = (line) => {
    const at = firstNonSpace(line);
    return text[at] === "/" && text[at + 1] === "/" && kinds[at] === "c";
  };
  let run = -1;
  for (let line = 0; line <= starts.length; line++) {
    const comment = line < starts.length && lineComment(line);
    if (comment && run < 0) run = line;
    if (!comment && run >= 0) {
      if (line - 1 > run) found(starts[run], starts[line - 1]);
      run = -1;
    }
  }

  return [...best.values()].sort((a, b) => a.firstLine - b.firstLine);
}

// The last document folded, and its folds: CodeMirror asks line by line, and the document is the same between edits.
let lastDoc = null, lastFolds = [];

/** The editor's fold service: the block that starts on the line asked about, folded from that line's end to its last line's text. */
export const verilogFolds = foldService.of((state, lineStart, lineEnd) => {
  if (state.doc !== lastDoc) {
    lastDoc = state.doc;
    lastFolds = foldsOf(state.doc.toString());
  }
  const line = state.doc.lineAt(lineStart).number - 1;
  const fold = lastFolds.find((f) => f.firstLine === line);
  return fold ? { from: fold.from, to: fold.to } : null;
});
