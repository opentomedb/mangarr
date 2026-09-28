import { metadataProfileNames } from 'Helpers/Props';
import sortByName from 'Utilities/Array/sortByName';
import { LIGHT_NOVEL } from 'Utilities/Author/libraries';
import { profileClass } from 'Utilities/Author/libraryRoots';
import monitorNewItemsOptions from 'Utilities/Author/monitorNewItemsOptions';
import monitorOptions from 'Utilities/Author/monitorOptions';
import { ARCHIVE, EBOOK } from 'Utilities/Book/mediaTypes';

function firstValid(list, ...ids) {
  return ids.find((id) => !!id && list.some((p) => p.id === id)) || 0;
}

function firstKey(options, ...keys) {
  return keys.find((key) => !!key && options.some((o) => o.key === key)) || options[0].key;
}

function firstByName(list) {
  const sorted = [...list].sort(sortByName);

  return sorted.length ? sorted[0].id : 0;
}

// The starting values for every row of one root folder's import (and the footer): the root
// folder's own defaults first, then the tab's library defaults -- the blob the Add modal
// remembers (search.authorDefaults / lightNovelDefaults) and its first-time seeds by profile name
// (Search/Author/AddNewAuthorModalContentConnector). Each id is checked against the profiles that
// exist, so a deleted profile never reaches the payload. The metadata profile has no column: it is
// what the Add modal's hidden select would post (never None, which a series may not have).
export function libraryImportDefaults(rootFolder, library, searchState, qualityProfiles, metadataProfiles) {
  const isLightNovel = library === LIGHT_NOVEL;
  const stored = (isLightNovel ? searchState.lightNovelDefaults : searchState.authorDefaults) || {};
  const profileId = (name) => (qualityProfiles.find((p) => p.name === name) || {}).id || 0;
  const usableMetadataProfiles = metadataProfiles.filter((p) => p.name !== metadataProfileNames.NONE);

  // A root that holds both libraries has one default profile; it only seeds the tab whose class
  // it is (Ebook on Light Novels, manga archives on Manga), else the next step decides.
  const rootProfile = qualityProfiles.find((p) => p.id === rootFolder.defaultQualityProfileId);
  const rootQualityProfileId = rootProfile && profileClass(rootProfile) === (isLightNovel ? EBOOK : ARCHIVE) ?
    rootProfile.id :
    0;

  return {
    monitor: firstKey(monitorOptions, rootFolder.defaultMonitorOption, stored.monitor),
    monitorNewItems: firstKey(monitorNewItemsOptions, rootFolder.defaultNewItemMonitorOption, stored.monitorNewItems),
    qualityProfileId: firstValid(
      qualityProfiles,
      rootQualityProfileId,
      stored.qualityProfileId,
      profileId(isLightNovel ? 'Light Novel EPUB' : 'Manga')
    ) || firstByName(qualityProfiles),
    audioQualityProfileId: isLightNovel ?
      firstValid(qualityProfiles, stored.audioQualityProfileId, profileId('Light Novel Audio')) || firstByName(qualityProfiles) :
      0,
    metadataProfileId: firstValid(
      usableMetadataProfiles,
      rootFolder.defaultMetadataProfileId,
      stored.metadataProfileId
    ) || firstByName(usableMetadataProfiles),
    tags: stored.tags || []
  };
}
