# Mangarr metadata ingest (offline build-tooling)

Produces the read-only `manga-metadata.sqlite` artifact the bundled C# metadata layer reads.
**Offline only** — not part of the runtime container. Runtime is pure C#; this is just how the
artifact is generated. See `../../docs/bundled-metadata-blueprint.md` for the full design.

## Why this exists separately
The GCD bulk dump is a **manual, Cloudflare + login-gated, bi-weekly download** — not scriptable.
It is fetched **once, centrally** (by the maintainer), turned into a clean SQLite here, and that
artifact is what ships (baked into the image and/or published). No Mangarr instance ever touches
the gate.

## Usage
```bash
# Prove the pipeline + schema with no external input:
python3 build_metadata_db.py --synthetic --out manga-metadata.sqlite

# Run the tests:
python3 test_build_metadata_db.py

# Real build (after the gated dump is downloaded — ingest mapping finalized then):
python3 build_metadata_db.py --dump /path/to/gcd-dump --out manga-metadata.sqlite
```

## Status
- `schema.sql` — target SQLite schema (ours, stable): series + volumes + omnibus composition
  (`[1,2,3]` JSON) + English-edition flag + cross-IDs + aliases + provenance/license.
- `--synthetic` + tests — green; proves schema + omnibus parsing without the dump.
- `--dump` — stub. The GCD dump is a MySQL dump of `gcd_series` / `gcd_issue` / `gcd_story` /
  `gcd_publisher`; map it into the `records` dict that `build_db()` consumes. Finalized when the
  real dump is in hand (gates on the maintainer's manual download, not on code).

## License
The data is derived from the **Grand Comics Database** (https://www.comics.org), **CC BY-SA 4.0**.
Attribution is written into the artifact's `meta` table; any redistribution must preserve it and
remain share-alike.
