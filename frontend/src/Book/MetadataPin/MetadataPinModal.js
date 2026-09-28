import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import styles from './MetadataPinModal.css';

// Pin a volume's metadata: a value that wins over every provider and every plausibility
// guard, stored in <appdata>/metadata/overrides.json. Saving queues a re-resolve for the
// series, so the pinned value shows without the user needing to know that a second,
// separate action exists.
//
// The source field is deliberately prominent. A pin with no source is a guess that
// outranks every metadata provider; a pin that records where its value came from can be
// promoted into an OpenTome correction verbatim.
//
// mode="author" (one copy each, 2026-09-20) edits the series row instead: volumeNumber "*",
// which carries the light novel's writer and nothing else (the API refuses volume values
// there), so only Writer and Source are shown and sent. Opened from Edit Series (Pin Writer)
// and Edit Volume (Pin Metadata) since the 2026-09-24 UI pass.
const FIELDS = ['releaseDate', 'pageCount', 'isbn13', 'coverUrl', 'author', 'source'];

class MetadataPinModal extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = this.emptyState();
  }

  componentDidUpdate(prevProps) {
    if (!prevProps.isOpen && this.props.isOpen) {
      this.load();
    }
  }

  emptyState() {
    return {
      isFetching: false,
      isSaving: false,
      error: null,
      exists: false,
      checked: null,
      // Read from the fetched pin and never edited here; the volume save sends it back so a UI
      // save keeps it (the Audible title pin is set elsewhere).
      audiobookTitle: null,
      values: { releaseDate: '', pageCount: '', isbn13: '', coverUrl: '', author: '', source: '' }
    };
  }

  //
  // Control

  load() {
    const { authorId, volumeNumber } = this.props;

    this.setState({ ...this.emptyState(), isFetching: true });

    createAjaxRequest({
      url: `/metadatapin?authorId=${authorId}`
    }).request.then((pins) => {
      const want = String(volumeNumber);
      const pin = (pins || []).find((p) => String(p.volumeNumber) === want);

      this.setState({
        isFetching: false,
        exists: !!pin,
        checked: pin ? pin.checked : null,
        audiobookTitle: (pin && pin.audiobookTitle) || null,
        values: {
          releaseDate: (pin && pin.releaseDate) || '',
          pageCount: pin && pin.pageCount ? String(pin.pageCount) : '',
          isbn13: (pin && pin.isbn13) || '',
          coverUrl: (pin && pin.coverUrl) || '',
          author: (pin && pin.author) || '',
          source: (pin && pin.source) || ''
        }
      });
    }, (xhr) => {
      this.setState({ isFetching: false, error: xhr });
    });
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.setState({ values: { ...this.state.values, [name]: value } });
  };

  onSavePress = () => {
    const { mode, authorId, volumeNumber, onModalClose } = this.props;
    const { audiobookTitle, values } = this.state;

    this.setState({ isSaving: true });

    // The series row takes the author only (the API refuses volume values there). A volume save
    // also carries the fields this form does not edit — audiobookTitle and author — as fetched,
    // so saving never drops a pin that was set elsewhere.
    const body = mode === 'author' ?
      {
        authorId,
        volumeNumber: '*',
        author: values.author || null,
        source: values.source || null
      } :
      {
        authorId,
        volumeNumber: String(volumeNumber),
        releaseDate: values.releaseDate || null,
        pageCount: values.pageCount ? Number(values.pageCount) : null,
        isbn13: values.isbn13 || null,
        coverUrl: values.coverUrl || null,
        audiobookTitle,
        author: values.author || null,
        source: values.source || null
      };

    createAjaxRequest({
      method: 'PUT',
      url: '/metadatapin',
      contentType: 'application/json',
      data: JSON.stringify(body)
    }).request.then(() => {
      this.setState({ isSaving: false });
      onModalClose();
    }, (xhr) => {
      this.setState({ isSaving: false, error: xhr });
    });
  };

  onRemovePress = () => {
    const { authorId, volumeNumber, onModalClose } = this.props;

    this.setState({ isSaving: true });

    createAjaxRequest({
      method: 'DELETE',
      url: `/metadatapin?authorId=${authorId}&volumeNumber=${encodeURIComponent(String(volumeNumber))}`
    }).request.then(() => {
      this.setState({ isSaving: false });
      onModalClose();
    }, (xhr) => {
      this.setState({ isSaving: false, error: xhr });
    });
  };

  //
  // Render

  render() {
    const { isOpen, mode, bookTitle, onModalClose } = this.props;
    const { isFetching, isSaving, error, exists, checked, values } = this.state;

    const isAuthorMode = mode === 'author';
    const isEmpty = isAuthorMode ?
      !values.author :
      FIELDS.every((f) => f === 'source' || f === 'author' || !values[f]);

    return (
      <Modal
        isOpen={isOpen}
        size={sizes.MEDIUM}
        onModalClose={onModalClose}
      >
        <ModalContent onModalClose={onModalClose}>
          <ModalHeader>
            {isAuthorMode ? translate('PinWriter') : translate('PinMetadata')} - {bookTitle}
          </ModalHeader>

          <ModalBody>
            {
              isFetching ?
                <LoadingIndicator /> :
                null
            }

            {
              error ?
                <Alert kind={kinds.DANGER}>
                  {translate('MetadataPinError')}
                </Alert> :
                null
            }

            {
              !isFetching && isAuthorMode ?
                <Form>
                  <div className={styles.hint}>
                    {translate('PinWriterHint')}
                    {
                      exists && checked ?
                        <div className={styles.checked}>{translate('PinnedOn', { date: checked })}</div> :
                        null
                    }
                  </div>

                  <FormGroup>
                    <FormLabel>{translate('Writer')}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="author"
                      value={values.author}
                      placeholder="Reki Kawahara"
                      onChange={this.onInputChange}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('Source')}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="source"
                      value={values.source}
                      placeholder="https://publisher.example/series"
                      helpText={translate('PinSourceHelpText')}
                      onChange={this.onInputChange}
                    />
                  </FormGroup>
                </Form> :
                null
            }

            {
              !isFetching && !isAuthorMode ?
                <Form>
                  <div className={styles.hint}>
                    {translate('PinMetadataHint')}
                    {
                      exists && checked ?
                        <div className={styles.checked}>{translate('PinnedOn', { date: checked })}</div> :
                        null
                    }
                  </div>

                  <FormGroup>
                    <FormLabel>{translate('ReleaseDate')}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="releaseDate"
                      value={values.releaseDate}
                      placeholder="2026-01-25"
                      helpText={translate('PinReleaseDateHelpText')}
                      onChange={this.onInputChange}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('PageCount')}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.NUMBER}
                      name="pageCount"
                      value={values.pageCount}
                      onChange={this.onInputChange}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('Isbn13')}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="isbn13"
                      value={values.isbn13}
                      placeholder="9781612624204"
                      onChange={this.onInputChange}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('CoverUrl')}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="coverUrl"
                      value={values.coverUrl}
                      placeholder="https://covers.example/volume-24.jpg"
                      onChange={this.onInputChange}
                    />
                  </FormGroup>

                  <FormGroup>
                    <FormLabel>{translate('Source')}</FormLabel>
                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="source"
                      value={values.source}
                      placeholder="https://publisher.example/volume/1"
                      helpText={translate('PinSourceHelpText')}
                      onChange={this.onInputChange}
                    />
                  </FormGroup>
                </Form> :
                null
            }
          </ModalBody>

          <ModalFooter>
            {
              exists ?
                <Button
                  className={styles.deleteButton}
                  kind={kinds.DANGER}
                  onPress={this.onRemovePress}
                >
                  {translate('RemovePin')}
                </Button> :
                null
            }

            <Button onPress={onModalClose}>
              {translate('Cancel')}
            </Button>

            <SpinnerButton
              kind={kinds.PRIMARY}
              isSpinning={isSaving}
              isDisabled={isFetching || isEmpty}
              onPress={this.onSavePress}
            >
              {translate('Save')}
            </SpinnerButton>
          </ModalFooter>
        </ModalContent>
      </Modal>
    );
  }
}

MetadataPinModal.propTypes = {
  isOpen: PropTypes.bool.isRequired,
  mode: PropTypes.oneOf(['volume', 'author']),
  authorId: PropTypes.number.isRequired,
  volumeNumber: PropTypes.oneOfType([PropTypes.number, PropTypes.string]).isRequired,
  bookTitle: PropTypes.string.isRequired,
  onModalClose: PropTypes.func.isRequired
};

MetadataPinModal.defaultProps = {
  mode: 'volume'
};

export default MetadataPinModal;
