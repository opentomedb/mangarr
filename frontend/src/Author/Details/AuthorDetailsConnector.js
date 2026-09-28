/* eslint max-params: 0 */
import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { toggleAuthorMonitored } from 'Store/Actions/authorActions';
import { clearBookFiles, fetchBookFiles } from 'Store/Actions/bookFileActions';
import { saveBookEditor } from 'Store/Actions/bookIndexActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { clearQueueDetails, fetchQueueDetails } from 'Store/Actions/queueActions';
import { cancelFetchReleases, clearReleases } from 'Store/Actions/releaseActions';
import { clearSeries, fetchSeries } from 'Store/Actions/seriesActions';
import { fetchMediaManagementSettings } from 'Store/Actions/settingsActions';
import createAllAuthorSelector from 'Store/Selectors/createAllAuthorsSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import { findCommand, isCommandExecuting } from 'Utilities/Command';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import AuthorDetails from './AuthorDetails';

const selectBooks = createSelector(
  (state) => state.books,
  (state) => state.bookIndex,
  (books, index) => {
    const {
      items,
      isFetching,
      isPopulated,
      error
    } = books;

    const {
      isSaving,
      saveError,
      isDeleting,
      deleteError
    } = index;

    const hasBooks = !!items.length;
    const hasMonitoredBooks = items.some((e) => e.monitored);

    return {
      isBooksFetching: isFetching,
      isBooksPopulated: isPopulated,
      booksError: error,
      hasBooks,
      hasMonitoredBooks,
      isSaving,
      saveError,
      isDeleting,
      deleteError
    };
  }
);

const selectSeries = createSelector(
  createSortedSectionSelector('series', (a, b) => a.title.localeCompare(b.title)),
  (state) => state.series,
  (series) => {
    const {
      items,
      isFetching,
      isPopulated,
      error
    } = series;

    const hasSeries = !!items.length;

    return {
      isSeriesFetching: isFetching,
      isSeriesPopulated: isPopulated,
      seriesError: error,
      hasSeries,
      series: series.items
    };
  }
);

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

    return {
      isBookFilesFetching: isFetching,
      isBookFilesPopulated: isPopulated,
      bookFilesError: error,
      hasBookFiles
    };
  }
);

function createMapStateToProps() {
  return createSelector(
    (state, { titleSlug }) => titleSlug,
    selectBooks,
    selectSeries,
    selectBookFiles,
    createAllAuthorSelector(),
    createCommandsSelector(),
    createDimensionsSelector(),
    (state) => state.authorDetails.selectedMediaType,
    (state) => state.settings.mediaManagement,
    (titleSlug, books, series, bookFiles, allAuthors, commands, dimensions, selectedMediaType, mediaManagementSettings) => {
      const sortedAuthor = _.orderBy(allAuthors, 'sortNameLastFirst');
      const authorIndex = _.findIndex(sortedAuthor, { titleSlug });
      const author = sortedAuthor[authorIndex];

      if (!author) {
        return {};
      }

      const {
        isBooksFetching,
        isBooksPopulated,
        booksError,
        hasBooks,
        hasMonitoredBooks,
        isSaving,
        saveError,
        isDeleting,
        deleteError
      } = books;

      const {
        isSeriesFetching,
        isSeriesPopulated,
        seriesError,
        hasSeries,
        series: seriesItems
      } = series;

      const {
        isBookFilesFetching,
        isBookFilesPopulated,
        bookFilesError,
        hasBookFiles
      } = bookFiles;

      const previousAuthor = sortedAuthor[authorIndex - 1] || _.last(sortedAuthor);
      const nextAuthor = sortedAuthor[authorIndex + 1] || _.first(sortedAuthor);
      const isAuthorRefreshing = isCommandExecuting(findCommand(commands, { name: commandNames.REFRESH_AUTHOR, authorId: author.id }));
      const authorRefreshingCommand = findCommand(commands, { name: commandNames.REFRESH_AUTHOR });
      const allAuthorRefreshing = (
        isCommandExecuting(authorRefreshingCommand) &&
        !authorRefreshingCommand.body.authorId
      );
      const isRefreshing = isAuthorRefreshing || allAuthorRefreshing;
      const isSearching = isCommandExecuting(findCommand(commands, { name: commandNames.AUTHOR_SEARCH, authorId: author.id }));
      const isImportingExisting = isCommandExecuting(findCommand(commands, { name: commandNames.IMPORT_EXISTING_LIGHT_NOVELS, authorId: author.id }));
      const isConvertingLightNovelFormat = isCommandExecuting(findCommand(commands, { name: commandNames.CONVERT_LIGHT_NOVEL_FORMAT, authorId: author.id }));
      // Conversion needs calibre as the ebook home (light-novel storage, 2026-09-22): no Convert button otherwise.
      const preferredLightNovelFormat = mediaManagementSettings.item.lightNovelEbookHome === 'calibre' ? mediaManagementSettings.item.preferredLightNovelFormat : null;
      const isRenamingFiles = isCommandExecuting(findCommand(commands, { name: commandNames.RENAME_FILES, authorId: author.id }));
      const isRenamingAuthorCommand = findCommand(commands, { name: commandNames.RENAME_AUTHOR });
      const isRenamingAuthor = (
        isCommandExecuting(isRenamingAuthorCommand) &&
        isRenamingAuthorCommand.body.authorIds.indexOf(author.id) > -1
      );

      const isFetching = isBooksFetching || isSeriesFetching || isBookFilesFetching;
      const isPopulated = isBooksPopulated && isSeriesPopulated && isBookFilesPopulated;

      const alternateTitles = _.reduce(author.alternateTitles, (acc, alternateTitle) => {
        if ((alternateTitle.seasonNumber === -1 || alternateTitle.seasonNumber === undefined) &&
            (alternateTitle.sceneSeasonNumber === -1 || alternateTitle.sceneSeasonNumber === undefined)) {
          acc.push(alternateTitle.title);
        }

        return acc;
      }, []);

      return {
        ...author,
        alternateTitles,
        isAuthorRefreshing,
        allAuthorRefreshing,
        isRefreshing,
        isSearching,
        isImportingExisting,
        isConvertingLightNovelFormat,
        preferredLightNovelFormat,
        isMediaManagementPopulated: mediaManagementSettings.isPopulated,
        isRenamingFiles,
        isRenamingAuthor,
        isFetching,
        isPopulated,
        booksError,
        isSaving,
        saveError,
        isDeleting,
        deleteError,
        seriesError,
        bookFilesError,
        hasBooks,
        hasMonitoredBooks,
        hasSeries,
        series: seriesItems,
        hasBookFiles,
        previousAuthor,
        nextAuthor,
        selectedMediaType,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const mapDispatchToProps = {
  fetchSeries,
  clearSeries,
  saveBookEditor,
  fetchBookFiles,
  clearBookFiles,
  toggleAuthorMonitored,
  fetchQueueDetails,
  clearQueueDetails,
  clearReleases,
  cancelFetchReleases,
  executeCommand,
  fetchMediaManagementSettings
};

class AuthorDetailsConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    registerPagePopulator(this.populate);
    this.populate();

    // The series toolbar's "Convert to <FORMAT>" button needs the delivery-format setting; the
    // Media Management page fetches it itself, but a series page opened directly may not have it
    // in the store yet.
    if (!this.props.isMediaManagementPopulated) {
      this.props.fetchMediaManagementSettings();
    }
  }

  componentDidUpdate(prevProps) {
    const {
      id,
      isAuthorRefreshing,
      allAuthorRefreshing,
      isRenamingFiles,
      isRenamingAuthor
    } = this.props;

    if (
      (prevProps.isAuthorRefreshing && !isAuthorRefreshing) ||
      (prevProps.allAuthorRefreshing && !allAuthorRefreshing) ||
      (prevProps.isRenamingFiles && !isRenamingFiles) ||
      (prevProps.isRenamingAuthor && !isRenamingAuthor)
    ) {
      this.populate();
    }

    // If the id has changed we need to clear the books
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
    const authorId = this.props.id;

    this.props.fetchSeries({ authorId });
    this.props.fetchBookFiles({ authorId });
    this.props.fetchQueueDetails({ authorId });
  };

  unpopulate = () => {
    this.props.cancelFetchReleases();
    this.props.clearSeries();
    this.props.clearBookFiles();
    this.props.clearQueueDetails();
    this.props.clearReleases();
  };

  //
  // Listeners

  onMonitorTogglePress = (monitored) => {
    this.props.toggleAuthorMonitored({
      authorId: this.props.id,
      monitored
    });
  };

  onRefreshPress = () => {
    this.props.executeCommand({
      name: commandNames.REFRESH_AUTHOR,
      authorId: this.props.id
    });
  };

  onSearchPress = () => {
    const {
      id,
      library,
      selectedMediaType
    } = this.props;

    // A light novel searches the selected edition class (the EPUB | Audio tab); manga sends no
    // media type — the merged search it always was.
    if (library === 'lightNovel') {
      this.props.executeCommand({
        name: commandNames.AUTHOR_SEARCH,
        authorId: id,
        mediaType: selectedMediaType
      });

      return;
    }

    this.props.executeCommand({
      name: commandNames.AUTHOR_SEARCH,
      authorId: this.props.id
    });
  };

  onImportExistingPress = () => {
    this.props.executeCommand({
      name: commandNames.IMPORT_EXISTING_LIGHT_NOVELS,
      authorId: this.props.id
    });
  };

  onConvertFormatPress = () => {
    this.props.executeCommand({
      name: commandNames.CONVERT_LIGHT_NOVEL_FORMAT,
      authorId: this.props.id
    });
  };

  onSaveSelected = (payload) => {
    this.props.saveBookEditor(payload);
  };

  //
  // Render

  render() {
    return (
      <AuthorDetails
        {...this.props}
        onMonitorTogglePress={this.onMonitorTogglePress}
        onRefreshPress={this.onRefreshPress}
        onSearchPress={this.onSearchPress}
        onImportExistingPress={this.onImportExistingPress}
        onConvertFormatPress={this.onConvertFormatPress}
        onSaveSelected={this.onSaveSelected}
      />
    );
  }
}

AuthorDetailsConnector.propTypes = {
  id: PropTypes.number.isRequired,
  titleSlug: PropTypes.string.isRequired,
  library: PropTypes.string,
  selectedMediaType: PropTypes.string.isRequired,
  isAuthorRefreshing: PropTypes.bool.isRequired,
  allAuthorRefreshing: PropTypes.bool.isRequired,
  isRefreshing: PropTypes.bool.isRequired,
  isRenamingFiles: PropTypes.bool.isRequired,
  isRenamingAuthor: PropTypes.bool.isRequired,
  isMediaManagementPopulated: PropTypes.bool.isRequired,
  fetchSeries: PropTypes.func.isRequired,
  clearSeries: PropTypes.func.isRequired,
  saveBookEditor: PropTypes.func.isRequired,
  fetchBookFiles: PropTypes.func.isRequired,
  clearBookFiles: PropTypes.func.isRequired,
  toggleAuthorMonitored: PropTypes.func.isRequired,
  fetchQueueDetails: PropTypes.func.isRequired,
  clearQueueDetails: PropTypes.func.isRequired,
  clearReleases: PropTypes.func.isRequired,
  cancelFetchReleases: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired,
  fetchMediaManagementSettings: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AuthorDetailsConnector);
