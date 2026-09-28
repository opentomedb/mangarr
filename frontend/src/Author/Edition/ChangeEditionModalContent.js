import PropTypes from 'prop-types';
import React, { Component } from 'react';
import * as commandNames from 'Commands/commandNames';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import ChangeEditionPreviewRow from './ChangeEditionPreviewRow';
import styles from './ChangeEditionModalContent.css';

// Preferred Edition (2026-09-24, spec §4): pick an edition, preview what it changes, apply.
// Blocked series (D9) are listed with the reason and skipped. The list holds concrete languages only,
// never "Auto" (plan A10). Closing after an apply passes true, as Organize does, so the caller can
// clear its selection or close Edit Series (whose form would otherwise still hold the old path).
// Fix round 1 (2026-09-24): a series with no lines to choose from (unbound, or an older catalogue) shows
// one line of help instead of a lone select; a rename whose destination is taken shows why instead of
// its checkbox; a rename-only change (same edition, back to its name) applies only with Rename ticked.
class ChangeEditionModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      languages: [],
      isLanguagesLoaded: false,
      language: props.initialLanguage || '',
      previews: null,
      isPreviewing: false,
      isApplying: false,
      rename: false
    };
  }

  componentDidMount() {
    const { authorIds } = this.props;
    const single = authorIds.length === 1;
    const { request, abortRequest } = createAjaxRequest({ url: single ? `/edition/author/${authorIds[0]}` : '/edition/markets' });

    this._abortLanguagesRequest = abortRequest;

    request.done((data) => {
      const languages = single ?
        // Preferred Edition (2026-09-25, M15 polish): singular "1 Vol.", like EditionFilesCount/EditionFilesCountOne.
        data.options.map((option) => ({
          key: option.language,
          value: option.volumeCount === 1 ?
            translate('EditionOptionLabelOne', { name: option.name }) :
            translate('EditionOptionLabel', { name: option.name, count: option.volumeCount })
        })) :
        data.languages.map((market) => ({ key: market.language, value: market.name }));

      this.setState({ languages, isLanguagesLoaded: true });
    });
  }

  componentWillUnmount() {
    if (this._abortLanguagesRequest) {
      this._abortLanguagesRequest();
    }
  }

  //
  // Control

  // What Apply would send: unblocked series; a rename-only one only while its rename is ticked and allowed.
  getApplicable() {
    const { isManga } = this.props;
    const { previews, rename } = this.state;

    if (!previews) {
      return { applicable: [], canRename: false };
    }

    const single = previews.length === 1 && !previews[0].blockedReason ? previews[0] : null;
    const canRename = isManga && !!single && !!single.newName && !single.renameBlockedReason;
    const applicable = previews.filter((preview) => !preview.blockedReason && (!preview.renameOnly || (canRename && rename)));

    return { applicable, canRename };
  }

  //
  // Listeners

  onLanguageChange = ({ value }) => {
    this.setState({ language: value, previews: null, rename: false });
  };

  onRenameChange = ({ value }) => {
    this.setState({ rename: value });
  };

  onPreviewPress = () => {
    this.setState({ isPreviewing: true });

    const { request } = createAjaxRequest({
      url: '/edition/preview',
      method: 'POST',
      contentType: 'application/json',
      dataType: 'json',
      data: JSON.stringify({ authorIds: this.props.authorIds, language: this.state.language })
    });

    request.done((previews) => {
      this.setState({ previews, isPreviewing: false });
    });

    request.fail(() => {
      this.setState({ isPreviewing: false });
    });
  };

  onApplyPress = () => {
    const {
      language,
      rename
    } = this.state;

    const { applicable, canRename } = this.getApplicable();

    this.setState({ isApplying: true });

    const { request } = createAjaxRequest({
      url: '/command',
      method: 'POST',
      contentType: 'application/json',
      dataType: 'json',
      data: JSON.stringify({
        name: commandNames.RE_RESOLVE_EDITION,
        authorIds: applicable.map((preview) => preview.authorId),
        language,
        rename: rename && canRename
      })
    });

    request.done(() => {
      this.props.onModalClose(true);
    });

    request.fail(() => {
      this.setState({ isApplying: false });
    });
  };

  //
  // Render

  render() {
    const {
      authorIds,
      isManga,
      onModalClose
    } = this.props;

    const {
      languages,
      isLanguagesLoaded,
      language,
      previews,
      isPreviewing,
      isApplying,
      rename
    } = this.state;

    const single = previews && previews.length === 1 && !previews[0].blockedReason ? previews[0] : null;
    const renameTo = isManga && single ? single.newName : null;
    const renameBlockedReason = renameTo ? single.renameBlockedReason : null;
    const { applicable } = this.getApplicable();
    const hasNoOptions = authorIds.length === 1 && isLanguagesLoaded && !languages.length;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('ChangeEdition')}
        </ModalHeader>

        <ModalBody>
          <FormGroup>
            <FormLabel>{translate('Edition')}</FormLabel>

            {
              hasNoOptions ?
                <FormInputHelpText text={translate('EditionNoOptionsHelpText')} /> :
                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="language"
                  value={language}
                  values={[{ key: '', value: translate('Edition'), isDisabled: true }, ...languages]}
                  helpText={translate('EditionChangeHelpText')}
                  onChange={this.onLanguageChange}
                />
            }
          </FormGroup>

          {
            previews ?
              <div className={styles.previews}>
                {
                  previews.map((preview) => (
                    <ChangeEditionPreviewRow
                      key={preview.authorId}
                      {...preview}
                    />
                  ))
                }
              </div> :
              null
          }

          {
            renameTo ?
              <FormGroup>
                <FormLabel>{translate('EditionRenameTo', { name: renameTo })}</FormLabel>

                {
                  renameBlockedReason ?
                    <FormInputHelpText
                      text={renameBlockedReason}
                      isWarning={true}
                    /> :
                    <FormInputGroup
                      type={inputTypes.CHECK}
                      name="rename"
                      value={rename}
                      onChange={this.onRenameChange}
                    />
                }
              </FormGroup> :
              null
          }
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>
            {translate('Cancel')}
          </Button>

          <SpinnerButton
            isSpinning={isPreviewing}
            isDisabled={!language || hasNoOptions}
            onPress={this.onPreviewPress}
          >
            {translate('EditionPreview')}
          </SpinnerButton>

          <SpinnerButton
            kind={kinds.PRIMARY}
            isSpinning={isApplying}
            isDisabled={!applicable.length}
            onPress={this.onApplyPress}
          >
            {translate('ChangeEdition')}
          </SpinnerButton>
        </ModalFooter>
      </ModalContent>
    );
  }
}

ChangeEditionModalContent.propTypes = {
  authorIds: PropTypes.arrayOf(PropTypes.number).isRequired,
  initialLanguage: PropTypes.string,
  isManga: PropTypes.bool.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default ChangeEditionModalContent;
