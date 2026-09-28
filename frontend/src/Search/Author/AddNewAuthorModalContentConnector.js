import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { addAuthor, setAuthorAddDefault } from 'Store/Actions/searchActions';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import createSystemStatusSelector from 'Store/Selectors/createSystemStatusSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import { lightNovelRoot } from 'Utilities/Author/libraryRoots';
import AddNewAuthorModalContent from './AddNewAuthorModalContent';

function createMapStateToProps() {
  return createSelector(
    (state) => state.search,
    (state) => state.settings.metadataProfiles,
    (state) => state.settings.qualityProfiles,
    (state) => state.settings.rootFolders,
    (state, { library }) => library === 'lightNovel',
    (state, { isAdding }) => isAdding,
    (state, { addError }) => addError,
    (state) => state.settings.ui.item.preferredEditionLanguages,
    createDimensionsSelector(),
    createSystemStatusSelector(),
    (searchState, metadataProfiles, qualityProfiles, rootFolders, isLightNovel, ownIsAdding, ownAddError, preferredEditionLanguages, dimensions, systemStatus) => {
      const {
        authorDefaults,
        lightNovelDefaults
      } = searchState;

      // A caller with its own add (Collections) passes its add state; Add New uses the search's.
      const isAdding = ownIsAdding === undefined ? searchState.isAdding : ownIsAdding;
      const addError = ownIsAdding === undefined ? searchState.addError : ownAddError;

      // First-time seeds, by the names of the profiles the app creates on startup (contract
      // "Quality / parser / extensions"). A value the user picked is persisted in the library's
      // defaults blob and wins; a missing profile leaves the stored value as it was.
      const profileId = (name) => (qualityProfiles.items.find((p) => p.name === name) || {}).id || 0;

      // Manga: the select would otherwise auto-pick the first profile sorted by name, which is a
      // light-novel profile now that those exist, and a manga on the audio-only profile never grabs.
      let defaults = {
        ...authorDefaults,
        qualityProfileId: authorDefaults.qualityProfileId || profileId('Manga')
      };

      if (isLightNovel) {
        // Light novel: the light-novel root (its default profile's class, then the /lightnovel path
        // rule; beta readiness F4) and the two light-novel profiles.
        const root = lightNovelRoot(rootFolders, qualityProfiles.items) || {};

        defaults = {
          ...lightNovelDefaults,
          rootFolderPath: lightNovelDefaults.rootFolderPath || root.path || '',
          qualityProfileId: lightNovelDefaults.qualityProfileId || profileId('Light Novel EPUB'),
          audioQualityProfileId: lightNovelDefaults.audioQualityProfileId || profileId('Light Novel Audio')
        };
      }

      const {
        settings,
        validationErrors,
        validationWarnings
      } = selectSettings(defaults, {}, addError);

      // Preferred Edition (2026-09-24): the Settings -> UI chain, read as the backend's ParseChain /
      // IsEnglishOnly do (blank or every entry "en" = English only) -- the Add form's Edition select stays
      // hidden for an English-only chain unless the work has another edition.
      const isEnglishChain = (preferredEditionLanguages || '')
        .split(',')
        .map((code) => code.trim().toLowerCase())
        .filter(Boolean)
        .every((code) => code === 'en');

      return {
        isAdding,
        addError,
        isLightNovel,
        isEnglishChain,
        showMetadataProfile: metadataProfiles.items.length > 2, // NONE (not allowed for authors) and one other
        isSmallScreen: dimensions.isSmallScreen,
        validationErrors,
        validationWarnings,
        isWindows: systemStatus.isWindows,
        ...settings
      };
    }
  );
}

const mapDispatchToProps = {
  setAuthorAddDefault,
  addAuthor
};

class AddNewAuthorModalContentConnector extends Component {

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.setAuthorAddDefault({
      [name]: value,
      library: this.props.isLightNovel ? 'lightNovel' : 'manga'
    });
  };

  onAddAuthorPress = (searchForMissingBooks, formats = {}, editionLanguage = null) => {
    const {
      foreignAuthorId,
      isLightNovel,
      rootFolderPath,
      monitor,
      monitorNewItems,
      qualityProfileId,
      audioQualityProfileId,
      metadataProfileId,
      tags
    } = this.props;

    const payload = {
      foreignAuthorId,
      rootFolderPath: rootFolderPath.value,
      monitor: monitor.value,
      monitorNewItems: monitorNewItems.value,
      qualityProfileId: qualityProfileId.value,
      metadataProfileId: metadataProfileId.value,
      tags: tags.value,
      searchForMissingBooks,
      editionLanguage
    };

    // Light novel: both editions of every volume are minted; an unchecked format's editions are
    // added unmonitored (addOptions.monitorMediaTypes, contract "Add modal").
    if (isLightNovel) {
      payload.audioQualityProfileId = audioQualityProfileId.value;
      payload.monitorMediaTypes = [
        formats.addEbook ? 'ebook' : null,
        formats.addAudio ? 'audio' : null
      ].filter(Boolean);
    }

    // Collections (UI pass 2026-09-24, CO-4) open this same modal for a member that is not a
    // search result; they hand in their own add so the payload goes to the collection's add.
    if (this.props.onAddAuthor) {
      this.props.onAddAuthor(payload);
    } else {
      this.props.addAuthor(payload);
    }
  };

  //
  // Render

  render() {
    return (
      <AddNewAuthorModalContent
        {...this.props}
        onInputChange={this.onInputChange}
        onAddAuthorPress={this.onAddAuthorPress}
      />
    );
  }
}

AddNewAuthorModalContentConnector.propTypes = {
  foreignAuthorId: PropTypes.string.isRequired,
  library: PropTypes.string,
  isLightNovel: PropTypes.bool.isRequired,
  rootFolderPath: PropTypes.object,
  monitor: PropTypes.object.isRequired,
  monitorNewItems: PropTypes.object.isRequired,
  qualityProfileId: PropTypes.object,
  audioQualityProfileId: PropTypes.object,
  metadataProfileId: PropTypes.object,
  tags: PropTypes.object.isRequired,
  onModalClose: PropTypes.func.isRequired,
  setAuthorAddDefault: PropTypes.func.isRequired,
  addAuthor: PropTypes.func.isRequired,
  onAddAuthor: PropTypes.func
};

export default connect(createMapStateToProps, mapDispatchToProps)(AddNewAuthorModalContentConnector);
