#!/usr/bin/env node
// UI translations v1 (2026-09-25): proves a routing task left the rendered English unchanged.
//   node scripts/i18n/check-routing.js --task <tag> [--skip-literals] <base-ref> <file>...
//   node scripts/i18n/check-routing.js --all-tasks [--skip-literals] <base-ref> <file>...
// --all-tasks approves every task's equivalences.json entries, for a proof against a ref older than
// several tasks (Task 14, ruling S2: the plan's base with only one tag approved fails on the other
// tasks' reviewed entries, the typo fixes among them).
// For every listed file, the multiset of English fragments (fragments.js) at <base-ref>, rendered
// with that ref's en.json, equals the working tree's, rendered with today's en.json -- after the
// task's reviewed restructures in equivalences.json are taken out of both sides. Also fails on:
//  - a <base-ref> en.json key whose value changed or vanished, unless an equivalences.json entry
//    of the task names it (the approved English typo fixes);
//  - translate() at module scope (it runs before the translations load: the key name renders);
//  - a getter in Store state, or () => translate() on a prop that reads a string;
//  - a translate() key missing from en.json anywhere in frontend/src;
//  - a UI-looking literal left in a listed file that keep.json does not excuse (unless
//    --skip-literals: the typo task touches files whose literals a later task routes);
//  - a translate()/translateElements() call whose en value names a token the call doesn't supply
//    (fix round 1, R1a: translate.ts renders that token's literal "{name}" text to real users);
//  - a translateElements() sentence whose text, in order, doesn't match one specific sentence the
//    base JSX rendered (fix round 1, R1b: matching each piece unordered let a swap pass).
// checkFile() below is the per-file check, exported so scripts/i18n/test_check_routing.js can run
// it directly against in-memory source strings; the CLI section (require.main guarded) is the only
// part that touches git or the filesystem.
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const { extract } = require('./fragments');

const REPO = path.resolve(__dirname, '../..');
const EN = 'src/NzbDrone.Core/Localization/Core/en.json';
const ELEMENT_RE = /<[A-Za-z][\w.]*>/;
const ELEMENT_SPLIT_RE = /<[A-Za-z][\w.]*>/g;

function git(...args) {
  return execFileSync('git', args, { cwd: REPO, stdio: ['ignore', 'pipe', 'ignore'] }).toString();
}

function atRef(ref, file) {
  try {
    return git('show', `${ref}:${file}`);
  } catch (e) {
    return null;
  }
}

function counts(list) {
  const map = new Map();
  list.forEach((s) => map.set(s, (map.get(s) || 0) + 1));
  return map;
}

function minus(a, b) {
  const out = [];
  a.forEach((n, s) => {
    for (let i = 0; i < n - (b.get(s) || 0); i++) {
      out.push(s);
    }
  });
  return out;
}

function take(map, list) {
  list.forEach((s) => map.set(s, (map.get(s) || 0) - 1));
}

// The per-file check: base/head are source text (or null, meaning the file doesn't exist there),
// enBase/enHead the two localizations, equivalences already filtered to this task.
function checkFile(file, baseSource, headSource, enBase, enHead, equivalences, keep, skipLiterals) {
  const problems = [];

  if (baseSource === null && headSource === null) {
    problems.push(`${file}: no such file at the base ref or in the working tree`);
    return problems;
  }

  const baseResult = baseSource === null ? { fragments: [], composites: [] } : extract(baseSource, enBase);
  const base = counts(baseResult.fragments);
  const headResult = headSource === null ?
    { fragments: [], moduleScope: [], candidates: [], unfilled: [], composites: [] } : extract(headSource, enHead);
  const head = counts(headResult.fragments);

  equivalences.filter((e) => e.file === file && e.base).forEach((e) => {
    take(base, e.base);
    take(head, e.head);
  });

  let gone = counts(minus(base, head));
  const added = [];
  const baseComposites = counts(baseResult.composites || []);

  // A sentence routed through translateElements() renders its element tokens as <Name>; it proves
  // itself only when it equals -- in order -- one specific sentence the base JSX actually rendered
  // (fix round 1, R1b: two pieces that each existed somewhere in base, out of order, do not prove
  // this; they prove a different, wrong sentence).
  minus(head, base).forEach((s) => {
    if (ELEMENT_RE.test(s) && (baseComposites.get(s) || 0) > 0) {
      // A part with no English outside its placeholders (the " " or "." after an element) was never
      // a fragment on either side -- push() drops it -- so it can't be in `gone`; the whole-sentence
      // equality with a base composite above already pins it, in order (Task 5).
      const parts = s.split(ELEMENT_SPLIT_RE).filter((x) => /[A-Za-z]/.test(x.replace(/\{[^}]*\}/g, '')));
      const trial = new Map(gone);
      const ok = parts.every((x) => {
        if ((trial.get(x) || 0) > 0) {
          trial.set(x, trial.get(x) - 1);
          return true;
        }
        return false;
      });

      if (ok) {
        baseComposites.set(s, baseComposites.get(s) - 1);
        gone = trial;
        return;
      }
    }

    added.push(s);
  });

  gone.forEach((n, s) => {
    for (let i = 0; i < n; i++) {
      problems.push(`${file}: English gone or changed: ${JSON.stringify(s)}`);
    }
  });
  added.forEach((s) => problems.push(`${file}: English new or changed: ${JSON.stringify(s)}`));
  [...base.values(), ...head.values()].filter((n) => n < 0).length &&
    problems.push(`${file}: an equivalences.json entry lists a fragment the file does not have`);

  headResult.moduleScope.forEach((m) => problems.push(`${file}:${m.line}: translate() at module scope`));

  // A translate()/translateElements() call whose en value names a token the call never supplies:
  // translate.ts's own fallback renders that token's raw "{name}" text to real users (fix round 1,
  // R1a). Global tokens (appName) are always supplied and can never appear here.
  (headResult.unfilled || []).forEach((u) =>
    problems.push(`${file}:${u.line}: ${u.callee}('${u.key}') leaves {${u.token}} unfilled`));

  // Store state is copied with Object.assign when a column is new since the last persist
  // (createPersistState.mergeColumns), before translations load: a getter there bakes in the key.
  if (file.includes('/Store/')) {
    (headResult.getters || []).forEach((g) => problems.push(`${file}:${g.line}: getter in store state; use label: () => translate()`));
  }

  // Only these props accept a function (Table, TableHeaderCell, TableOptionsColumn(DragSource),
  // FilterMenuContent, FilterBuilderRow, Icon); anywhere else a function renders as nothing.
  (headResult.arrows || []).filter((a) => !['label', 'columnLabel', 'title'].includes(a.prop))
    .forEach((a) => problems.push(`${file}:${a.line}: ${a.prop}: () => translate() is read as a string there; use a getter`));

  const excused = counts((keep[file] || []).map((k) => k.text));
  (headResult.candidates || []).forEach((c) => {
    if (skipLiterals || excused.get('*') > 0) {
      return; // not checked here, or the whole file stays English (keep.json says why)
    }

    if (excused.get(c.text) > 0) {
      excused.set(c.text, excused.get(c.text) - 1);
    } else {
      problems.push(`${file}:${c.line}: hard-coded UI text not in keep.json: ${JSON.stringify(c.text)}`);
    }
  });

  return problems;
}

// The equivalences.json entries a run approves: one task's, or every task's when task is null
// (--all-tasks), plus the en.json keys those entries allow to change.
function selectEquivalences(all, task) {
  const equivalences = task === null ? all : all.filter((e) => e.task === task);
  const approvedEn = new Set(equivalences.filter((e) => e.enKey).map((e) => e.enKey));

  return { equivalences, approvedEn };
}

if (require.main === module) {
  // A newline-joined list arrives as one argument when a shell does not split (zsh): split it here.
  const args = process.argv.slice(2).flatMap((a) => a.split('\n')).filter((a) => a !== '');
  const skipLiterals = args.includes('--skip-literals');
  const rest = args.filter((a) => a !== '--skip-literals');
  const allTasks = rest[0] === '--all-tasks';
  const [flag, task, ref, ...files] = allTasks ? [rest[0], null, ...rest.slice(1)] : rest;

  if (!(allTasks || (flag === '--task' && task)) || !ref || !files.length) {
    console.error('usage: check-routing.js --task <tag> | --all-tasks [--skip-literals] <base-ref> <file>...');
    process.exit(2);
  }

  const enBase = JSON.parse(atRef(ref, EN));
  const enHead = JSON.parse(fs.readFileSync(path.join(REPO, EN), 'utf8'));
  const { equivalences, approvedEn } =
    selectEquivalences(JSON.parse(fs.readFileSync(path.join(__dirname, 'equivalences.json'), 'utf8')), task);
  const keep = JSON.parse(fs.readFileSync(path.join(__dirname, 'keep.json'), 'utf8'));
  const problems = [];

  Object.keys(enBase).forEach((k) => {
    if (enHead[k] !== enBase[k] && !approvedEn.has(k)) {
      problems.push(`en.json: existing key ${k} changed or removed`);
    }
  });

  files.forEach((file) => {
    const baseSource = atRef(ref, file);
    const headSource = fs.existsSync(path.join(REPO, file)) ? fs.readFileSync(path.join(REPO, file), 'utf8') : null;

    problems.push(...checkFile(file, baseSource, headSource, enBase, enHead, equivalences, keep, skipLiterals));
  });

  git('ls-files', 'frontend/src').split('\n').filter((f) => /\.(js|jsx|ts|tsx)$/.test(f) && !/\.d\.ts$/.test(f)).forEach((file) => {
    extract(fs.readFileSync(path.join(REPO, file), 'utf8'), enHead).keys.forEach((k) => {
      if (!Object.prototype.hasOwnProperty.call(enHead, k.key)) {
        problems.push(`${file}:${k.line}: translate key missing from en.json: ${k.key}`);
      }
    });
  });

  problems.forEach((p) => console.log(p));
  console.log(problems.length ? `routing check: ${problems.length} problem(s)` : 'routing check: OK');
  process.exit(problems.length ? 1 : 0);
}

module.exports = { checkFile, selectEquivalences };
