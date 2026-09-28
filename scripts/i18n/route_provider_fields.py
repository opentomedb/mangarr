#!/usr/bin/env python3
"""UI translations v1 (2026-09-25): routes provider field labels and help text through en.json.

  python3 scripts/i18n/route_provider_fields.py <new-keys.json>

SchemaBuilder already passes every [FieldDefinition] Label/HelpText/HelpTextWarning through
GetLocalizedString, so a literal only translates when it happens to be a key. This rewrites each
literal that is not a key (case-insensitively, as the lookup is) into one, and writes the new keys
with the old literal as their English value for `en_keys.py add`. Key choice, in order:
  1. an existing en key whose value is exactly the literal (never a *Placeholder key: a placeholder
     is not a field label, whatever its English);
  2. a key this run already made for the same literal;
  3. Label: the literal's words in PascalCase ("Watch Folder" -> WatchFolder);
     HelpText/HelpTextWarning: <Owner><Property><Kind> (TorrentBlackholeWatchFolderHelpText),
     Owner = the class name without a trailing "Settings"/"Specification";
  4. if that name is taken in any letter case: <Owner><Property><Kind> for a label too, then a
     numeric suffix.
A literal counts as a key only if en.json had it before this run: one that matches a key this run
made in another letter case ("URL" after "Url") would render that key's English, so it gets its own.
Never edits a FieldToken (none reference a literal label today; the script stops if one does).
"""
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
EN = REPO / 'src/NzbDrone.Core/Localization/Core/en.json'
ATTR = re.compile(r'\[FieldDefinition\(.*\)\]')
ARG = re.compile(r'\b(Label|HelpText|HelpTextWarning)\s*=\s*"((?:[^"\\]|\\.)*)"')
PROP = re.compile(r'public\s+(?:override\s+|virtual\s+|new\s+)*[\w<>\[\],?. ]+?\s+(\w+)\s*(?:\{|=>|$)')
CLASS = re.compile(r'\bclass\s+(\w+)')


def pascal(text):
    words = re.findall(r'[A-Za-z0-9]+', text)
    return ''.join(w[:1].upper() + w[1:] for w in words)


def main(out_path):
    en = json.loads(EN.read_text(encoding='utf-8'))
    taken = {k.lower() for k in en}
    existing = set(taken)
    by_value = {}
    for k, v in en.items():
        if not k.endswith('Placeholder'):
            by_value.setdefault(v, k)
    made = {}
    new_keys = {}
    files = subprocess.check_output(['git', 'ls-files', 'src/NzbDrone.Core/*.cs'], cwd=REPO, text=True).split()
    changed = 0
    for rel in files:
        path = REPO / rel
        text = path.read_text(encoding='utf-8-sig')
        if '[FieldDefinition(' not in text:
            continue
        for label in re.findall(r'\[FieldToken\(\s*TokenField\.\w+\s*,\s*"([^"]*)"', text):
            if label.lower() not in taken:
                sys.exit(f'{rel}: FieldToken label "{label}" is a literal; route it by hand')
        lines = text.split('\n')
        owner = None
        for i, line in enumerate(lines):
            m = CLASS.search(line)
            if m:
                owner = re.sub(r'(Settings|Specification)$', '', m.group(1))
            if not ATTR.search(line):
                continue
            prop = next((PROP.search(l).group(1) for l in lines[i + 1:i + 6] if PROP.search(l)), None)
            if prop is None:
                sys.exit(f'{rel}:{i + 1}: no property after the attribute')

            def swap(m):
                kind, raw = m.group(1), m.group(2)
                literal = json.loads('"' + raw + '"')
                if literal.lower() in existing:
                    return m.group(0)  # already a key: GetLocalizedString finds it today
                key = by_value.get(literal) or made.get(literal)
                if key is None:
                    suffix = '' if kind == 'Label' else kind
                    candidates = ([pascal(literal)] if kind == 'Label' else []) + [f'{owner}{prop}{suffix}']
                    key = next((c for c in candidates if c and c.lower() not in taken), None)
                    n = 2
                    while key is None:
                        key = f'{owner}{prop}{suffix}{n}' if f'{owner}{prop}{suffix}{n}'.lower() not in taken else None
                        n += 1
                    taken.add(key.lower())
                    made[literal] = key
                    new_keys[key] = literal
                return f'{kind} = "{key}"'

            new_line = ARG.sub(swap, line)
            if new_line != line:
                lines[i] = new_line
                changed += 1
        new_text = '\n'.join(lines)
        if new_text != text:
            bom = path.read_bytes().startswith(b'\xef\xbb\xbf')
            path.write_bytes((b'\xef\xbb\xbf' if bom else b'') + new_text.encode('utf-8'))
    Path(out_path).write_text(json.dumps(new_keys, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f'{changed} attribute line(s) rewritten, {len(new_keys)} new key(s) -> {out_path}')


if __name__ == '__main__':
    main(sys.argv[1])
