#!/usr/bin/env python3
"""Tests for the metadata ingest: omnibus-composition parsing + schema/build round-trip.

Run: python3 tools/metadata-ingest/test_build_metadata_db.py
No external input — proves the pipeline and schema against synthetic data, before the real GCD dump.
"""
import json
import os
import sqlite3
import tempfile

import build_metadata_db as b


def test_parse_omnibus_composition():
    cases = [
        ("1 (1-2-3)", [1, 2, 3]),       # 3-in-1, explicit list inside parens
        ("24 (70-71-72)", [70, 71, 72]),  # last 3-in-1 volume of a 72-vol series
        ("1-2-3", [1, 2, 3]),            # explicit list, no parens
        ("1-3", [1, 2, 3]),             # two-number range -> inclusive
        ("5", [5]),                      # single
        ("Vol. 1 (1-2-3)", [1, 2, 3]),  # noise before parens
        ("", []),                        # empty
        (None, []),                      # missing
        ("Complete", []),               # no numbers
    ]
    for descriptor, expected in cases:
        got = b.parse_omnibus_composition(descriptor)
        assert got == expected, f"parse({descriptor!r}) = {got}, expected {expected}"
    print("ok  parse_omnibus_composition (9 cases)")


def test_build_and_query_synthetic():
    with tempfile.TemporaryDirectory() as d:
        out = os.path.join(d, "t.sqlite")
        b.build_db(b.synthetic_records(), out)
        conn = sqlite3.connect(out)
        conn.row_factory = sqlite3.Row
        try:
            n_series = conn.execute("SELECT COUNT(*) FROM series").fetchone()[0]
            assert n_series == 3, n_series

            # Naruto 3-in-1: omnibus, 24 vols, linked to the 72-vol original.
            omni = conn.execute("SELECT * FROM series WHERE gcd_series_id=76512").fetchone()
            assert omni["is_omnibus"] == 1
            assert omni["volume_count"] == 24
            assert omni["orig_series_id"] == 25323

            vc = conn.execute("SELECT COUNT(*) FROM volumes WHERE gcd_series_id=76512").fetchone()[0]
            assert vc == 24, vc

            v1 = conn.execute(
                "SELECT composition FROM volumes WHERE gcd_series_id=76512 AND volume_number=1"
            ).fetchone()
            assert json.loads(v1["composition"]) == [1, 2, 3]
            v24 = conn.execute(
                "SELECT composition FROM volumes WHERE gcd_series_id=76512 AND volume_number=24"
            ).fetchone()
            assert json.loads(v24["composition"]) == [70, 71, 72]

            # Original Naruto singles: 72 volumes, not omnibus, no composition.
            singles = conn.execute("SELECT * FROM series WHERE gcd_series_id=25323").fetchone()
            assert singles["is_omnibus"] == 0 and singles["volume_count"] == 72
            n_single_vols = conn.execute(
                "SELECT COUNT(*) FROM volumes WHERE gcd_series_id=25323"
            ).fetchone()[0]
            assert n_single_vols == 72, n_single_vols
            n_with_comp = conn.execute(
                "SELECT COUNT(*) FROM volumes WHERE gcd_series_id=25323 AND composition IS NOT NULL"
            ).fetchone()[0]
            assert n_with_comp == 0

            # Chainsaw Man v1 carries real per-volume enrichment (date/isbn/pages).
            cm1 = conn.execute(
                "SELECT * FROM volumes WHERE gcd_series_id=154385 AND volume_number=1"
            ).fetchone()
            assert cm1["release_date"] == "2020-10-06"
            assert cm1["isbn13"] == "9781974709939"
            assert cm1["page_count"] == 192

            # Provenance / license recorded.
            lic = conn.execute("SELECT value FROM meta WHERE key='license'").fetchone()[0]
            assert "CC BY-SA" in lic

            # Alias lookup works (drives the additive title-match for existing series).
            hit = conn.execute(
                "SELECT gcd_series_id FROM series_alias WHERE alias='Chainsaw Man'"
            ).fetchone()[0]
            assert hit == 154385
        finally:
            conn.close()
    print("ok  build_db + query (synthetic: 3 series, omnibus mapping, enrichment, alias, provenance)")


def test_clean_date():
    cases = [
        ("2020-10-06", "2020-10-06"),   # full date passes through
        ("2018-02-00", None),           # unknown day -> None (month precision, no fake day 1)
        ("2015-00-00", None),           # year-only -> None (no fabricated Jan 1)
        ("2008-00-03", None),           # year-only with sequence junk in the day field
        ("2019-01-01", None),           # Jan-1 "full" date = year-only entered as real
        ("2018-01-00", None),           # January day-unknown lands on Jan-1 -> same treatment
        ("2019-01-15", "2019-01-15"),   # a real mid-January date is untouched
        ("0000-01-01", None),           # year 0000
        ("2021-02-30", None),           # impossible calendar date
        ("2021-13-01", None),           # impossible month
        ("", None),
        (None, None),
        ("garbage", None),
    ]
    for raw, expected in cases:
        got = b._clean_date(raw)
        assert got == expected, f"_clean_date({raw!r}) = {got}, expected {expected}"
    print("ok  _clean_date (%d cases)" % len(cases))


def test_null_date_outliers():
    def vols(dates):
        return {i + 1: {"date": d} for i, d in enumerate(dates)}

    # Fire Force shape: vol 29 stamped 2018 between consistent 2022 neighbors -> nulled.
    ff = vols(["2022-07-05", "2022-08-16", "2018-10-18", "2022-12-20"])
    b._null_date_outliers(ff)
    assert ff[3]["date"] is None
    assert ff[2]["date"] == "2022-08-16" and ff[4]["date"] == "2022-12-20"

    # Monotonic series untouched.
    ok = vols(["2020-01-01", "2020-06-01", "2021-01-01"])
    b._null_date_outliers(ok)
    assert [ok[k]["date"] for k in ok] == ["2020-01-01", "2020-06-01", "2021-01-01"]

    # Reprint spike: a date a year past BOTH agreeing neighbors is an edition-mix
    # artifact (reprint date on a first-run line) -> nulled like a regression.
    spike = vols(["2011-01-01", "2016-06-01", "2011-06-01"])
    b._null_date_outliers(spike)
    assert spike[2]["date"] is None
    assert spike[1]["date"] and spike[3]["date"]

    # Dateless volumes are skipped; flanking works across the gap.
    gap = vols(["2022-07-05", None, "2018-10-18", "2022-12-20"])
    b._null_date_outliers(gap)
    assert gap[3]["date"] is None and gap[2]["date"] is None

    # Edges (first/last dated) are never nulled — only flanked outliers.
    edge = vols(["2010-01-01", "2020-01-01", "2020-06-01"])
    b._null_date_outliers(edge)
    assert edge[1]["date"] == "2010-01-01"
    print("ok  _null_date_outliers (5 scenarios)")


def test_date_overrides_shape():
    # Every override targets a real (series, volume) key with a valid ISO date — a typo'd id
    # would silently apply to nothing.
    import datetime
    for (sid, vol), date in b.DATE_OVERRIDES.items():
        assert isinstance(sid, int) and sid > 0
        assert isinstance(vol, int) and vol > 0
        datetime.date.fromisoformat(date)
    print("ok  DATE_OVERRIDES shape (%d entries)" % len(b.DATE_OVERRIDES))


if __name__ == "__main__":
    test_parse_omnibus_composition()
    test_build_and_query_synthetic()
    test_clean_date()
    test_null_date_outliers()
    test_date_overrides_shape()
    print("\nALL PASS")
