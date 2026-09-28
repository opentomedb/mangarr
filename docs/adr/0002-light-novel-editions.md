# ADR 0002 — Light novels as one entry with an EPUB edition and an Audio edition

- **Status:** Accepted
- **Date:** 2026-09-14 (accepted 2026-09-15, live)
- **Deciders:** the maintainer, with the v1 → v2 design comparison in
  `docs/superpowers/specs/2026-09-14-light-novel-libraries-design.md`

## Context

Mangarr needed a second library (light novels) where every series is wanted in two formats,
EPUB for Calibre and audio for Audiobookshelf. v1 encoded the format in the foreign id
(`~ln`, `~audio`) and made a light novel two entries; the seam showed everywhere (two rows in
history, two things to edit, duplicates in Wanted). Chaptarr's answer (per-format columns on
the author and on every book) hardcodes exactly two formats.

## Decision

One entry per catalogue line. The library type is derived from the id (`local-<slug>` manga,
`local-<slug>~ln` light novel). Formats are **editions**: a light-novel volume is minted with an
EPUB edition (`<bookId>-ed`, `MediaType = Ebook`) and an Audio edition (`<bookId>-audio-ed`,
`MediaType = Audio`); `Edition.MediaType` is a real column (migration 048). Readarr already
keys files by edition, so storage is unchanged; every per-book check (has-file, cutoff, wanted,
statistics, search, import gate) became per edition, with "at most one monitored edition per
(book, media type)" replacing "one monitored edition per book". A second quality profile
(`AudioQualityProfileId`) and a per-series `AudioAvailable` flag ("not yet" until the first
audio file arrives) live on the author. Naming for the light-novel library is one folder per
volume so Audiobookshelf reads the EPUB as the item's e-book.

## Consequences

- Manga volumes have one edition and behave byte-for-byte as before (fixtures kept green).
- A third format later is an edition media type, not a schema change.
- The catalogue is the only source for light novels: a title without an English LN line is
  refused with a link to OpenTome's edition form (no live-source fallback).
- The UI shows the formats as tabs on the series page and as two progress rows on posters;
  the Wanted / Calendar rows badge the media type.
- Existing EPUBs (Calibre) and audiobooks (Audiobookshelf) are copied in by a command that
  reads both read-only; originals are never moved.

## Verified on live 2026-09-15

Deployed as build 10.0.0.219 after a staging pass (migration 048 on a copy of the live DB, the
44 manga entries re-listed unchanged, Sword Art Online added as the first light novel with both
editions, the copy-in pass 47 copied / 4 skipped / 0 unmatched / 0 errors). The first real add
and search surfaced three incident fixes, each deployed the same day: **A** — a light novel
looks the catalogue up by its raw name before AniList's manga-only relaxed match and sheds the
catalogue's "(novel series)" qualifier (staging fixes 1–3b, in 219); **B** —
`FranchiseLineSpecification` rejects a release whose series extends the author's name
("Sword Art Online - Progressive" for "Sword Art Online"; 10.0.0.220); **C** — the audiobook
marker gate in `NonVolumeContentSpecification` skips the Audio search leg (10.0.0.221). The
Audio leg searches with category 3030 only; the EPUB leg with the book categories only.

Two documented limitations, unchanged by this decision:

- The franchise guard judges the series part of a release name, so it rejects an arc name that
  appears before the volume token; a franchise line that is its own catalogue entry (Sword Art
  Online: Progressive) cannot be obtained through the "SAO - Progressive - Volume 02" shape until
  the `ParsingService.GetAuthor` name guard (`:294`) becomes suffix-aware.
- That `:294` name guard is inert for light-novel authors until then.
