/* eslint max-params: 0 */
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import { AUDIO, mediaTypeOfFile, mediaTypeOfQuality } from 'Utilities/Book/mediaTypes';
import queueItemFillsFiles from 'Utilities/Book/queueItemFillsFiles';
import BookRow from './BookRow';

const selectBookFiles = createSelector(
  (state) => state.bookFiles,
  (bookFiles) => {
    const { items } = bookFiles;

    return items.reduce((acc, file) => {
      const bookId = file.bookId;
      if (!acc.hasOwnProperty(bookId)) {
        acc[bookId] = [];
      }

      acc[bookId].push(file);

      return acc;
    }, {});
  }
);

function createMapStateToProps() {
  // Per-connected-row selector (defined inside the factory so each row memoizes its own queue
  // lookup) — the book's download-queue items, matched by book id, mirroring createQueueItemSelector.
  // A light-novel volume can have an EPUB and an audiobook download at once, so all of them.
  const selectQueueItems = createSelector(
    (state, { id }) => id,
    (state) => state.queue.details.items,
    (bookId, details) => {
      if (!bookId || !details) {
        return [];
      }

      return details.filter((item) => item.book && item.book.id === bookId);
    }
  );

  return createSelector(
    createAuthorSelector(),
    selectBookFiles,
    selectQueueItems,
    (state, { id }) => id,
    (state) => state.authorDetails.selectedMediaType,
    (state) => state.settings.qualityProfiles.items,
    (state) => state.settings.mediaManagement.item.downloadPropersAndRepacks,
    (author = {}, bookFiles, queueItems, bookId, selectedMediaType, qualityProfiles, downloadPropersAndRepacks) => {
      const allFiles = bookFiles[bookId] ?? [];
      const isLightNovel = author.library === 'lightNovel';

      // A manga volume has one edition, so every file is the row's file (today's behaviour). A
      // light-novel volume has an EPUB edition and an Audio edition: the row shows the files of
      // the edition selected in the tab row. A file's media type comes from its extension and a
      // queue item's from its quality (Utilities/Book/mediaTypes mirrors the backend rules).
      // A light novel's .pdf is its ebook (mediaTypeOfFile).
      const files = isLightNovel ?
        allFiles.filter((f) => mediaTypeOfFile(f.path, author.library) === selectedMediaType) :
        allFiles;
      const bookFile = files[0];

      // The edition's profile: a light novel's audiobook has its own (QualityProfileFor).
      const profileId = isLightNovel && selectedMediaType === AUDIO ?
        (author.audioQualityProfileId || author.qualityProfileId) :
        author.qualityProfileId;
      const profile = qualityProfiles.find((p) => p.id === profileId);

      // A pack grab names owned volumes too: those keep their quality badge unless a download
      // (of the selected edition, on a light novel) will actually fill them (queueItemFillsFiles).
      const rowQueueItem = queueItems.find((item) => {
        return (!isLightNovel || mediaTypeOfQuality(item.quality?.quality?.name) === selectedMediaType) &&
          queueItemFillsFiles(item, files, profile, downloadPropersAndRepacks);
      }) ?? null;

      return {
        authorMonitored: author.monitored,
        authorName: author.authorName,
        authorLibrary: author.library,
        selectedMediaType,
        bookFiles: files,
        queueItem: rowQueueItem,
        indexerFlags: bookFile ? bookFile.indexerFlags : 0
      };
    }
  );
}
export default connect(createMapStateToProps)(BookRow);
