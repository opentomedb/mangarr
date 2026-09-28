> **Superseded 2026-09-04:** shipped as Collections (build 10.0.0.165) on the artifact's `parent_series_id` — the authoritative grouping signal this design said did not exist. See HANDOFF.md.

# Design: Franchise / arc-split grouping in search ("one search → add all arcs")

**Status:** DEFERRED — execute during distribution-prep, not before.
**Decided:** 2026-06-29. Approach chosen = "one search → add all arcs" (keep arcs as separate,
correct series; surface + add them together). Shelf-grouping and full-merge approaches rejected.

## Problem

Some franchises legitimately publish as multiple separate series, not one continuous one:
- **Arc-splits** (the target case): e.g. Re:ZERO ships as *Chapter 1: A Day in the Capital*,
  *Chapter 2: A Week at the Mansion*, *Chapter 3: Truth of Zero*, *Chapter 4: …*, plus side
  stories (*The Frozen Bond*) — each is its own book series with its own Vol.1..N. Common across
  manga/light-novel adaptations (Monogatari, etc.).
- **Main + spin-off** (NOT the target — must stay separate): Kaiju No.8 + Kaiju No.8: Relax;
  My Hero Academia + My Hero Academia: Vigilantes.

Today, searching the franchise name (e.g. "re:zero") returns **one** result (the single most
popular arc), so the other arcs are undiscoverable without knowing and searching each arc's exact
title. The user must search N times and add N series. Goal: one search surfaces all arcs with an
"Add all" action; each stays a separate, correctly-structured series underneath.

## Why it's deferred (cost/benefit at time of decision)

- The search/identity path is the most fragile area of the fork; the 2026-06-29 session was spent
  stabilizing it (40s freeze, per-volume covers, variant-id dedup, de-slug names). Stacking a
  multi-result feature on immediately risks that stability.
- There is **no low-risk subset**: the value requires changing search from 1→N results, which
  changes behavior for *every* add (sequels/spin-offs/similar titles now appear), not just arc-splits.
- Each of N results needs its own stable `foreignAuthorId`; "Add all" creating N series at once is
  N× the surface for the variant-id / de-slug bugs just fixed.
- Payoff is distribution-gated and affects ~1 series in the current library. Build it when
  distribution is actually being prepared, with multiple test libraries to validate grouping.

## Current-state constraints (verified 2026-06-29)

Search collapses to a single result at every layer:
- Providers each return ONE best match:
  - `AniListService.FindSeries(title) -> AniListSeries`
  - `MangaDexService.Lookup(title) -> MangaDexResult`
  - `MangaUpdatesService.FindSeries(title) -> MangaUpdatesSeries`
- `MangaSeriesMetadataProvider.FetchProviders(name)` combines those into one `ProviderLookup`
  (singular ani/mdx/mu), and `GetSeries` returns one `MangaSeriesMetadata`.
- `BookInfoProxy.SearchForNewAuthor(title)` returns `new List<Author>{ BuildFakeAuthor(title, false) }`
  — exactly one fake author. `SearchForNewEntity` likewise builds one author + its books.

So "show all arcs" is NOT un-collapsing an existing list — the list is discarded upstream and must
be plumbed through.

## Proposed implementation

### Backend
1. **Multi-result provider search.** Add `SearchSeries(title, int limit) -> List<…>` to the providers
   (AniList + MangaDex are the rich ones; MangaUpdates optional). The search endpoints already return
   a page; stop discarding all-but-best. Keep the existing single-result `FindSeries`/`Lookup` for the
   resolve/refresh path unchanged (additive — don't rewrite the single path).
2. **`MangaSeriesMetadataProvider.SearchSeries(name, limit) -> List<MangaSeriesMetadata>`** — merge
   provider candidates, dedupe by normalized title (reuse `CleanAuthorName`-style normalization),
   return top-N as **structure-only** (title + volume count + cover; no per-volume HTTP — keep search fast).
3. **`BookInfoProxy.SearchForNewAuthor`/`SearchForNewEntity`** — build one fake author per candidate.
   - `foreignAuthorId` = `local-` + slug(**candidate canonical title**) (not the raw search term).
     NOTE: this is the slug-from-resolved-title direction; if the canonical-id redesign (below) lands
     first, anchor to the provider's stable id instead.
   - Reuse `ResolveDisplayName` so a failed candidate resolution doesn't produce an ugly name.
4. **Grouping hint on the resource.** Add `franchiseGroup` to `AuthorResource` = the shared main-title
   prefix among results (the 5 Re:ZERO arcs share "Re:ZERO -Starting Life in Another World-"). Unrelated
   results don't share it, so they stay ungrouped. Heuristic — see Risks.

### Frontend (Add New page)
5. Group results by `franchiseGroup` into a collapsible header ("Re:ZERO (5 related series)") + an
   **"Add all"** button. Ungrouped results render as normal single cards (no behavior change).
6. **"Add all" reuses the existing bulk endpoint** `POST /api/v1/author/import` (built for Library
   Import). The **normalized-title dedup guard** (`AddAuthorService.SetPropertiesAndValidate`,
   commit e0c2e6f) automatically skips any arc already in the library — no new add plumbing, and
   no duplicates even if the per-result "already added" marker is stale.

## Risks / caveats to handle during the build

- **Common-case regression:** returning N results changes every search. Mitigate by ranking the exact/best
  match first and only showing the grouped "Add all" block when a multi-member franchise group is detected.
- **Grouping false positives:** a naive title-prefix would group "My Hero Academia" with "…: Vigilantes"
  (a spin-off the user wants kept separate) and offer to Add-all. Need a stricter signal — require the
  differing suffix to look like an arc marker ("Chapter N", "Part N", "Season N", a numbered/sequential
  pattern) rather than a distinct spin-off subtitle. MangaDex **relations** (related-manga with relation
  types) would be the authoritative signal; bigger lift, best future refinement.
- **"Already in library" ✓ marker** keys on exact `foreignAuthorId`; legacy entries created under
  slightly different slugs may not show the ✓ (cosmetic — dedup still blocks the actual duplicate).
- **Performance:** building N structure-only fake authors is N× the work; keep it strictly structure-only
  on the search path (the per-volume resolve stays on add/refresh).

## Related architectural debt (ideal precursor)

The real root cause of the identity fragility is that `foreignAuthorId` is `local-`+slug of the **search
term**, not a stable per-series id from a provider (AniList id / MangaDex UUID / MangaUpdates id — all
available but unused). The 2026-06-29 fixes (normalized-title dedup `e0c2e6f`, name-preserve guard
`9e937d1`) patch the symptoms. A **canonical-series-id redesign** — anchor identity to a provider's
stable id, migrate existing `local-*` ids — would make this feature (and "already added" markers, and
refresh stability) clean rather than heuristic. Worth doing first, or alongside, the franchise feature.

## Verification (when built)

- Unit: multi-result merge/dedupe; franchise-group key (arc-markers group, spin-offs don't).
- Throwaway/live: search "re:zero" → 5 arcs grouped with Add-all; Add-all adds missing arcs, skips
  existing (dedup); searching a single series ("Solo Leveling") still shows it cleanly without noise;
  "My Hero Academia" does NOT offer to Add-all the Vigilantes spin-off.
