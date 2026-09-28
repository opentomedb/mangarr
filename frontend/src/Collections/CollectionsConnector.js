import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { addCollectionMembers, fetchCollections, setCollectionsLibrary } from 'Store/Actions/collectionActions';
import { executeCommand } from 'Store/Actions/commandActions';
import { fetchRootFolders } from 'Store/Actions/settingsActions';
import { LIGHT_NOVEL, MANGA } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import { collectionAddDefaults } from './collectionAddDefaults';
import { cardsOf } from './collectionCards';
import Collections from './Collections';
import { collectionsLibraryRoute } from './collectionsLibraries';

// The page shows one library's collections at a time, the same tab-is-the-route shape as the
// author index (AuthorIndexConnector). A collection is shown on a tab when at least one of its
// cards (root or member) is that tab's library (collectionCards.cardsOf); only those cards show.
// Tab counts are how many collections qualify for each tab, independent of which is open.
function createLibraryItemsSelector() {
  return createSelector(
    (state) => state.collections,
    (state) => state.authors.items,
    (state) => state.search,
    (state) => state.settings.rootFolders,
    (state) => state.settings.qualityProfiles.items,
    (state, { library }) => library,
    (collectionsState, authors, searchState, rootFolders, qualityProfiles, library) => {
      const authorsById = {};
      authors.forEach((author) => {
        authorsById[author.id] = author;
      });

      const withCards = collectionsState.items.map((collection) => {
        return { collection, cards: cardsOf(collection, authorsById) };
      });

      const mangaCount = withCards.filter(({ cards }) => cards.some((c) => c.library === MANGA)).length;
      const lightNovelCount = withCards.filter(({ cards }) => cards.some((c) => c.library === LIGHT_NOVEL)).length;

      // A collection shown on this tab only because one of its members carries this library's
      // medium keeps collection.defaults sourced from its own root's library (CollectionController's
      // `sample`), the wrong library for that member -- the same per-library defaults a fresh add
      // on the Add New page would use for this tab stand in instead (collectionAddDefaults).
      const tabDefaults = collectionAddDefaults(searchState, rootFolders, library, qualityProfiles);

      const items = withCards
        .filter(({ cards }) => cards.some((c) => c.library === library))
        .map(({ collection, cards }) => {
          return {
            ...collection,
            defaults: collection.library === library ? collection.defaults : tabDefaults,
            cards: cards.filter((c) => c.library === library)
          };
        });

      return {
        ...collectionsState,
        library,
        items,
        authorsById,
        mangaCount,
        lightNovelCount
      };
    }
  );
}

function createMapStateToProps() {
  return createLibraryItemsSelector();
}

const mapDispatchToProps = {
  fetchCollections,
  addCollectionMembers,
  executeCommand,
  fetchRootFolders,
  dispatchSetCollectionsLibrary: setCollectionsLibrary
};

class CollectionsConnector extends Component {

  //
  // Lifecycle

  // The tab is the route (/collections/manga, /collections/lightnovels); remember it so
  // /collections reopens it -- the same pattern as AuthorIndexConnector/authorIndex.library.
  componentDidMount() {
    this.props.fetchCollections();
    // The single Add modal's root folder list, as Add New loads it.
    this.props.fetchRootFolders();
    this.props.dispatchSetCollectionsLibrary(this.props.library);
  }

  componentDidUpdate(prevProps) {
    if (prevProps.library !== this.props.library) {
      this.props.dispatchSetCollectionsLibrary(this.props.library);
    }
  }

  //
  // Listeners

  onLibrarySelect = (library) => {
    if (library === this.props.library) {
      return;
    }

    this.props.history.push(getPathWithUrlBase(collectionsLibraryRoute(library)));
  };

  onAddMembersPress = (collection, members) => {
    this.props.addCollectionMembers({
      members: members.filter((m) => !m.inLibrary),
      defaults: collection.defaults,
      searchForMissingBooks: false
    });
  };

  // One member through the standard Add modal: its choices (root folder, profiles, monitoring,
  // search) ride along as `options`.
  onAddMemberPress = (member, options) => {
    this.props.addCollectionMembers({
      members: [member],
      options
    });
  };

  // One missing-volume search per member already in the library -- the same command the
  // series page's "Search" runs, queued in order.
  onSearchAllPress = (collection, members) => {
    members.filter((m) => m.authorId).forEach((m) => {
      this.props.executeCommand({
        name: commandNames.MISSING_BOOK_SEARCH,
        authorId: m.authorId
      });
    });
  };

  //
  // Render

  render() {
    return (
      <Collections
        {...this.props}
        onLibrarySelect={this.onLibrarySelect}
        onAddMembersPress={this.onAddMembersPress}
        onAddMemberPress={this.onAddMemberPress}
        onSearchAllPress={this.onSearchAllPress}
      />
    );
  }
}

CollectionsConnector.propTypes = {
  library: PropTypes.string.isRequired,
  history: PropTypes.object.isRequired,
  fetchCollections: PropTypes.func.isRequired,
  addCollectionMembers: PropTypes.func.isRequired,
  executeCommand: PropTypes.func.isRequired,
  fetchRootFolders: PropTypes.func.isRequired,
  dispatchSetCollectionsLibrary: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(CollectionsConnector);
