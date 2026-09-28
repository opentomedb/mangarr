#!/usr/bin/env python3
"""UI translations v1 (2026-09-25): draft, apply and check one locale file against en.json.

  python3 scripts/i18n/locale_tool.py sheet <lang>   writes docs/i18n/<lang>-review.csv (reads it first:
      a stale key whose value is that sheet's draft for today's English is not listed again, so copy
      the reviewed sheet aside -- cp, not mv -- and append the new rows back to it after apply)
  python3 scripts/i18n/locale_tool.py apply <lang>   writes every sheet draft into <lang>.json
  python3 scripts/i18n/locale_tool.py check <lang>   the acceptance checks; exit 1 on a problem

The sheet has one row per key that needs a draft: missing (no value yet), stale (the English
changed after the Readarr fork 0b79d30, so the value translates old English), vocab (a word the
glossary forbids: livre/auteur/Buch/Autor/映画 ...), readarr (the value says Readarr and the
English does not), tokens (placeholder set differs from en), glossary (a Mangarr term rendered
off-glossary, or a key whose whole value the glossary fixes). Columns: key, reasons, en_fork, en,
owner, old, suggest (this file's value for the same English under another, unflagged key), draft, note.
owner lists the provider settings classes whose [FieldDefinition] Label/HelpText/HelpTextWarning
uses the key: generic names (Key, Retry, Event, DelugeMusicCategoryHelpText) mean what their
provider means, so a draft follows the owner and the English, never the key's name.
Drafts are written from the CURRENT English (column en), never by editing the old value.
Locale files keep their own format: 4-space indent, unescaped UTF-8, existing key order, new keys
appended in en.json order, trailing newline.
"""
import csv
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CORE = REPO / 'src/NzbDrone.Core/Localization/Core'
FORK = '0b79d30'
TOKEN = re.compile(r'\{([a-z0-9]+?)\}', re.I)
GLOSSARY = json.loads((Path(__file__).parent / 'glossary.json').read_text(encoding='utf-8'))
COLUMNS = ['key', 'reasons', 'en_fork', 'en', 'owner', 'old', 'suggest', 'draft', 'note']
FIELD_ARG = re.compile(r'\b(?:Label|HelpText|HelpTextWarning)\s*=\s*"((?:[^"\\]|\\.)*)"')
CLASS = re.compile(r'\bclass\s+(\w+)')


def load(name):
    return json.loads((CORE / f'{name}.json').read_text(encoding='utf-8'))


def owners():
    """key (lower case, as the SchemaBuilder lookup ignores case) -> provider settings classes using it."""
    found = {}
    files = subprocess.check_output(['git', 'ls-files', 'src/NzbDrone.Core/*.cs'], cwd=REPO, text=True).split()
    for rel in files:
        text = (REPO / rel).read_text(encoding='utf-8-sig')
        if '[FieldDefinition(' not in text:
            continue
        owner = Path(rel).stem
        for line in text.splitlines():
            m = CLASS.search(line)
            if m:
                owner = m.group(1)
            if '[FieldDefinition(' in line:
                for key in FIELD_ARG.findall(line):
                    found.setdefault(key.lower(), set()).add(re.sub(r'(Settings|Specification)$', '', owner))
    return {k: ' '.join(sorted(v)) for k, v in found.items()}


def tokens(value):
    # Case-sensitive, as both ends look tokens up; appName is always supplied, so a translation
    # may say {appName} where the English says Mangarr, and the other way round.
    return sorted(set(TOKEN.findall(value)) - {'appName'})


def excepted(key):
    return any(re.search(pattern, key) for pattern in GLOSSARY['exceptions'])


def problems_for(lang, key, en_value, value):
    g = GLOSSARY[lang]
    if value is None:
        return ['missing']
    out = []
    if value == '':
        out.append('empty')
    if tokens(value) != tokens(en_value):
        out.append('tokens')
    if 'Readarr' in value and 'Readarr' not in en_value:
        out.append('readarr')
    if key in g['exact'] and value != g['exact'][key]:
        out.append('glossary:exact')
    if excepted(key):
        return out
    # Placeholders are code ({bookCount}, {volume}): never vocabulary.
    en_text, text = TOKEN.sub('', en_value), TOKEN.sub('', value)
    if any(re.search(f, text, re.I) for f in g['forbidden']):
        out.append('vocab')
    for term in g['terms']:
        flags = 0 if term.get('case') else re.I
        if re.search(term['en'], en_text, flags) and not re.search(term['target'], text, re.I):
            out.append(f"glossary:{term['name']}")
    return out


def reviewed(path):
    """key -> (en, draft) of the sheet a sheet run is about to overwrite (last row wins, as in apply)."""
    if not path.exists():
        return {}
    with path.open(encoding='utf-8', newline='') as f:
        return {r['key']: (r['en'], r['draft']) for r in csv.DictReader(f)}


def sheet(lang):
    en, loc = load('en'), load(lang)
    fork = json.loads(subprocess.check_output(['git', 'show', f'{FORK}:src/NzbDrone.Core/Localization/Core/en.json'], cwd=REPO, text=True))
    owner = owners()
    path = REPO / 'docs/i18n' / f'{lang}-review.csv'
    # A stale key stays drafted once its value came from a sheet row written for today's English:
    # the fork's English never changes, so without this every sheet run re-lists it (final review).
    done = reviewed(path)
    flagged = {}
    for key, en_value in en.items():
        reasons = problems_for(lang, key, en_value, loc.get(key))
        if key in loc and key in fork and fork[key] != en_value and done.get(key) != (en_value, loc[key]):
            reasons.append('stale')
        if reasons:
            flagged[key] = reasons
    by_english = {}
    for key, value in loc.items():
        if key in en and key not in flagged:
            by_english.setdefault(en[key], value)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, lineterminator='\n')  # LF, as git stores it (* text=auto)
        w.writerow(COLUMNS)
        for key, reasons in flagged.items():
            w.writerow([key, ' '.join(reasons), fork.get(key, ''), en[key], owner.get(key.lower(), ''), loc.get(key, ''), by_english.get(en[key], ''), '', ''])
    print(f'{len(flagged)} row(s) -> {path.relative_to(REPO)}')


def apply(lang):
    en = load('en')
    path = CORE / f'{lang}.json'
    loc = json.loads(path.read_text(encoding='utf-8'))
    rows = list(csv.DictReader((REPO / 'docs/i18n' / f'{lang}-review.csv').open(encoding='utf-8', newline='')))
    blank = [r['key'] for r in rows if not r['draft'].strip()]
    if blank:
        sys.exit(f'{len(blank)} row(s) without a draft, first: {blank[:5]}')
    drafts = {r['key']: r['draft'] for r in rows}
    for key in loc:
        if key in drafts:
            loc[key] = drafts[key]
    for key in en:  # new keys go after the existing ones, in en.json order
        if key in drafts and key not in loc:
            loc[key] = drafts[key]
    path.write_text(json.dumps(loc, ensure_ascii=False, indent=4) + '\n', encoding='utf-8')
    print(f'applied {len(drafts)} draft(s) to {path.relative_to(REPO)}')


def check(lang):
    en, loc = load('en'), load(lang)
    found = []
    for key, en_value in en.items():
        for p in problems_for(lang, key, en_value, loc.get(key)):
            found.append(f'{key}: {p}')
    raw = (CORE / f'{lang}.json').read_text(encoding='utf-8')
    if raw != json.dumps(json.loads(raw), ensure_ascii=False, indent=4) + '\n':
        found.append('format: not 4-space, unescaped UTF-8 with a trailing newline')
    for p in found:
        print(p)
    print(f'{lang}: {len(found)} problem(s)' if found else f'{lang}: OK')
    sys.exit(1 if found else 0)


if __name__ == '__main__':
    {'sheet': sheet, 'apply': apply, 'check': check}[sys.argv[1]](sys.argv[2])
