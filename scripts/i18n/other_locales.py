#!/usr/bin/env python3
"""Server messages round, task 9 (2026-09-26): the 41 other locale files after the Mangarr rebrand.

  python3 scripts/i18n/other_locales.py report [<lang>...]   dry run: what apply would do, per locale
  python3 scripts/i18n/other_locales.py apply  [<lang>...]   rewrites the locale files

Reference English is upstream Readarr's en.json at REF (origin/develop, "Retirement announcement",
2025-06-27): the fork's root commit 96f7eb4b already carries the rebrand (175 en values differ from
upstream), so it cannot be the fork-point reference (spec section 5). For every locale except
en/fr/de/ja (and the empty files):
  changed  the key's English now means something else than upstream's (compared after folding
           Readarr/Mangarr/{appName}, the v1 typo fix, letter case and spacing): the value is removed, the key falls
           back to English.
  vocab    the value names an author, a book or a film (per-language patterns below) where the
           English says neither: removed. Catches the ~116 keys v1 reused whose English did not change.
  orphan   the key is no longer in en.json: removed (nothing renders it; it still says Libros).
  otherarr the value names a different *arr app (radarr/lidarr/sonarr, any case): removed, a stale
           seed from the app it was translated for before being reused for Readarr.
  readarr  a remaining value says Readarr (any case): {appName} when the English has {appName},
           else Mangarr, regardless of the matched case (fi "readarrin" -> "Mangarrin").
No new translation is written. Locale files keep their format: 4-space indent, unescaped UTF-8,
existing key order, trailing newline.

Fix round (2026-09-27, task 9 review): case-insensitive Readarr matching, the otherarr rule, and
four word-list additions (ko film 동영상, zh_CN/zh_TW film 影片, fi author esittäj) added on review;
sk UnableToLoadBackups ("albumy" = albums) and other placeholder-style debt are known and left alone.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
CORE = REPO / 'src/NzbDrone.Core/Localization/Core'
REF = '0b79d300'
SKIP = {'en', 'fr', 'de', 'ja'}
# The v1 typo fixes, as (upstream text, today's text): a key whose only change is one of these
# keeps its value.
TYPOS = [(' the the ', ' the ')]
# Words in the English that make an author/book word in the translation right.
EN_AUTHOR = re.compile(r'\b(authors?|writers?)\b', re.I)
EN_BOOK = re.compile(r'(books?|bookshelf|bookshelves)\b', re.I)
# Readarr appears in any case (fi "readarrin"): matched case-insensitively, always replaced with
# Mangarr/{appName} regardless of the matched case -- it is a proper noun, so the output case does
# not need to track the input's.
READARR = re.compile(r'readarr', re.I)
# A value naming a different *arr app (radarr/lidarr/sonarr) is a stale Radarr/Lidarr seed, never a
# rename target: removed outright so the key falls back to English (cs ReplaceIllegalCharactersHelpText
# still says "radarr will remove them instead").
OTHER_ARR = re.compile(r'radarr|lidarr|sonarr', re.I)
# Per language: author, book and film (Radarr leftovers) patterns, matched case-insensitively.
# Words that share a stem are excluded: fi kirjasto (library), it libreria, he מספר (number) and
# ספרייה (library), hu könyvtár (directory), vi danh sách (list), cs knihovna and el βιβλιοθήκη
# (library), is bókasafn (library), tr kitaplık (library), ar الكتابة (writing), zh 证书 (certificate)
# and every autor-/автор- that is authorization.
WORDS = {
    'ar': {'author': r'مؤلف', 'book': r'كتاب(?!ة)|الكتب', 'film': r'فيلم|أفلام'},
    'bg': {'author': r'автор', 'book': r'книг', 'film': r'филм'},
    'bn': {'author': r'লেখক', 'book': r'বই', 'film': r'চলচ্চিত্র|সিনেমা'},
    'ca': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'\bllibre', 'film': r'pel·lícul'},
    'cs': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'\bknih(?!ovn)', 'film': r'\bfilm'},
    'da': {'author': r'forfatter', 'book': r'\bbog\b|\bbøger|\bbogen', 'film': r'\bfilm'},
    'el': {'author': r'συγγραφ', 'book': r'βιβλ(?!ιοθ)', 'film': r'ταιν'},
    'es': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'\blibro', 'film': r'pel[ií]cula'},
    'fa': {'author': r'نویسنده', 'book': r'کتاب', 'film': r'فیلم'},
    'fi': {'author': r'kirjailij|esittäj', 'book': r'\bkirj(a|at|an|oja|ojen|oille|oista)\b', 'film': r'elokuv'},
    'he': {'author': r'מחבר|סופר', 'book': r'(?<!מ)ספר(?!י)|ספרים', 'film': r'סרט'},
    'hi': {'author': r'लेखक', 'book': r'पुस्तक|किताब', 'film': r'फ़िल्म|फिल्म|मूवी'},
    'hr': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'\bknjig', 'film': r'\bfilm'},
    'hu': {'author': r'szerző', 'book': r'könyv(?!tár)', 'film': r'\bfilm'},
    'id': {'author': r'penulis|pengarang', 'book': r'\bbuku', 'film': r'\bfilm'},
    'is': {'author': r'höfund', 'book': r'\bbók(?!asafn|un)|\bbæk', 'film': r'kvikmynd'},
    'it': {'author': r'\bautor[ei]\b', 'book': r'\blibr[oi]\b', 'film': r'\bfilm'},
    'ko': {'author': r'저자|저작자|작가', 'book': r'책|도서', 'film': r'영화|동영상'},
    'lv': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'grāmat', 'film': r'\bfilm'},
    'nb_NO': {'author': r'forfatter', 'book': r'\bbok\b|\bbøker|\bboken', 'film': r'\bfilm'},
    'nl': {'author': r'auteur|schrijver', 'book': r'\bboek', 'film': r'\bfilm'},
    'pl': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'książ', 'film': r'\bfilm'},
    'pt': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'\blivro', 'film': r'\bfilme'},
    'pt_BR': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'\blivro', 'film': r'\bfilme'},
    'ro': {'author': r'\bautor(?!i[sz]|yz)', 'book': r'\bc[aă]r[tț]', 'film': r'\bfilm'},
    'ru': {'author': r'автор(?!из)', 'book': r'книг', 'film': r'фильм'},
    'sk': {'author': r'\bautor(?!i[sz]|yz)|interpret', 'book': r'\bknih', 'film': r'\bfilm'},
    'sv': {'author': r'författar', 'book': r'\bbok\b|\bböcker|\bboken', 'film': r'\bfilm'},
    'th': {'author': r'ผู้แต่ง|ผู้เขียน', 'book': r'หนังสือ', 'film': r'ภาพยนตร์'},
    'tr': {'author': r'yazar', 'book': r'kitap(?!lı)', 'film': r'\bfilm'},
    'uk': {'author': r'автор(?!из)', 'book': r'книг', 'film': r'фільм'},
    'vi': {'author': r'tác giả', 'book': r'(?<!danh )\bsách', 'film': r'\bphim'},
    'zh_CN': {'author': r'作者', 'book': r'(?<!证)书', 'film': r'电影|影片'},
    'zh_Hans': {'author': r'作者', 'book': r'(?<!证)书', 'film': r'电影'},
    'zh_TW': {'author': r'作者', 'book': r'(?<!證)書', 'film': r'電影|影片'},
}


def load_ref():
    return json.loads(subprocess.check_output(['git', 'show', f'{REF}:src/NzbDrone.Core/Localization/Core/en.json'], cwd=REPO, text=True))


def fold(text):
    text = text.replace('{appName}', 'Readarr').replace('Mangarr', 'Readarr')
    for old, new in TYPOS:
        text = text.replace(old, new)
    # Letter case and spacing never change a meaning (Title Case passes, trailing spaces).
    return ' '.join(text.split()).casefold()


def plan(lang, en, ref):
    loc = json.loads((CORE / f'{lang}.json').read_text(encoding='utf-8'))
    words = {k: re.compile(v, re.I) for k, v in WORDS.get(lang, {}).items()}
    removed, renamed = {}, {}
    for key, value in loc.items():
        english = en.get(key)
        if english is None:
            removed[key] = 'orphan'  # en.json dropped the key (BooksTotal, TooManyBooks): nothing renders it
            continue
        if key in ref and fold(ref[key]) != fold(english):
            removed[key] = 'changed'
            continue
        hits = [w for w, rx in words.items() if rx.search(value)]
        hits = [w for w in hits if w == 'film' or not (EN_AUTHOR if w == 'author' else EN_BOOK).search(english)]
        if hits:
            removed[key] = 'vocab:' + '+'.join(hits)
            continue
        if OTHER_ARR.search(value):
            removed[key] = 'otherarr'
            continue
        if READARR.search(value):
            renamed[key] = READARR.sub('{appName}' if '{appName}' in english else 'Mangarr', value)
    return loc, removed, renamed


def main(mode, langs):
    en = json.loads((CORE / 'en.json').read_text(encoding='utf-8'))
    ref = load_ref()
    langs = langs or sorted(p.stem for p in CORE.glob('*.json') if p.stem not in SKIP)
    total = [0, 0, 0]
    for lang in langs:
        loc, removed, renamed = plan(lang, en, ref)
        kinds = {k: sum(1 for r in removed.values() if r.split(':')[0] == k) for k in ('changed', 'vocab', 'orphan', 'otherarr')}
        print(f"{lang}: {len(loc)} values, remove {len(removed)} (changed {kinds['changed']}, vocab {kinds['vocab']}, orphan {kinds['orphan']}, otherarr {kinds['otherarr']}), Readarr -> {len(renamed)}")
        total = [total[0] + len(loc), total[1] + len(removed), total[2] + len(renamed)]
        if mode == 'report' and len(langs) == 1:
            for key, why in removed.items():
                print(f'  - {key} [{why}]: {loc[key]}')
            for key, value in renamed.items():
                print(f'  ~ {key}: {value}')
        if mode == 'apply' and (removed or renamed):
            out = {k: renamed.get(k, v) for k, v in loc.items() if k not in removed}
            (CORE / f'{lang}.json').write_text(json.dumps(out, ensure_ascii=False, indent=4) + '\n', encoding='utf-8')
    print(f'total: {total[0]} values, remove {total[1]}, Readarr -> {total[2]}')


if __name__ == '__main__':
    if sys.argv[1:2] not in (['report'], ['apply']):
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2:])
