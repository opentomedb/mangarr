import { ARCHIVE, AUDIO, EBOOK, mediaTypeOfQuality } from 'Utilities/Book/mediaTypes';

// A profile's class is the media type of every quality it allows (MediaTypes.OfQuality); a
// profile that allows nothing, or mixes classes, has none.
export function profileClass(profile) {
  const classes = new Set();

  ((profile && profile.items) || []).filter((item) => item.allowed).forEach((item) => {
    // A group carries its qualities as items; a single quality carries its own.
    (item.quality ? [item] : item.items || []).forEach((i) => classes.add(mediaTypeOfQuality(i.quality.name)));
  });

  return classes.size === 1 ? [...classes][0] : null;
}

// The old rule, kept only as the fallback: a root whose path names light novels.
const LIGHT_NOVEL_PATH = /lightnovel/i;

function rootClass(rootFolder, qualityProfiles) {
  return profileClass((qualityProfiles || []).find((p) => p.id === rootFolder.defaultQualityProfileId));
}

// Beta readiness (2026-09-28, F4): a root folder has no library field, so the light-novel root is
// the one whose default quality profile is Ebook- or Audio-class -- the class rule Library Import
// uses (libraryImportDefaults) -- and only then the first root whose path names light novels.
// Several roots of that class (review M5): the one whose path names light novels, else the first.
export function lightNovelRoot(rootFolders, qualityProfiles) {
  const items = rootFolders.items || [];
  const byClass = items.filter((r) => [EBOOK, AUDIO].includes(rootClass(r, qualityProfiles)));

  return byClass.find((r) => LIGHT_NOVEL_PATH.test(r.path)) ||
    byClass[0] ||
    items.find((r) => LIGHT_NOVEL_PATH.test(r.path));
}

// The manga root: never the light-novel root; the first other root with an Archive-class default
// profile, else the first other root whose path doesn't name light novels (the old rule).
export function mangaRoot(rootFolders, qualityProfiles) {
  const lightNovel = lightNovelRoot(rootFolders, qualityProfiles);
  const others = (rootFolders.items || []).filter((r) => r !== lightNovel);

  return others.find((r) => rootClass(r, qualityProfiles) === ARCHIVE) ||
    others.find((r) => !LIGHT_NOVEL_PATH.test(r.path));
}

export function libraryRoot(rootFolders, qualityProfiles, isLightNovel) {
  return isLightNovel ? lightNovelRoot(rootFolders, qualityProfiles) : mangaRoot(rootFolders, qualityProfiles);
}
