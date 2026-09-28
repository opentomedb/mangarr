#!/usr/bin/env python3
"""Verify the built artifact against the user's actual library (the 15 folders)."""
import sqlite3
import sys

DB = sys.argv[1] if len(sys.argv) > 1 else "manga-metadata.sqlite"

TERMS = [
    ("Black Clover", "black clover"),
    ("Chainsaw Man", "chainsaw man"),
    ("Dandadan", "dandadan"),
    ("Demon Slayer - Kimetsu no Yaiba", "demon slayer"),
    ("Fire Force", "fire force"),
    ("Frieren - Beyond Journey's End", "frieren"),
    ("Gyo", "gyo"),
    ("Horimiya", "horimiya"),
    ("Kaiju No. 8", "kaiju no"),
    ("My Dress-Up Darling", "dress-up darling"),
    ("My Hero Academia", "my hero academia"),
    ("Solo Leveling", "solo leveling"),
    ("Sword Art Online - Progressive", "sword art online"),
    ("Vigilante - Boku no Hero Academia Illegals", "vigilante"),
    ("Witch Hat Atelier", "witch hat"),
]

c = sqlite3.connect(DB)
c.row_factory = sqlite3.Row
tot = c.execute("SELECT COUNT(*) FROM series").fetchone()[0]
totv = c.execute("SELECT COUNT(*) FROM volumes").fetchone()[0]
print("artifact: %d series, %d volumes\n" % (tot, totv))

missing = []
for folder, term in TERMS:
    rows = c.execute(
        "SELECT gcd_series_id,name,publisher,is_omnibus,volume_count,status FROM series "
        "WHERE name LIKE ? COLLATE NOCASE ORDER BY is_omnibus, volume_count DESC",
        ("%" + term + "%",)).fetchall()
    print("### %s  (LIKE %%%s%%) -> %d match(es)" % (folder, term, len(rows)))
    if not rows:
        missing.append(folder)
        print("   !!! NO MATCH\n")
        continue
    for r in rows[:5]:
        v1 = c.execute("SELECT release_date,isbn13,page_count FROM volumes WHERE gcd_series_id=? AND volume_number=1",
                       (r["gcd_series_id"],)).fetchone()
        vmax = c.execute("SELECT volume_number,release_date,isbn13 FROM volumes WHERE gcd_series_id=? ORDER BY volume_number DESC LIMIT 1",
                         (r["gcd_series_id"],)).fetchone()
        print("   id=%s %r pub=%r omni=%s vols=%s status=%s" % (
            r["gcd_series_id"], r["name"], r["publisher"], r["is_omnibus"], r["volume_count"], r["status"]))
        if v1:
            print("      v1:   date=%s isbn=%s pages=%s" % (v1["release_date"], v1["isbn13"], v1["page_count"]))
        if vmax:
            print("      vMax: #%s date=%s isbn=%s" % (vmax["volume_number"], vmax["release_date"], vmax["isbn13"]))
    print()

print("MISSING (no match): %s" % (missing or "none"))
