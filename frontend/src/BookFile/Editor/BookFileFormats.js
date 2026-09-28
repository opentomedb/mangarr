import PropTypes from 'prop-types';
import React, { Component } from 'react';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import styles from './BookFileFormats.css';

// Task 5 (D4): the volume page shows every format calibre actually holds for a light-novel
// EPUB file. Fetched lazily per row (not put in the redux store -- a files table can have many
// rows) the same way BookIndexOverview fetches its overview text.
class BookFileFormats extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      formats: []
    };
  }

  componentDidMount() {
    const { id } = this.props;

    const promise = createAjaxRequest({
      url: `/bookFile/${id}/formats`
    }).request;

    promise.done((data) => {
      this.setState({ formats: data.formats || [] });
    });
  }

  //
  // Render

  render() {
    const { formats } = this.state;

    if (!formats.length) {
      return null;
    }

    return (
      <div className={styles.formats}>
        {translate('BookFileFormats', { formats: formats.join(' · ') })}
      </div>
    );
  }
}

BookFileFormats.propTypes = {
  id: PropTypes.number.isRequired
};

export default BookFileFormats;
