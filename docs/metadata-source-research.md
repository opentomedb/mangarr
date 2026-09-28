# Manga metadata sources — "Is there a TVDB for manga?" (research, 2026-06-02)

Research sweep: 16 agents, ~754k tokens, live API probes against One Piece (long/ongoing),
Chainsaw Man (completed ~24), Naruto (completed; 72 JP tankoubon = 24 EN 3-in-1 omnibus).
Anything that looked authoritative was adversarially re-probed on long-tail + ongoing titles
to confirm the volume list was actually *complete*, not just present.

## Verdict: PARTIAL

A true "TVDB for manga" does **not** exist as a turnkey source you can point an instance at —
but TVDB-**grade data** exists in exactly one place. The honest answer separates two bars the
question bundles together:

- **DATA bar** — does authoritative, complete, per-volume, English/omnibus-aware data exist anywhere?
  → **YES**, in one source (GCD).
- **BEGIN-AND-FORGET bar** — can a source hand that data to any user's instance turnkey/automatable?
  → **NO** for everyone, GCD included.

## The one that clears the DATA bar

**Grand Comics Database (GCD / comics.org)** — the genuine article, and the only one.
- Per-volume records: release date, ISBN-10/13, page count, indicia publisher, and the chapters
  (`story_set`) inside each volume.
- **English localization + omnibus done right**: localized editions and 3-in-1s are stored as
  SEPARATE series with explicit composition descriptors. Naruto base id 25323 = 72 vols;
  "Naruto 3-in-1" id 76512 = 24 vols mapped `1 (1-2-3)` … `24 (70-71-72)`. This is precisely the
  JP→EN omnibus mapping that defeats AniList/MangaUpdates.
- **Survived adversarial verification**: an independent re-probe reproduced every field via keyless
  curl AND matched the publishers' own catalogs for **7/7** series incl. long-tail (Mieruko-chan,
  The Girl from the Other Side) and ongoing titles — zero missing/incorrect volumes, no indexing lag
  even on a volume released ~5 weeks prior. License CC BY-SA 4.0 (attribution + share-alike).

### Why GCD still fails the begin-and-forget bar
- Anon API throttled **30 req/hour** (verifier hit HTTP 429 after ~17 calls).
- Per-volume dates/ISBNs need **N+1** issue fetches → One Piece = 111+ calls ≈ **4 hrs to enumerate one series** via API. API backfill of a real library is infeasible.
- Only bulk path is the **bi-weekly DB dump**, gated behind a **Cloudflare JS challenge + account login** — a manual browser download, **not scriptable per-instance**.
- **Print-only** scope: no light novels, digital-only manga, or webtoons.
- Omnibus composition is freeform text; `active_issues` overcounts variants (dedupe by `issue_descriptor`/`variant_of`).

## The rest (ranked)

| Source | Tier | What it's good for |
|---|---|---|
| **Open Library** | Partial / enrichment | Every EN volume as a work/edition w/ ISBN + language tags; EN omnibuses surface. **No** series→ordered-volume model (series field null), duplicates, gappy. Free/open + bulk dump. Best as ISBN/cover/date enricher keyed by (series, vol#). |
| **MangaUpdates** | Count-only (integrated) | Broad, manga-native, keyless. Total count + completion + the only *other* EN/omnibus hint (publisher `notes`: "Naruto Viz: 72 Vols \| 24 Omnibus, 3-in-1"). Freeform only — no per-volume array, no ISBNs. Keep as count + EN/omnibus HINT. |
| **AniList** | Count-only (integrated) | Excellent series metadata/discovery. Volume catalog = single scalar, JP-tankoubon only, null for ongoing. Keep for metadata, not volumes. |
| **Hardcover** | **REFUTED → enrichment** | Survey rated it "clears the bar"; adversarial verify refuted it — `/series/kingdom` resolved to the WRONG work (Chuck Black novel), YKK fragmented across 3+ dup series, self-contradicting counts (Kingdom 77 vs 75), year-1900 dates. Good *model*, unconfirmed completeness, needs account token. Must not sit near GCD. |
| **MAL/Jikan, Kitsu, mangabaka.dev** | Count-only | Keyless, JP-tankoubon count, no per-volume, no omnibus. **mangabaka** is the best-built cross-ID/dedupe hub (maps AniList/MAL/Kitsu/MU IDs) — useful for reconciliation. |
| **Wikidata** | Sparse / CC0 hub | CAN model per-volume via P577 qualifiers (One Piece = 112 vols w/ dates+JP ISBNs) but coverage ~0.8% of manga; 2/3 mainstream tests failed. Best as a CC0 cross-ID hub. |
| **Google Books / ISBNdb / WorldCat** | Per-volume ENRICHMENT (integrated) | Authoritative EN date/pages/ISBN for a volume you ALREADY KNOW exists. Cannot enumerate or count a series. Keep as the (series, vol#) enrichment leg. |
| **Comic Vine** | Mismatched | Best Western-comics DB but inverse of manga: no tankoubon concept, no ISBN/language fields, dup JP/EN entries, mainstream-only, needs key. |
| **MangaDex** | Chapter-only (integrated) | Scanlation-centric; `/aggregate` has huge gaps on licensed series (One Piece EN = 1 vol). Keep for chapter availability only. |
| **Readarr bookinfo + Goodreads** | **DEAD / compromised** | bookinfo.club offline (apex parked, api NXDOMAIN). Only live half = Goodreads legacy XML via a leaked private key on a deprecated API; not EN/omnibus aware. Do not depend on it. |

## If we ever want true begin-and-forget: the architecture

GCD as the **spine**, not GCD alone:
1. **SPINE** = GCD bi-weekly **bulk dump**, ingested ONCE into a local DB (the dump, because 30/hr API can't backfill). Authoritative per-volume catalog w/ dates+ISBNs + omnibus-as-separate-series.
2. **DISCOVERY + print-scope gaps** = keep AniList (metadata/search) + MangaUpdates (count + EN/omnibus hint). These also cover what GCD's print-only charter excludes (light novels, digital-only, webtoons).
3. **ENRICHMENT** = Google Books / Open Library for ISBN/date/page backfill on a known (series, vol#).
4. **Optional** = mangabaka.dev or Wikidata as a free cross-ID/dedupe hub.
5. **One-time build**: ingest the gated dump, dedupe variants by `issue_descriptor`/`variant_of`, parse the freeform omnibus descriptors (`1 (1-2-3)`) to map JP tankoubon → EN omnibus.

## The key insight (the real reason Sonarr feels turnkey)

Sonarr isn't turnkey because **TVDB** is turnkey — it's because the Sonarr *project* runs a central
metadata service (Skyhook) in front of TVDB, so no user ever scrapes TVDB. Servarr's real secret is
the **central metadata service**, not the upstream DB. The manga equivalent doesn't exist yet — so
"begin-and-forget simple for any user" means **building** that layer: a central Mangarr metadata
service that ingests GCD (+ AniList/MU/Google Books) once and serves all instances. The simplicity
has to be built once, centrally — it cannot be inherited from any single source. Do it once and end
users get Sonarr-style begin-and-forget; skip it and the 30/hr throttle keeps GCD unusable per-instance.
