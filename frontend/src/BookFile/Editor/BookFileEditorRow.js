import PropTypes from 'prop-types';
import React from 'react';
import BookQuality from 'Book/BookQuality';
import Label from 'Components/Label';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import { EBOOK, mediaTypeOfFile } from 'Utilities/Book/mediaTypes';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import BookFileActionsCell from './BookFileActionsCell';
import BookFileFormats from './BookFileFormats';
import styles from './BookFileEditorRow.css';

function BookFileEditorRow(props) {
  const {
    id,
    path,
    size,
    dateAdded,
    quality,
    qualityCutoffNotMet,
    adopted,
    isSelected,
    isLightNovel,
    onSelectedChange,
    deleteBookFile
  } = props;

  // Task 5 (D4): formats only make sense for a light-novel EBOOK file -- audio editions and
  // manga (CBZ) files don't go through calibre's format conversion. A light novel's .pdf is an
  // ebook (LN PDF, 2026-09-22), and calibre lists its PDF like any format.
  const showFormats = isLightNovel && mediaTypeOfFile(path, 'lightNovel') === EBOOK;

  return (
    <TableRow>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={onSelectedChange}
      />
      <TableRowCell
        className={styles.path}
      >
        {path}

        {
          adopted ?
            <Label
              className={styles.adopted}
              kind={kinds.INFO}
              title={translate('AdoptedTooltip')}
            >
              {translate('Adopted')}
            </Label> :
            null
        }

        {
          showFormats ?
            <BookFileFormats id={id} /> :
            null
        }
      </TableRowCell>

      <TableRowCell
        className={styles.size}
      >
        {formatBytes(size)}
      </TableRowCell>

      <RelativeDateCellConnector
        className={styles.dateAdded}
        date={dateAdded}
      />

      <TableRowCell
        className={styles.quality}
      >
        <BookQuality
          quality={quality}
          isCutoffNotMet={qualityCutoffNotMet}
        />
      </TableRowCell>

      <BookFileActionsCell
        id={id}
        path={path}
        deleteBookFile={deleteBookFile}
      />
    </TableRow>
  );
}

BookFileEditorRow.propTypes = {
  id: PropTypes.number.isRequired,
  path: PropTypes.string.isRequired,
  size: PropTypes.number.isRequired,
  quality: PropTypes.object.isRequired,
  qualityCutoffNotMet: PropTypes.bool.isRequired,
  adopted: PropTypes.bool,
  dateAdded: PropTypes.string.isRequired,
  isSelected: PropTypes.bool,
  isLightNovel: PropTypes.bool,
  onSelectedChange: PropTypes.func.isRequired,
  deleteBookFile: PropTypes.func.isRequired
};

export default BookFileEditorRow;
