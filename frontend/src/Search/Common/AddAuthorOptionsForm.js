import PropTypes from 'prop-types';
import React, { Component } from 'react';
import AuthorMetadataProfilePopoverContent from 'AddAuthor/AuthorMetadataProfilePopoverContent';
import AuthorMonitoringOptionsPopoverContent from 'AddAuthor/AuthorMonitoringOptionsPopoverContent';
import AuthorMonitorNewItemsOptionsPopoverContent from 'AddAuthor/AuthorMonitorNewItemsOptionsPopoverContent';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Icon from 'Components/Icon';
import Popover from 'Components/Tooltip/Popover';
import { icons, inputTypes, tooltipPositions } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './AddAuthorOptionsForm.css';

class AddAuthorOptionsForm extends Component {

  //
  // Listeners

  onQualityProfileIdChange = ({ value }) => {
    this.props.onInputChange({ name: 'qualityProfileId', value: parseInt(value) });
  };

  onMetadataProfileIdChange = ({ value }) => {
    this.props.onInputChange({ name: 'metadataProfileId', value: parseInt(value) });
  };

  onAudioQualityProfileIdChange = ({ value }) => {
    this.props.onInputChange({ name: 'audioQualityProfileId', value: parseInt(value) });
  };

  //
  // Render

  render() {
    const {
      rootFolderPath,
      monitor,
      monitorNewItems,
      qualityProfileId,
      audioQualityProfileId,
      metadataProfileId,
      includeNoneMetadataProfile,
      includeSpecificBookMonitor,
      showMetadataProfile,
      folder,
      tags,
      isWindows,
      isLightNovel,
      onInputChange,
      ...otherProps
    } = this.props;

    return (
      <Form {...otherProps}>
        <FormGroup>
          <FormLabel>
            {translate('RootFolder')}
          </FormLabel>

          <FormInputGroup
            type={inputTypes.ROOT_FOLDER_SELECT}
            name="rootFolderPath"
            valueOptions={{
              authorFolder: folder,
              isWindows
            }}
            selectedValueOptions={{
              authorFolder: folder,
              isWindows
            }}
            helpText={isLightNovel ? translate('AddNewLightNovelRootFolderHelpText', { folder }) : translate('AddNewAuthorRootFolderHelpText', { folder })}
            onChange={onInputChange}
            {...rootFolderPath}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>
            {translate('Monitor')}

            <Popover
              anchor={
                <Icon
                  className={styles.labelIcon}
                  name={icons.INFO}
                />
              }
              title={translate('MonitoringOptions')}
              body={<AuthorMonitoringOptionsPopoverContent />}
              position={tooltipPositions.RIGHT}
            />
          </FormLabel>

          <FormInputGroup
            type={inputTypes.MONITOR_BOOKS_SELECT}
            name="monitor"
            helpText={translate('MonitoringOptionsHelpText')}
            onChange={onInputChange}
            includeSpecificBook={includeSpecificBookMonitor}
            {...monitor}
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
            onChange={this.onQualityProfileIdChange}
            {...qualityProfileId}
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
                onChange={this.onAudioQualityProfileIdChange}
                {...audioQualityProfileId}
              />
            </FormGroup> :
            null
        }

        <FormGroup className={showMetadataProfile ? undefined : styles.hideMetadataProfile}>
          <FormLabel>
            {translate('MetadataProfile')}

            {
              includeNoneMetadataProfile &&
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
            }
          </FormLabel>

          <FormInputGroup
            type={inputTypes.METADATA_PROFILE_SELECT}
            name="metadataProfileId"
            includeNone={includeNoneMetadataProfile}
            onChange={this.onMetadataProfileIdChange}
            {...metadataProfileId}
          />
        </FormGroup>

        <FormGroup>
          <FormLabel>
            {translate('Tags')}
          </FormLabel>

          <FormInputGroup
            type={inputTypes.TAG}
            name="tags"
            onChange={onInputChange}
            {...tags}
          />
        </FormGroup>
      </Form>
    );
  }
}

AddAuthorOptionsForm.propTypes = {
  rootFolderPath: PropTypes.object,
  monitor: PropTypes.object.isRequired,
  monitorNewItems: PropTypes.object.isRequired,
  qualityProfileId: PropTypes.object,
  audioQualityProfileId: PropTypes.object,
  metadataProfileId: PropTypes.object,
  showMetadataProfile: PropTypes.bool.isRequired,
  includeNoneMetadataProfile: PropTypes.bool.isRequired,
  includeSpecificBookMonitor: PropTypes.bool.isRequired,
  folder: PropTypes.string.isRequired,
  tags: PropTypes.object.isRequired,
  isWindows: PropTypes.bool.isRequired,
  isLightNovel: PropTypes.bool,
  onInputChange: PropTypes.func.isRequired
};

AddAuthorOptionsForm.defaultProps = {
  includeSpecificBookMonitor: false,
  isLightNovel: false
};

export default AddAuthorOptionsForm;
