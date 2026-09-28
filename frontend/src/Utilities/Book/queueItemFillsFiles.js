// QualityProfile.GetIndex (respectGroupOrder off): the index of the top-level item that holds the
// quality, or is the group with that id; a group's qualities share its rank, higher is better. A
// quality the profile doesn't list ranks 0, as the backend's default QualityIndex does.
function rankOf(profile, qualityId) {
  const index = profile.items.findIndex((item) => {
    return (item.quality && item.quality.id === qualityId) ||
      (item.id > 0 && item.id === qualityId) ||
      (item.items || []).some((groupItem) => groupItem.quality && groupItem.quality.id === qualityId);
  });

  return index < 0 ? 0 : index;
}

// Revision.CompareTo: real first, then version.
function compareRevisions(left = {}, right = {}) {
  const realDiff = (left.real || 0) - (right.real || 0);

  return realDiff === 0 ? (left.version || 0) - (right.version || 0) : realDiff;
}

// UpgradeSpecification (the import): a release is refused for an edition only when its quality is
// lower than a file's, or equal with a lower revision while propers/repacks are not set to Do Not
// Prefer -- otherwise it replaces the file. downloadPropersAndRepacks comes from the Media
// Management settings; where they aren't loaded (undefined, e.g. the volume page) the revision
// rule applies, the default setting's behaviour.
function isNotWorse(release, file, profile, downloadPropersAndRepacks) {
  // Profile not loaded: the file's own cutoff flag is the best guess.
  if (!profile || !release) {
    return !!file.qualityCutoffNotMet;
  }

  const releaseRank = rankOf(profile, release.quality.id);
  const fileRank = rankOf(profile, file.quality.quality.id);

  if (releaseRank !== fileRank) {
    return releaseRank > fileRank;
  }

  return downloadPropersAndRepacks === 'doNotPrefer' ||
    compareRevisions(release.revision, file.quality.revision) >= 0;
}

// Series page round (2026-09-24): a pack download fans out to every volume it names, owned or
// not. It fills a volume when the volume has no file (in the edition the row shows) or the import
// would take it over every file there (isNotWorse), in the profile that edition uses.
export default function queueItemFillsFiles(queueItem, files, profile, downloadPropersAndRepacks) {
  if (!files.length) {
    return true;
  }

  return files.every((file) => isNotWorse(queueItem.quality, file, profile, downloadPropersAndRepacks));
}
