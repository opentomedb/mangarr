import PropTypes from 'prop-types';
import React from 'react';
import Modal from 'Components/Modal/Modal';
import FixMatchModalContentConnector from './FixMatchModalContentConnector';

function FixMatchModal({ isOpen, onModalClose, ...otherProps }) {
  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      <FixMatchModalContentConnector
        {...otherProps}
        onModalClose={onModalClose}
      />
    </Modal>
  );
}

FixMatchModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default FixMatchModal;
