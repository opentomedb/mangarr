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

// Volumes built from chapter packs (2026-10-21): a CBZ (chapters) file names the chapters its
// edition lists -- the first whole chapter to the last one listed (a trailing n.5 included).
const CBZ_CHAPTERS_QUALITY_ID = 9;

function getChapterRange(quality, chapters) {
  if (!quality || !quality.quality || quality.quality.id !== CBZ_CHAPTERS_QUALITY_ID || !chapters) {
    return null;
  }

  const whole = chapters.filter((c) => Number.isInteger(c));

  if (!whole.length) {
    return null;
  }

  return {
    first: Math.min(...whole),
    last: Math.max(...chapters)
  };
}

function BookFileEditorRow(props) {
  const {
    id,
    path,
    size,
    dateAdded,
    quality,
    qualityCutoffNotMet,
    adopted,
    chapters,
    isSelected,
    isLightNovel,
    onSelectedChange,
    deleteBookFile
  } = props;

  // Task 5 (D4): formats only make sense for a light-novel EBOOK file -- audio editions and
  // manga (CBZ) files don't go through calibre's format conversion. A light novel's .pdf is an
  // ebook (LN PDF, 2026-09-22), and calibre lists its PDF like any format.
  const showFormats = isLightNovel && mediaTypeOfFile(path, 'lightNovel') === EBOOK;
  const chapterRange = getChapterRange(quality, chapters);

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
          chapterRange ?
            <Label
              className={styles.builtFromChapters}
              kind={kinds.INFO}
            >
              {translate('BuiltFromChapters', chapterRange)}
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
  chapters: PropTypes.arrayOf(PropTypes.number),
  dateAdded: PropTypes.string.isRequired,
  isSelected: PropTypes.bool,
  isLightNovel: PropTypes.bool,
  onSelectedChange: PropTypes.func.isRequired,
  deleteBookFile: PropTypes.func.isRequired
};

export default BookFileEditorRow;
