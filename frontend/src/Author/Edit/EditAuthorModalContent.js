import PropTypes from 'prop-types';
import React, { Component } from 'react';
import AuthorMetadataProfilePopoverContent from 'AddAuthor/AuthorMetadataProfilePopoverContent';
import AuthorMonitorNewItemsOptionsPopoverContent from 'AddAuthor/AuthorMonitorNewItemsOptionsPopoverContent';
import ChangeEditionModal from 'Author/Edition/ChangeEditionModal';
import MoveAuthorModal from 'Author/MoveAuthor/MoveAuthorModal';
import MetadataPinModal from 'Book/MetadataPin/MetadataPinModal';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import Popover from 'Components/Tooltip/Popover';
import { icons, inputTypes, kinds, tooltipPositions } from 'Helpers/Props';
import editionName from 'Utilities/String/editionName';
import translate from 'Utilities/String/translate';
import styles from './EditAuthorModalContent.css';

class EditAuthorModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isConfirmMoveModalOpen: false,
      isPinWriterModalOpen: false,
      isChangeEditionModalOpen: false
    };
  }

  //
  // Listeners

  onSavePress = () => {
    const {
      isPathChanging,
      onSavePress
    } = this.props;

    if (isPathChanging && !this.state.isConfirmMoveModalOpen) {
      this.setState({ isConfirmMoveModalOpen: true });
    } else {
      this.setState({ isConfirmMoveModalOpen: false });

      onSavePress(false);
    }
  };

  // UI pass (2026-09-24, SD-7): the light novel's pinned writer is a per-entry override, so it
  // lives here with the other overrides instead of on the series toolbar.
  onPinWriterPress = () => {
    this.setState({ isPinWriterModalOpen: true });
  };

  onPinWriterModalClose = () => {
    this.setState({ isPinWriterModalOpen: false });
  };

  // Preferred Edition (2026-09-24, spec §4): the edition is changed by its own previewed command, never by
  // Save (the PUT never copies metadata). An applied change closes Edit Series too: a rename moves the
  // folder, and a Save from this form would write the old path back.
  onChangeEditionPress = () => {
    this.setState({ isChangeEditionModalOpen: true });
  };

  onChangeEditionModalClose = (applied) => {
    this.setState({ isChangeEditionModalOpen: false });

    if (applied === true) {
      this.props.onModalClose();
    }
  };

  onMoveAuthorPress = () => {
    this.setState({ isConfirmMoveModalOpen: false });

    this.props.onSavePress(true);
  };

  //
  // Render

  render() {
    const {
      authorId,
      authorName,
      library,
      editionLanguage,
      item,
      isSaving,
      showMetadataProfile,
      originalPath,
      onInputChange,
      onModalClose,
      onDeleteAuthorPress,
      ...otherProps
    } = this.props;

    const {
      monitored,
      monitorNewItems,
      qualityProfileId,
      audioQualityProfileId,
      audioAvailable,
      adoptedTagWrite,
      metadataProfileId,
      maxVolume,
      path,
      tags
    } = item;

    const isLightNovel = library === 'lightNovel';

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('EditSeriesModalHeader', { authorName })}
        </ModalHeader>

        <ModalBody>
          <Form {...otherProps}>
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
                {translate('MonitorNewItems')}
                <Popover
                  anchor={
                    <Icon
                      className={styles.labelIcon}
                      name={icons.INFO}
                    />
                  }
                  title={translate('MonitorNewItems')}
                  body={<AuthorMonitorNewItemsOptionsPopoverContent />}
                  position={tooltipPositions.RIGHT}
                />
              </FormLabel>

              <FormInputGroup
                type={inputTypes.MONITOR_NEW_ITEMS_SELECT}
                name="monitorNewItems"
                helpText={translate('MonitorNewItemsHelpText')}
                {...monitorNewItems}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>
                {isLightNovel ? translate('EbookQualityProfile') : translate('QualityProfile')}
              </FormLabel>

              <FormInputGroup
                type={inputTypes.QUALITY_PROFILE_SELECT}
                name="qualityProfileId"
                {...qualityProfileId}
                onChange={onInputChange}
              />
            </FormGroup>

            {
              isLightNovel ?
                <FormGroup>
                  <FormLabel>
                    {translate('AudioQualityProfile')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.QUALITY_PROFILE_SELECT}
                    name="audioQualityProfileId"
                    {...audioQualityProfileId}
                    onChange={onInputChange}
                  />
                </FormGroup> :
                null
            }

            {
              showMetadataProfile &&
                <FormGroup>
                  <FormLabel>
                    {translate('MetadataProfile')}

                    <Popover
                      anchor={
                        <Icon
                          className={styles.labelIcon}
                          name={icons.INFO}
                        />
                      }
                      title={translate('MetadataProfile')}
                      body={<AuthorMetadataProfilePopoverContent />}
                      position={tooltipPositions.RIGHT}
                    />

                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.METADATA_PROFILE_SELECT}
                    name="metadataProfileId"
                    helpText={translate('MetadataProfileIdHelpText')}
                    includeNone={true}
                    {...metadataProfileId}
                    onChange={onInputChange}
                  />
                </FormGroup>
            }

            <FormGroup>
              <FormLabel>
                {translate('MaxVolume')}
              </FormLabel>

              <FormInputGroup
                type={inputTypes.NUMBER}
                name="maxVolume"
                helpText={translate('MaxVolumeHelpText')}
                {...maxVolume}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>
                {translate('Path')}
              </FormLabel>

              <FormInputGroup
                type={inputTypes.PATH}
                name="path"
                {...path}
                onChange={onInputChange}
              />
            </FormGroup>

            <FormGroup>
              <FormLabel>{translate('Edition')}</FormLabel>

              <div>
                <Button onPress={this.onChangeEditionPress}>
                  {`${editionName(editionLanguage)} · ${translate('ChangeEdition')}`}
                </Button>

                <FormInputHelpText text={translate('EditionHelpText')} />
              </div>
            </FormGroup>

            <FormGroup>
              <FormLabel>
                {translate('Tags')}
              </FormLabel>

              <FormInputGroup
                type={inputTypes.TAG}
                name="tags"
                {...tags}
                onChange={onInputChange}
              />
            </FormGroup>

            {
              isLightNovel ?
                <FormGroup>
                  <FormLabel>
                    {translate('AudioAvailable')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="audioAvailable"
                    helpText={translate('AudioAvailableHelpText')}
                    {...audioAvailable}
                    onChange={onInputChange}
                  />
                </FormGroup> :
                null
            }

            {
              isLightNovel ?
                <FormGroup>
                  <FormLabel>
                    {translate('AdoptedTagWrite')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="adoptedTagWrite"
                    helpText={translate('AdoptedTagWriteHelpText')}
                    {...adoptedTagWrite}
                    onChange={onInputChange}
                  />
                </FormGroup> :
                null
            }

            {
              isLightNovel ?
                <FormGroup>
                  <FormLabel>
                    {translate('Writer')}
                  </FormLabel>

                  <div>
                    <Button onPress={this.onPinWriterPress}>
                      {translate('PinWriter')}
                    </Button>

                    <FormInputHelpText text={translate('PinWriterHelpText')} />
                  </div>
                </FormGroup> :
                null
            }
          </Form>
        </ModalBody>
        <ModalFooter>
          <Button
            className={styles.deleteButton}
            kind={kinds.DANGER}
            onPress={onDeleteAuthorPress}
          >
            {translate('Delete')}
          </Button>

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

        <MoveAuthorModal
          originalPath={originalPath}
          destinationPath={path.value}
          isOpen={this.state.isConfirmMoveModalOpen}
          onSavePress={this.onSavePress}
          onMoveAuthorPress={this.onMoveAuthorPress}
        />

        <ChangeEditionModal
          isOpen={this.state.isChangeEditionModalOpen}
          authorIds={[authorId]}
          initialLanguage={editionLanguage || 'en'}
          isManga={!isLightNovel}
          onModalClose={this.onChangeEditionModalClose}
        />

        {
          isLightNovel ?
            <MetadataPinModal
              isOpen={this.state.isPinWriterModalOpen}
              mode="author"
              authorId={authorId}
              volumeNumber="*"
              bookTitle={authorName}
              onModalClose={this.onPinWriterModalClose}
            /> :
            null
        }

      </ModalContent>
    );
  }
}

EditAuthorModalContent.propTypes = {
  authorId: PropTypes.number.isRequired,
  authorName: PropTypes.string.isRequired,
  library: PropTypes.string,
  editionLanguage: PropTypes.string,
  item: PropTypes.object.isRequired,
  isSaving: PropTypes.bool.isRequired,
  showMetadataProfile: PropTypes.bool.isRequired,
  isPathChanging: PropTypes.bool.isRequired,
  originalPath: PropTypes.string.isRequired,
  onInputChange: PropTypes.func.isRequired,
  onSavePress: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired,
  onDeleteAuthorPress: PropTypes.func.isRequired
};

export default EditAuthorModalContent;
