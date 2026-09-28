import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import { scrollDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import translateElements from 'Utilities/String/translateElements';
import SelectEditionRowConnector from './SelectEditionRowConnector';
import styles from './SelectEditionModalContent.css';

const columns = [
  {
    name: 'file',
    label: () => translate('File'),
    isVisible: true
  },
  {
    name: 'book',
    label: () => translate('Book'),
    isVisible: true
  },
  {
    name: 'edition',
    label: () => translate('Edition'),
    isVisible: true
  }
];

class SelectEditionModalContent extends Component {

  //
  // Render

  render() {
    const {
      files,
      isPopulated,
      isFetching,
      error,
      onEditionSelect,
      onModalClose,
      ...otherProps
    } = this.props;

    if (!isPopulated && !error) {
      return (<LoadingIndicator />);
    }

    if (!isFetching && error) {
      return (
        <div>
          {translate('LoadingEditionsFailed')}
        </div>
      );
    }

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('ManualImportSelectEdition')}
        </ModalHeader>

        <ModalBody
          className={styles.modalBody}
          scrollDirection={scrollDirections.VERTICAL}
        >
          <Alert>
            {translateElements('SelectEditionOverrideWarning', {
              disableAutomatic: <b>{translate('SelectEditionDisablesAutomatic')}</b>
            })}
          </Alert>

          <Table
            columns={columns}
            {...otherProps}
          >
            <TableBody>
              {
                files.map((item) => {
                  return (
                    <SelectEditionRowConnector
                      key={item.id}
                      importId={item.id}
                      fileName={item.name}
                      matchedEditionId={item.matchedEditionId}
                      columns={columns}
                      onEditionSelect={onEditionSelect}
                      {...item.book}
                    />
                  );
                })
              }
            </TableBody>
          </Table>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            {translate('Cancel')}
          </Button>
        </ModalFooter>
      </ModalContent>
    );
  }
}

SelectEditionModalContent.propTypes = {
  files: PropTypes.arrayOf(PropTypes.object).isRequired,
  isFetching: PropTypes.bool,
  isPopulated: PropTypes.bool,
  error: PropTypes.object,
  onEditionSelect: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default SelectEditionModalContent;
