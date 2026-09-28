-- Mangarr bundled metadata — target SQLite schema (read-only at runtime).
-- Generated offline from the GCD bulk dump (see build_metadata_db.py). The GCD-dump->records
-- mapping is finalized when the real dump is in hand; THIS schema is ours and stable.

PRAGMA user_version = 1;

-- One row per series-as-released. GCD stores localized English editions and omnibus/3-in-1 lines
-- as SEPARATE series; we keep that — each release line is its own row, linked to its original.
CREATE TABLE IF NOT EXISTS series (
    gcd_series_id   INTEGER PRIMARY KEY,   -- GCD series id of THIS release line
    name            TEXT    NOT NULL,
    year_began      INTEGER,
    publisher       TEXT,
    language        TEXT,                  -- e.g. 'en'
    country         TEXT,
    is_omnibus      INTEGER NOT NULL DEFAULT 0,  -- 1 = 3-in-1 / omnibus English line
    volume_count    INTEGER NOT NULL DEFAULT 0,  -- volumes in THIS edition (e.g. 24 for Naruto 3-in-1)
    status          TEXT,                  -- 'completed' | 'ongoing' | NULL
    orig_series_id  INTEGER,               -- for an omnibus line: the original-language base series
    anilist_id      INTEGER,               -- cross-IDs for the live-source fallback / reconciliation
    mangaupdates_id INTEGER,
    mangadex_id     TEXT
);

-- One row per volume of a release line, ordered 1..N within that line.
CREATE TABLE IF NOT EXISTS volumes (
    id             INTEGER PRIMARY KEY,
    gcd_series_id  INTEGER NOT NULL REFERENCES series(gcd_series_id),
    volume_number  INTEGER NOT NULL,       -- 1..N within this release line
    title          TEXT,
    release_date   TEXT,                   -- ISO 8601 (YYYY-MM-DD) when known
    isbn13         TEXT,
    isbn10         TEXT,
    page_count     INTEGER,
    composition    TEXT,                   -- JSON array of original tankoubon vols, e.g. "[1,2,3]"
                                           -- (omnibus only; NULL/empty for a plain single volume)
    UNIQUE (gcd_series_id, volume_number)
);

-- Alternate / localized titles, to match a user's existing series to a GCD line by name.
CREATE TABLE IF NOT EXISTS series_alias (
    gcd_series_id  INTEGER NOT NULL REFERENCES series(gcd_series_id),
    alias          TEXT    NOT NULL,
    UNIQUE (gcd_series_id, alias)
);

-- Provenance + license. One row per key.
CREATE TABLE IF NOT EXISTS meta (
    key    TEXT PRIMARY KEY,
    value  TEXT
);

CREATE INDEX IF NOT EXISTS idx_series_name        ON series (name);
CREATE INDEX IF NOT EXISTS idx_series_orig        ON series (orig_series_id);
CREATE INDEX IF NOT EXISTS idx_volumes_series     ON volumes (gcd_series_id);
CREATE INDEX IF NOT EXISTS idx_alias_alias        ON series_alias (alias);
