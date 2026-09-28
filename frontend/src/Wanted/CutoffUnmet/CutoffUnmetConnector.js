import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withCurrentPage from 'Components/withCurrentPage';
import { clearBookFiles, fetchBookFiles } from 'Store/Actions/bookFileActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { clearQueueDetails, fetchQueueDetails } from 'Store/Actions/queueActions';
import * as wantedActions from 'Store/Actions/wantedActions';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import { filtersForLibrary } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import hasDifferentItems from 'Utilities/Object/hasDifferentItems';
import selectUniqueIds from 'Utilities/Object/selectUniqueIds';
import { registerPagePopulator, unregisterPagePopulator } from 'Utilities/pagePopulator';
import { wantedLibraryRoute } from '../wantedLibraries';
import CutoffUnmet from './CutoffUnmet';

function createMapStateToProps() {
  return createSelector(
    (state) => state.wanted.cutoffUnmet,
    (state) => state.authors,
    createCommandExecutingSelector(commandNames.CUTOFF_UNMET_BOOK_SEARCH),
    (cutoffUnmet, authors, isSearchingForCutoffUnmetBooks) => {

      return {
        isAuthorFetching: authors.isFetching,
        isAuthorPopulated: authors.isPopulated,
        isSearchingForCutoffUnmetBooks,
        isSaving: cutoffUnmet.items.filter((m) => m.isSaving).length > 1,
        ...cutoffUnmet
      };
    }
  );
}

const mapDispatchToProps = {
  ...wantedActions,
  executeCommand,
  fetchQueueDetails,
  clearQueueDetails,
  fetchBookFiles,
  clearBookFiles
};

class CutoffUnmetConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    const {
      useCurrentPage,
      fetchCutoffUnmet,
      gotoCutoffUnmetFirstPage
    } = this.props;

    registerPagePopulator(this.repopulate, ['bookFileUpdated', 'bookFileDeleted']);

    // The tab is the route; the store's library scopes every fetch, so set it first.
    this.props.setWantedLibrary({ library: this.props.library });

    if (useCurrentPage) {
      fetchCutoffUnmet();
    } else {
      gotoCutoffUnmetFirstPage();
    }
  }

  componentDidUpdate(prevProps) {
    if (prevProps.library !== this.props.library) {
      this.props.setWantedLibrary({ library: this.props.library });
      this.props.gotoCutoffUnmetFirstPage();
    }

    if (hasDifferentItems(prevProps.items, this.props.items)) {
      const bookIds = selectUniqueIds(this.props.items, 'id');
      const bookFileIds = selectUniqueIds(this.props.items, 'bookFileId');

      this.props.fetchQueueDetails({ bookIds });

      if (bookFileIds.length) {
        this.props.fetchBookFiles({ bookFileIds });
      }
    }
  }

  componentWillUnmount() {
    unregisterPagePopulator(this.repopulate);
    this.props.clearCutoffUnmet();
    this.props.clearQueueDetails();
    this.props.clearBookFiles();
  }

  //
  // Control

  repopulate = () => {
    this.props.fetchCutoffUnmet();
  };

  //
  // Listeners

  onLibrarySelect = (library) => {
    if (library !== this.props.library) {
      this.props.history.push(getPathWithUrlBase(wantedLibraryRoute('cutoffUnmet', library)));
    }
  };

  onFirstPagePress = () => {
    this.props.gotoCutoffUnmetFirstPage();
  };

  onPreviousPagePress = () => {
    this.props.gotoCutoffUnmetPreviousPage();
  };

  onNextPagePress = () => {
    this.props.gotoCutoffUnmetNextPage();
  };

  onLastPagePress = () => {
    this.props.gotoCutoffUnmetLastPage();
  };

  onPageSelect = (page) => {
    this.props.gotoCutoffUnmetPage({ page });
  };

  onSortPress = (sortKey) => {
    this.props.setCutoffUnmetSort({ sortKey });
  };

  onFilterSelect = (selectedFilterKey) => {
    this.props.setCutoffUnmetFilter({ selectedFilterKey });
  };

  onTableOptionChange = (payload) => {
    this.props.setCutoffUnmetTableOption(payload);

    if (payload.pageSize) {
      this.props.gotoCutoffUnmetFirstPage();
    }
  };

  onSearchSelectedPress = (selected) => {
    // A page filtered to one light-novel edition searches that edition only: the EPUB filter
    // must not also fetch audiobooks. Every other filter sends no media type, which is the
    // every-class search it always was.
    const mediaType = wantedActions.FILTER_MEDIA_TYPES[this.props.selectedFilterKey];

    this.props.executeCommand({
      name: commandNames.BOOK_SEARCH,
      bookIds: selected,
      ...(mediaType ? { mediaType } : {}),
      commandFinished: this.repopulate
    });
  };

  onSearchAllCutoffUnmetPress = () => {
    const mediaType = wantedActions.FILTER_MEDIA_TYPES[this.props.selectedFilterKey];

    // Search All hunts the library the page lists: the tab's library, never both.
    const library = this.props.library;

    // The Both preset hunts only the volumes whose EPUB and audiobook are both wanted -- the
    // volumes the page lists. Its library alone would sweep every wanted light-novel edition.
    const bothEditions = wantedActions.FILTER_BOTH_EDITIONS[this.props.selectedFilterKey];

    this.props.executeCommand({
      name: commandNames.CUTOFF_UNMET_BOOK_SEARCH,
      ...(mediaType ? { mediaType } : {}),
      library,
      ...(bothEditions ? { bothEditions } : {}),
      commandFinished: this.repopulate
    });
  };

  //
  // Render

  render() {
    return (
      <CutoffUnmet
        onFirstPagePress={this.onFirstPagePress}
        onPreviousPagePress={this.onPreviousPagePress}
        onNextPagePress={this.onNextPagePress}
        onLastPagePress={this.onLastPagePress}
        onPageSelect={this.onPageSelect}
        onSortPress={this.onSortPress}
        onFilterSelect={this.onFilterSelect}
        onTableOptionChange={this.onTableOptionChange}
        onSearchSelectedPress={this.onSearchSelectedPress}
        onToggleSelectedPress={this.onToggleSelectedPress}
        onSearchAllCutoffUnmetPress={this.onSearchAllCutoffUnmetPress}
        onLibrarySelect={this.onLibrarySelect}
        {...this.props}
        filters={filtersForLibrary(this.props.filters, this.props.library)}
      />
    );
  }
}

CutoffUnmetConnector.propTypes = {
  useCurrentPage: PropTypes.bool.isRequired,
  library: PropTypes.string.isRequired,
  history: PropTypes.object.isRequired,
  filters: PropTypes.arrayOf(PropTypes.object).isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  selectedFilterKey: PropTypes.oneOfType([PropTypes.string, PropTypes.number]).isRequired,
  fetchCutoffUnmet: PropTypes.func.isRequired,
  gotoCutoffUnmetFirstPage: PropTypes.func.isRequired,
  gotoCutoffUnmetPreviousPage: PropTypes.func.isRequired,
  gotoCutoffUnmetNextPage: PropTypes.func.isRequired,
  gotoCutoffUnmetLastPage: PropTypes.func.isRequired,
  gotoCutoffUnmetPage: PropTypes.func.isRequired,
  setCutoffUnmetSort: PropTypes.func.isRequired,
  setCutoffUnmetFilter: PropTypes.func.isRequired,
  setCutoffUnmetTableOption: PropTypes.func.isRequired,
  clearCutoffUnmet: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired,
  fetchQueueDetails: PropTypes.func.isRequired,
  clearQueueDetails: PropTypes.func.isRequired,
  setWantedLibrary: PropTypes.func.isRequired,
  fetchBookFiles: PropTypes.func.isRequired,
  clearBookFiles: PropTypes.func.isRequired
};

export default withCurrentPage(
  connect(createMapStateToProps, mapDispatchToProps)(CutoffUnmetConnector)
);
