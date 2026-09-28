import PropTypes from 'prop-types';
import React from 'react';
import AuthorNameLink from 'Author/AuthorNameLink';
import bookEntities from 'Book/bookEntities';
import BookSearchCellConnector from 'Book/BookSearchCellConnector';
import FormatLabel from 'Book/FormatLabel';
import RelativeDateCellConnector from 'Components/Table/Cells/RelativeDateCellConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import formatPrecisionDate from 'Utilities/Date/formatPrecisionDate';
import WantedVolumeTitle from 'Wanted/WantedVolumeTitle';

function MissingRow(props) {
  const {
    id,
    author,
    releaseDate,
    releaseDatePrecision,
    titleSlug,
    title,
    volumeNumber,
    lastSearchTime,
    disambiguation,
    subtitle,
    mediaTypes,
    missingMediaTypes,
    searchMediaType,
    isSelected,
    columns,
    onSelectedChange
  } = props;

  if (!author) {
    return null;
  }

  return (
    <TableRow>
      <TableSelectCell
        id={id}
        isSelected={isSelected}
        onSelectedChange={onSelectedChange}
      />

      {
        columns.map((column) => {
          const {
            name,
            isVisible
          } = column;

          if (!isVisible) {
            return null;
          }

          if (name === 'authorMetadata.sortName') {
            return (
              <TableRowCell key={name}>
                <AuthorNameLink
                  titleSlug={author.titleSlug}
                  authorName={author.authorName}
                />
              </TableRowCell>
            );
          }

          if (name === 'books.title') {
            return (
              <TableRowCell key={name}>
                <WantedVolumeTitle
                  titleSlug={titleSlug}
                  title={title}
                  seriesName={author.authorName}
                  volumeNumber={volumeNumber}
                  subtitle={subtitle}
                  disambiguation={disambiguation}
                />

                <FormatLabel
                  mediaTypes={mediaTypes}
                  only={missingMediaTypes}
                />
              </TableRowCell>
            );
          }

          if (name === 'releaseDate') {
            // Preferred Edition (2026-09-24, D6, final fix round Minor 1): an edition's year/month date reads "2019" / "Nov 2026".
            if (releaseDate && releaseDatePrecision) {
              return (
                <TableRowCell key={name}>
                  {formatPrecisionDate(releaseDate, releaseDatePrecision)}
                </TableRowCell>
              );
            }

            return (
              <RelativeDateCellConnector
                key={name}
                date={releaseDate.slice(0, 10)}
              />
            );
          }

          if (name === 'books.lastSearchTime') {
            return (
              <RelativeDateCellConnector
                key={name}
                date={lastSearchTime}
              />
            );
          }

          if (name === 'actions') {
            return (
              <BookSearchCellConnector
                key={name}
                bookId={id}
                authorId={author.id}
                bookTitle={title}
                authorName={author.authorName}
                mediaType={searchMediaType}
                bookEntity={bookEntities.WANTED_MISSING}
                showOpenAuthorButton={true}
              />
            );
          }

          return null;
        })
      }
    </TableRow>
  );
}

MissingRow.propTypes = {
  id: PropTypes.number.isRequired,
  author: PropTypes.object.isRequired,
  releaseDate: PropTypes.string.isRequired,
  releaseDatePrecision: PropTypes.string,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  volumeNumber: PropTypes.number,
  lastSearchTime: PropTypes.string,
  disambiguation: PropTypes.string,
  subtitle: PropTypes.string,
  mediaTypes: PropTypes.arrayOf(PropTypes.object),
  missingMediaTypes: PropTypes.arrayOf(PropTypes.string),
  searchMediaType: PropTypes.string,
  isSelected: PropTypes.bool,
  columns: PropTypes.arrayOf(PropTypes.object).isRequired,
  onSelectedChange: PropTypes.func.isRequired
};

export default MissingRow;
