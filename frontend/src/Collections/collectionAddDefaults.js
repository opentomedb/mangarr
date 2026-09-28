import { LIGHT_NOVEL } from 'Utilities/Author/libraries';
import { libraryRoot } from 'Utilities/Author/libraryRoots';

// The same per-library defaults a fresh add on the Add New page would use (Search/Author/
// AddNewAuthorModalContentConnector): the remembered blob (search.lightNovelDefaults /
// authorDefaults) for whichever field the user has actually set, the matching root folder's own
// path and profile defaults for whichever it hasn't. The library's root is found the same way the
// Add New page finds it (Utilities/Author/libraryRoots, beta readiness F4): by its default quality
// profile's class, then by the old path rule (this assumes the usual one-manga, one-light-novel
// layout). No root folder for this library at all -- no defaults, and the caller hides Add the same
// way it already does for a collection.defaults-less collection.
export function collectionAddDefaults(searchState, rootFolders, library, qualityProfiles) {
  const isLightNovel = library === LIGHT_NOVEL;
  const stored = isLightNovel ? searchState.lightNovelDefaults : searchState.authorDefaults;
  const root = libraryRoot(rootFolders, qualityProfiles, isLightNovel);

  if (!root) {
    return null;
  }

  // The remembered path wins only while it still names a real root folder (one could have been
  // removed since); the profile fallbacks come from that same folder, not the library's root in
  // general, so the path and its defaults are never a mismatched pair.
  const remembered = rootFolders.items.find((r) => r.path === stored.rootFolderPath);
  const folder = remembered || root;

  return {
    rootFolderPath: folder.path,
    qualityProfileId: stored.qualityProfileId || folder.defaultQualityProfileId || 0,
    metadataProfileId: stored.metadataProfileId || folder.defaultMetadataProfileId || 0,
    monitorNewItems: stored.monitorNewItems || 'all',
    tags: (stored.tags && stored.tags.length ? stored.tags : folder.defaultTags) || []
  };
}
