
function getNewAuthor(author, payload) {
  const {
    rootFolderPath,
    monitor,
    monitorNewItems,
    qualityProfileId,
    audioQualityProfileId,
    metadataProfileId,
    tags,
    searchForMissingBooks = false,
    monitorMediaTypes,
    editionLanguage
  } = payload;

  const addOptions = {
    monitor,
    searchForMissingBooks
  };

  // Preferred Edition (2026-09-24): an explicit edition from the Add form (absent = Auto, the chain).
  if (editionLanguage) {
    addOptions.editionLanguage = editionLanguage;
  }

  // Light novels only (contract "Add modal"): which media types' editions start monitored and
  // the audiobook profile. Both are absent for manga, so a manga add posts today's body.
  if (Array.isArray(monitorMediaTypes) && monitorMediaTypes.length > 0) {
    addOptions.monitorMediaTypes = monitorMediaTypes;
  }

  author.addOptions = addOptions;
  author.monitored = true;
  author.monitorNewItems = monitorNewItems;
  author.qualityProfileId = qualityProfileId;
  author.metadataProfileId = metadataProfileId;
  author.rootFolderPath = rootFolderPath;
  author.tags = tags;

  if (audioQualityProfileId) {
    author.audioQualityProfileId = audioQualityProfileId;
  }

  return author;
}

export default getNewAuthor;
