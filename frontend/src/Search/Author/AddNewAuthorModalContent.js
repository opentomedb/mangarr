import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import AuthorPoster from 'Author/AuthorPoster';
import CheckInput from 'Components/Form/CheckInput';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import AddAuthorOptionsForm from '../Common/AddAuthorOptionsForm.js';
import styles from './AddNewAuthorModalContent.css';

class AddNewAuthorModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      addEbook: true,
      addAudio: true,
      edition: 'auto'
    };
  }

  //
  // Control

  // Preferred Edition (2026-09-24): "French · 20 Vols" ("French · 1 Vol." singular, M15 polish).
  getOptionLabel(option) {
    return option.volumeCount === 1 ?
      translate('EditionOptionLabelOne', { name: option.name }) :
      translate('EditionOptionLabel', { name: option.name, count: option.volumeCount });
  }

  // Preferred Edition (2026-09-24): what Auto (the Settings -> UI chain) resolved this search result to.
  // No edition language under a non-English chain means none of its languages had a line: a fallback.
  getAutoLabel() {
    const {
      editionLanguage,
      isEnglishChain
    } = this.props;

    const editionOptions = this.props.editionOptions || [];
    const language = editionLanguage || (isEnglishChain ? 'en' : null);
    const resolved = language ? editionOptions.find((option) => option.language === language) : null;

    if (resolved) {
      return translate('EditionAuto', { edition: this.getOptionLabel(resolved) });
    }

    return editionLanguage ? translate('EditionAuto', { edition: editionLanguage }) : translate('EditionAutoEnglish');
  }

  //
  // Listeners

  // Remembered per library with the other add defaults (search.authorDefaults /
  // lightNovelDefaults), so a ticked box stays ticked for that library's next add.
  onSearchForMissingBooksChange = ({ name, value }) => {
    this.props.onInputChange({ name, value });
  };

  onFormatChange = ({ name, value }) => {
    this.setState({ [name]: value });
  };

  onEditionChange = ({ value }) => {
    this.setState({ edition: value });
  };

  onAddAuthorPress = () => {
    const {
      addEbook,
      addAudio
    } = this.state;

    // Preferred Edition (2026-09-24): Auto sends no edition, so the backend's chain decides.
    this.props.onAddAuthorPress(this.props.searchForMissingBooks.value, { addEbook, addAudio }, this.state.edition === 'auto' ? null : this.state.edition);
  };

  //
  // Render

  render() {
    const {
      authorName,
      disambiguation,
      overview,
      images,
      isAdding,
      isSmallScreen,
      isLightNovel,
      editionLanguage,
      editionOptions,
      isEnglishChain,
      searchForMissingBooks,
      onModalClose,
      ...otherProps
    } = this.props;

    const {
      addEbook,
      addAudio,
      edition
    } = this.state;

    // Preferred Edition (2026-09-24): an English-only chain and a work with only its English line add
    // exactly as before -- no select. It shows when the work offers another edition, or when a
    // non-English chain has resolved this result (so the form says what Auto picked). The API sends
    // null for a result with no catalogue line, and Collections open this modal without one.
    const options = editionOptions || [];
    const showEdition = options.length > 1 || (!isEnglishChain && options.length > 0);

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('AddNewAuthor')}
        </ModalHeader>

        <ModalBody>
          <div className={styles.container}>
            {
              isSmallScreen ?
                null:
                <div className={styles.poster}>
                  <AuthorPoster
                    className={styles.poster}
                    images={images}
                    size={250}
                  />
                </div>
            }

            <div className={styles.info}>
              <div className={styles.name}>
                {authorName}
              </div>

              {
                !!disambiguation &&
                  <span className={styles.disambiguation}>({disambiguation})</span>
              }

              {
                overview ?
                  <div className={styles.overview}>
                    <TextTruncate
                      truncateText="…"
                      line={8}
                      text={overview}
                    />
                  </div> :
                  null
              }

              {
                showEdition ?
                  <FormGroup>
                    <FormLabel>
                      {translate('Edition')}
                    </FormLabel>

                    <FormInputGroup
                      type={inputTypes.SELECT}
                      name="edition"
                      value={edition}
                      values={[
                        {
                          key: 'auto',
                          value: this.getAutoLabel()
                        },
                        ...options.map((option) => ({
                          key: option.language,
                          value: this.getOptionLabel(option)
                        }))
                      ]}
                      helpText={translate('EditionHelpText')}
                      onChange={this.onEditionChange}
                    />
                  </FormGroup> :
                  null
              }

              {
                isLightNovel ?
                  <FormGroup>
                    <FormLabel>
                      {translate('AddFormats')}
                    </FormLabel>

                    <div className={styles.formats}>
                      <label className={styles.format}>
                        <CheckInput
                          containerClassName={styles.formatContainer}
                          className={styles.formatInput}
                          name="addEbook"
                          value={addEbook}
                          onChange={this.onFormatChange}
                        />
                        <span>{translate('Ebook')}</span>
                      </label>

                      <label className={styles.format}>
                        <CheckInput
                          containerClassName={styles.formatContainer}
                          className={styles.formatInput}
                          name="addAudio"
                          value={addAudio}
                          onChange={this.onFormatChange}
                        />
                        <span>{translate('Audiobook')}</span>
                      </label>
                    </div>
                  </FormGroup> :
                  null
              }

              <AddAuthorOptionsForm
                includeNoneMetadataProfile={false}
                isLightNovel={isLightNovel}
                {...otherProps}
              />

            </div>
          </div>
        </ModalBody>

        <ModalFooter className={styles.modalFooter}>
          <label className={styles.searchForMissingBooksLabelContainer}>
            <span className={styles.searchForMissingBooksLabel}>
              {translate('StartSearchForMissingVolumes')}
            </span>

            <CheckInput
              containerClassName={styles.searchForMissingBooksContainer}
              className={styles.searchForMissingBooksInput}
              name="searchForMissingBooks"
              value={searchForMissingBooks.value}
              onChange={this.onSearchForMissingBooksChange}
            />
          </label>

          <SpinnerButton
            className={styles.addButton}
            kind={kinds.SUCCESS}
            isSpinning={isAdding}
            isDisabled={isLightNovel && !addEbook && !addAudio}
            onPress={this.onAddAuthorPress}
          >
            {translate('AddTitle', { title: authorName })}
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

AddNewAuthorModalContent.propTypes = {
  authorName: PropTypes.string.isRequired,
  disambiguation: PropTypes.string,
  overview: PropTypes.string,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  isAdding: PropTypes.bool.isRequired,
  addError: PropTypes.object,
  isSmallScreen: PropTypes.bool.isRequired,
  isLightNovel: PropTypes.bool.isRequired,
  editionLanguage: PropTypes.string,
  editionOptions: PropTypes.arrayOf(PropTypes.object),
  isEnglishChain: PropTypes.bool.isRequired,
  searchForMissingBooks: PropTypes.object.isRequired,
  onModalClose: PropTypes.func.isRequired,
  onInputChange: PropTypes.func.isRequired,
  onAddAuthorPress: PropTypes.func.isRequired
};

export default AddNewAuthorModalContent;
