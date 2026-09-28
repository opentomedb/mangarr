import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { saveMediaManagementSettings, setMediaManagementSettingsValue, toggleAdvancedSettings } from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import translate from 'Utilities/String/translate';
import { fetchCalibreLibraries, hasSavedOrTypedValue, libraryValues, post, preferredLightNovelFormatOptions, valueOf } from './lightNovelStorageHelpers';
import styles from './EditStorageModalContent.css';

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

function createMapStateToProps() {
  return createSelector(
    (state) => state.settings.advancedSettings,
    createSettingsSectionSelector('mediaManagement'),
    (advancedSettings, sectionSettings) => {
      return {
        advancedSettings,
        ...sectionSettings
      };
    }
  );
}

const mapDispatchToProps = {
  onInputChange: setMediaManagementSettingsValue,
  saveMediaManagementSettings,
  toggleAdvancedSettings
};

class EditEbookStorageModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      calibreLibraries: [],
      calibreDefault: null,
      calibreProblems: null,
      isTestingCalibre: false
    };
  }

  componentDidMount() {
    if (valueOf(this.props.settings, 'lightNovelEbookHome') === 'calibre' && valueOf(this.props.settings, 'calibreContentServerUrl')) {
      this.fetchLibraries();
    }
  }

  componentDidUpdate(prevProps) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalSaved();
    }
  }

  //
  // Control

  fetchLibraries = () => {
    fetchCalibreLibraries(this.props.settings)
      .done((data) => {
        this.setState({ calibreLibraries: data.libraries || [], calibreDefault: data.defaultLibrary || null });
      })
      .fail(() => {
        this.setState((state) => (state.calibreProblems === null ? { calibreProblems: [translate('LightNovelStorageTestFailed')] } : null));
      });
  };

  //
  // Listeners

  onTestPress = () => {
    this.setState({ isTestingCalibre: true });

    post('/config/lightnovelstorage/calibre/test', {
      url: valueOf(this.props.settings, 'calibreContentServerUrl'),
      library: valueOf(this.props.settings, 'calibreLibrary'),
      username: valueOf(this.props.settings, 'calibreUsername'),
      password: valueOf(this.props.settings, 'calibrePassword'),
      remotePath: valueOf(this.props.settings, 'calibreRemotePath'),
      localPath: valueOf(this.props.settings, 'calibreLocalPath')
    })
      .done((data) => this.setState({ isTestingCalibre: false, calibreProblems: data.problems || [] }))
      .fail(() => this.setState({ isTestingCalibre: false, calibreProblems: [translate('LightNovelStorageTestFailed')] }));

    this.fetchLibraries();
  };

  onSavePress = () => {
    this.props.saveMediaManagementSettings();
  };

  //
  // Render

  renderResult() {
    const { calibreProblems } = this.state;

    if (calibreProblems === null) {
      return null;
    }

    if (!calibreProblems.length) {
      return (
        <Alert kind={kinds.SUCCESS}>
          {translate('LightNovelStorageTestPassed')}
        </Alert>
      );
    }

    return (
      <Alert kind={kinds.DANGER}>
        <ul>
          {calibreProblems.map((problem) => <li key={problem}>{problem}</li>)}
        </ul>
      </Alert>
    );
  }

  render() {
    const {
      advancedSettings,
      isSaving,
      saveError,
      settings,
      validationErrors,
      validationWarnings,
      onInputChange,
      onModalClose,
      toggleAdvancedSettings: onAdvancedSettingsPress
    } = this.props;

    const {
      calibreLibraries,
      calibreDefault,
      calibreProblems,
      isTestingCalibre
    } = this.state;

    const ebookHome = valueOf(settings, 'lightNovelEbookHome');
    const conversionAvailable = ebookHome === 'calibre';

    const ebookHomeValues = [
      { key: 'entry', value: translate('LightNovelHomeEntryFolder') },
      { key: 'calibre', value: translate('LightNovelHomeCalibre') }
    ];

    const calibreLibraryValues = libraryValues(calibreLibraries, valueOf(settings, 'calibreLibrary'), {
      key: '',
      value: calibreDefault ? translate('CalibreServerDefaultLibraryNamed', { name: calibreDefault }) : translate('CalibreServerDefaultLibrary')
    });

    // The path pair is advanced (the *arrs' isAdvanced), but a configured one always shows -- saved
    // or typed -- so a working setup never looks broken with Show Advanced off.
    const showPathPair = advancedSettings || hasSavedOrTypedValue(settings, 'calibreRemotePath') || hasSavedOrTypedValue(settings, 'calibreLocalPath');

    // A validation failure on a field this modal doesn't render (e.g. the recycle bin path) would
    // otherwise fail the Save with no visible reason -- the per-field errors below already cover
    // every field this modal owns.
    const foreignErrors = Object.keys(settings)
      .filter((key) => key !== 'fields' && !MODAL_KEYS.includes(key))
      .reduce((messages, key) => messages.concat((settings[key].errors || []).map((e) => e.message)), []);

    const testError = calibreProblems && calibreProblems.length ?
      { status: 400, responseJSON: calibreProblems.map((message) => ({ errorMessage: message, isWarning: false })) } :
      null;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('EditEbookStorage')}
        </ModalHeader>

        <ModalBody>
          <Form
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            {
              foreignErrors.map((message) => (
                <Alert key={message} kind={kinds.DANGER}>
                  {message}
                </Alert>
              ))
            }

            <FormGroup>
              <FormLabel>{translate('LightNovelEbookHome')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="lightNovelEbookHome"
                helpText={translate('LightNovelEbookHomeHelpText')}
                values={ebookHomeValues}
                onChange={onInputChange}
                {...settings.lightNovelEbookHome}
              />
            </FormGroup>

            {
              ebookHome === 'calibre' &&
                <div>
                  <FormGroup>
                    <FormLabel>{translate('CalibreContentServerUrl')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="calibreContentServerUrl"
                      helpText={translate('CalibreContentServerUrlHelpText')}
                      onChange={onInputChange}
                      {...settings.calibreContentServerUrl}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('CalibreUsername')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="calibreUsername"
                      onChange={onInputChange}
                      {...settings.calibreUsername}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('CalibrePassword')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.PASSWORD}
                      name="calibrePassword"
                      onChange={onInputChange}
                      {...settings.calibrePassword}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('CalibreLibrary')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="calibreLibrary"
                      helpText={translate('LightNovelLibraryHelpText')}
                      values={calibreLibraryValues}
                      onChange={onInputChange}
                      {...settings.calibreLibrary}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('CalibreOutputFormat')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="preferredLightNovelFormat"
                      helpText={translate('PreferredLightNovelFormatHelpText')}
                      values={preferredLightNovelFormatOptions}
                      onChange={onInputChange}
                      {...settings.preferredLightNovelFormat}
                    />
                  </FormGroup>

                  <FormGroup
                    advancedSettings={showPathPair}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('PathAsCalibreSeesIt')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="calibreRemotePath"
                      helpText={translate('CalibrePathMappingHelpText')}
                      onChange={onInputChange}
                      {...settings.calibreRemotePath}
                    />
                  </FormGroup>

                  <FormGroup
                    advancedSettings={showPathPair}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('PathAsMangarrSeesIt')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="calibreLocalPath"
                      onChange={onInputChange}
                      {...settings.calibreLocalPath}
                    />
                  </FormGroup>

                  {this.renderResult()}
                </div>
            }
          </Form>
        </ModalBody>

        <ModalFooter>
          <AdvancedSettingsButton
            advancedSettings={advancedSettings}
            onAdvancedSettingsPress={onAdvancedSettingsPress}
            showLabel={false}
          />

          {
            conversionAvailable &&
              <SpinnerErrorButton
                className={styles.testButton}
                isSpinning={isTestingCalibre}
                error={testError}
                onPress={this.onTestPress}
              >
                {translate('Test')}
              </SpinnerErrorButton>
          }

          <Button onPress={onModalClose}>
            {translate('Cancel')}
          </Button>

          <SpinnerErrorButton
            isSpinning={isSaving}
            error={saveError}
            onPress={this.onSavePress}
          >
            {translate('Save')}
          </SpinnerErrorButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

EditEbookStorageModalContent.propTypes = {
  advancedSettings: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  settings: PropTypes.object.isRequired,
  validationErrors: PropTypes.arrayOf(PropTypes.object),
  validationWarnings: PropTypes.arrayOf(PropTypes.object),
  onInputChange: PropTypes.func.isRequired,
  saveMediaManagementSettings: PropTypes.func.isRequired,
  toggleAdvancedSettings: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired,
  onModalSaved: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(EditEbookStorageModalContent);
