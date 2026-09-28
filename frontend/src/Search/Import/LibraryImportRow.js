import PropTypes from 'prop-types';
import React, { Component } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import MonitorBooksSelectInput from 'Components/Form/MonitorBooksSelectInput';
import QualityProfileSelectInputConnector from 'Components/Form/QualityProfileSelectInputConnector';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import LibraryImportSelectSeries from './LibraryImportSelectSeries';
import styles from './LibraryImportRow.css';

// One unmapped folder: what it will be added as (Monitor, profiles, and on the Light Novels tab
// the formats) and the series it matched. A row can only be checked once it has a match that
// isn't already in the library.
class LibraryImportRow extends Component {

  //
  // Listeners

  onInputChange = (change) => {
    this.props.onInputChange(this.props.path, change);
  };

  onTermChange = (term) => {
    this.props.onTermChange(this.props.path, term);
  };

  onMatchSelect = (match) => {
    this.props.onMatchSelect(this.props.path, match);
  };

  //
  // Render

  render() {
    const {
      path,
      name,
      term,
      isFetching,
      items,
      match,
      isSelected,
      isExisting,
      existingForeignIds,
      monitor,
      qualityProfileId,
      audioQualityProfileId,
      addEbook,
      addAudio,
      isLightNovel,
      onSelectedChange
    } = this.props;

    return (
      <TableRow>
        <TableSelectCell
          id={path}
          isSelected={isSelected}
          isDisabled={isFetching || !match || isExisting}
          onSelectedChange={onSelectedChange}
        />

        <TableRowCell className={styles.folder}>
          <div className={styles.folderName}>
            {name}
          </div>

          <div className={styles.folderPath}>
            {path}
          </div>
        </TableRowCell>

        <TableRowCell className={styles.input}>
          <MonitorBooksSelectInput
            name="monitor"
            value={monitor}
            onChange={this.onInputChange}
          />
        </TableRowCell>

        <TableRowCell className={styles.input}>
          <QualityProfileSelectInputConnector
            name="qualityProfileId"
            value={qualityProfileId}
            onChange={this.onInputChange}
          />
        </TableRowCell>

        {
          isLightNovel ?
            <TableRowCell className={styles.input}>
              <QualityProfileSelectInputConnector
                name="audioQualityProfileId"
                value={audioQualityProfileId}
                onChange={this.onInputChange}
              />
            </TableRowCell> :
            null
        }

        {
          isLightNovel ?
            <TableRowCell className={styles.formatsCell}>
              <div className={styles.formats}>
                <label className={styles.format}>
                  <CheckInput
                    containerClassName={styles.formatContainer}
                    className={styles.formatInput}
                    name="addEbook"
                    value={addEbook}
                    onChange={this.onInputChange}
                  />
                  <span>{translate('Ebook')}</span>
                </label>

                <label className={styles.format}>
                  <CheckInput
                    containerClassName={styles.formatContainer}
                    className={styles.formatInput}
                    name="addAudio"
                    value={addAudio}
                    onChange={this.onInputChange}
                  />
                  <span>{translate('Audiobook')}</span>
                </label>
              </div>
            </TableRowCell> :
            null
        }

        <TableRowCell className={styles.series}>
          <LibraryImportSelectSeries
            folderName={name}
            term={term}
            isFetching={isFetching}
            items={items}
            match={match}
            isExisting={isExisting}
            existingForeignIds={existingForeignIds}
            onTermChange={this.onTermChange}
            onMatchSelect={this.onMatchSelect}
          />
        </TableRowCell>
      </TableRow>
    );
  }
}

LibraryImportRow.propTypes = {
  path: PropTypes.string.isRequired,
  name: PropTypes.string.isRequired,
  term: PropTypes.string.isRequired,
  isFetching: PropTypes.bool.isRequired,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  match: PropTypes.object,
  isSelected: PropTypes.bool.isRequired,
  isExisting: PropTypes.bool.isRequired,
  existingForeignIds: PropTypes.instanceOf(Set).isRequired,
  monitor: PropTypes.string.isRequired,
  qualityProfileId: PropTypes.number,
  audioQualityProfileId: PropTypes.number,
  addEbook: PropTypes.bool.isRequired,
  addAudio: PropTypes.bool.isRequired,
  isLightNovel: PropTypes.bool.isRequired,
  onSelectedChange: PropTypes.func.isRequired,
  onInputChange: PropTypes.func.isRequired,
  onTermChange: PropTypes.func.isRequired,
  onMatchSelect: PropTypes.func.isRequired
};

export default LibraryImportRow;
