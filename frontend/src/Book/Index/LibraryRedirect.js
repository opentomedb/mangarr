import PropTypes from 'prop-types';
import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import { bookIndexLibraryRoute } from './bookIndexLibraries';

// /books opens the remembered tab (bookIndex.library, persisted; default Manga) -- the same
// pattern as the author index's LibraryRedirect (authorIndex.library).
function LibraryRedirect(props) {
  const {
    library
  } = props;

  return (
    <Redirect to={getPathWithUrlBase(bookIndexLibraryRoute(library))} />
  );
}

LibraryRedirect.propTypes = {
  library: PropTypes.string.isRequired
};

function mapStateToProps(state) {
  return {
    library: state.bookIndex.library
  };
}

export default connect(mapStateToProps)(LibraryRedirect);
