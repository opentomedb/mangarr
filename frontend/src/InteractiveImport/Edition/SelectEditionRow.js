import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import FormInputGroup from 'Components/Form/FormInputGroup';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { inputTypes } from 'Helpers/Props';
import titleCase from 'Utilities/String/titleCase';
import translate from 'Utilities/String/translate';

class SelectEditionRow extends Component {

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    // name is the import item id (one row per file), not the book id
    this.props.onEditionSelect(parseInt(name), value);
  };

  //
  // Render

  render() {
    const {
      importId,
      fileName,
      matchedEditionId,
      title,
      disambiguation,
      editions,
      columns
    } = this.props;

    const extendedTitle = disambiguation ? `${title} (${disambiguation})` : title;

    const values = _.map(editions, (bookEdition) => {

      let value = `${bookEdition.title}`;

      if (bookEdition.disambiguation) {
        value = `${value} (${titleCase(bookEdition.disambiguation)})`;
      }

      const extras = [];
      if (bookEdition.language) {
        extras.push(bookEdition.language);
      }
      if (bookEdition.publisher) {
        extras.push(bookEdition.publisher);
      }
      if (bookEdition.isbn13) {
        extras.push(bookEdition.isbn13);
      }
      if (bookEdition.asin) {
        extras.push(bookEdition.asin);
      }
      // Light novels (2026-09): say which class an edition is (Ebook / Audiobook); format is
      // the metadata string behind it ("ebook" / "Audiobook" / "Paperback").
      if (bookEdition.mediaType === 'ebook') {
        extras.push(translate('Ebook'));
      } else if (bookEdition.mediaType === 'audio') {
        extras.push(translate('Audiobook'));
      } else if (bookEdition.format) {
        extras.push(bookEdition.format);
      }
      if (bookEdition.pageCount > 0) {
        extras.push(translate('PageCountShort', { pageCount: bookEdition.pageCount }));
      }

      if (extras) {
        value = `${value} [${extras.join(', ')}]`;
      }

      return {
        key: bookEdition.foreignEditionId,
        value
      };
    });

    const sortedValues = _.orderBy(values, ['value']);

    return (
      <TableRow>
        {
          columns.map((column) => {
            const {
              name,
              isVisible
            } = column;

            if (!isVisible) {
              return null;
            }

            if (name === 'file') {
              return (
                <TableRowCell key={name}>
                  {fileName}
                </TableRowCell>
              );
            }

            if (name === 'book') {
              return (
                <TableRowCell key={name}>
                  {extendedTitle}
                </TableRowCell>
              );
            }

            if (name === 'edition') {
              return (
                <TableRowCell key={name}>
                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name={importId.toString()}
                    values={sortedValues}
                    value={matchedEditionId}
                    onChange={this.onInputChange}
                  />
                </TableRowCell>
              );
            }

            return null;
          })
        }
      </TableRow>

    );
  }
}

SelectEditionRow.propTypes = {
  importId: PropTypes.number.isRequired,
  fileName: PropTypes.string.isRequired,
  matchedEditionId: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  disambiguation: PropTypes.string,
  editions: PropTypes.arrayOf(PropTypes.object).isRequired,
  onEditionSelect: PropTypes.func.isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default SelectEditionRow;
