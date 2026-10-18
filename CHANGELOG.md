# Changelog

Mangarr's user-facing changes, newest first. Each beta image also carries a build number
(`10.0.0.N`, shown in System → Status); the release notes on GitHub give both.

## 0.1.0-beta.2

### Matching

- **A manga series takes the catalogue's manga line** even when a light-novel line carries the
  series' exact name and the manga line has it only as an alternate name. Light novels likewise
  prefer novel lines. Existing series move to the right line on their next refresh.
- A light novel whose catalogue line is named "… (novel series)" is found again on refresh;
  before, the entry stayed empty.
- When you kept your own name for a series, AniList's English title is now one of its alternate
  names, so releases named after the English title are no longer rejected as another series.

### Downloads

- A release that was already imported is not grabbed again when another indexer lists it under a
  slightly different name.
- A finished download whose title is in a language the series doesn't take is failed
  (blocklisted, then searched again) instead of imported. In search results, a release Mangarr
  can't parse is still checked against the series' language rule, and that reason shows first.

### Editions

- **Korean and Chinese editions.** Volume labels (5권, 第5卷), release-name parsing and search
  tokens, like French, German and Japanese ones. For a series whose catalogue line is a manhwa or
  manhua, AniList's country of origin guards the binding, so a same-titled Japanese entry is not
  picked by mistake. Manhwa and manhua totals count print volumes only, so webtoon seasons no
  longer appear as Coming Soon rows.
- **Release names in Korean or Chinese** (Hangul, or a volume marker such as 第5卷) now count as
  that language: an English series rejects them, as it already did for Japanese names, and a
  Korean or Chinese edition accepts them.
- Existing Korean- or Chinese-edition series keep their names; on their next refresh their volume
  titles (and Calibre titles) read 5권 / 第5卷 instead of Vol. 5.

### PDFs

- PDF originals: choose the folder they are kept in (a dedicated empty folder) and optionally delete them after N days (Media Management). Defaults are unchanged.
- The nightly PDF sweep leaves alone a PDF it refused (not one image per page) until the file
  changes; the volume's own Convert action still tries it. A PDF whose CBZ already exists is
  skipped before anything is extracted.

### Health

- When a root folder can't be written to, the log now says why.

## 0.1.0-beta.1

The first public beta. Everything below is new compared with Readarr.

### Libraries

- **Manga library.** A series has volumes, each a single archive: CBZ, CBR, ZIP, RAR or PDF.
  Volume packs ("Vol. 1-13") map to every volume they hold.
- **Light-novel library.** Each volume has an ebook edition (EPUB, with AZW3 as a fallback and
  PDF as an opt-in) and an audiobook edition (M4B, FLAC, MP3), searched and imported separately
  with their own profiles, **Light Novel EPUB** and **Light Novel Audio**.
- The series and volume lists, Add New, Library Import, Collections and Wanted have **Manga |
  Light Novels** tabs; Wanted also filters light novels by edition (ebook, audiobook).
- **Collections** group franchises published as several series (arcs, spin-offs, side stories)
  so you can add them together.
- **Library Import** suggests the right library and root folder for each folder.

### Metadata

- **OpenTome catalogue.** Per-volume release dates, ISBNs and page counts from an open catalogue,
  downloaded on first start and kept up to date automatically (System → Tasks → Metadata Update
  checks now).
- Live sources layered on top: AniList (matching, covers, synopsis, ratings), MangaDex (volume
  counts of ongoing series, English covers), MangaUpdates, Google Books (dates, ISBNs, blurbs,
  covers) and Open Library.
- **English covers first.** Volume covers and series posters prefer the English edition's art.
- **Fix Match** changes which AniList entry a series is matched to.
- **Pins.** Fix a volume's release date, page count, ISBN or cover yourself; a pin survives every
  refresh.
- **Suggest a Correction** on series and volume pages opens a prefilled OpenTome report.
- Light-novel audiobooks are identified from the Audible catalogue: title, runtime and release
  date. An audiobook released inside another volume's file shows as "Covered by Vol. N". An
  audiobook that isn't out yet isn't counted as missing.

### Editions

- **Preferred Edition** (Settings → UI → Language): collect the French, German or Japanese
  edition of new series instead of the English one: its volume count, dates, ISBNs, covers and
  local name, and only releases in that language.
- **Change Edition** switches an existing series to another market edition.
- English series reject releases tagged with another language.

### Files

- **Flat volume names.** New installs rename every volume into its series folder as
  `<Series> - Vol. 01.cbz`, the layout Komga and Kavita read as one series. Light-novel volumes
  keep a `<Series> - Vol. N` folder each for the ebook and audiobook.
- **ComicInfo.xml** is written into downloaded CBZ files (Settings → Metadata → Write ComicInfo
  To: New Downloads, All Imports or Never).
- **PDF to CBZ.** Downloaded manga PDFs are converted to CBZ on import; an optional daily task
  converts the rest of the library (originals are kept in `/config/pdf-originals`).
- **Light-novel storage.** Ebooks can go to a Calibre library and audiobooks to an Audiobookshelf
  library instead of the series folder, with a Test button and health checks. Calibre can add a
  Kindle (AZW3) or Kobo (KEPUB) copy beside the EPUB.
- **Import Existing** registers the light novels you already have in Calibre and Audiobookshelf,
  in place.
- Audiobook tags are written additively: only title, album and series, so chapters and your own
  tags survive.
- Backups include your metadata pins.

### Interface

- UI in **French, German and Japanese** besides English (drafts, not yet reviewed by native
  speakers), including server messages: rejection reasons, queue warnings, task progress.
- Series and volume pages rebuilt around volumes, with per-volume covers and badges, and a
  phone-friendly layout.
- **New release notice.** System → Status shows a warning when a newer Mangarr release is out on
  GitHub, with a link to it. Nothing is downloaded or installed. Settings → General → Updates →
  **Check for New Releases** turns the check off.

### Privacy

- **No telemetry.** Readarr's crash reporting and analytics are removed, and Mangarr no longer
  contacts Readarr's servers.

### Import lists

- **MangaDex Follows** adds the series you follow on MangaDex.
