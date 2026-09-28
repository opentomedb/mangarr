import PropTypes from 'prop-types';
import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import { wantedLibraryRoute } from './wantedLibraries';

// /wanted/missing and /wanted/cutoffunmet open the remembered tab (wanted.library, persisted;
// default Manga) -- the same pattern as the index's and Collections' library redirects.
function WantedLibraryRedirect(props) {
  const {
    page,
    library
  } = props;

  return (
    <Redirect to={getPathWithUrlBase(wantedLibraryRoute(page, library))} />
  );
}

WantedLibraryRedirect.propTypes = {
  page: PropTypes.oneOf(['missing', 'cutoffUnmet']).isRequired,
  library: PropTypes.string.isRequired
};

function mapStateToProps(state) {
  return {
    library: state.wanted.library
  };
}

export default connect(mapStateToProps)(WantedLibraryRedirect);
