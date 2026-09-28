/* eslint max-params: 0 */
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import withScrollPosition from 'Components/withScrollPosition';
import { saveAuthorEditor, setAuthorFilter, setAuthorLibrary, setAuthorSort, setAuthorTableOption, setAuthorView } from 'Store/Actions/authorIndexActions';
import { executeCommand } from 'Store/Actions/commandActions';
import scrollPositions from 'Store/scrollPositions';
import createAuthorClientSideCollectionItemsSelector from 'Store/Selectors/createAuthorClientSideCollectionItemsSelector';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import { filtersForLibrary, libraryRoute, LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import AuthorIndex from './AuthorIndex';

// The index shows one library at a time: the route's `library` (the tab) scopes the filtered and
// sorted list — filters and sort are per item and keep their order, so scoping after them equals
// scoping before them — and the tab row's counts are each library's entries before any filter.
function createLibraryItemsSelector() {
  return createSelector(
    createAuthorClientSideCollectionItemsSelector('authorIndex'),
    (state) => state.authors.items,
    (state, { library }) => library,
    (authors, allAuthors, library) => {
      const mangaCount = allAuthors.filter((a) => a.library === MANGA).length;
      const lightNovelCount = allAuthors.filter((a) => a.library === LIGHT_NOVEL).length;

      return {
        ...authors,
        library,
        filters: filtersForLibrary(authors.filters, library),
        items: authors.items.filter((a) => a.library === library),
        totalItems: library === LIGHT_NOVEL ? lightNovelCount : mangaCount,
        mangaCount,
        lightNovelCount
      };
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    createLibraryItemsSelector(),
    createCommandExecutingSelector(commandNames.BULK_REFRESH_AUTHOR),
    createCommandExecutingSelector(commandNames.REFRESH_AUTHOR),
    createCommandExecutingSelector(commandNames.RSS_SYNC),
    createCommandExecutingSelector(commandNames.RENAME_AUTHOR),
    createCommandExecutingSelector(commandNames.RETAG_AUTHOR),
    createDimensionsSelector(),
    (
      author,
      isBulkRefreshingAuthor,
      isRefreshingAllAuthors,
      isRssSyncExecuting,
      isOrganizingAuthor,
      isRetaggingAuthor,
      dimensionsState
    ) => {
      return {
        ...author,
        isRefreshingAuthor: isBulkRefreshingAuthor || isRefreshingAllAuthors,
        isRssSyncExecuting,
        isOrganizingAuthor,
        isRetaggingAuthor,
        isSmallScreen: dimensionsState.isSmallScreen
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onTableOptionChange(payload) {
      dispatch(setAuthorTableOption(payload));
    },

    onSortSelect(sortKey) {
      dispatch(setAuthorSort({ sortKey }));
    },

    onFilterSelect(selectedFilterKey) {
      dispatch(setAuthorFilter({ selectedFilterKey }));
    },

    dispatchSetAuthorView(view) {
      dispatch(setAuthorView({ view }));
    },

    dispatchSetAuthorLibrary(library) {
      dispatch(setAuthorLibrary({ library }));
    },

    dispatchSaveAuthorEditor(payload) {
      dispatch(saveAuthorEditor(payload));
    },

    // UI pass (2026-09-24, SI-6): a tab only ever refreshes the entries it shows. The button is
    // disabled on an empty tab, so the unscoped refresh-all command is never sent from here.
    onRefreshAuthorPress(items) {
      dispatch(executeCommand({
        name: commandNames.BULK_REFRESH_AUTHOR,
        authorIds: items
      }));
    },

    onRssSyncPress() {
      dispatch(executeCommand({
        name: commandNames.RSS_SYNC
      }));
    }
  };
}

class AuthorIndexConnector extends Component {

  //
  // Lifecycle

  // The tab is the route (/series/manga, /series/lightnovels); remember it so /, /series and
  // /authors reopen it.
  componentDidMount() {
    this.props.dispatchSetAuthorLibrary(this.props.library);
  }

  componentDidUpdate(prevProps) {
    if (prevProps.library !== this.props.library) {
      this.props.dispatchSetAuthorLibrary(this.props.library);
    }
  }

  //
  // Listeners

  onLibrarySelect = (library) => {
    if (library === this.props.library) {
      return;
    }

    this.props.history.push(getPathWithUrlBase(libraryRoute(library)));
  };

  onViewSelect = (view) => {
    this.props.dispatchSetAuthorView(view);
  };

  onSaveSelected = (payload) => {
    this.props.dispatchSaveAuthorEditor(payload);
  };

  onScroll = ({ scrollTop }) => {
    scrollPositions.authorIndex = scrollTop;
  };

  //
  // Render

  render() {
    return (
      <AuthorIndex
        {...this.props}
        onLibrarySelect={this.onLibrarySelect}
        onViewSelect={this.onViewSelect}
        onScroll={this.onScroll}
        onSaveSelected={this.onSaveSelected}
      />
    );
  }
}

AuthorIndexConnector.propTypes = {
  isSmallScreen: PropTypes.bool.isRequired,
  view: PropTypes.string.isRequired,
  library: PropTypes.string.isRequired,
  history: PropTypes.object.isRequired,
  dispatchSetAuthorView: PropTypes.func.isRequired,
  dispatchSetAuthorLibrary: PropTypes.func.isRequired,
  dispatchSaveAuthorEditor: PropTypes.func.isRequired
};

export default withScrollPosition(
  connect(createMapStateToProps, createMapDispatchToProps)(AuthorIndexConnector),
  'authorIndex'
);
