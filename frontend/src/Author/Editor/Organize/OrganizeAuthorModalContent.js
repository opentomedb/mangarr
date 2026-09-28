import PropTypes from 'prop-types';
import React from 'react';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import translateElements from 'Utilities/String/translateElements';
import styles from './OrganizeAuthorModalContent.css';

function OrganizeAuthorModalContent(props) {
  const {
    authorNames,
    onModalClose,
    onOrganizeAuthorPress
  } = props;

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('OrganizeSelectedSeries')}
      </ModalHeader>

      <ModalBody>
        <Alert>
          {translateElements('OrganizeRenamePreviewTip', {
            icon: (
              <Icon
                className={styles.renameIcon}
                name={icons.ORGANIZE}
              />
            )
          })}
        </Alert>

        <div className={styles.message}>
          {translate('OrganizeSelectedSeriesMessage', { count: authorNames.length })}
        </div>

        <ul>
          {
            authorNames.map((authorName) => {
              return (
                <li key={authorName}>
                  {authorName}
                </li>
              );
            })
          }
        </ul>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>
          {translate('Cancel')}
        </Button>

        <Button
          kind={kinds.DANGER}
          onPress={onOrganizeAuthorPress}
        >
          {translate('Organize')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

OrganizeAuthorModalContent.propTypes = {
  authorNames: PropTypes.arrayOf(PropTypes.string).isRequired,
  onModalClose: PropTypes.func.isRequired,
  onOrganizeAuthorPress: PropTypes.func.isRequired
};

export default OrganizeAuthorModalContent;
