import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as releaseActions from 'Store/Actions/releaseActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import InteractiveSearch from './InteractiveSearch';

function createMapStateToProps(appState, { type }) {
  return createSelector(
    (state) => state.releases.items.length,
    createClientSideCollectionSelector('releases', `releases.${type}`),
    createUISettingsSelector(),
    (totalReleasesCount, releases, uiSettings) => {
      return {
        totalReleasesCount,
        longDateFormat: uiSettings.longDateFormat,
        timeFormat: uiSettings.timeFormat,
        ...releases
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    dispatchFetchReleases(payload) {
      dispatch(releaseActions.fetchReleases(payload));
    },

    onSortPress(sortKey, sortDirection) {
      dispatch(releaseActions.setReleasesSort({ sortKey, sortDirection }));
    },

    onFilterSelect(selectedFilterKey) {
      const action = props.type === 'book' ?
        releaseActions.setBookReleasesFilter :
        releaseActions.setAuthorReleasesFilter;

      dispatch(action({ selectedFilterKey }));
    },

    onGrabPress(payload) {
      dispatch(releaseActions.grabRelease(payload));
    }
  };
}

class InteractiveSearchConnector extends Component {

  //
  // Lifecycle

  componentDidMount() {
    const {
      searchPayload,
      lastSearchPayload,
      isPopulated,
      dispatchFetchReleases
    } = this.props;

    // If search results are not yet isPopulated fetch them,
    // otherwise re-show the existing props — unless they were fetched for another payload (a
    // light-novel page remounts this tab after the EPUB | Audio tab changed).

    if (!isPopulated || !_.isEqual(lastSearchPayload, searchPayload)) {
      dispatchFetchReleases(searchPayload);
    }
  }

  componentDidUpdate(prevProps) {
    const {
      searchPayload,
      dispatchFetchReleases
    } = this.props;

    // The volume page's EPUB | Audio toggle changes the payload while this tab is showing.
    if (!_.isEqual(prevProps.searchPayload, searchPayload)) {
      dispatchFetchReleases(searchPayload);
    }
  }

  //
  // Render

  render() {
    const {
      dispatchFetchReleases,
      ...otherProps
    } = this.props;

    return (

      <InteractiveSearch
        {...otherProps}
      />
    );
  }
}

InteractiveSearchConnector.propTypes = {
  searchPayload: PropTypes.object.isRequired,
  lastSearchPayload: PropTypes.object,
  isPopulated: PropTypes.bool.isRequired,
  dispatchFetchReleases: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, createMapDispatchToProps)(InteractiveSearchConnector);
