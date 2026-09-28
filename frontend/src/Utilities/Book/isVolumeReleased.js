// A volume counts as released (so a missing file is grabbable and shown as "Missing") when its
// release date is in the past OR unknown. Not released is reserved for a known FUTURE date: the
// series page greys those rows. Manga volumes often have no date from the metadata sources, so a
// missing date must not read as unreleased. Release dates are calendar dates (UTC midnight):
// compare them as local dates or everything shifts a day in negative-offset timezones.
export default function isVolumeReleased(releaseDate) {
  return !releaseDate || new Date(`${releaseDate.slice(0, 10)}T00:00:00`) <= new Date();
}
