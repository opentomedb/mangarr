import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import Card from 'Components/Card';
import Modal from 'Components/Modal/Modal';
import { sizes } from 'Helpers/Props';
import { set } from 'Store/Actions/baseActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import translate from 'Utilities/String/translate';
import EditAudiobookStorageModalContent from './EditAudiobookStorageModalContent';
import { fetchAudiobookshelfLibraries, hostPort, libraryName, valueOf } from './lightNovelStorageHelpers';
import styles from './StorageCard.css';

const SECTION = 'settings.mediaManagement';

// Same reasoning as EbookStorageCard's MODAL_KEYS: this is what a Cancel (or the X, the backdrop,
// Escape) must put back, and only this -- the section is shared with the rest of the page.
const MODAL_KEYS = [
  'lightNovelAudioHome',
  'audiobookshelfUrl',
  'audiobookshelfApiKey',
  'audiobookshelfLibraryId',
  'audiobookshelfRemotePath',
  'audiobookshelfLocalPath'
];

// A factory, not a plain mapStateToProps: connect() calls this once per component instance and
// reuses the selector it returns, so createSettingsSectionSelector's memoization actually holds
// (a fresh selector built on every render would never hit its own cache).
function createMapStateToProps() {
  return createSettingsSectionSelector('mediaManagement');
}

const mapDispatchToProps = {
  dispatchSet: set
};

class AudiobookStorageCard extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.snapshot = {};

    this.state = {
      isModalOpen: false,
      libraries: []
    };
  }

  componentDidMount() {
    this.fetchLibraries();
  }

  //
  // Control

  fetchLibraries = () => {
    if (valueOf(this.props.settings, 'lightNovelAudioHome') === 'audiobookshelf') {
      fetchAudiobookshelfLibraries(this.props.settings)
        .done((data) => this.setState({ libraries: data.libraries || [] }))
        .fail(() => {});
    }
  };

  //
  // Listeners

  onCardPress = () => {
    this.snapshot = _.pick(this.props.pendingChanges, MODAL_KEYS);
    this.setState({ isModalOpen: true });
  };

  onModalClose = () => {
    const current = this.props.pendingChanges;
    const pendingChanges = { ..._.omit(current, MODAL_KEYS), ...this.snapshot };

    this.props.dispatchSet({ section: SECTION, pendingChanges, saveError: null });
    this.setState({ isModalOpen: false });
  };

  // The fields were just persisted -- close without reverting, and refresh the library list so the
  // summary line's name (not just its id) reflects what was actually saved.
  onModalSaved = () => {
    this.setState({ isModalOpen: false });
    this.fetchLibraries();
  };

  //
  // Render

  render() {
    const { settings } = this.props;
    const { isModalOpen, libraries } = this.state;

    const audioHome = valueOf(settings, 'lightNovelAudioHome');

    const summary = audioHome === 'audiobookshelf' ?
      [
        'Audiobookshelf',
        hostPort(valueOf(settings, 'audiobookshelfUrl')),
        libraryName(libraries, valueOf(settings, 'audiobookshelfLibraryId'), translate('ChooseAudiobookshelfLibrary'))
      ].join(' · ') :
      translate('LightNovelHomeEntryFolder');

    return (
      <Card
        className={styles.card}
        overlayContent={true}
        onPress={this.onCardPress}
      >
        <div className={styles.name}>
          {translate('LightNovelAudiobooks')}
        </div>

        <div className={styles.summary}>
          {summary}
        </div>

        <Modal
          size={sizes.MEDIUM}
          isOpen={isModalOpen}
          onModalClose={this.onModalClose}
        >
          <EditAudiobookStorageModalContent
            onModalClose={this.onModalClose}
            onModalSaved={this.onModalSaved}
          />
        </Modal>
      </Card>
    );
  }
}

AudiobookStorageCard.propTypes = {
  settings: PropTypes.object.isRequired,
  pendingChanges: PropTypes.object.isRequired,
  dispatchSet: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(AudiobookStorageCard);
