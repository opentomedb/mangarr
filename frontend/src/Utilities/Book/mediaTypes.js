import translate from 'Utilities/String/translate';

// Browser mirror of NzbDrone.Core.Books.MediaTypes. A book file resource carries no media type
// (the backend derives it from the extension, contract "Media type"), and a queue item only
// carries a quality, so the UI derives the media type the same two ways. Values are the API's
// camelCase enum strings.
export const ARCHIVE = 'archive';
export const EBOOK = 'ebook';
export const AUDIO = 'audio';

const EBOOK_EXTENSIONS = ['.epub', '.mobi', '.azw', '.azw3', '.kepub'];
const AUDIO_EXTENSIONS = ['.mp3', '.m4b', '.m4a', '.flac', '.aac', '.ogg', '.opus', '.wma'];
const AUDIO_QUALITIES = ['MP3', 'FLAC', 'M4B', 'Unknown Audio'];

// MediaTypes.OfExtension: .epub/.mobi/.azw/.azw3/.kepub -> ebook; audio extensions -> audio; else archive.
export function mediaTypeOfPath(path) {
  const match = (/\.[a-z0-9]+$/i).exec(path || '');
  const extension = match ? match[0].toLowerCase() : '';

  if (EBOOK_EXTENSIONS.includes(extension)) {
    return EBOOK;
  }

  if (AUDIO_EXTENSIONS.includes(extension)) {
    return AUDIO;
  }

  return ARCHIVE;
}

// MediaTypes.OfFile (2026-09-22): a light novel's .pdf is its ebook (quality Ebook PDF); every
// other file, and a manga .pdf, is mediaTypeOfPath. library is the API string ('lightNovel').
export function mediaTypeOfFile(path, library) {
  if (library === 'lightNovel' && (/\.pdf$/i).test(path || '')) {
    return EBOOK;
  }

  return mediaTypeOfPath(path);
}

// MediaTypes.OfQuality: EPUB/AZW3/Ebook PDF -> ebook; MP3/FLAC/M4B/Unknown Audio -> audio; everything else archive.
export function mediaTypeOfQuality(qualityName) {
  if (qualityName === 'EPUB' || qualityName === 'AZW3' || qualityName === 'Ebook PDF') {
    return EBOOK;
  }

  if (AUDIO_QUALITIES.includes(qualityName)) {
    return AUDIO;
  }

  return ARCHIVE;
}

export function mediaTypeLabel(mediaType) {
  // UI translations v1 (2026-09-25): CBZ is a format name; Ebook/Audiobook are words.
  switch (mediaType) {
    case ARCHIVE:
      return 'CBZ';
    case EBOOK:
      return translate('QualityClassEbook');
    case AUDIO:
      return translate('QualityClassAudio');
    default:
      return mediaType;
  }
}
