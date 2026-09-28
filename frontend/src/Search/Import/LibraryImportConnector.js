import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { batchActions } from 'redux-batched-actions';
import { createSelector } from 'reselect';
import { fetchAuthor } from 'Store/Actions/authorActions';
import { updateItem } from 'Store/Actions/baseActions';
import { setSearchLibrary } from 'Store/Actions/searchActions';
import { fetchRootFolders } from 'Store/Actions/settingsActions';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import { importLibraryRoute } from './importLibraries';
import LibraryImport from './LibraryImport';

function createMapStateToProps() {
  return createSelector(
    (state) => state.settings.rootFolders,
    (state) => state.settings.qualityProfiles.items,
    (state) => state.settings.metadataProfiles.items,
    (state) => state.search,
    (state) => state.authors.items,
    (state, { match }) => match.params.rootFolderId,
    (rootFolders, qualityProfiles, metadataProfiles, searchState, authors, rootFolderId) => {
      return {
        isRootFoldersPopulated: rootFolders.isPopulated,
        rootFoldersError: rootFolders.error,
        rootFolders,
        rootFolderId: rootFolderId == null ? null : parseInt(rootFolderId),
        qualityProfiles,
        metadataProfiles,
        searchState,
        // By foreign id, not titleSlug: the id carries the library ("~ln"), so the manga of a
        // light novel's name never marks the light novel's folder as already added.
        existingForeignIds: new Set(authors.map((a) => a.foreignAuthorId))
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchFetchRootFolders: fetchRootFolders,
  dispatchSetSearchLibrary: setSearchLibrary,
  dispatchBatchActions: batchActions,
  dispatchFetchAuthor: fetchAuthor
};

class LibraryImportConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    // The tab is the route; remember it for /add/import (shared with Add New's tab).
    this.props.dispatchSetSearchLibrary({ library: this.props.library });
    this.props.dispatchFetchRootFolders();
  }

  componentDidUpdate(prevProps) {
    if (prevProps.library !== this.props.library) {
      this.props.dispatchSetSearchLibrary({ library: this.props.library });
    }
  }

  //
  // Listeners

  // A tab switch keeps the chosen root folder: a user may keep both libraries under one root.
  onLibrarySelect = (library) => {
    if (library !== this.props.library) {
      this.props.history.push(getPathWithUrlBase(importLibraryRoute(library, this.props.rootFolderId)));
    }
  };

  onRootFolderSelect = (rootFolderId) => {
    this.props.history.push(getPathWithUrlBase(importLibraryRoute(this.props.library, rootFolderId)));
  };

  // The bulk import isn't broadcast to the UI (AuthorController handles single adds only), so
  // the returned series go into the store here, as the Add modal's add does.
  onImported = (authors) => {
    this.props.dispatchBatchActions(authors.map((author) => updateItem({ section: 'authors', ...author })));
    this.props.dispatchFetchRootFolders();
  };

  onImportFailed = () => {
    this.props.dispatchFetchAuthor();
    this.props.dispatchFetchRootFolders();
  };

  //
  // Render

  render() {
    const {
      dispatchFetchRootFolders,
      dispatchSetSearchLibrary,
      dispatchBatchActions,
      dispatchFetchAuthor,
      history,
      location,
      match,
      staticContext,
      ...otherProps
    } = this.props;

    return (
      <LibraryImport
        {...otherProps}
        onLibrarySelect={this.onLibrarySelect}
        onRootFolderSelect={this.onRootFolderSelect}
        onImported={this.onImported}
        onImportFailed={this.onImportFailed}
      />
    );
  }
}

LibraryImportConnector.propTypes = {
  library: PropTypes.string.isRequired,
  rootFolderId: PropTypes.number,
  history: PropTypes.object.isRequired,
  location: PropTypes.object.isRequired,
  match: PropTypes.object.isRequired,
  staticContext: PropTypes.object,
  dispatchFetchRootFolders: PropTypes.func.isRequired,
  dispatchSetSearchLibrary: PropTypes.func.isRequired,
  dispatchBatchActions: PropTypes.func.isRequired,
  dispatchFetchAuthor: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(LibraryImportConnector);
