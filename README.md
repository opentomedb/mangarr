# Mangarr

Mangarr finds, downloads and organises **manga** and **light novels** the way Sonarr does for TV.
You add a series, choose which volumes you want, and Mangarr searches your indexers, sends
releases to your download client, imports the files and names them.

It is a fork of [Readarr](https://github.com/Readarr/Readarr), so the screens, settings and
workflow are the ones you know from Sonarr, Radarr and Readarr. In Mangarr's terms a Readarr
author is a **series** and a book is a **volume**.

- **Two libraries.** Manga (CBZ, CBR, ZIP, RAR and PDF) and light novels. Each light-novel volume
  has an ebook edition (EPUB) and an audiobook edition (M4B, MP3, FLAC).
- **Per-volume metadata.** Release dates, page counts, ISBNs, covers and blurbs come from the
  [OpenTome catalogue](#the-opentome-catalogue) plus AniList, MangaDex, MangaUpdates, Google
  Books and Open Library.
- **Collections.** Franchises that are published as several series (arcs, spin-offs, side
  stories) are grouped, so you can add them together.
- **Preferred Edition.** Collect the French, German or Japanese edition of a series instead of the
  English one.
- **Light-novel storage.** Ebooks and audiobooks can go into the series folder, or straight into
  a Calibre library and an Audiobookshelf library.
- **UI in English, French, German and Japanese.**

## Beta status

This is the first public beta. It runs every day on the maintainer's server, but you are one of
its first outside users. Back up your library before you point Mangarr at it, expect rough
edges, and please [report what you find](#reporting-bugs). Mangarr does not update itself:
watch the [releases](https://github.com/opentomedb/mangarr/releases) or Discord #beta for new
images. See [Known limitations](#known-limitations) before you start.

## Install

Mangarr ships as a Docker image: `ghcr.io/opentomedb/mangarr:beta`.

The container keeps its settings and database in `/config`. You also mount your manga folder,
your light-novel folder and the download client's download folder. The example below uses
`/data/manga`, `/data/lightnovels` and `/downloads` inside the container; you can choose other
paths, and you tell Mangarr which ones to use in the first-run steps.

| Setting | What it's for |
|---|---|
| Port `8787` | The web UI. It's the same port as Readarr, so if you run Readarr too, map another host port (`8788:8787`). |
| `PUID` / `PGID` | The user and group Mangarr runs as. The image default is `99` / `100` (Unraid's nobody:users). On most Linux hosts use your own user, usually `1000` / `1000`. |
| `UMASK` | Permissions for new files. Default `002`. |
| `TZ` | Your time zone, for example `America/Chicago`. |
| `GOOGLE_BOOKS_API_KEY` | Optional. See [Google Books key](#google-books-key-optional). |

On start the container takes ownership of `/config` (as `PUID:PGID`). It doesn't change the
permissions of your media or download folders, so those must already be writable by that user.

### Docker Compose

Copy [`docker-compose.example.yml`](docker-compose.example.yml) to `docker-compose.yml`, change
the host paths, then run `docker compose up -d`.

### docker run

```sh
docker run -d --name mangarr \
  -p 8787:8787 \
  -e PUID=1000 -e PGID=1000 -e TZ=America/Chicago \
  -v /path/to/mangarr/config:/config \
  -v /path/to/manga:/data/manga \
  -v /path/to/lightnovels:/data/lightnovels \
  -v /path/to/downloads:/downloads \
  --restart unless-stopped \
  ghcr.io/opentomedb/mangarr:beta
```

### Unraid

There is no Community Applications template yet. Add it from the Docker tab with **Add
Container**: repository `ghcr.io/opentomedb/mangarr:beta`, port `8787`, and a path for
`/config` (your appdata folder for Mangarr) plus the manga, light-novel and download paths above.
The image's default `PUID=99` / `PGID=100` already matches Unraid, so you don't need to set them.

### Download paths

Mangarr imports a finished download from the path your download client reports. If the
download client runs in another container, mount the downloads folder at the **same path** in
both containers (for example `/downloads` in each). If the paths differ, add a Remote Path
Mapping under Settings → Download Clients.

### Updating

Pull the new image and recreate the container (`docker compose pull && docker compose up -d`).
On Unraid, use the Docker tab's update. Watchtower works too. Mangarr never updates itself,
and System → Updates doesn't list these images' changes; the release notes are on GitHub.

## First steps

Open `http://<your-server>:8787`.

1. **Sign-in.** The first page is the **Authentication Required** dialog. Pick an Authentication
   Method (**Forms (Login Page)** or **Basic (Browser Popup)**), choose whether it applies to
   every request (**Enabled**) or not to your local network (**Disabled for Local Addresses**),
   set a username and password, and save.

2. **Root folders: one per library.** Go to Settings → Media Management → Root Folders → **Add
   Root Folder** and add two:
   - the manga folder (`/data/manga`), with Quality Profile **Manga**;
   - the light-novel folder (`/data/lightnovels`), with Quality Profile **Light Novel EPUB**.

   Mangarr recognises the light-novel root by its default quality profile, so this profile
   choice matters. Leave **Use Calibre Content Server** off on both; light novels reach Calibre
   through Light Novel Storage (below). The profiles are under Settings → Profiles:
   **Manga** (CBZ first), **Light Novel EPUB** (EPUB, AZW3) and **Light Novel Audio** (M4B
   first, then FLAC and MP3).

3. **Indexers through Prowlarr.** In Prowlarr, go to Settings → Apps → **+** and choose
   **Readarr** (Mangarr speaks Readarr's API). Set the Readarr Server to Mangarr's address, for
   example `http://mangarr:8787`, and paste Mangarr's API key from Settings → General → Security
   → API Key. In **Sync Categories**, make sure **Books/Comics (7030)** is selected; that's where
   indexers put manga. Keep the ebook and audiobook categories for light novels. Prowlarr then
   adds its indexers to Mangarr. You can also add Newznab or Torznab indexers directly under
   Settings → Indexers; their default categories don't include 7030, so add it.

4. **A download client.** Settings → Download Clients → **+**. Give Mangarr its own category
   (for example `mangarr`) and check the [download paths](#download-paths).

5. **Recommended.**
   - Settings → Media Management → Volume Naming → **Rename Volumes**. Audiobookshelf storage
     needs it on.
   - A **Recycling Bin** (Settings → Media Management → File Management, under **Show
     Advanced**). Without one, a replaced or deleted file is deleted for good.
   - If Komga or Kavita reads your manga folder, set **Standard Volume Format** to a flat name,
     for example `{Author Name} - Vol {Volume:000}`. The default puts each volume in its own
     subfolder, which those readers can show as separate series. Naming tokens keep Readarr's
     names: `Author` tokens are the series and `Book` tokens are the volume.

Then use Library → **Add New** (with its Manga | Light Novels tabs) to add a series, or Library
→ **Library Import** to bring in folders you already have.

## Manga and light novels

The two libraries sit side by side: the series and volume lists, Add New, Library Import,
Collections and Wanted all have **Manga | Light Novels** tabs.

A **manga** series has one file per volume, in the series folder.

A **light novel** series has two editions per volume: the ebook and the audiobook. Each is
searched, grabbed and imported on its own, with the **Light Novel EPUB** and **Light Novel
Audio** profiles. Audiobook details (title, runtime, release date) come from the Audible US
catalogue. A volume whose audiobook is released as part of another volume's file shows as
**Covered by Vol. N** and isn't searched on its own.

You can only add a light novel that's in the OpenTome catalogue. If Add New says **Not in the
OpenTome catalogue yet**, use its link to suggest it.

### Light-novel storage

Settings → Media Management → **Light Novel Storage** decides where new light-novel files go.
Each kind has its own card:

- **Ebooks Go To**: **Entry Folder** (the default: the series folder in your light-novel root)
  or **Calibre Library**. Calibre needs its **Content Server** (not Calibre-Web or the desktop
  app), its URL, a user with write access (or this container listed in Calibre's
  `trusted_ips`), and the library. Calibre can also add a converted copy (AZW3 for Kindle,
  KEPUB for Kobo) beside the EPUB.
- **Audiobooks Go To**: **Entry Folder** or **Audiobookshelf Library**. Audiobookshelf needs its
  URL, an API key, the library, **Rename Volumes** on, and its library folder mounted into
  Mangarr.

If Calibre or Audiobookshelf sees the library at a different path than Mangarr does, fill in
**Path As Calibre Sees It** / **Path As Audiobookshelf Sees It** and **Path As Mangarr Sees It**
(under **Show Advanced**). The **Test** button checks the whole setup, and a health warning
appears if it breaks later. These settings affect new imports only.

Already have light novels in Calibre or Audiobookshelf? The light-novel series page's **Import
Existing** button (or System → Tasks → Import Existing Light Novels) registers them where they
are. Nothing is copied or moved, and Mangarr leaves those files alone afterwards; it only sets
the display title in Calibre and the series and title in Audiobookshelf.

### Settings that change your files

- **Write ComicInfo To** (Settings → Metadata): which CBZ files get a `ComicInfo.xml` written
  into them. The default, **New Downloads**, only touches files from your download client.
  **All Imports** also rewrites archives you already owned when a scan or Library Import brings
  them in.
- **Convert PDFs to CBZ Daily** (Settings → Media Management → File Management), off by
  default: converts every manga PDF in the library to CBZ once a day. Originals are moved to
  `pdf-originals` in `/config`, never deleted. Downloaded manga PDFs are converted on import
  either way.

## The OpenTome catalogue

[OpenTome](https://opentomedb.com) is an open catalogue of manga and light-novel editions:
which volumes exist, their release dates, ISBNs and page counts, and in which markets. Mangarr
uses it for per-volume dates and ISBNs, Collections, Preferred Edition markets, and to know
which light novels exist.

- **First start.** The image doesn't include the catalogue. Mangarr downloads it (about 36 MB)
  within a few minutes of its first start and retries every hour until it has it. Until then,
  manga works from the live sources only and light novels can't be added. Settings → Metadata
  Source shows the loaded version and the last check.
- **Updates.** A new catalogue is published roughly weekly. With **Automatic Updates** on (the
  default), Mangarr checks once a day and installs a newer one. To check right away, run
  System → Tasks → **Metadata Update**.
- **Licence.** The catalogue data is licensed [CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/)
  (non-commercial), and some of its sources add their own terms; Settings → Metadata Source
  shows the attribution. Mangarr's code is GPLv3; the catalogue is a separate download.
- **Corrections.** Wrong date, missing volume, wrong title? Use **Suggest a Correction** on the
  series or volume page. It opens a prefilled form on the OpenTome GitHub (you need a GitHub
  account). Without an account, post it in **#corrections** on the
  [Discord](https://discord.gg/bQVwv54KdP). Fixes arrive with the next catalogue.

## Known limitations

- **Volumes only.** Mangarr grabs volumes and volume packs, never chapter releases. A series
  that's only released as chapters stays in Wanted.
- **Packs are strict.** A pack is rejected if any volume in it already has a file that the pack
  wouldn't upgrade, as in Sonarr with season packs. To take one anyway, grab it from
  Interactive Search.
- **Release language.** An English series rejects a release tagged with another language, and
  a French, German or Japanese edition series only takes releases tagged with its language. An
  untagged release counts as English, so a mislabelled foreign release can still be grabbed for
  an English series.
- **Collected editions in other markets.** For a French, German or Japanese edition series,
  numbered collected releases (Intégrale, Coffret, Box Set, Doppelband, Perfect Edition…) are
  rejected unless the series is itself that collected edition. A series whose catalogue line
  is omnibus-only but whose names don't say so can't grab them automatically.
- **File formats.** Manga imports CBZ, CBR, ZIP, RAR and PDF. Light-novel ebooks import EPUB and
  AZW3 (PDF only if you tick Ebook PDF in the profile). `.mobi`, `.azw` and `.kepub` files are
  never imported.
- **Change Edition renames manga only.** Switching a light novel to another edition keeps its
  name and folder.
- **Translations are drafts.** The French, German and Japanese UI hasn't been reviewed by native
  speakers yet. Other UI languages are Readarr's partial translations.
- **No in-app updates.** See [Updating](#updating).
- **Audiobook data is from the Audible US store.**
- **arm64.** Images are built for amd64 and arm64. If the arm64 image fails its start-up test
  when a release is built, that release ships amd64 only, and its release notes say so.

## What Mangarr contacts

Mangarr has no telemetry, no analytics and no update server. It talks to:

- **AniList** (`graphql.anilist.co`): series matching, covers, synopsis, ratings, final volume
  counts.
- **MangaDex** (`api.mangadex.org`, `uploads.mangadex.org`): live volume counts for ongoing
  series and English covers.
- **MangaUpdates** (`api.mangaupdates.com`).
- **Google Books** (`www.googleapis.com`): per-volume release dates, page counts, ISBNs,
  descriptions and covers. Works without a key, at a low shared quota.
- **Open Library** (`openlibrary.org`): dates and covers Google doesn't have.
- **Audible** (`api.audible.com`): light-novel audiobook details, from the US store's public
  catalogue API (unofficial).
- **The catalogue**: `opentomedb.com/catalogue/version.json`, falling back to the same file on
  GitHub, and the catalogue download itself from GitHub releases. A proxy health check probes
  the same address when you use a proxy.
- The image hosts those sources point to, to download covers.
- Everything you configure yourself: indexers, Prowlarr, download clients, Calibre,
  Audiobookshelf, notifications (Settings → Connect) and import lists such as MangaDex Follows.

Lookups are cached in `/config/metadata` (Google Books ISBN records and Audible answers), so a
volume isn't asked about again on every refresh.

### Google Books key (optional)

Without a key, Google's anonymous quota runs out quickly on a large library, and volumes the
catalogue doesn't cover get fewer dates, blurbs and covers. To get a free key:

1. In the [Google Cloud console](https://console.cloud.google.com/), create a project.
2. Under APIs & Services → Library, enable the **Books API**.
3. Under APIs & Services → Credentials, choose Create credentials → **API key**. Restricting the
   key to the Books API is a good idea.

Paste it into Settings → Metadata Source → **Google Books API Key**, or set the
`GOOGLE_BOOKS_API_KEY` environment variable. The setting wins when both are set.

## Reporting bugs

- **Bugs:** open a [GitHub issue](https://github.com/opentomedb/mangarr/issues/new/choose).
  The form asks for a **trace log**: set Settings → General → Logging → Log Level to **Trace**,
  reproduce the problem, and attach `mangarr.trace.txt` from System → Log Files (or
  `/config/logs/`). **Read the log first.** It lists your series, folder paths and indexers;
  keys and passwords are masked, but check before you post. Set the level back to Info
  afterwards.
- **Questions and chat:** Discord **#beta**, <https://discord.gg/bQVwv54KdP>.
- **Catalogue data:** see [Corrections](#the-opentome-catalogue).
- **Security problems:** privately, see [SECURITY.md](SECURITY.md).

## Licence and source

Mangarr is free software under the [GNU General Public License v3](LICENSE.md).

It is a modified version of [Readarr](https://github.com/Readarr/Readarr) (GPLv3, © the Readarr
and Servarr contributors). The Mangarr changes started on 2026-06-01 and are listed in
[CHANGELOG.md](CHANGELOG.md). With thanks to the Servarr team.

Source code: <https://github.com/opentomedb/mangarr>. Each image's
`org.opencontainers.image.revision` label names the commit it was built from. To build or
contribute, see [CONTRIBUTING.md](CONTRIBUTING.md).

The OpenTome catalogue data is not part of this repository or the image. It is licensed
separately under CC BY-NC 4.0 (see [The OpenTome catalogue](#the-opentome-catalogue)).
