import translate from 'Utilities/String/translate';

// Series page round (2026-09-24): the series page lists a volume without the series name it
// already shows -- "Vol. 1", or "Vol. 1: <subtitle>" for a light novel whose one display title
// (LightNovelTitles.Display) carries one. Full-title subtitles (2026-09-24): the subtitle is the
// API's displaySubtitle (LightNovelTitles.DisplaySubtitleOf, the junk-filtered one calibre and
// Audiobookshelf show, which may be a whole title such as "Rascal Does Not Dream of Petite Devil
// Kohai"); "" = a light novel with no subtitle ("Vol. N"). The API drops null fields, so a
// missing displaySubtitle falls back to reading the subtitle out of a "<series>: <subtitle>
// (Vol. N)" display title, as before; any other shape reads "Vol. N". No volume number keeps
// the full title.
export default function volumeRowTitle({ title, displayTitle, displaySubtitle, volumeNumber }, seriesName) {
  if (volumeNumber == null) {
    return title;
  }

  if (displaySubtitle != null) {
    return displaySubtitle ?
      translate('VolumeRowTitleWithSubtitle', { volume: volumeNumber, subtitle: displaySubtitle }) :
      translate('VolumeRowTitle', { volume: volumeNumber });
  }

  const prefix = `${seriesName}: `;
  const suffix = (/ \(Vol\. [^()]+\)$/).exec(displayTitle || '');

  if (seriesName && suffix && displayTitle.startsWith(prefix) && suffix.index > prefix.length) {
    return translate('VolumeRowTitleWithSubtitle', {
      volume: volumeNumber,
      subtitle: displayTitle.slice(prefix.length, suffix.index)
    });
  }

  return translate('VolumeRowTitle', { volume: volumeNumber });
}
