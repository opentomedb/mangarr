#!/usr/bin/env node
// UI translations v1 (2026-09-25), fix round 1: reproduces the reviewer's two false passes against
// checkFile() directly (no git, no working tree -- see check-routing.js's exported checkFile) and
// proves both now fail, alongside a positive control for each showing the correct case still
// reports nothing.
//   node scripts/i18n/test_check_routing.js
const assert = require('assert');
const { checkFile, selectEquivalences } = require('./check-routing');

let failures = 0;

function check(name, fn) {
  try {
    fn();
    console.log(`ok - ${name}`);
  } catch (e) {
    failures += 1;
    console.log(`not ok - ${name}`);
    console.log(`  ${e.message}`);
  }
}

function run(file, baseSource, headSource, enBase, enHead) {
  return checkFile(file, baseSource, headSource, enBase, enHead, [], {}, false);
}

// --- R1a: a translate() call whose en value names a token the call never supplies. -------------
// Base: <p>{total} items</p> (an unresolved expression, prints as the "{total}" convention).
// Head: translate('TotalItems', { count: total }) -- the call supplies "count", not "total"; en's
// "{total}" falls back to its own raw text (translate.ts's real behaviour), which a routing task
// must not get away with.
const R1A_FILE = 'fixture/R1aFile.js';
const R1A_BASE = "function C() { return <p>{total} items</p>; }";

check('R1a: mismatched named token is reported as unfilled, not silently passed', () => {
  const enBase = {};
  const enHeadBad = { TotalItems: '{total} items' };
  const headBad = "function C() { return <p>{translate('TotalItems', { count: total })}</p>; }";

  const problems = run(R1A_FILE, R1A_BASE, headBad, enBase, enHeadBad);

  assert.ok(
    problems.some((p) => p.includes("translate('TotalItems') leaves {total} unfilled")),
    `expected an "unfilled" problem, got:\n${problems.join('\n')}`
  );
});

check('R1a positive control: the correctly named token reports nothing', () => {
  const enBase = {};
  const enHeadGood = { TotalItems: '{total} items' };
  const headGood = "function C() { return <p>{translate('TotalItems', { total })}</p>; }";

  const problems = run(R1A_FILE, R1A_BASE, headGood, enBase, enHeadGood);

  assert.deepStrictEqual(problems, [], `expected no problems, got:\n${problems.join('\n')}`);
});

check('R1a positive control: {appName}, a token the app supplies globally, is never "unfilled"', () => {
  const enBase = {};
  const enHeadGood = { AppItems: '{appName} items' };
  const base = "function C() { return <p>Mangarr items</p>; }";
  const headGood = "function C() { return <p>{translate('AppItems')}</p>; }";

  const problems = run('fixture/AppNameFile.js', base, headGood, enBase, enHeadGood);

  assert.deepStrictEqual(problems, [], `expected no problems, got:\n${problems.join('\n')}`);
});

// --- R1b: a translateElements() sentence whose pieces are real but out of order. ----------------
// Base: <p>Before the <a>link</a>, after the link</p> -- one ordered sentence, "Before the <a>,
// after the link" (elements become placeholders), and its two text pieces also exist standalone in
// the old fragment model. A routing task can wire this up correctly (order preserved) or wrongly
// (order swapped) while every individual English piece still, technically, exists somewhere in the
// base multiset -- the bug the old unordered "each part exists somewhere" check let through.
const R1B_FILE = 'fixture/R1bFile.js';
const R1B_BASE = "function C() { return <p>Before the <a>link</a>, after the link</p>; }";

check('R1b: a reordered translateElements sentence is reported, not silently passed', () => {
  const enBase = {};
  // The two halves swapped verbatim: each piece still exists somewhere in base ("Before the " and
  // ", after the link"), which is exactly what let the old unordered check pass this.
  const enHeadBad = { Sentence: ', after the link{name}Before the ' };
  const headBad = "function C() { return <p>{translateElements('Sentence', { name: <a>link</a> })}</p>; }";

  const problems = run(R1B_FILE, R1B_BASE, headBad, enBase, enHeadBad);

  assert.ok(problems.length > 0, 'expected the reordered sentence to be reported as a problem');
  assert.ok(
    problems.some((p) => p.includes('English new or changed') && p.includes(', after the link<a>Before the ')),
    `expected the scrambled composite reported as new/changed, got:\n${problems.join('\n')}`
  );
});

check('R1b positive control: the correctly ordered translateElements sentence reports nothing', () => {
  const enBase = {};
  const enHeadGood = { Sentence: 'Before the {name}, after the link' };
  const headGood = "function C() { return <p>{translateElements('Sentence', { name: <a>link</a> })}</p>; }";

  const problems = run(R1B_FILE, R1B_BASE, headGood, enBase, enHeadGood);

  assert.deepStrictEqual(problems, [], `expected no problems, got:\n${problems.join('\n')}`);
});

// --- Task 5: translateElements(key, elements, tokens), a data token beside an element. ------------
// Base: <p>Version {version} by <a>link</a>.</p> -- a data value, an element, and a tail with no
// English (".") that was never a fragment on either side, so it must not be required in `gone`.
const T5_FILE = 'fixture/T5File.js';
const T5_BASE = "function C() { return <p>Version {version} by <a>link</a>.</p>; }";

check('Task 5: a data token beside an element, with a punctuation tail, reports nothing', () => {
  const enHead = { Sentence: 'Version {version} by {author}.' };
  const head = "function C() { return <p>{translateElements('Sentence', { author: <a>link</a> }, { version })}</p>; }";

  const problems = run(T5_FILE, T5_BASE, head, {}, enHead);

  assert.deepStrictEqual(problems, [], `expected no problems, got:\n${problems.join('\n')}`);
});

check('Task 5: a misnamed data token in the third argument is reported as unfilled', () => {
  const enHead = { Sentence: 'Version {version} by {author}.' };
  const head = "function C() { return <p>{translateElements('Sentence', { author: <a>link</a> }, { ver: version })}</p>; }";

  const problems = run(T5_FILE, T5_BASE, head, {}, enHead);

  assert.ok(
    problems.some((p) => p.includes("translateElements('Sentence') leaves {version} unfilled")),
    `expected an "unfilled" problem, got:\n${problems.join('\n')}`
  );
});

check('Task 5: an element followed by a whitespace-only tail (About.js\'s shape) reports nothing', () => {
  const base = "function C() { return <span> {packageVersion} {' by '} <b>x</b> </span>; }";
  const enHead = { PackageBy: '{packageVersion}  by  {packageAuthor}' };
  const head = "function C() { return <span> {translateElements('PackageBy', { packageAuthor: <b>x</b> }, { packageVersion })} </span>; }";

  const problems = run('fixture/T5Tail.js', base, head, {}, enHead);

  assert.deepStrictEqual(problems, [], `expected no problems, got:\n${problems.join('\n')}`);
});

// --- Task 14 (ruling S2): --all-tasks approves every task's entries, not one tag's. ---------------
// A file two tasks restructured (an earlier task's plural split, then the final task's bug fix),
// proved against a ref older than both: one tag's entries leave the other's change unexplained.
const S2_ENTRIES = [
  { task: 'typos', enKey: 'TypoKey', why: 'typo fix' },
  { task: 'library', file: 'fixture/S2File.js', base: ['{n} item{…}', 's'], head: ['{n} items', '{n} item'], why: 'plural split' },
  { task: 'final', file: 'fixture/S2File.js', base: ['Deceased'], head: ['Ended'], why: 'bug fix' }
];
const S2_BASE = "function C() { const a = `${n} item${n > 1 ? 's' : ''}`; const d = 'Deceased'; return <p>{a}{d}</p>; }";
const S2_HEAD = "function C() { const a = n > 1 ? translate('Items', { n }) : translate('Item', { n }); const d = translate('StatusEnded'); return <p>{a}{d}</p>; }";
const S2_EN = { Items: '{n} items', Item: '{n} item', StatusEnded: 'Ended' };

check('S2: --all-tasks approves every task\'s en keys; one tag approves only its own', () => {
  assert.ok(selectEquivalences(S2_ENTRIES, null).approvedEn.has('TypoKey'));
  assert.ok(!selectEquivalences(S2_ENTRIES, 'final').approvedEn.has('TypoKey'));
  assert.strictEqual(selectEquivalences(S2_ENTRIES, null).equivalences.length, 3);
  assert.strictEqual(selectEquivalences(S2_ENTRIES, 'final').equivalences.length, 1);
});

check('S2: a file two tasks restructured passes with --all-tasks', () => {
  const { equivalences } = selectEquivalences(S2_ENTRIES, null);
  const problems = checkFile('fixture/S2File.js', S2_BASE, S2_HEAD, {}, S2_EN, equivalences, {}, false);

  assert.deepStrictEqual(problems, [], `expected no problems, got:\n${problems.join('\n')}`);
});

check('S2 control: the same file fails with only the final tag approved', () => {
  const { equivalences } = selectEquivalences(S2_ENTRIES, 'final');
  const problems = checkFile('fixture/S2File.js', S2_BASE, S2_HEAD, {}, S2_EN, equivalences, {}, false);

  assert.ok(problems.some((p) => p.includes('English new or changed: "{n} items"')),
    `expected the other task's plural split reported, got:\n${problems.join('\n')}`);
});

console.log(failures ? `test_check_routing: ${failures} failure(s)` : 'test_check_routing: OK');
process.exit(failures ? 1 : 0);
