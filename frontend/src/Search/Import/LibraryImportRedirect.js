import PropTypes from 'prop-types';
import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import { importLibraryRoute } from './importLibraries';

// /add/import opens the remembered tab. Library Import shares `search.library` (persisted) with
// Add New, the way the two Wanted pages share `wanted.library`: both are "add a series to this
// library" pages under Add New in the sidebar, so the last library chosen on either opens both.
function LibraryImportRedirect(props) {
  return (
    <Redirect to={getPathWithUrlBase(importLibraryRoute(props.library))} />
  );
}

LibraryImportRedirect.propTypes = {
  library: PropTypes.string.isRequired
};

function mapStateToProps(state) {
  return {
    library: state.search.library
  };
}

export default connect(mapStateToProps)(LibraryImportRedirect);
