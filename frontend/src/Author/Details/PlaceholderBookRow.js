import PropTypes from 'prop-types';
import React from 'react';
import Label from 'Components/Label';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './BookRow.css';

// A display-only row for a volume that exists in the original language but has not been released
// in the user's language yet (volume number within the series' total but absent from the owned/
// acquirable book list). It is NOT a real Book: no link, no monitor toggle, no actions, not in
// stats, never searched — it just surfaces the gap as "Coming Soon", greyed to match the
// future-dated real rows (BookRow's .unreleased treatment).
//
// Mirrors BookRow's per-column cells (same classNames/widths) so the row lines up with the real
// rows. The English release date is unknown for these volumes, so the date column shows "TBA".
function PlaceholderBookRow(props) {
  const {
    volumeNumber,
    columns
  } = props;

  return (
    <TableRow className={styles.unreleased}>
      {
        columns.map((column) => {
          const {
            name,
            isVisible
          } = column;

          if (!isVisible) {
            return null;
          }

          // Placeholders aren't selectable; BookRow renders no cell here outside the editor.
          if (name === 'select') {
            return null;
          }

          if (name === 'title') {
            return (
              <TableRowCell
                key={name}
                className={styles.title}
              >
                {translate('VolumeRowTitle', { volume: volumeNumber })}
              </TableRowCell>
            );
          }

          if (name === 'releaseDate') {
            return (
              <TableRowCell
                key={name}
                className={styles.releaseDate}
              >
                {translate('TBA')}
              </TableRowCell>
            );
          }

          if (name === 'status') {
            return (
              <TableRowCell
                key={name}
                className={styles.status}
              >
                <Label
                  title={translate('ComingSoon')}
                  kind={kinds.INFO}
                >
                  {translate('ComingSoon')}
                </Label>
              </TableRowCell>
            );
          }

          // Every other visible column renders an empty cell carrying BookRow's width class so
          // the fixed columns line up and the flexible title column fills the same space.
          return (
            <TableRowCell
              key={name}
              className={styles[name]}
            />
          );
        })
      }
    </TableRow>
  );
}

PlaceholderBookRow.propTypes = {
  volumeNumber: PropTypes.number.isRequired,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired
};

export default PlaceholderBookRow;
