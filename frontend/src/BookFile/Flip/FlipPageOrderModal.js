import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './FlipPageOrderModal.css';

// Confirmation dialog for flipping a volume's page order. Shows the file's actual first and
// last page so the decision is made on evidence: a correctly-ordered volume has its cover
// first; a reversed one has the cover last. Flipping is a pure reorder, so running it again
// undoes it — reopen the dialog after the command finishes to verify the result.
class FlipPageOrderModal extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    // Cache-buster fixed per open, so both images reload fresh each time the dialog opens
    // (the file changes underneath after a flip) but don't flicker while it is open.
    this.state = { openedAt: Date.now() };
  }

  componentDidUpdate(prevProps) {
    if (!prevProps.isOpen && this.props.isOpen) {
      this.setState({ openedAt: Date.now() });
    }
  }

  //
  // Listeners

  onFlipConfirmed = () => {
    this.props.onFlipPress();
    this.props.onModalClose();
  };

  //
  // Render

  render() {
    const {
      isOpen,
      bookFileId,
      bookTitle,
      onModalClose
    } = this.props;

    const apiKey = encodeURIComponent(window.Readarr.apiKey);
    const urlBase = window.Readarr.urlBase;
    const pageUrl = (position) => `${urlBase}/api/v1/bookfile/${bookFileId}/page?position=${position}&apikey=${apiKey}&t=${this.state.openedAt}`;

    return (
      <Modal
        isOpen={isOpen}
        size={sizes.MEDIUM}
        onModalClose={onModalClose}
      >
        <ModalContent onModalClose={onModalClose}>
          <ModalHeader>
            {translate('FlipPageOrderModalHeader', { bookTitle })}
          </ModalHeader>

          <ModalBody>
            <div className={styles.hint}>
              {translate('FlipPageOrderHint')}
            </div>

            <div className={styles.pages}>
              <div className={styles.page}>
                <div className={styles.pageLabel}>{translate('FirstPage')}</div>
                <img
                  className={styles.pageImage}
                  src={pageUrl('first')}
                  alt={translate('FirstPage')}
                />
              </div>

              <div className={styles.page}>
                <div className={styles.pageLabel}>{translate('LastPage')}</div>
                <img
                  className={styles.pageImage}
                  src={pageUrl('last')}
                  alt={translate('LastPage')}
                />
              </div>
            </div>
          </ModalBody>

          <ModalFooter>
            <Button onPress={onModalClose}>
              {translate('Cancel')}
            </Button>

            <Button
              kind={kinds.PRIMARY}
              onPress={this.onFlipConfirmed}
            >
              {translate('FlipPageOrder')}
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    );
  }
}

FlipPageOrderModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  bookFileId: PropTypes.number.isRequired,
  bookTitle: PropTypes.string.isRequired,
  onFlipPress: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default FlipPageOrderModal;
