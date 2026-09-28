import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextInput from 'Components/Form/TextInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './LibraryImportSelectSeries.css';

function SeriesTitle({ series }) {
  const {
    authorName,
    year,
    disambiguation
  } = series;

  return (
    <span>
      {authorName}
      {year && !String(authorName).includes(year) ? <span className={styles.year}> ({year})</span> : null}
      {disambiguation ? <span className={styles.year}> ({disambiguation})</span> : null}
    </span>
  );
}

SeriesTitle.propTypes = {
  series: PropTypes.object.isRequired
};

// The Series cell, as Sonarr's ImportSeriesSelectSeries: the folder's top match, and a picker
// with that search's results plus a box to search a different term. A new term re-runs the
// row's search (queued with the others); its top result becomes the match, like the first one.
class LibraryImportSelectSeries extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isOpen: false
    };
  }

  //
  // Listeners

  onOpenPress = () => {
    this.setState({ isOpen: true });
  };

  onModalClose = () => {
    this.setState({ isOpen: false });
  };

  onTermChange = ({ value }) => {
    this.props.onTermChange(value);
  };

  onSeriesPress = (series) => {
    this.props.onMatchSelect(series);
    this.setState({ isOpen: false });
  };

  //
  // Render

  render() {
    const {
      folderName,
      term,
      isFetching,
      items,
      match,
      isExisting,
      existingForeignIds
    } = this.props;

    return (
      <div>
        {
          isFetching ?
            <LoadingIndicator
              className={styles.loading}
              size={20}
            /> :
            <Link
              className={styles.button}
              onPress={this.onOpenPress}
            >
              {
                match ?
                  <SeriesTitle series={match} /> :
                  <span className={styles.noMatch}>{translate('NoMatchFound')}</span>
              }

              <Icon
                className={styles.caret}
                name={icons.CARET_DOWN}
              />
            </Link>
        }

        {
          !isFetching && isExisting ?
            <div className={styles.existing}>
              {translate('AlreadyInYourLibrary')}
            </div> :
            null
        }

        <Modal
          isOpen={this.state.isOpen}
          onModalClose={this.onModalClose}
        >
          <ModalContent onModalClose={this.onModalClose}>
            <ModalHeader>
              {folderName}
            </ModalHeader>

            <ModalBody>
              <TextInput
                className={styles.searchInput}
                name="term"
                value={term}
                placeholder={translate('SearchBoxPlaceHolder')}
                autoFocus={true}
                onChange={this.onTermChange}
              />

              {
                isFetching ?
                  <LoadingIndicator /> :
                  null
              }

              {
                !isFetching && !items.length ?
                  <div className={styles.message}>
                    {translate('NoMatchFound')}
                  </div> :
                  null
              }

              {
                isFetching ?
                  null :
                  items.map((series) => {
                    const isCurrent = !!match && match.foreignAuthorId === series.foreignAuthorId;

                    return (
                      <Link
                        key={series.foreignAuthorId}
                        className={isCurrent ? styles.currentResult : styles.result}
                        onPress={() => this.onSeriesPress(series)}
                      >
                        <SeriesTitle series={series} />

                        {
                          existingForeignIds.has(series.foreignAuthorId) ?
                            <span className={styles.existing}>
                              {translate('AlreadyInYourLibrary')}
                            </span> :
                            null
                        }
                      </Link>
                    );
                  })
              }
            </ModalBody>

            <ModalFooter>
              <Button onPress={this.onModalClose}>
                {translate('Cancel')}
              </Button>
            </ModalFooter>
          </ModalContent>
        </Modal>
      </div>
    );
  }
}

LibraryImportSelectSeries.propTypes = {
  folderName: PropTypes.string.isRequired,
  term: PropTypes.string.isRequired,
  isFetching: PropTypes.bool.isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  match: PropTypes.object,
  isExisting: PropTypes.bool.isRequired,
  existingForeignIds: PropTypes.instanceOf(Set).isRequired,
  onTermChange: PropTypes.func.isRequired,
  onMatchSelect: PropTypes.func.isRequired
};

export default LibraryImportSelectSeries;
