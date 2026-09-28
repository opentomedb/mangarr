import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import { fetchUISettings, saveUISettings, setUISettingsValue } from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import UISettings from './UISettings';

const SECTION = 'ui';

function createLanguagesSelector() {
  return createSelector(
    (state) => state.settings.languages,
    (languages) => {
      const items = languages.items;
      const filterItems = ['Any', 'Unknown'];

      if (!items) {
        return [];
      }

      const newItems = items.filter((lang) => !filterItems.includes(lang.name)).map((item) => {
        return {
          key: item.id,
          value: item.name
        };
      });

      return newItems;
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    (state) => state.settings.advancedSettings,
    createSettingsSectionSelector(SECTION),
    createLanguagesSelector(),
    (advancedSettings, sectionSettings, languages) => {
      return {
        advancedSettings,
        languages,
        ...sectionSettings
      };
    }
  );
}

const mapDispatchToProps = {
  setUISettingsValue,
  saveUISettings,
  fetchUISettings,
  clearPendingChanges
};

class UISettingsConnector extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      editionMarkets: [],
      otherEditionSeries: 0,
      otherEditionManga: 0,
      otherEditionLightNovels: 0
    };
  }

  componentDidMount() {
    this.props.fetchUISettings();

    // Preferred Edition (2026-09-24): the catalogue's markets for the edition list (its own GET, never
    // part of the UI config resource, which SaveConfig writes back whole).
    const { request, abortRequest } = createAjaxRequest({ url: '/edition/markets' });

    this._abortMarketsRequest = abortRequest;

    request.done((data) => {
      this.setState({
        editionMarkets: data.languages,
        otherEditionSeries: data.otherEditionSeries,
        otherEditionManga: data.otherEditionManga,
        otherEditionLightNovels: data.otherEditionLightNovels
      });
    });

    // Preferred Edition (2026-09-25, M15 polish): this used to fail silently. editionMarkets stays
    // [], which is the English-only fallback PreferredEditionInput already renders with no markets.
    request.fail((xhr) => {
      console.error('Failed to fetch edition markets', xhr);
    });
  }

  componentWillUnmount() {
    if (this._abortMarketsRequest) {
      this._abortMarketsRequest();
    }

    this.props.clearPendingChanges({ section: `settings.${SECTION}` });
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.setUISettingsValue({ name, value });
  };

  onSavePress = () => {
    this.props.saveUISettings();
  };

  //
  // Render

  render() {
    return (
      <UISettings
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        {...this.props}
        {...this.state}
      />
    );
  }
}

UISettingsConnector.propTypes = {
  setUISettingsValue: PropTypes.func.isRequired,
  saveUISettings: PropTypes.func.isRequired,
  fetchUISettings: PropTypes.func.isRequired,
  clearPendingChanges: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(UISettingsConnector);
