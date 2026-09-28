/* eslint max-params: 0 */
import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { setAuthorDetailsMediaType } from 'Store/Actions/authorDetailsActions';
import { toggleBooksMonitored } from 'Store/Actions/bookActions';
import { clearBookFiles, fetchBookFiles } from 'Store/Actions/bookFileActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { clearEditions, fetchEditions } from 'Store/Actions/editionActions';
import { clearQueueDetails, fetchQueueDetails } from 'Store/Actions/queueActions';
import { cancelFetchReleases, clearReleases } from 'Store/Actions/releaseActions';
import createAllAuthorSelector from 'Store/Selectors/createAllAuthorsSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import { findCommand, isCommandExecuting } from 'Utilities/Command';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import BookDetails from './BookDetails';

const selectBookFiles = createSelector(
  (state) => state.bookFiles,
  (bookFiles) => {
    const {
      items,
      isFetching,
      isPopulated,
      error
    } = bookFiles;

    const hasBookFiles = !!items.length;

    // Page-scoped: populate() fetches only this book's files, so these are safe finds.
    const pdfFile = items.find((f) => /\.pdf$/i.test(f.path || ''));
    const cbzFile = items.find((f) => /\.(cbz|zip)$/i.test(f.path || ''));

    return {
      isBookFilesFetching: isFetching,
      isBookFilesPopulated: isPopulated,
      bookFilesError: error,
      hasBookFiles,
      pdfFile,
      cbzFile
    };
  }
);

function createMapStateToProps() {
  return createSelector(
    (state, { titleSlug }) => titleSlug,
    selectBookFiles,
    (state) => state.books,
    (state) => state.editions,
    createAllAuthorSelector(),
    createCommandsSelector(),
    createUISettingsSelector(),
    createDimensionsSelector(),
    (state) => state.authorDetails.selectedMediaType,
    (titleSlug, bookFiles, books, editions, authors, commands, uiSettings, dimensions, selectedMediaType) => {
      const book = books.items.find((b) => b.titleSlug === titleSlug);
      const author = authors.find((a) => a.id === book.authorId);
      const sortedBooks = books.items.filter((b) => b.authorId === book.authorId);
      sortedBooks.sort((a, b) => ((a.releaseDate > b.releaseDate) ? 1 : -1));
      const bookIndex = sortedBooks.findIndex((b) => b.id === book.id);

      if (!book) {
        return {};
      }

      const {
        isBookFilesFetching,
        isBookFilesPopulated,
        bookFilesError,
        hasBookFiles,
        pdfFile,
        cbzFile
      } = bookFiles;

      const previousBook = sortedBooks[bookIndex - 1] || _.last(sortedBooks);
      const nextBook = sortedBooks[bookIndex + 1] || _.first(sortedBooks);
      const isRefreshingCommand = findCommand(commands, { name: commandNames.REFRESH_BOOK });
      const isRefreshing = (
        isCommandExecuting(isRefreshingCommand) &&
        isRefreshingCommand.body.bookId === book.id
      );
      const isSearchingCommand = findCommand(commands, { name: commandNames.BOOK_SEARCH });
      const isSearching = (
        isCommandExecuting(isSearchingCommand) &&
        isSearchingCommand.body.bookIds.indexOf(book.id) > -1
      );
      const isConvertingPdf = isCommandExecuting(findCommand(commands, { name: commandNames.CONVERT_BOOK_PDF_TO_CBZ, bookId: book.id }));
      const isFlippingPages = (
        !!cbzFile &&
        isCommandExecuting(findCommand(commands, { name: commandNames.FLIP_PAGE_ORDER, bookFileId: cbzFile.id }))
      );
      const isRenamingFiles = isCommandExecuting(findCommand(commands, { name: commandNames.RENAME_FILES, authorId: author.id }));
      const isRenamingAuthorCommand = findCommand(commands, { name: commandNames.RENAME_AUTHOR });
      const isRenamingAuthor = (
        isCommandExecuting(isRenamingAuthorCommand) &&
        isRenamingAuthorCommand.body.authorIds.indexOf(author.id) > -1
      );

      const isFetching = isBookFilesFetching || editions.isFetching;
      const isPopulated = isBookFilesPopulated && editions.isPopulated;

      // Page-scoped like bookFiles: populate() fetches only this book's editions.
      const audioEdition = editions.items.find((e) => e.mediaType === 'audio');

      return {
        ...book,
        shortDateFormat: uiSettings.shortDateFormat,
        author,
        isRefreshing,
        isSearching,
        isRenamingFiles,
        isRenamingAuthor,
        isFetching,
        isPopulated,
        bookFilesError,
        hasBookFiles,
        // LN PDF (2026-09-22): Convert PDFs is a manga tool; a light novel's PDF is its ebook.
        hasPdfFile: author.library !== 'lightNovel' && !!pdfFile,
        hasCbzFile: !!cbzFile,
        cbzFile,
        isConvertingPdf,
        isFlippingPages,
        previousBook,
        nextBook,
        selectedMediaType,
        coveredByVolume: audioEdition ? audioEdition.coveredByVolume : null,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const mapDispatchToProps = {
  executeCommand,
  fetchBookFiles,
  clearBookFiles,
  fetchEditions,
  clearEditions,
  fetchQueueDetails,
  clearQueueDetails,
  clearReleases,
  cancelFetchReleases,
  toggleBooksMonitored,
  setAuthorDetailsMediaType
};

function getMonitoredEditions(props) {
  return _.map(_.filter(props.editions, { monitored: true }), 'id').sort();
}

class BookDetailsConnector extends Component {

  componentDidMount() {
    registerPagePopulator(this.populate);
    this.populate();
  }

  componentDidUpdate(prevProps) {
    const {
      id,
      anyReleaseOk,
      isRenamingFiles,
      isRenamingAuthor,
      isConvertingPdf,
      isFlippingPages
    } = this.props;

    if (
      (prevProps.isRenamingFiles && !isRenamingFiles) ||
      (prevProps.isRenamingAuthor && !isRenamingAuthor) ||
      (prevProps.isConvertingPdf && !isConvertingPdf) ||
      (prevProps.isFlippingPages && !isFlippingPages) ||
      !_.isEqual(getMonitoredEditions(prevProps), getMonitoredEditions(this.props)) ||
      (prevProps.anyReleaseOk === false && anyReleaseOk === true)
    ) {
      this.unpopulate();
      this.populate();
    }

    // If the id has changed we need to clear the book
    // files and fetch from the server.

    if (prevProps.id !== id) {
      this.unpopulate();
      this.populate();
    }
  }

  componentWillUnmount() {
    unregisterPagePopulator(this.populate);
    this.unpopulate();
  }

  //
  // Control

  populate = () => {
    const bookId = this.props.id;

    this.props.fetchBookFiles({ bookId });
    this.props.fetchEditions({ bookId });
    // The header's download label (series page round, 2026-09-24); SignalR refetches with these params.
    this.props.fetchQueueDetails({ bookIds: [bookId] });
  };

  unpopulate = () => {
    this.props.cancelFetchReleases();
    this.props.clearReleases();
    this.props.clearBookFiles();
    this.props.clearEditions();
    this.props.clearQueueDetails();
  };

  //
  // Listeners

  onMonitorTogglePress = (monitored) => {
    this.props.toggleBooksMonitored({
      bookIds: [this.props.id],
      monitored
    });
  };

  onRefreshPress = () => {
    this.props.executeCommand({
      name: commandNames.REFRESH_BOOK,
      bookId: this.props.id
    });
  };

  onMediaTypeSelect = (mediaType) => {
    this.props.setAuthorDetailsMediaType({ mediaType });
  };

  onSearchPress = () => {
    const {
      id,
      author,
      selectedMediaType
    } = this.props;

    // A light-novel volume searches the selected edition class (the EPUB | Audio toggle); a manga
    // volume sends no media type — the search it always was.
    if (author.library === 'lightNovel') {
      this.props.executeCommand({
        name: commandNames.BOOK_SEARCH,
        bookIds: [id],
        mediaType: selectedMediaType
      });

      return;
    }

    this.props.executeCommand({
      name: commandNames.BOOK_SEARCH,
      bookIds: [this.props.id]
    });
  };

  onConvertPdfPress = () => {
    this.props.executeCommand({
      name: commandNames.CONVERT_BOOK_PDF_TO_CBZ,
      bookId: this.props.id
    });
  };

  onFlipPress = () => {
    this.props.executeCommand({
      name: commandNames.FLIP_PAGE_ORDER,
      bookFileId: this.props.cbzFile.id
    });
  };

  //
  // Render

  render() {
    return (
      <BookDetails
        {...this.props}
        onMonitorTogglePress={this.onMonitorTogglePress}
        onRefreshPress={this.onRefreshPress}
        onSearchPress={this.onSearchPress}
        onMediaTypeSelect={this.onMediaTypeSelect}
        onConvertPdfPress={this.onConvertPdfPress}
        onFlipPress={this.onFlipPress}
      />
    );
  }
}

BookDetailsConnector.propTypes = {
  id: PropTypes.number,
  author: PropTypes.object,
  selectedMediaType: PropTypes.string.isRequired,
  anyReleaseOk: PropTypes.bool,
  isRenamingFiles: PropTypes.bool.isRequired,
  isRenamingAuthor: PropTypes.bool.isRequired,
  isConvertingPdf: PropTypes.bool,
  isFlippingPages: PropTypes.bool,
  cbzFile: PropTypes.object,
  pdfFile: PropTypes.object,
  isBookFetching: PropTypes.bool,
  isBookPopulated: PropTypes.bool,
  titleSlug: PropTypes.string.isRequired,
  fetchBookFiles: PropTypes.func.isRequired,
  clearBookFiles: PropTypes.func.isRequired,
  fetchEditions: PropTypes.func.isRequired,
  clearEditions: PropTypes.func.isRequired,
  fetchQueueDetails: PropTypes.func.isRequired,
  clearQueueDetails: PropTypes.func.isRequired,
  clearReleases: PropTypes.func.isRequired,
  cancelFetchReleases: PropTypes.func.isRequired,
  toggleBooksMonitored: PropTypes.func.isRequired,
  setAuthorDetailsMediaType: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(BookDetailsConnector);
