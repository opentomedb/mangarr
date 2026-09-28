// The upstream link (2026-09-18): a "Wrong fact" issue on the OpenTome catalogue, pre-filled with
// what the page shows (the series name, or "<series> Vol. N" from a volume page) and the entry's
// AniList binding when it has one. The catalogue covers both libraries, so every entry gets it.
export default function suggestCorrectionUrl(title, aniListId) {
  return 'https://github.com/opentomedb/opentome/issues/new?template=wrong-fact.yml' +
    `&title=${encodeURIComponent(title)}` +
    `&anilist_id=${aniListId || ''}`;
}
