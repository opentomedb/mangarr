import PropTypes from 'prop-types';
import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { libraryRoute } from 'Utilities/Author/libraries';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';

// /, /series and /authors open the remembered tab (authorIndex.library, persisted; default Manga).
function LibraryRedirect(props) {
  const {
    library
  } = props;

  return (
    <Redirect to={getPathWithUrlBase(libraryRoute(library))} />
  );
}

LibraryRedirect.propTypes = {
  library: PropTypes.string.isRequired
};

function mapStateToProps(state) {
  return {
    library: state.authorIndex.library
  };
}

export default connect(mapStateToProps)(LibraryRedirect);
