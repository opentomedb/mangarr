import PropTypes from 'prop-types';
import React, { Component } from 'react';
import MetadataPinModal from 'Book/MetadataPin/MetadataPinModal';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import styles from './EditBookModalContent.css';

// A light-novel volume's editions are monitored per media type: one FormGroup each (VD-4).
const LIGHT_NOVEL_EDITIONS = [
  { mediaType: 'ebook', label: 'EbookEdition' },
  { mediaType: 'audio', label: 'AudiobookEdition' }
];

class EditBookModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isPinMetadataModalOpen: false
    };
  }

  //
  // Listeners

  // UI pass (2026-09-24, SD-12/VD-3): the per-volume metadata pin is an edit of the volume, so it
  // opens from Edit Volume on the volume page instead of a pencil in the series row.
  onPinMetadataPress = () => {
    this.setState({ isPinMetadataModalOpen: true });
  };

  onPinMetadataModalClose = () => {
    this.setState({ isPinMetadataModalOpen: false });
  };

  onSavePress = () => {
    const {
      onSavePress
    } = this.props;

    onSavePress(false);

  };

  // Light novels: the Audio edition's coveredByVolume rides along inside the editions value, the
  // same shape BookEditionSelectInputConnector emits, so the existing save sends it. Blank / 0
  // clears the mark.
  onCoveredByVolumeChange = ({ value }) => {
    const {
      item,
      onInputChange
    } = this.props;

    const coveredByVolume = (value == null || value === '' || Number(value) <= 0) ? null : Number(value);
    const updatedEditions = item.editions.value.map((e) => {
      return e.mediaType === 'audio' ? { ...e, coveredByVolume } : e;
    });

    onInputChange({ name: 'editions', value: updatedEditions });
  };

  //
  // Render

  render() {
    const {
      title,
      authorId,
      volumeNumber,
      displayTitle,
      isLightNovelSeries,
      isReResolving,
      statistics,
      item,
      isFetching,
      isPopulated,
      error,
      isSaving,
      onInputChange,
      onModalClose,
      onReResolvePress,
      ...otherProps
    } = this.props;

    const {
      monitored,
      anyEditionOk,
      editions
    } = item;

    const hasFile = statistics ? statistics.bookFileCount > 0 : false;
    const errorMessage = getErrorMessage(error, translate('UnableToLoadEditions'));
    const audioEdition = editions.value.find((e) => e.mediaType === 'audio');
    const isLightNovel = !!audioEdition;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('EditBookModalHeader', { title: displayTitle })}
        </ModalHeader>

        <ModalBody>
          <Form
            {...otherProps}
          >
            <FormGroup>
              <FormLabel>
                {translate('Monitored')}
              </FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="monitored"
                helpText={translate('MonitoredHelpText')}
                {...monitored}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>
                {translate('AutomaticallySwitchEdition')}
              </FormLabel>

              <FormInputGroup
                type={inputTypes.CHECK}
                name="anyEditionOk"
                helpText={translate('AnyEditionOkHelpText')}
                {...anyEditionOk}
                onChange={onInputChange}
              />
            </FormGroup>

            {
              isFetching &&
                <LoadingIndicator />
            }

            {
              error &&
                <div>{errorMessage}</div>
            }

            {
              isPopulated && !isFetching && !!editions.value.length && !isLightNovel &&
                <FormGroup>
                  <FormLabel>
                    {translate('Edition')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.BOOK_EDITION_SELECT}
                    name="editions"
                    helpText={translate('EditionsHelpText')}
                    isDisabled={anyEditionOk.value && hasFile}
                    bookEditions={editions}
                    onChange={onInputChange}
                  />
                </FormGroup>
            }

            {
              isPopulated && !isFetching && isLightNovel &&
                LIGHT_NOVEL_EDITIONS.filter(({ mediaType }) => editions.value.some((e) => e.mediaType === mediaType)).map(({ mediaType, label }) => {
                  return (
                    <FormGroup key={mediaType}>
                      <FormLabel>
                        {translate(label)}
                      </FormLabel>

                      <FormInputGroup
                        type={inputTypes.BOOK_EDITION_SELECT}
                        name="editions"
                        mediaType={mediaType}
                        isDisabled={anyEditionOk.value && hasFile}
                        bookEditions={editions}
                        onChange={onInputChange}
                      />
                    </FormGroup>
                  );
                })
            }

            {
              isPopulated && !isFetching && !!audioEdition &&
                <FormGroup>
                  <FormLabel>
                    {translate('CoveredByVolumeInput')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="coveredByVolume"
                    min={0}
                    isFloat={true}
                    helpText={translate('CoveredByVolumeInputHelpText')}
                    value={audioEdition.coveredByVolume}
                    onChange={this.onCoveredByVolumeChange}
                  />
                </FormGroup>
            }

          </Form>
        </ModalBody>
        <ModalFooter className={styles.modalFooter}>
          <div className={styles.metadataButtons}>
            {
              volumeNumber == null ?
                null :
                <Button onPress={this.onPinMetadataPress}>
                  {translate('PinMetadata')}
                </Button>
            }

            {/* UI pass (SD-5): moved off the volume toolbar; manga only, as the button was. */}
            {
              isLightNovelSeries ?
                null :
                <SpinnerButton
                  isSpinning={isReResolving}
                  title={translate('ReResolveMetadataTooltip')}
                  onPress={onReResolvePress}
                >
                  {translate('ReResolveMetadata')}
                </SpinnerButton>
            }
          </div>

          <Button
            onPress={onModalClose}
          >
            {translate('Cancel')}
          </Button>

          <SpinnerButton
            isSpinning={isSaving}
            onPress={this.onSavePress}
          >
            {translate('Save')}
          </SpinnerButton>
        </ModalFooter>

        {
          volumeNumber == null ?
            null :
            <MetadataPinModal
              isOpen={this.state.isPinMetadataModalOpen}
              authorId={authorId}
              volumeNumber={volumeNumber}
              bookTitle={title}
              onModalClose={this.onPinMetadataModalClose}
            />
        }
      </ModalContent>
    );
  }
}

EditBookModalContent.propTypes = {
  bookId: PropTypes.number.isRequired,
  title: PropTypes.string.isRequired,
  authorId: PropTypes.number.isRequired,
  volumeNumber: PropTypes.oneOfType([PropTypes.number, PropTypes.string]),
  displayTitle: PropTypes.string.isRequired,
  isLightNovelSeries: PropTypes.bool.isRequired,
  isReResolving: PropTypes.bool.isRequired,
  statistics: PropTypes.object.isRequired,
  item: PropTypes.object.isRequired,
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  isPopulated: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  onInputChange: PropTypes.func.isRequired,
  onSavePress: PropTypes.func.isRequired,
  onReResolvePress: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default EditBookModalContent;
