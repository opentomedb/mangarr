/* eslint max-params: 0 */
import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { toggleAuthorMonitored } from 'Store/Actions/authorActions';
import createAuthorSelector from 'Store/Selectors/createAuthorSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import translate from 'Utilities/String/translate';
import AuthorDetailsHeader from './AuthorDetailsHeader';

function createMapStateToProps() {
  return createSelector(
    (state) => state.authors,
    createAuthorSelector(),
    createDimensionsSelector(),
    (state) => state.settings.qualityProfiles.items,
    (authors, author, dimensions, qualityProfiles) => {
      const alternateTitles = _.reduce(author.alternateTitles, (acc, alternateTitle) => {
        if ((alternateTitle.seasonNumber === -1 || alternateTitle.seasonNumber === undefined) &&
            (alternateTitle.sceneSeasonNumber === -1 || alternateTitle.sceneSeasonNumber === undefined)) {
          acc.push(alternateTitle.title);
        }

        return acc;
      }, []);

      // For the profile labels' tooltips (a title attribute needs the name as a string).
      const profileName = (id) => qualityProfiles.find((p) => p.id === id)?.name ?? translate('Unknown');

      return {
        ...author,
        qualityProfileName: profileName(author.qualityProfileId),
        audioQualityProfileName: profileName(author.audioQualityProfileId),
        isSaving: authors.isSaving,
        alternateTitles,
        isSmallScreen: dimensions.isSmallScreen
      };
    }
  );
}

const mapDispatchToProps = {
  toggleAuthorMonitored
};

class AuthorDetailsHeaderConnector extends Component {

  //
  // Listeners

  onMonitorTogglePress = (monitored) => {
    this.props.toggleAuthorMonitored({
      authorId: this.props.authorId,
      monitored
    });
  };

  //
  // Render

  render() {
    return (
      <AuthorDetailsHeader
        {...this.props}
        onMonitorTogglePress={this.onMonitorTogglePress}
      />
    );
  }
}

AuthorDetailsHeaderConnector.propTypes = {
  authorId: PropTypes.number.isRequired,
  toggleAuthorMonitored: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AuthorDetailsHeaderConnector);
