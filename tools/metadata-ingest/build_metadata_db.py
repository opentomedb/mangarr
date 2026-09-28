#!/usr/bin/env python3
"""Build the Mangarr bundled metadata SQLite from GCD data.

OFFLINE build-tooling only (not shipped in the runtime container). The runtime is pure C#; this
just produces the read-only `manga-metadata.sqlite` artifact the C# service reads.

Two modes:
  --synthetic            build a tiny known dataset (no external input) — proves the pipeline + schema.
  --dump <path>          build from a real GCD bulk dump  [stub: finalized when the gated dump is in hand].

The GCD dump itself is a manual, Cloudflare+login-gated, bi-weekly download — done once, centrally,
so no Mangarr instance ever touches the gate. See README.md.
"""
import argparse
import datetime
import json
import os
import re
import sqlite3

SCHEMA_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "schema.sql")


def parse_omnibus_composition(descriptor):
    """Parse a GCD omnibus volume descriptor into the list of original tankoubon volumes it contains.

    GCD stores these as freeform text on the omnibus issue, e.g. '1 (1-2-3)', '24 (70-71-72)', '1-3'.
    The number BEFORE the parens is the omnibus volume number (handled by the caller); the composition
    is what's inside (or the whole token if there are no parens). Heuristic:
      - exactly two numbers 'a-b' with a<b  -> inclusive range [a..b]   (e.g. '1-3' -> [1,2,3])
      - otherwise the explicit list of numbers                          (e.g. '1-2-3' -> [1,2,3])
    Returns [] when nothing parses. This is a documented heuristic over freeform data, not a guarantee.
    """
    if not descriptor:
        return []
    m = re.search(r"\(([^)]*)\)", descriptor)
    body = m.group(1) if m else descriptor
    tokens = [t for t in re.split(r"[^\d]+", body) if t]
    ints = [int(t) for t in tokens]
    if not ints:
        return []
    if len(ints) == 2 and ints[0] < ints[1]:
        return list(range(ints[0], ints[1] + 1))
    return ints


def build_db(records, out_path):
    """Create the SQLite at out_path from a normalized records dict:
        {"series": [ {...} ], "volumes": [ {...} ], "aliases": [ {gcd_series_id, alias} ], "meta": {k: v}}
    """
    if os.path.exists(out_path):
        os.remove(out_path)
    with open(SCHEMA_PATH, "r", encoding="utf-8") as f:
        schema = f.read()

    conn = sqlite3.connect(out_path)
    try:
        conn.executescript(schema)

        conn.executemany(
            """INSERT INTO series
               (gcd_series_id, name, year_began, publisher, language, country, is_omnibus,
                volume_count, status, orig_series_id, anilist_id, mangaupdates_id, mangadex_id)
               VALUES (:gcd_series_id, :name, :year_began, :publisher, :language, :country,
                       :is_omnibus, :volume_count, :status, :orig_series_id, :anilist_id,
                       :mangaupdates_id, :mangadex_id)""",
            [_series_row(s) for s in records.get("series", [])],
        )

        conn.executemany(
            """INSERT INTO volumes
               (gcd_series_id, volume_number, title, release_date, isbn13, isbn10, page_count, composition)
               VALUES (:gcd_series_id, :volume_number, :title, :release_date, :isbn13, :isbn10,
                       :page_count, :composition)""",
            [_volume_row(v) for v in records.get("volumes", [])],
        )

        conn.executemany(
            "INSERT OR IGNORE INTO series_alias (gcd_series_id, alias) VALUES (:gcd_series_id, :alias)",
            records.get("aliases", []),
        )

        conn.executemany(
            "INSERT OR REPLACE INTO meta (key, value) VALUES (?, ?)",
            list(records.get("meta", {}).items()),
        )
        conn.commit()
    finally:
        conn.close()
    return out_path


def _series_row(s):
    row = {k: None for k in (
        "gcd_series_id", "name", "year_began", "publisher", "language", "country",
        "is_omnibus", "volume_count", "status", "orig_series_id", "anilist_id",
        "mangaupdates_id", "mangadex_id")}
    row.update(s)
    row["is_omnibus"] = 1 if row.get("is_omnibus") else 0
    row["volume_count"] = int(row.get("volume_count") or 0)
    return row


def _volume_row(v):
    row = {k: None for k in (
        "gcd_series_id", "volume_number", "title", "release_date", "isbn13", "isbn10",
        "page_count", "composition")}
    row.update(v)
    comp = row.get("composition")
    if isinstance(comp, (list, tuple)):
        row["composition"] = json.dumps(list(comp)) if comp else None
    return row


def synthetic_records():
    """A tiny known dataset: Naruto English singles (72), Naruto 3-in-1 omnibus (24), Chainsaw Man (24).

    Naruto 3-in-1 vol k bundles original vols (3k-2, 3k-1, 3k) -> exercises the omnibus mapping.
    """
    series = [
        {"gcd_series_id": 25323, "name": "Naruto", "year_began": 2003, "publisher": "Viz",
         "language": "en", "country": "us", "is_omnibus": 0, "volume_count": 72,
         "status": "completed", "anilist_id": 30011, "mangaupdates_id": 17360452316},
        {"gcd_series_id": 76512, "name": "Naruto (3-in-1 Edition)", "year_began": 2011,
         "publisher": "Viz", "language": "en", "country": "us", "is_omnibus": 1,
         "volume_count": 24, "status": "completed", "orig_series_id": 25323},
        {"gcd_series_id": 154385, "name": "Chainsaw Man", "year_began": 2020, "publisher": "Viz",
         "language": "en", "country": "us", "is_omnibus": 0, "volume_count": 24,
         "status": "completed", "anilist_id": 105778},
    ]
    volumes = []
    for k in range(1, 73):
        volumes.append({"gcd_series_id": 25323, "volume_number": k, "title": f"Naruto, Vol. {k}"})
    for k in range(1, 25):
        comp = [3 * k - 2, 3 * k - 1, 3 * k]
        volumes.append({"gcd_series_id": 76512, "volume_number": k,
                        "title": f"Naruto (3-in-1 Edition), Vol. {k}", "composition": comp,
                        "release_date": "2011-05-03" if k == 1 else None})
    for k in range(1, 25):
        v = {"gcd_series_id": 154385, "volume_number": k, "title": f"Chainsaw Man, Vol. {k}"}
        if k == 1:
            v.update({"release_date": "2020-10-06", "isbn13": "9781974709939", "page_count": 192})
        volumes.append(v)
    aliases = [
        {"gcd_series_id": 25323, "alias": "Naruto"},
        {"gcd_series_id": 154385, "alias": "Chainsaw Man"},
        {"gcd_series_id": 76512, "alias": "Naruto 3-in-1"},
    ]
    meta = {
        "schema_version": "1",
        "source": "synthetic",
        "license": "CC BY-SA 4.0 (derived from the Grand Comics Database)",
        "attribution": "Data derived from the Grand Comics Database (https://www.comics.org), CC BY-SA 4.0.",
    }
    return {"series": series, "volumes": volumes, "aliases": aliases, "meta": meta}


# ---------------------------------------------------------------------------
# GCD MySQL-dump streaming parser
# ---------------------------------------------------------------------------
# The dump is a single ~3.7GB mysqldump (extended INSERTs, one statement per line).
# We stream it line by line and parse only the tables we need, mapping positional
# values by the column order from the CREATE TABLE statements.

# 0-indexed column positions (from the dump's CREATE TABLE statements).
SERIES_COLS = {"id": 0, "name": 1, "year_began": 4, "year_ended": 6, "is_current": 11,
               "publisher_id": 12, "country_id": 13, "language_id": 14, "issue_count": 18,
               "deleted": 21, "publishing_format": 32}
ISSUE_COLS = {"id": 0, "number": 1, "volume": 2, "series_id": 5, "publication_date": 10,
              "key_date": 11, "sort_code": 12, "page_count": 14, "deleted": 23, "isbn": 25,
              "valid_isbn": 26, "variant_of_id": 28, "title": 32, "on_sale_date": 34, "notes": 20}
PUBLISHER_COLS = {"id": 0, "name": 1}


def _unescape(s):
    if "\\" not in s:
        return s
    out = []
    i = 0
    while i < len(s):
        c = s[i]
        if c == "\\" and i + 1 < len(s):
            nxt = s[i + 1]
            out.append({"n": "\n", "r": "\r", "t": "\t", "0": "\0", "\\": "\\",
                        "'": "'", '"': '"'}.get(nxt, nxt))
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def _to_py(raw):
    raw = raw.strip()
    if raw == "NULL":
        return None
    if len(raw) >= 2 and raw[0] == "'" and raw[-1] == "'":
        return _unescape(raw[1:-1])
    return raw


_TOKEN_RE = re.compile(r"'(?:[^'\\]|\\.)*'|[(),]|[^'(),]+", re.DOTALL)


def iter_table_rows(dump_path, tables):
    """Yield (table, [python values]) for each row of the requested tables, streaming.

    Regex tokenizer: a quoted string (honoring backslash escapes) is matched as a single token,
    so commas/parens inside string fields are never mistaken for structure. Much faster than the
    char-by-char scanner over millions of issue rows.
    """
    prefixes = {t: "INSERT INTO `%s` VALUES " % t for t in tables}
    with open(dump_path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            table = None
            for t, prefix in prefixes.items():
                if line.startswith(prefix):
                    table = t
                    s = line[len(prefix):]
                    break
            if table is None:
                continue

            depth = 0
            field = []
            row = []
            for m in _TOKEN_RE.finditer(s):
                tok = m.group()
                if tok == "(":
                    if depth == 0:
                        row = []
                        field = []
                    else:
                        field.append(tok)
                    depth += 1
                elif tok == ")":
                    depth -= 1
                    if depth == 0:
                        row.append("".join(field))
                        field = []
                        yield table, [_to_py(x) for x in row]
                    else:
                        field.append(tok)
                elif tok == "," and depth == 1:
                    row.append("".join(field))
                    field = []
                else:
                    field.append(tok)


COMPOSITION_RE = re.compile(r"\(([0-9]+(?:\s*-\s*[0-9]+)+)\)")
LEADING_INT_RE = re.compile(r"\d+")


def extract_composition(*texts):
    """Find an omnibus composition like '(1-2-3)' or '(70-71-72)' in any of the given texts."""
    for t in texts:
        if not t:
            continue
        m = COMPOSITION_RE.search(t)
        if m:
            return parse_omnibus_composition(m.group(0))
    return None


def cmd_inspect(dump_path, substr):
    """Print sample series + issues whose name contains substr (case-insensitive), to learn shapes."""
    subs = [x.strip().lower() for x in substr.split(",") if x.strip()]
    languages = {}
    publishers = {}
    series = {}
    want_series = set()
    issues = {}

    for table, row in iter_table_rows(dump_path, ("stddata_language", "gcd_publisher", "gcd_series", "gcd_issue")):
        if table == "stddata_language":
            if any(isinstance(v, str) and v.lower() == "english" for v in row):
                languages[row[0]] = row
        elif table == "gcd_publisher":
            publishers[row[PUBLISHER_COLS["id"]]] = row[PUBLISHER_COLS["name"]]
        elif table == "gcd_series":
            nm = (row[SERIES_COLS["name"]] or "").lower()
            if row[SERIES_COLS["deleted"]] in ("0", 0, None) and any(sb in nm for sb in subs):
                series[row[SERIES_COLS["id"]]] = row
                want_series.add(row[SERIES_COLS["id"]])
        elif table == "gcd_issue":
            if row[ISSUE_COLS["series_id"]] in want_series:
                issues.setdefault(row[ISSUE_COLS["series_id"]], []).append(row)

    print("English language rows (id, ...):")
    for lid, lrow in languages.items():
        print("  ", lrow[:4])
    print()
    for sid, srow in sorted(series.items(), key=lambda kv: (srow_name(kv[1]))):
        pub = publishers.get(srow[SERIES_COLS["publisher_id"]], "?")
        print("SERIES id=%s name=%r year=%s ended=%s is_current=%s lang=%s pub=%r fmt=%r issue_count=%s" % (
            sid, srow[SERIES_COLS["name"]], srow[SERIES_COLS["year_began"]], srow[SERIES_COLS["year_ended"]],
            srow[SERIES_COLS["is_current"]], srow[SERIES_COLS["language_id"]], pub,
            srow[SERIES_COLS["publishing_format"]], srow[SERIES_COLS["issue_count"]]))
        for irow in sorted(issues.get(sid, []), key=lambda r: int(r[ISSUE_COLS["sort_code"]] or 0))[:6]:
            comp = extract_composition(irow[ISSUE_COLS["number"]], irow[ISSUE_COLS["title"]], irow[ISSUE_COLS["notes"]])
            print("    ISSUE number=%r vol=%r key_date=%r on_sale=%r pages=%r isbn=%r valid=%r variant_of=%s title=%r comp=%s" % (
                irow[ISSUE_COLS["number"]], irow[ISSUE_COLS["volume"]], irow[ISSUE_COLS["key_date"]],
                irow[ISSUE_COLS["on_sale_date"]], irow[ISSUE_COLS["page_count"]], irow[ISSUE_COLS["isbn"]],
                irow[ISSUE_COLS["valid_isbn"]], irow[ISSUE_COLS["variant_of_id"]], irow[ISSUE_COLS["title"]], comp))
        print("    (%d issues total)" % len(issues.get(sid, [])))
        print()


def srow_name(srow):
    return (srow[SERIES_COLS["name"]] or "").lower()


ENGLISH_LANG_ID = "25"  # stddata_language: (25,'en','English') — verified from the dump.

# English-manga-focused publishers (lowercase substring match on gcd_publisher.name). Series from
# these are included wholesale so future adds resolve; the user's library is additionally pinned
# by name via TARGET_SERIES below.
MANGA_PUBLISHER_KEYWORDS = (
    "viz", "kodansha", "yen press", "yen on", "seven seas", "ghost ship", "square enix manga",
    "square enix books", "vertical", "denpa", "j-novel", "one peace", "tokyopop", "del rey manga",
    "dark horse manga", "udon",
)

# The user's actual library (normalized match prefixes), so they're guaranteed in the artifact.
TARGET_SERIES = (
    "black clover", "chainsaw man", "dandadan", "demon slayer", "fire force", "frieren",
    "gyo", "horimiya", "jujutsu kaisen", "kaiju no 8", "my dress up darling", "my hero academia",
    "re zero", "solo leveling", "sword art online", "witch hat atelier",
)

# Manual bridges: a library series whose folder title is misspelled or arc-split won't match GCD by
# name, so map the exact folder-derived title -> its GCD series id (matched case-insensitively).
# Only emitted when that series is present in the build. Keep in sync with the live artifact.
BRIDGE_ALIASES = (
    {"gcd_series_id": 153402, "alias": "Jujustu Kaisen"},
    {"gcd_series_id": 169205, "alias": "Rezero Starting Life In Another World Chapter 4 The Sanctuary And The Witch Of Greed"},
    {"gcd_series_id": 185888, "alias": "Rezero Starting Life In Another World The Frozen Bond"},
)

# Publisher-verified first-English-release dates for volumes where GCD has no usable date
# and the runtime Google Books backfill matches a wrong edition. An artifact-supplied date
# suppresses the backfill, so these survive every refresh. Keyed (gcd_series_id, volume);
# applied after the outlier pass. Verified 2026-07-30.
DATE_OVERRIDES = {
    # Fairy Tail (66396) — full 63-volume first-EN-release list (Wikipedia, Del Rey 1-12 /
    # Kodansha 13+). GCD's line is mostly dateless and Google Books only carries the 2018
    # digital re-release batch (2018-02-09 stamped on whichever volume gets backfilled),
    # so per-volume patching never converges — the whole line is pinned instead.
    (66396, 1): "2008-03-25",
    (66396, 2): "2008-03-25",
    (66396, 3): "2008-06-24",
    (66396, 4): "2008-09-16",
    (66396, 5): "2009-01-27",
    (66396, 6): "2009-04-28",
    (66396, 7): "2009-07-07",
    (66396, 8): "2009-10-27",
    (66396, 9): "2009-12-29",
    (66396, 10): "2010-03-23",
    (66396, 11): "2010-06-22",
    (66396, 12): "2010-09-28",
    (66396, 13): "2011-05-10",
    (66396, 14): "2011-07-12",
    (66396, 15): "2011-09-27",
    (66396, 16): "2011-11-08",
    (66396, 17): "2012-01-24",
    (66396, 18): "2012-03-06",
    (66396, 19): "2012-05-29",
    (66396, 20): "2012-07-10",
    (66396, 21): "2012-09-25",
    (66396, 22): "2012-11-27",
    (66396, 23): "2013-01-29",
    (66396, 24): "2013-03-26",
    (66396, 25): "2013-04-23",
    (66396, 26): "2013-05-28",
    (66396, 27): "2013-06-25",
    (66396, 28): "2013-07-30",
    (66396, 29): "2013-08-27",
    (66396, 30): "2013-09-24",
    (66396, 31): "2013-10-29",
    (66396, 32): "2013-11-19",
    (66396, 33): "2013-12-03",
    (66396, 34): "2014-01-07",
    (66396, 35): "2014-02-25",
    (66396, 36): "2014-03-25",
    (66396, 37): "2014-04-15",
    (66396, 38): "2014-05-13",
    (66396, 39): "2014-06-04",
    (66396, 40): "2014-07-15",
    (66396, 41): "2014-08-12",
    (66396, 42): "2014-09-30",
    (66396, 43): "2014-10-28",
    (66396, 44): "2014-11-25",
    (66396, 45): "2014-12-30",
    (66396, 46): "2015-01-27",
    (66396, 47): "2015-03-31",
    (66396, 48): "2015-05-26",
    (66396, 49): "2015-07-28",
    (66396, 50): "2015-09-29",
    (66396, 51): "2015-11-24",
    (66396, 52): "2016-01-26",
    (66396, 53): "2016-04-12",
    (66396, 54): "2016-06-21",
    (66396, 55): "2016-08-09",
    (66396, 56): "2016-09-27",
    (66396, 57): "2016-11-29",
    (66396, 58): "2017-02-28",
    (66396, 59): "2017-03-28",
    (66396, 60): "2017-05-30",
    (66396, 61): "2017-07-25",
    (66396, 62): "2017-11-14",
    (66396, 63): "2018-01-23",
    (76094, 7): "2015-12-29",    # Vinland Saga Book 7 hardcover (GB matched the 2025 Deluxe)
    (118146, 1): "2017-08-22",   # Slime vol 1 (GB junk: 2017-12-19, after vol 2's real date)
    (212849, 6): "2025-04-01",   # Fragrant Flower 6 (GCD carried a 2026 second-print date)
    (212849, 7): "2025-06-03",   # Fragrant Flower 7 (GCD carried a 2026 second-print date)
    (212849, 8): "2025-08-05",   # Fragrant Flower 8 (GB junk: 2026-05-01 reprint)
}


def _norm(s):
    return re.sub(r"\s+", " ", re.sub(r"[^a-z0-9]+", " ", (s or "").lower())).strip()


def _matches_target(norm_name):
    return any(norm_name == t or norm_name.startswith(t + " ") for t in TARGET_SERIES)


def _is_manga_publisher(pub_name):
    p = (pub_name or "").lower()
    return any(k in p for k in MANGA_PUBLISHER_KEYWORDS)


def _int_or_none(v):
    if v is None:
        return None
    m = re.match(r"\s*(\d+)", str(v))
    return int(m.group(1)) if m else None


def _clean_date(v):
    # GCD key_date like '2020-10-06'; day may be '00' ('2018-01-00') -> month-only, None.
    # A '00' MONTH means year-only ('2015-00-00') — return None instead of fabricating Jan 1:
    # those sorted a volume ahead of its real neighbors, and a truthy fake here masked the
    # on_sale_date fallback, which often carries the real full date for these rows.
    if not v:
        return None
    m = re.match(r"(\d{4})-(\d{2})-(\d{2})", v)
    if not m:
        return None
    y, mo, d = m.groups()
    if y == "0000" or mo == "00":
        return None
    if d == "00":
        # Day '00' is month-only precision; fabricating day 1 produced dates the app
        # treats as day-precise (Tokyo Ghoul V1 stored Jun 1 vs real Jun 16). None lets
        # the on_sale_date fallback / downstream backfill supply the real full date.
        return None
    if mo == "01" and d == "01":
        # A Jan-1 "full" date is year-only precision entered as a real date (GCD holds
        # hundreds; no English manga volume releases on New Year's Day) — same treatment
        # as month '00': None, so the on_sale_date fallback / downstream backfill can fill.
        return None
    try:
        datetime.date(int(y), int(mo), int(d))
    except ValueError:
        return None
    return "%s-%s-%s" % (y, mo, d)


def _null_date_outliers(byvol):
    # GCD occasionally carries a flat-wrong per-volume date (e.g. Fire Force EN vol 29
    # stamped 2018-10-18 between neighbors both in 2022). Null a date that disagrees by
    # >365 days with BOTH flanking dated volumes when those two agree with each other,
    # so the honest gap can be backfilled downstream (ISBN/title lookup). Comparisons use
    # the original dates (single pass, no cascade).
    tol = datetime.timedelta(days=365)
    dated = []
    for vol in sorted(byvol):
        it = byvol[vol]
        if it["date"]:
            dated.append((vol, datetime.date.fromisoformat(it["date"])))
    for k in range(1, len(dated) - 1):
        vol, d = dated[k]
        d_prev, d_next = dated[k - 1][1], dated[k + 1][1]
        neighbors_agree = d_next >= d_prev - datetime.timedelta(days=90)
        if neighbors_agree and (d < d_prev - tol or d > d_next + tol):
            byvol[vol]["date"] = None


def build_from_dump(dump_path):
    publishers = {}
    series = {}
    target_ids = set()
    issues_by_series = {}
    n_issue = 0

    for table, row in iter_table_rows(dump_path, ("gcd_publisher", "gcd_series", "gcd_issue")):
        if table == "gcd_publisher":
            publishers[row[PUBLISHER_COLS["id"]]] = row[PUBLISHER_COLS["name"]]
        elif table == "gcd_series":
            if row[SERIES_COLS["deleted"]] not in ("0", 0, None):
                continue
            if row[SERIES_COLS["language_id"]] != ENGLISH_LANG_ID:
                continue
            name = row[SERIES_COLS["name"]] or ""
            norm = _norm(name)
            pub = publishers.get(row[SERIES_COLS["publisher_id"]], "")
            if not (_matches_target(norm) or _is_manga_publisher(pub)):
                continue
            sid = row[SERIES_COLS["id"]]
            series[sid] = {
                "gcd_series_id": int(sid), "name": name, "norm": norm,
                "year_began": _int_or_none(row[SERIES_COLS["year_began"]]),
                "is_current": row[SERIES_COLS["is_current"]], "publisher": pub,
            }
            target_ids.add(sid)
        elif table == "gcd_issue":
            n_issue += 1
            if n_issue % 500000 == 0:
                print("  ...scanned %d issues" % n_issue, flush=True)
            if row[ISSUE_COLS["series_id"]] not in target_ids:
                continue
            if row[ISSUE_COLS["deleted"]] not in ("0", 0, None):
                continue
            if row[ISSUE_COLS["variant_of_id"]] is not None:
                continue  # skip variant covers — not separate volumes
            vol = _int_or_none(row[ISSUE_COLS["number"]]) or _int_or_none(row[ISSUE_COLS["volume"]])
            if vol is None:
                continue
            issues_by_series.setdefault(row[ISSUE_COLS["series_id"]], []).append({
                "vol": vol,
                "sort": _int_or_none(row[ISSUE_COLS["sort_code"]]) or 0,
                "date": _clean_date(row[ISSUE_COLS["key_date"]]) or _clean_date(row[ISSUE_COLS["on_sale_date"]]),
                "isbn13": row[ISSUE_COLS["valid_isbn"]] or None,
                "pages": _int_or_none(row[ISSUE_COLS["page_count"]]),
                "comp": extract_composition(row[ISSUE_COLS["number"]], row[ISSUE_COLS["title"]], row[ISSUE_COLS["notes"]]),
            })

    out_series, out_volumes, out_aliases = [], [], []
    for sid, s in series.items():
        byvol = {}
        for it in sorted(issues_by_series.get(sid, []), key=lambda x: x["sort"]):
            byvol.setdefault(it["vol"], it)  # dedup by volume number, keep lowest sort_code
        if not byvol:
            continue  # no usable numbered volumes -> drop (e.g. a one-shot with '[nn]')
        _null_date_outliers(byvol)
        for (osid, ovol), odate in DATE_OVERRIDES.items():
            if osid == s["gcd_series_id"] and ovol in byvol:
                byvol[ovol]["date"] = odate
        is_omnibus = any(k in s["norm"] for k in ("omnibus", "3 in 1", "2 in 1"))
        out_series.append({
            "gcd_series_id": s["gcd_series_id"], "name": s["name"], "year_began": s["year_began"],
            "publisher": s["publisher"], "language": "en", "country": None, "is_omnibus": is_omnibus,
            "volume_count": max(byvol), "status": "ongoing" if s["is_current"] in ("1", 1) else "ended",
            "orig_series_id": None, "anilist_id": None, "mangaupdates_id": None, "mangadex_id": None,
        })
        out_aliases.append({"gcd_series_id": s["gcd_series_id"], "alias": s["name"]})
        if s["norm"] and s["norm"] != (s["name"] or "").lower():
            out_aliases.append({"gcd_series_id": s["gcd_series_id"], "alias": s["norm"]})
        for v, it in byvol.items():
            out_volumes.append({
                "gcd_series_id": s["gcd_series_id"], "volume_number": v, "title": None,
                "release_date": it["date"], "isbn13": it["isbn13"], "isbn10": None,
                "page_count": it["pages"], "composition": it["comp"],
            })

    # Bridge aliases for misspelled / arc-split library titles (only for series present in this build).
    present_ids = {s["gcd_series_id"] for s in out_series}
    out_aliases.extend(b for b in BRIDGE_ALIASES if b["gcd_series_id"] in present_ids)

    meta = {
        "schema_version": "1", "source": "GCD", "gcd_dump": os.path.basename(dump_path),
        "license": "CC BY-SA 4.0 (derived from the Grand Comics Database)",
        "attribution": "Data derived from the Grand Comics Database (https://www.comics.org), CC BY-SA 4.0.",
    }
    print("Parsed %d series, %d volumes (scanned %d issues)" % (len(out_series), len(out_volumes), n_issue))
    return {"series": out_series, "volumes": out_volumes, "aliases": out_aliases, "meta": meta}


def main():
    ap = argparse.ArgumentParser(description="Build the Mangarr bundled metadata SQLite.")
    ap.add_argument("--out", default="manga-metadata.sqlite")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--synthetic", action="store_true", help="build a tiny known dataset (no input)")
    g.add_argument("--dump", metavar="PATH", help="build from a real GCD bulk dump")
    g.add_argument("--inspect", metavar="SUBSTR", help="print sample series+issues matching SUBSTR (needs --dump-path)")
    ap.add_argument("--dump-path", metavar="PATH", help="path to the GCD dump for --inspect")
    args = ap.parse_args()

    if args.inspect:
        cmd_inspect(args.dump_path, args.inspect)
        return

    records = synthetic_records() if args.synthetic else build_from_dump(args.dump)
    path = build_db(records, args.out)
    print(f"Wrote {path}: {len(records['series'])} series, {len(records['volumes'])} volumes")


if __name__ == "__main__":
    main()
