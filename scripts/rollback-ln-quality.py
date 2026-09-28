#!/usr/bin/env python3
"""rollback-ln-quality.py <readarr.db> --quality 7|8 [--apply]

Undoes one light-novel ebook quality so an OLDER Mangarr build -- one whose Quality.FindById does
not know the id and throws on it -- can safely start against this database again:
  7 = AZW3       (light-novel ebook formats round, 2026-09-22)
  8 = Ebook PDF  (light-novel PDF round, 2026-09-22)

Rewrites "quality": <id> to "quality": 0 (Unknown) inside the JSON blobs in BookFiles.Quality,
History.Quality, Blocklist.Quality and PendingReleases.ParsedBookInfo (that last one embeds a
QualityModel two levels deep: {"quality": {"quality": {"id": <id>, "name": ...}, ...}, ...} --
both the id and the name are reset to Unknown); removes any item carrying the id (flat, or
nested inside a group) from every QualityProfiles.Items JSON array; moves a profile's Cutoff off
the id (to EPUB if the profile allows it, else to the highest-ranked item still allowed); and
deletes the QualityDefinitions row for the id; and deletes the migration's own VersionInfo row
(56 for 8, 54 for 7) so a later roll-forward re-runs it instead of finding the id "already
listed" and no-op'ing, which would leave every profile without it. For 7 only, it also resets the
light-novel delivery format (Config key "preferredlightnovelformat", stored lower-cased) to
"epub" and deletes queued or started ConvertLightNovelFormat rows from Commands -- the build
before AZW3 has no such command. Prints a per-table count of rows touched, plus one line per
profile whose Cutoff moved.

The profile item and the QualityDefinitions row alone are enough to break an older build (both
are read through Quality.FindById), so run this before ANY image rollback past the round that
added the id -- even when no file carries it. Rolling back past both rounds: --quality 8 first,
then --quality 7. Undoing 7 while any trace of 8 remains (a row, a profile item, its definition,
or VersionInfo 56) is refused: Ebook PDF's insertion position depends on AZW3 already being
there, so undoing 7 first would leave it orphaned against a build that knows neither.

Dry run by default -- nothing is written unless --apply is given.

REQUIRED before running with --apply:
  1. Stop Mangarr (this script does not lock against a running instance).
  2. Back up the database file first. Run this against the backup, or against a copy, if unsure.

Pure Python 3 standard library (sqlite3, json, argparse) -- nothing to install.
"""
import argparse
import json
import os
import sqlite3
import sys

# The ids this script undoes, and what each one is.
ROLLBACK_QUALITIES = {7: "AZW3", 8: "Ebook PDF"}
AZW3_QUALITY_ID = 7
EBOOK_PDF_QUALITY_ID = 8

# The VersionInfo.Version of the migration that added each id -- deleted on rollback so a later
# roll-forward re-runs it instead of finding the id "already listed" (it isn't) and no-op'ing.
ROLLBACK_MIGRATION_VERSION = {7: 54, 8: 56}

UNKNOWN_QUALITY_ID = 0
UNKNOWN_QUALITY_NAME = "Unknown"
EPUB_QUALITY_ID = 6

# ConfigService lower-cases every key it stores.
LIGHT_NOVEL_FORMAT_KEY = "preferredlightnovelformat"
DEFAULT_LIGHT_NOVEL_FORMAT = "epub"

# Command.Name is the class name without "Command"; CommandStatus Queued = 0, Started = 1.
CONVERT_COMMAND_NAME = "ConvertLightNovelFormat"
LIVE_COMMAND_STATUSES = (0, 1)

# BookFiles.Quality / History.Quality / Blocklist.Quality store a JSON object shaped like
# {"quality": <id>, "revision": {...}}.
QUALITY_JSON_TABLES = ["BookFiles", "History", "Blocklist"]


def rewrite_quality_json_column(conn, table, quality_id, apply):
    """Rewrite "quality": <id> to "quality": 0 in every row of <table>.Quality. Returns the row count."""
    rows = conn.execute(f'SELECT "Id", "Quality" FROM "{table}"').fetchall()

    changed = []
    for row_id, raw in rows:
        if raw is None:
            continue

        try:
            doc = json.loads(raw)
        except (TypeError, ValueError):
            continue

        if not isinstance(doc, dict) or doc.get("quality") != quality_id:
            continue

        doc["quality"] = UNKNOWN_QUALITY_ID
        changed.append((row_id, json.dumps(doc)))

    if apply and changed:
        conn.executemany(f'UPDATE "{table}" SET "Quality" = ? WHERE "Id" = ?', [(doc, row_id) for row_id, doc in changed])

    return len(changed)


def rewrite_pending_releases(conn, quality_id, apply):
    """Rewrite the embedded quality id (and name) inside PendingReleases.ParsedBookInfo.

    Unlike BookFiles/History/Blocklist, ParsedBookInfo is serialized without the int-quality
    shortcut, so the quality id sits two levels deep:
    {"quality": {"quality": {"id": <id>, "name": ...}, "revision": {...}}, ...}. Returns the row
    count.
    """
    rows = conn.execute('SELECT "Id", "ParsedBookInfo" FROM "PendingReleases"').fetchall()

    changed = []
    for row_id, raw in rows:
        if raw is None:
            continue

        try:
            doc = json.loads(raw)
        except (TypeError, ValueError):
            continue

        if not isinstance(doc, dict):
            continue

        quality_model = doc.get("quality")
        if not isinstance(quality_model, dict):
            continue

        quality = quality_model.get("quality")
        if not isinstance(quality, dict) or quality.get("id") != quality_id:
            continue

        quality["id"] = UNKNOWN_QUALITY_ID
        quality["name"] = UNKNOWN_QUALITY_NAME
        changed.append((row_id, json.dumps(doc)))

    if apply and changed:
        conn.executemany('UPDATE "PendingReleases" SET "ParsedBookInfo" = ? WHERE "Id" = ?', [(doc, row_id) for row_id, doc in changed])

    return len(changed)


def strip_quality_item(items, quality_id):
    """Recursively drop any item (flat, or nested inside a group's "items") carrying the id."""
    kept = []
    removed = False

    for item in items:
        if isinstance(item, dict) and item.get("quality") == quality_id:
            removed = True
            continue

        if isinstance(item, dict) and isinstance(item.get("items"), list):
            item["items"], nested_removed = strip_quality_item(item["items"], quality_id)
            removed = removed or nested_removed

        kept.append(item)

    return kept, removed


def epub_allowed(items):
    """True if EPUB's own top-level entry is allowed: the flat item itself, or, when EPUB is
    grouped, the GROUP's own allowed flag. The runtime (QualityProfile.GetIndex with the default
    respectGroupOrder=false, read by QualityAllowedByProfileSpecification) resolves a grouped
    quality to its group's top-level index and reads allowed there -- never the nested item's own
    flag -- so that flag is ignored here too."""
    for item in items:
        if not isinstance(item, dict):
            continue

        if item.get("quality") == EPUB_QUALITY_ID:
            return bool(item.get("allowed"))

        if isinstance(item.get("items"), list) and any(
            isinstance(n, dict) and n.get("quality") == EPUB_QUALITY_ID for n in item["items"]
        ):
            return bool(item.get("allowed"))

    return False


def highest_ranked_allowed_id(items):
    """The identifying id of the highest-ranked (last, per the stored worst-first order) allowed
    top-level item -- a flat item's quality, or a group's own id. None if nothing is allowed."""
    for item in reversed(items):
        if not isinstance(item, dict) or not item.get("allowed"):
            continue

        if item.get("quality") is not None:
            return item["quality"]

        if isinstance(item.get("items"), list):
            return item.get("id")

    return None


def rewrite_quality_profiles(conn, quality_id, apply):
    """Remove the id's item from every QualityProfiles.Items JSON array, and move a Cutoff on the
    id to EPUB (or the next highest-ranked allowed item). Returns (row count, cutoff report lines).
    """
    rows = conn.execute('SELECT "Id", "Name", "Cutoff", "Items" FROM "QualityProfiles"').fetchall()

    item_changes = []
    cutoff_changes = []
    cutoff_reports = []

    for row_id, name, cutoff, raw in rows:
        if raw is None:
            continue

        try:
            items = json.loads(raw)
        except (TypeError, ValueError):
            continue

        if not isinstance(items, list):
            continue

        new_items, removed = strip_quality_item(items, quality_id)
        if not removed:
            continue

        item_changes.append((row_id, json.dumps(new_items)))

        if cutoff == quality_id:
            fallback = False

            if epub_allowed(new_items):
                new_cutoff = EPUB_QUALITY_ID
            else:
                candidate = highest_ranked_allowed_id(new_items)
                if candidate is None:
                    # Nothing left allowed at all -- fall back to Unknown rather than leave the
                    # cutoff pointing at the id we just removed. Flagged in the report. (A
                    # legitimately-found candidate can itself be id 0/Unknown, so this is tracked
                    # separately rather than inferred from the resulting value.)
                    new_cutoff = UNKNOWN_QUALITY_ID
                    fallback = True
                else:
                    new_cutoff = candidate

            cutoff_changes.append((row_id, new_cutoff))
            cutoff_reports.append((row_id, name, cutoff, new_cutoff, fallback))

    if apply:
        if item_changes:
            conn.executemany('UPDATE "QualityProfiles" SET "Items" = ? WHERE "Id" = ?', [(items, row_id) for row_id, items in item_changes])
        if cutoff_changes:
            conn.executemany('UPDATE "QualityProfiles" SET "Cutoff" = ? WHERE "Id" = ?', [(new_cutoff, row_id) for row_id, new_cutoff in cutoff_changes])

    return len(item_changes), cutoff_reports


def delete_quality_definition(conn, quality_id, apply):
    """Delete the QualityDefinitions row for the id. Returns the row count."""
    count = conn.execute('SELECT COUNT(*) FROM "QualityDefinitions" WHERE "Quality" = ?', (quality_id,)).fetchone()[0]

    if apply and count:
        conn.execute('DELETE FROM "QualityDefinitions" WHERE "Quality" = ?', (quality_id,))

    return count


def version_info_count(conn, version):
    """How many VersionInfo rows exist for a migration version (0 or 1 -- Version is unique)."""
    return conn.execute('SELECT COUNT(*) FROM "VersionInfo" WHERE "Version" = ?', (version,)).fetchone()[0]


def delete_version_info(conn, quality_id, apply):
    """Delete the VersionInfo row for the migration that added the id, so a later roll-forward
    re-runs it (its "already listed" check would otherwise find nothing to do). Returns the row
    count."""
    version = ROLLBACK_MIGRATION_VERSION[quality_id]
    count = version_info_count(conn, version)

    if apply and count:
        conn.execute('DELETE FROM "VersionInfo" WHERE "Version" = ?', (version,))

    return count


def has_ebook_pdf_evidence(conn):
    """True if any trace of Ebook PDF (8) remains: a file/history/blocklist row, a pending
    release, a profile item (flat or nested in a group), its QualityDefinitions row, or migration
    056's VersionInfo row. Used to refuse rolling back 7 while 8 still depends on it being there."""
    for table in QUALITY_JSON_TABLES:
        if rewrite_quality_json_column(conn, table, EBOOK_PDF_QUALITY_ID, False):
            return True

    if rewrite_pending_releases(conn, EBOOK_PDF_QUALITY_ID, False):
        return True

    profile_count, _ = rewrite_quality_profiles(conn, EBOOK_PDF_QUALITY_ID, False)
    if profile_count:
        return True

    if delete_quality_definition(conn, EBOOK_PDF_QUALITY_ID, False):
        return True

    if version_info_count(conn, ROLLBACK_MIGRATION_VERSION[EBOOK_PDF_QUALITY_ID]):
        return True

    return False


def reset_light_novel_format(conn, apply):
    """Set the light-novel delivery format back to epub. Returns the row count (0 when the key was
    never stored -- the default is already epub)."""
    rows = conn.execute('SELECT "Id", "Value" FROM "Config" WHERE lower("Key") = ?', (LIGHT_NOVEL_FORMAT_KEY,)).fetchall()

    changed = [row_id for row_id, value in rows if (value or "").lower() != DEFAULT_LIGHT_NOVEL_FORMAT]

    if apply and changed:
        conn.executemany('UPDATE "Config" SET "Value" = ? WHERE "Id" = ?', [(DEFAULT_LIGHT_NOVEL_FORMAT, row_id) for row_id in changed])

    return len(changed)


def delete_convert_commands(conn, apply):
    """Delete queued/started ConvertLightNovelFormat commands. Returns the row count."""
    placeholders = ",".join("?" for _ in LIVE_COMMAND_STATUSES)
    where = f'"Name" = ? AND "Status" IN ({placeholders})'
    params = (CONVERT_COMMAND_NAME,) + LIVE_COMMAND_STATUSES

    count = conn.execute(f'SELECT COUNT(*) FROM "Commands" WHERE {where}', params).fetchone()[0]

    if apply and count:
        conn.execute(f'DELETE FROM "Commands" WHERE {where}', params)

    return count


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("db_path", help="path to the Mangarr/Readarr sqlite database")
    parser.add_argument("--quality", type=int, required=True, choices=sorted(ROLLBACK_QUALITIES),
                        help="the quality id to undo: 7 (AZW3) or 8 (Ebook PDF)")
    parser.add_argument("--apply", action="store_true", help="write the changes (default: dry run, report only)")
    args = parser.parse_args()

    if not os.path.isfile(args.db_path):
        print(f"no such file: {args.db_path}", file=sys.stderr)
        return 1

    quality_id = args.quality
    conn = sqlite3.connect(args.db_path)

    try:
        # Ebook PDF's insertion position depends on AZW3 already being there; undoing 7 first
        # would leave it orphaned against a build that knows neither. Refused in both dry run and
        # --apply, before anything else runs.
        if quality_id == AZW3_QUALITY_ID and has_ebook_pdf_evidence(conn):
            print(f"refusing: Ebook PDF (8) is still present in {args.db_path} -- "
                  "run 'rollback-ln-quality.py <db> --quality 8' first", file=sys.stderr)
            return 1

        counts = {}

        for table in QUALITY_JSON_TABLES:
            counts[table] = rewrite_quality_json_column(conn, table, quality_id, args.apply)

        counts["PendingReleases"] = rewrite_pending_releases(conn, quality_id, args.apply)
        counts["QualityProfiles"], cutoff_reports = rewrite_quality_profiles(conn, quality_id, args.apply)
        counts["QualityDefinitions"] = delete_quality_definition(conn, quality_id, args.apply)
        counts["VersionInfo"] = delete_version_info(conn, quality_id, args.apply)

        # The delivery-format setting and its conversion command came with AZW3.
        if quality_id == AZW3_QUALITY_ID:
            counts["Config"] = reset_light_novel_format(conn, args.apply)
            counts["Commands"] = delete_convert_commands(conn, args.apply)

        if args.apply:
            conn.commit()
    finally:
        conn.close()

    mode = "APPLIED" if args.apply else "DRY RUN — nothing written (pass --apply to write)"
    verb = "updated" if args.apply else "would be updated"
    print(f"rollback-ln-quality.py --quality {quality_id} ({ROLLBACK_QUALITIES[quality_id]}) against {args.db_path} ({mode})")
    for table, count in counts.items():
        print(f"  {table}: {count} row(s) {verb}")

    if cutoff_reports:
        cutoff_verb = "moved" if args.apply else "would move"
        for row_id, name, old_cutoff, new_cutoff, fallback in cutoff_reports:
            note = " (nothing left allowed -- fell back to Unknown)" if fallback else ""
            print(f"    profile {row_id} ({name}): cutoff {cutoff_verb} {old_cutoff} -> {new_cutoff}{note}")

    if not args.apply and (any(counts.values())):
        print("\nRe-run with --apply, on a backed-up DB, with Mangarr STOPPED, to write these changes.")

    return 0


if __name__ == "__main__":
    sys.exit(main())
