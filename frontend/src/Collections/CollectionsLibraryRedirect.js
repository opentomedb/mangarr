import PropTypes from 'prop-types';
import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { LIGHT_NOVEL } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import { collectionsLibraryRoute } from './collectionsLibraries';

// /collections opens the remembered tab (collections.library, persisted; default Manga) -- the
// same pattern as the author index's LibraryRedirect (authorIndex.library). A deep link carries a
// collection id as its hash (AuthorDetailsHeader's "Part of <parent line>"); the hash's own "~ln"
// (LibraryTypes.LightNovelSuffix) says which tab actually has it, so that link opens the right one
// regardless of which tab was last open, hash preserved so the page still scrolls to it.
function CollectionsLibraryRedirect(props) {
  const {
    library,
    location
  } = props;

  const hash = location.hash || '';
  const target = hash.includes('~ln') ? LIGHT_NOVEL : library;

  return (
    <Redirect to={getPathWithUrlBase(collectionsLibraryRoute(target)) + hash} />
  );
}

CollectionsLibraryRedirect.propTypes = {
  library: PropTypes.string.isRequired,
  location: PropTypes.shape({
    hash: PropTypes.string
  }).isRequired
};

function mapStateToProps(state) {
  return {
    library: state.collections.library
  };
}

export default connect(mapStateToProps)(CollectionsLibraryRedirect);
