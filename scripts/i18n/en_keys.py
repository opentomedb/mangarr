#!/usr/bin/env python3
"""UI translations v1 (2026-09-25): en.json edits without re-serializing the file.

  python3 scripts/i18n/en_keys.py add <new-keys.json>
      Inserts each {"Key": "value"} as one line at its ordinal-sorted position (en.json is sorted
      by ordinal key order, 2-space indent, UTF-8 unescaped, trailing newline). Refuses a key that
      exists, even in another letter case: the backend lookup ignores case, so a case twin would
      silently replace the other key's value. Refuses an empty value and a positional {0}.
  python3 scripts/i18n/en_keys.py verify <base-ref> [Key,Key...]
      en.json keeps its format and order, has no empty value and no case twins, and every key at
      <base-ref> is still there with the same value -- except the listed keys (the typo fixes).
"""
import bisect
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
EN = REPO / 'src/NzbDrone.Core/Localization/Core/en.json'


def line(key, value):
    return '  ' + json.dumps(key, ensure_ascii=False) + ': ' + json.dumps(value, ensure_ascii=False)


def add(new_path):
    new = json.loads(Path(new_path).read_text(encoding='utf-8'))
    lines = EN.read_text(encoding='utf-8').split('\n')  # '{', one line per key, '}', ''
    keys = [json.loads('{' + l.rstrip(',') + '}').popitem()[0] for l in lines[1:-2]]
    lower = {k.lower(): k for k in keys}
    for key, value in sorted(new.items()):
        if key.lower() in lower:
            sys.exit(f'{key}: already in en.json as {lower[key.lower()]}')
        if not value:
            sys.exit(f'{key}: empty value')
        if re.search(r'\{\d+\}', value):
            sys.exit(f'{key}: positional token; name it ({{count}}, not {{0}}): translate() maps {{0}} to the first token')
        at = bisect.bisect(keys, key)  # Python's str order is ordinal, the order en.json has
        keys.insert(at, key)
        lower[key.lower()] = key
        if at == len(keys) - 1:  # new last entry: the old last line gains its comma
            lines[at] = lines[at] + ','
            lines.insert(at + 1, line(key, value))
        else:
            lines.insert(at + 1, line(key, value) + ',')
    EN.write_text('\n'.join(lines), encoding='utf-8')
    print(f'added {len(new)} key(s)')


def verify(ref, allowed):
    raw = EN.read_text(encoding='utf-8')
    head = json.loads(raw)
    base = json.loads(subprocess.check_output(['git', 'show', f'{ref}:src/NzbDrone.Core/Localization/Core/en.json'], cwd=REPO, text=True))
    problems = []
    if raw != json.dumps(head, ensure_ascii=False, indent=2) + '\n':
        problems.append('format: en.json is not 2-space, unescaped UTF-8 with a trailing newline')
    if list(head) != sorted(head):
        problems.append('order: en.json keys are not in ordinal order')
    seen = {}
    for k, v in head.items():
        if k.lower() in seen:
            problems.append(f'case twins: {seen[k.lower()]} / {k}')
        seen[k.lower()] = k
        if not v:
            problems.append(f'empty value: {k}')
    for k, v in base.items():
        if k in allowed:
            continue
        if head.get(k) != v:
            problems.append(f'changed or removed: {k}')
    for p in problems:
        print(p)
    print(f'en.json: {len(problems)} problem(s)' if problems else 'en.json: OK')
    sys.exit(1 if problems else 0)


if __name__ == '__main__':
    if sys.argv[1:2] == ['add']:
        add(sys.argv[2])
    elif sys.argv[1:2] == ['verify']:
        verify(sys.argv[2], set(sys.argv[3].split(',')) if len(sys.argv) > 3 else set())
    else:
        sys.exit(__doc__)
