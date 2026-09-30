import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import FixMatchCandidate from './FixMatchCandidate';
import SwitchLine from './SwitchLine';
import styles from './FixMatchModalContent.css';

// Fix Match: AniList's ranked candidates for a free-text term (seeded with the series
// name). Select stores the chosen id on the series and queues a refresh; Unbind clears the
// id so the ranked title search chooses again on the next refresh. Until one of those two
// buttons is pressed the modal only reads. Below the candidates, a manga series can re-run its
// volume lookups without changing the binding (Re-resolve Metadata, moved off the toolbar, SD-5), and any
// series bound to a catalogue line can switch to another line of its work (Switch Line, 2026-09-28).
class FixMatchModalContent extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      term: props.authorName
    };
  }

  //
  // Listeners

  onTermChange = ({ value }) => {
    this.setState({ term: value });
    this.props.onSearchChange(value);
  };

  //
  // Render

  render() {
    const {
      authorId,
      authorName,
      aniListId,
      isLightNovel,
      isReResolving,
      isFetching,
      isPopulated,
      error,
      isSaving,
      saveError,
      items,
      onSelectPress,
      onUnbindPress,
      onReResolvePress,
      onModalClose
    } = this.props;

    const { term } = this.state;

    const showList = !isFetching && !error;
    const saveMessage = saveError && saveError.responseJSON && saveError.responseJSON.message;

    return (
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>
          {translate('FixMatch')} - {authorName}
        </ModalHeader>

        <ModalBody>
          <div className={styles.hint}>
            {translate('FixMatchHelpText')}
          </div>

          {
            aniListId ?
              <div className={styles.current}>
                {translate('MatchedTo', { id: aniListId })}
              </div> :
              null
          }

          <div className={styles.searchContainer}>
            <div className={styles.searchIconContainer}>
              <Icon
                name={icons.SEARCH}
                size={20}
              />
            </div>

            <TextInput
              className={styles.searchInput}
              name="term"
              value={term}
              placeholder={translate('SearchBoxPlaceHolder')}
              autoFocus={true}
              onChange={this.onTermChange}
            />
          </div>

          {
            isFetching ?
              <LoadingIndicator /> :
              null
          }

          {
            error ?
              <Alert kind={kinds.DANGER}>
                {translate('FixMatchSearchFailed')}
              </Alert> :
              null
          }

          {
            showList && isPopulated && !items.length ?
              <div className={styles.message}>
                {translate('NoMatchCandidates')}
              </div> :
              null
          }

          {
            showList ?
              items.map((item) => {
                return (
                  <FixMatchCandidate
                    key={item.aniListId}
                    {...item}
                    isCurrent={item.aniListId === aniListId}
                    isSaving={isSaving}
                    onSelectPress={onSelectPress}
                  />
                );
              }) :
              null
          }

          {
            saveError ?
              <Alert kind={kinds.DANGER}>
                {saveMessage || translate('FixMatchSaveFailed')}
              </Alert> :
              null
          }

          {
            isLightNovel ?
              null :
              <div className={styles.reResolve}>
                <div className={styles.reResolveHeading}>
                  {translate('RightSeriesWrongDatesOrPageCounts')}
                </div>

                <div className={styles.hint}>
                  {translate('ReResolveMetadataHelpText')}
                </div>

                <SpinnerButton
                  isSpinning={isReResolving}
                  onPress={onReResolvePress}
                >
                  {translate('ReResolveMetadata')}
                </SpinnerButton>
              </div>
          }

          {/* Line safety (2026-09-28): both libraries; renders nothing when the work has no other line here. */}
          <SwitchLine
            authorId={authorId}
            authorName={authorName}
          />
        </ModalBody>

        <ModalFooter>
          {
            aniListId ?
              <SpinnerButton
                className={styles.unbindButton}
                kind={kinds.DANGER}
                isSpinning={isSaving}
                onPress={onUnbindPress}
              >
                {translate('Unbind')}
              </SpinnerButton> :
              null
          }

          <Button onPress={onModalClose}>
            {translate('Cancel')}
          </Button>
        </ModalFooter>
      </ModalContent>
    );
  }
}

FixMatchModalContent.propTypes = {
  authorId: PropTypes.number.isRequired,
  authorName: PropTypes.string.isRequired,
  aniListId: PropTypes.number,
  isLightNovel: PropTypes.bool.isRequired,
  isReResolving: PropTypes.bool.isRequired,
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  error: PropTypes.object,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  onSearchChange: PropTypes.func.isRequired,
  onSelectPress: PropTypes.func.isRequired,
  onUnbindPress: PropTypes.func.isRequired,
  onReResolvePress: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default FixMatchModalContent;
