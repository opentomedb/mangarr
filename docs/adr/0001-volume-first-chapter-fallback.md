# ADR 0001 — Volume-first acquisition, chapter as a fallback (no Chapter entity)

- **Status:** Accepted
- **Date:** 2026-05-31
- **Deciders:** the maintainer, with analysis from a codebase-wide state map

## Context

Mangarr is a Readarr fork where **Book = Volume**. The owner wants manga acquisition to behave like
Sonarr/Radarr: prefer a full **volume**, but for ongoing/current series fall back to grabbing
**individual chapters** when no volume release exists yet.

A codebase-wide audit (four independent readers + direct confirmation) established that the existing
code is **strictly volume-first with no Chapter concept anywhere** — no Chapter model, no chapter
field on `ParsedBookInfo`, no chapter parser branch, no chapter grab unit, no chapter UI. The only
occurrences of the word "chapter" are comments stating chapters are deliberately NOT tracked.

The decision is how to satisfy "chapter fallback" without breaking the volume-first design.

## Decision

**Do not introduce a Chapter entity, and do not make `VolumeNumber` fractional.**

Implement chapter-fallback purely as a **parse-time classification** consumed only by the grab
planner:

1. Add a transient `IsChapterOnly` flag on `ParsedBookInfo`, set by `MangaVolumeParser` when a
   release names a chapter ("Ch. 152", "Chapter 200", "#7") with **no** volume marker.
2. The planner **prefers full-volume releases**. It accepts a chapter-only release **only** as a
   fallback that fills the **current/ongoing volume slot** when no volume release exists for it.
3. **Volume remains the only persisted, monitored, tracked unit.** Chapters are never stored as
   their own entity and never get independent progress tracking.

## Consequences

**Positive**
- No DB migration, no new tables, no new UI — the smallest change that satisfies the ask.
- Preserves byte-compatibility with Readarr's schema and the "everything exact, small additive
  changes" principle.
- Reversible: it is pure parse + planner logic.

**Negative / limits**
- No per-chapter progress or reading state. If the owner later wants to *track* individual chapters
  (not just grab them as a stopgap), that is a new decision and would supersede this ADR — it would
  require the Chapter entity, migration, parser, grab unit, and UI deliberately rejected here.

## Alternatives considered

- **Real Chapter entity** (Chapter model + migration + per-chapter monitoring + UI rows under each
  volume, like Sonarr episodes under a season): rejected as far larger, schema-breaking, and beyond
  the actual ask (fallback *grabbing*, not chapter *tracking*).
- **Fractional `VolumeNumber`** (e.g. 7.5 for a chapter): rejected — pollutes the volume model and
  the monitoring/sort logic that keys off integer `VolumeNumber`.

## Status of implementation

The classification flag and planner rule (Increment 4c) are **deferred** until a live
Prowlarr/Torznab manga indexer is configured, because they can only be meaningfully verified against
real release titles rather than mocked assumptions. Increment 4a (pack fan-out) is built and
verified; this ADR records the decision that governs 4c when it is built.
