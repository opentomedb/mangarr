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
import { fetchAudiobookshelfLibraries, hasSavedOrTypedValue, libraryValues, post, valueOf } from './lightNovelStorageHelpers';
import styles from './EditStorageModalContent.css';

const MODAL_KEYS = [
  'lightNovelAudioHome',
  'audiobookshelfUrl',
  'audiobookshelfApiKey',
  'audiobookshelfLibraryId',
  'audiobookshelfRemotePath',
  'audiobookshelfLocalPath'
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

class EditAudiobookStorageModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      audiobookshelfLibraries: [],
      audiobookshelfProblems: null,
      isTestingAudiobookshelf: false
    };
  }

  componentDidMount() {
    if (valueOf(this.props.settings, 'lightNovelAudioHome') === 'audiobookshelf' && valueOf(this.props.settings, 'audiobookshelfUrl')) {
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
    fetchAudiobookshelfLibraries(this.props.settings)
      .done((data) => {
        this.setState({ audiobookshelfLibraries: data.libraries || [] });
      })
      .fail(() => {
        this.setState((state) => (state.audiobookshelfProblems === null ? { audiobookshelfProblems: [translate('LightNovelStorageTestFailed')] } : null));
      });
  };

  //
  // Listeners

  onTestPress = () => {
    this.setState({ isTestingAudiobookshelf: true });

    post('/config/lightnovelstorage/audiobookshelf/test', {
      url: valueOf(this.props.settings, 'audiobookshelfUrl'),
      apiKey: valueOf(this.props.settings, 'audiobookshelfApiKey'),
      libraryId: valueOf(this.props.settings, 'audiobookshelfLibraryId'),
      remotePath: valueOf(this.props.settings, 'audiobookshelfRemotePath'),
      localPath: valueOf(this.props.settings, 'audiobookshelfLocalPath')
    })
      .done((data) => this.setState({ isTestingAudiobookshelf: false, audiobookshelfProblems: data.problems || [] }))
      .fail(() => this.setState({ isTestingAudiobookshelf: false, audiobookshelfProblems: [translate('LightNovelStorageTestFailed')] }));

    this.fetchLibraries();
  };

  onSavePress = () => {
    this.props.saveMediaManagementSettings();
  };

  //
  // Render

  renderResult() {
    const { audiobookshelfProblems } = this.state;

    if (audiobookshelfProblems === null) {
      return null;
    }

    if (!audiobookshelfProblems.length) {
      return (
        <Alert kind={kinds.SUCCESS}>
          {translate('LightNovelStorageTestPassed')}
        </Alert>
      );
    }

    return (
      <Alert kind={kinds.DANGER}>
        <ul>
          {audiobookshelfProblems.map((problem) => <li key={problem}>{problem}</li>)}
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
      audiobookshelfLibraries,
      audiobookshelfProblems,
      isTestingAudiobookshelf
    } = this.state;

    const audioHome = valueOf(settings, 'lightNovelAudioHome');
    const testAvailable = audioHome === 'audiobookshelf';

    const audioHomeValues = [
      { key: 'entry', value: translate('LightNovelHomeEntryFolder') },
      { key: 'audiobookshelf', value: translate('LightNovelHomeAudiobookshelf') }
    ];

    const audiobookshelfLibraryValues = libraryValues(audiobookshelfLibraries, valueOf(settings, 'audiobookshelfLibraryId'), {
      key: '',
      value: translate('ChooseAudiobookshelfLibrary')
    });

    // The path pair is advanced (the *arrs' isAdvanced), but a configured one always shows -- saved
    // or typed -- so a working setup never looks broken with Show Advanced off.
    const showPathPair = advancedSettings || hasSavedOrTypedValue(settings, 'audiobookshelfRemotePath') || hasSavedOrTypedValue(settings, 'audiobookshelfLocalPath');

    // A validation failure on a field this modal doesn't render (e.g. the recycle bin path) would
    // otherwise fail the Save with no visible reason -- the per-field errors below already cover
    // every field this modal owns.
    const foreignErrors = Object.keys(settings)
      .filter((key) => key !== 'fields' && !MODAL_KEYS.includes(key))
      .reduce((messages, key) => messages.concat((settings[key].errors || []).map((e) => e.message)), []);

    const testError = audiobookshelfProblems && audiobookshelfProblems.length ?
      { status: 400, responseJSON: audiobookshelfProblems.map((message) => ({ errorMessage: message, isWarning: false })) } :
      null;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('EditAudiobookStorage')}
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
              <FormLabel>{translate('LightNovelAudioHome')}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="lightNovelAudioHome"
                helpText={translate('LightNovelAudioHomeHelpText')}
                values={audioHomeValues}
                onChange={onInputChange}
                {...settings.lightNovelAudioHome}
              />
            </FormGroup>

            {
              audioHome === 'audiobookshelf' &&
                <div>
                  <FormGroup>
                    <FormLabel>{translate('AudiobookshelfUrl')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="audiobookshelfUrl"
                      helpText={translate('AudiobookshelfUrlHelpText')}
                      onChange={onInputChange}
                      {...settings.audiobookshelfUrl}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('AudiobookshelfApiKey')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.PASSWORD}
                      name="audiobookshelfApiKey"
                      onChange={onInputChange}
                      {...settings.audiobookshelfApiKey}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('AudiobookshelfLibrary')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="audiobookshelfLibraryId"
                      helpText={translate('LightNovelLibraryHelpText')}
                      values={audiobookshelfLibraryValues}
                      onChange={onInputChange}
                      {...settings.audiobookshelfLibraryId}
                    />
                  </FormGroup>

                  <FormGroup
                    advancedSettings={showPathPair}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('PathAsAudiobookshelfSeesIt')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="audiobookshelfRemotePath"
                      helpText={translate('AudiobookshelfPathMappingHelpText')}
                      onChange={onInputChange}
                      {...settings.audiobookshelfRemotePath}
                    />
                  </FormGroup>

                  <FormGroup
                    advancedSettings={showPathPair}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('PathAsMangarrSeesIt')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="audiobookshelfLocalPath"
                      onChange={onInputChange}
                      {...settings.audiobookshelfLocalPath}
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
            testAvailable &&
              <SpinnerErrorButton
                className={styles.testButton}
                isSpinning={isTestingAudiobookshelf}
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

EditAudiobookStorageModalContent.propTypes = {
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

export default connect(createMapStateToProps, mapDispatchToProps)(EditAudiobookStorageModalContent);
