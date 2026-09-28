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
import EditEbookStorageModalContent from './EditEbookStorageModalContent';
import { fetchCalibreLibraries, hostPort, libraryName, preferredLightNovelFormatOptions, valueOf } from './lightNovelStorageHelpers';
import styles from './StorageCard.css';

const SECTION = 'settings.mediaManagement';

// The fields this card's modal owns: a Cancel (or the X, the backdrop, Escape) must put exactly
// these back the way they were when the modal opened, and leave every other pending edit on the
// Media Management page alone -- clearPendingChanges (whole-section) is not safe to use here the
// way EditNotificationModalConnector/EditRootFolderModalConnector use it, because this section is
// shared with the rest of the page instead of being this card's own.
const MODAL_KEYS = [
  'lightNovelEbookHome',
  'calibreContentServerUrl',
  'calibreUsername',
  'calibrePassword',
  'calibreLibrary',
  'preferredLightNovelFormat',
  'calibreRemotePath',
  'calibreLocalPath'
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

class EbookStorageCard extends Component {

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
    if (valueOf(this.props.settings, 'lightNovelEbookHome') === 'calibre') {
      fetchCalibreLibraries(this.props.settings)
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

  // Cancel, the header's close button, a backdrop click and Escape all resolve here (they all call
  // the same onModalClose prop passed to Modal/ModalContent): put the modal's own fields back to
  // what they were when it opened, keep every other pending change on the page untouched.
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

    const ebookHome = valueOf(settings, 'lightNovelEbookHome');

    const summary = ebookHome === 'calibre' ?
      [
        translate('LightNovelHomeCalibre'),
        hostPort(valueOf(settings, 'calibreContentServerUrl')),
        libraryName(libraries, valueOf(settings, 'calibreLibrary'), translate('CalibreServerDefaultLibrary')),
        (preferredLightNovelFormatOptions.find((o) => o.key === valueOf(settings, 'preferredLightNovelFormat')) || preferredLightNovelFormatOptions[0]).value
      ].join(' · ') :
      translate('LightNovelHomeEntryFolder');

    return (
      <Card
        className={styles.card}
        overlayContent={true}
        onPress={this.onCardPress}
      >
        <div className={styles.name}>
          {translate('LightNovelEbooks')}
        </div>

        <div className={styles.summary}>
          {summary}
        </div>

        <Modal
          size={sizes.MEDIUM}
          isOpen={isModalOpen}
          onModalClose={this.onModalClose}
        >
          <EditEbookStorageModalContent
            onModalClose={this.onModalClose}
            onModalSaved={this.onModalSaved}
          />
        </Modal>
      </Card>
    );
  }
}

EbookStorageCard.propTypes = {
  settings: PropTypes.object.isRequired,
  pendingChanges: PropTypes.object.isRequired,
  dispatchSet: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(EbookStorageCard);
