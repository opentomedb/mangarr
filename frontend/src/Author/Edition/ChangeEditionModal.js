import PropTypes from 'prop-types';
import React from 'react';
import Modal from 'Components/Modal/Modal';
import ChangeEditionModalContent from './ChangeEditionModalContent';

function ChangeEditionModal(props) {
  const {
    isOpen,
    onModalClose,
    ...otherProps
  } = props;

  return (
    <Modal
      isOpen={isOpen}
      onModalClose={onModalClose}
    >
      {
        isOpen ?
          <ChangeEditionModalContent
            {...otherProps}
            onModalClose={onModalClose}
          /> :
          null
      }
    </Modal>
  );
}

ChangeEditionModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default ChangeEditionModal;
