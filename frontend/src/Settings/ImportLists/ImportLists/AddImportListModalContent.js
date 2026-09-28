import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';
import AddImportListItem from './AddImportListItem';
import styles from './AddImportListModalContent.css';

// UI translations v1 (2026-09-25): the group is the backend's ImportListType enum (ImportListType.cs),
// a fixed set shown as UI text; anything unknown keeps its title-cased name.
function getListGroupTitle(listType) {
  switch (listType) {
    case 'program':
      return translate('ImportListGroupProgram');
    case 'other':
      return translate('ImportListGroupOther');
    default:
      return translate('ImportListGroup', { type: titleCase(listType) });
  }
}

class AddImportListModalContent extends Component {

  //
  // Render

  render() {
    const {
      isSchemaFetching,
      isSchemaPopulated,
      schemaError,
      listGroups,
      onImportListSelect,
      onModalClose
    } = this.props;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('AddList')}
        </ModalHeader>

        <ModalBody>
          {
            isSchemaFetching &&
              <LoadingIndicator />
          }

          {
            !isSchemaFetching && !!schemaError &&
              <div>
                {translate('UnableToAddANewListPleaseTryAgain')}
              </div>
          }

          {
            isSchemaPopulated && !schemaError &&
              <div>

                <Alert kind={kinds.INFO}>
                  <div>
                    {translate('ReadarrSupportsMultipleListsForImportingBooksAndAuthorsIntoTheDatabase')}
                  </div>
                  <div>
                    {translate('ForMoreInformationOnTheIndividualListsClickOnTheInfoButtons')}
                  </div>
                </Alert>
                {
                  Object.keys(listGroups).map((key) => {
                    return (
                      <FieldSet legend={getListGroupTitle(key)} key={key}>
                        <div className={styles.lists}>
                          {
                            listGroups[key].map((list) => {
                              return (
                                <AddImportListItem
                                  key={list.implementation}
                                  implementation={list.implementation}
                                  {...list}
                                  onImportListSelect={onImportListSelect}
                                />
                              );
                            })
                          }
                        </div>
                      </FieldSet>
                    );
                  })
                }
              </div>
          }
        </ModalBody>
        <ModalFooter>
          <Button
            onPress={onModalClose}
          >
            {translate('Close')}
          </Button>
        </ModalFooter>
      </ModalContent>
    );
  }
}

AddImportListModalContent.propTypes = {
  isSchemaFetching: PropTypes.bool.isRequired,
  isSchemaPopulated: PropTypes.bool.isRequired,
  schemaError: PropTypes.object,
  listGroups: PropTypes.object.isRequired,
  onImportListSelect: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default AddImportListModalContent;
