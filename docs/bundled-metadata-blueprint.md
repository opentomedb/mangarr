# Bundled metadata — build blueprint (2026-06-02)

Personal, bundled, one container. The metadata layer lives **in-process inside the Mangarr
container**, backed by a local GCD-derived SQLite artifact, with the existing live sources
(AniList / MangaUpdates / MangaDex / Google Books) as fallback. No separate process, no HTTP hop.

## Decisions locked
- **Deployment:** bundled into the Mangarr container; one container; personal (this instance only).
- **Architecture:** **in-process behind the existing metadata interfaces.** The seam is the C#
  interface (`IProvideAuthorInfo` / `IProvideBookInfo` / `ISearchForNewBook|Author|Entity`), NOT an
  HTTP transport. Rationale: Sonarr's Skyhook HTTP boundary exists to support a *remote, central*
  service — the deployment we ruled out. Copying the hop inside one container is dead weight (a 2nd
  supervised process + JSON serialization per lookup for data sitting in a local SQLite). In-process
  is low-regret; if a public deployment is ever wanted, wrap the same implementation in an ASP.NET
  host and re-point — ~a day, reversible.
- **Spine + fallback:** GCD when present (per-volume, English/omnibus-aware); AniList/MangaUpdates/
  MangaDex/Google Books as the always-on baseline so nothing breaks when the artifact is stale or a
  series is one of GCD's print-only gaps (digital-only, webtoons, light novels).
- **Coverage validated:** GCD covers 13/15 of the live library by automated probe; the 2 misses
  (SAO Progressive, MHA Vigilantes) are search-matching artifacts — both have official English print —
  so true coverage ~15/15. Spine holds.

## Components

### 1. Data artifact — `manga-metadata.sqlite` (read-only at runtime)
A prebuilt SQLite generated offline from the GCD bulk dump. Baked into the image AND overridable from
`/config` so a fresher artifact can be dropped in without rebuilding (and, later, published for the
community to pull — GitHub Releases / R2, both free). Schema: `tools/metadata-ingest/schema.sql`
(series + volumes + omnibus composition + English-edition flag + cross-IDs + aliases + provenance).
Runtime lookup order: `/config/metadata/*.sqlite` (user override) → `/app/metadata/manga-metadata.sqlite`
(baked in). Carries CC BY-SA attribution in the `meta` table (license obligation on the derived data).

### 2. Ingest pipeline (offline, you-only) — `tools/metadata-ingest/`
Dump → normalized records → SQLite. Built and unit-tested NOW against synthetic data; the real
artifact gates on **your one manual gated-dump download** (Cloudflare + login — not scriptable, and
deliberately done once centrally so no instance ever touches the gate). Language: Python for the
offline tool only (MySQL-dump parsing + `sqlite3` stdlib; trivial to run on the workstation/Unraid).
Runtime stays pure C#. (Swappable to a C# console tool if you'd rather not have Python tooling in the
repo — flagged, your call.)

### 3. Runtime metadata layer (C#, in-process) — *next increment, not this one*
- New `GcdMetadataService` (reads the SQLite; per-volume titles/dates/ISBNs; omnibus → JP↔EN map).
- A composing provider behind the interfaces: GCD first, then the existing AniList/MU/MangaDex/
  GoogleBooks chain (the logic currently *inlined* in `BookInfoProxy` gets **extracted** into clean
  services). `BookInfoProxy` shrinks toward a thin coordinator.
- DI is automatic (DryIoc `RegisterMany`) — new services need no manual registration.

### 4. ID migration (data-touching — additive only) — *next increment*
- **Do NOT big-bang re-key** existing series to GCD ids. Add a nullable `GcdId` mapping (column on
  Authors, or a side table). Existing 15 series keep their current foreign keys and acquire GCD data
  via a **lazy title match** that *populates* the mapping; only **new** series adopt GCD ids natively.
  Old and new keys coexist; nothing is re-keyed out from under a series that has files.
- `RefreshBookService.ShouldDelete` still only drops fileless books, so owned volumes are safe — but
  additive mapping sidesteps the orphan/dup risk entirely.
- **Verify on a COPY of the live DB**: all 15 series + their file associations survive the migration
  and the title-match before it ever touches live.

### 5. Container packaging
Bake the SQLite into the image (`COPY metadata/ /app/metadata/`); add the `/config/metadata` override
read. **No entrypoint/supervisor change** — in-process means no second process to supervise.

## Build sequence (each step independently verifiable)
1. **[this increment]** Blueprint + target SQLite schema + ingest skeleton + synthetic-data tests.
   Verify: `python3 tools/metadata-ingest/test_build_metadata_db.py` green (schema + omnibus parse).
2. **[gates on you]** Manual GCD dump download → run ingest → produce the real `manga-metadata.sqlite`.
   Verify: spot-check the 15 series (counts, omnibus mapping, dates/ISBNs) against publishers.
3. `GcdMetadataService` (C#) reading the artifact + a thin unit test. Verify: lookups for the 15.
4. Extract inlined AniList/MU/etc. out of `BookInfoProxy` into the composing fallback provider.
   Verify: existing metadata tests still green; no behavior change when GCD misses.
5. Additive `GcdId` migration + lazy title-match. Verify: on a DB COPY, all 15 series + files intact.
6. Bake artifact into image + `/config` override. Verify: container serves GCD data for the 15.

## Future-public path (no re-architecture)
Same in-process implementation wraps in an ASP.NET host → community instances either bundle the
service+artifact (each self-contained, you owe no uptime — recommended) or point at one hosted
instance. Distribution = the published derived artifact (GitHub Releases free, or Cloudflare R2 free
tier, $0 egress). The gated dump stays a you-only, central, periodic step.
