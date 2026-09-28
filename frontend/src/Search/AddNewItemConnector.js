import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { clearSearchResults, getSearchResults, setSearchLibrary } from 'Store/Actions/searchActions';
import { fetchMetadataSource, fetchRootFolders } from 'Store/Actions/settingsActions';
import parseUrl from 'Utilities/String/parseUrl';
import AddNewItem from './AddNewItem';

function createMapStateToProps() {
  return createSelector(
    (state) => state.search,
    (state) => state.authors.items.length,
    (state) => state.router.location,
    (state) => state.settings.metadataSource.item.openTomeUrl,
    (search, existingAuthorsCount, location, openTomeUrl) => {
      const { params } = parseUrl(location.search);

      return {
        ...search,
        term: params.term,
        hasExistingAuthors: existingAuthorsCount > 0,
        openTomeUrl
      };
    }
  );
}

const mapDispatchToProps = {
  getSearchResults,
  clearSearchResults,
  setSearchLibrary,
  fetchRootFolders,
  fetchMetadataSource
};

class AddNewItemConnector extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this._searchTimeout = null;
  }

  componentDidMount() {
    this.props.fetchRootFolders();
    this.props.fetchMetadataSource();
  }

  componentWillUnmount() {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    this.props.clearSearchResults();
  }

  //
  // Listeners

  onSearchChange = (term) => {
    if (this._searchTimeout) {
      clearTimeout(this._searchTimeout);
    }

    if (term.trim() === '') {
      this.props.clearSearchResults();
    } else {
      // 1s, not Radarr's 300ms: our lookup is a multi-provider crawl, and every
      // partial term fired mid-typing burns rate-limited provider requests that
      // the real search then queues behind.
      this._searchTimeout = setTimeout(() => {
        this.props.getSearchResults({ term });
      }, 1000);
    }
  };

  onClearSearch = () => {
    this.props.clearSearchResults();
  };

  // Manga | Light Novels: persist the choice and re-run the current term in the other library.
  onLibraryChange = (library) => {
    this.props.setSearchLibrary({ library });

    if (this.props.term) {
      this.props.getSearchResults({ term: this.props.term });
    }
  };

  //
  // Render

  render() {
    const {
      term,
      ...otherProps
    } = this.props;

    return (
      <AddNewItem
        term={term}
        {...otherProps}
        onSearchChange={this.onSearchChange}
        onClearSearch={this.onClearSearch}
        onLibraryChange={this.onLibraryChange}
      />
    );
  }
}

AddNewItemConnector.propTypes = {
  term: PropTypes.string,
  getSearchResults: PropTypes.func.isRequired,
  clearSearchResults: PropTypes.func.isRequired,
  setSearchLibrary: PropTypes.func.isRequired,
  fetchRootFolders: PropTypes.func.isRequired,
  fetchMetadataSource: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AddNewItemConnector);
