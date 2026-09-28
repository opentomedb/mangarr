import PropTypes from 'prop-types';
import React, { Component } from 'react';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import { AUDIO, EBOOK } from 'Utilities/Book/mediaTypes';
import translate from 'Utilities/String/translate';
import BookInteractiveSearchModalConnector from './Search/BookInteractiveSearchModalConnector';
import styles from './BookSearchCell.css';

// What a press of the magnifier will search: a light-novel row searches the edition the series
// page is showing, a manga row (no media type) the volume.
const SEARCH_TOOLTIPS = {
  [EBOOK]: 'SearchEbookTooltip',
  [AUDIO]: 'SearchAudiobookTooltip'
};

class BookSearchCell extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false
    };
  }

  //
  // Listeners

  onManualSearchPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      bookId,
      bookTitle,
      authorName,
      isSearching,
      mediaType,
      onSearchPress,
      ...otherProps
    } = this.props;

    return (
      <TableRowCell className={styles.BookSearchCell}>
        <SpinnerIconButton
          name={icons.SEARCH}
          title={translate(SEARCH_TOOLTIPS[mediaType] || 'SearchVolumeTooltip')}
          isSpinning={isSearching}
          onPress={onSearchPress}
        />

        <IconButton
          name={icons.INTERACTIVE}
          onPress={this.onManualSearchPress}
        />

        <BookInteractiveSearchModalConnector
          isOpen={this.state.isDetailsModalOpen}
          bookId={bookId}
          bookTitle={bookTitle}
          authorName={authorName}
          onModalClose={this.onDetailsModalClose}
          {...otherProps}
        />

      </TableRowCell>
    );
  }
}

BookSearchCell.propTypes = {
  bookId: PropTypes.number.isRequired,
  authorId: PropTypes.number.isRequired,
  bookTitle: PropTypes.string.isRequired,
  authorName: PropTypes.string.isRequired,
  isSearching: PropTypes.bool.isRequired,
  mediaType: PropTypes.string,
  onSearchPress: PropTypes.func.isRequired
};

export default BookSearchCell;
