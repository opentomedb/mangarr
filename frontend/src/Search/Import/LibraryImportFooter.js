import PropTypes from 'prop-types';
import React from 'react';
import CheckInput from 'Components/Form/CheckInput';
import MonitorBooksSelectInput from 'Components/Form/MonitorBooksSelectInput';
import MonitorNewItemsSelectInput from 'Components/Form/MonitorNewItemsSelectInput';
import QualityProfileSelectInputConnector from 'Components/Form/QualityProfileSelectInputConnector';
import Link from 'Components/Link/Link';
import SpinnerButton from 'Components/Link/SpinnerButton';
import PageContentFooter from 'Components/Page/PageContentFooter';
import { kinds } from 'Helpers/Props';
import { libraryRoute, LIGHT_NOVEL } from 'Utilities/Author/libraries';
import translate from 'Utilities/String/translate';
import styles from './LibraryImportFooter.css';

// The sticky footer, as Sonarr's ImportSeriesFooter: defaults that apply to the selected rows
// when changed, the search-on-add choice, and the Import button with the outcome beside it.
function LibraryImportFooter(props) {
  const {
    library,
    monitor,
    monitorNewItems,
    qualityProfileId,
    audioQualityProfileId,
    searchForMissingBooks,
    importCount,
    isImporting,
    importError,
    importResult,
    onInputChange,
    onSearchForMissingBooksChange,
    onImportPress
  } = props;

  const isLightNovel = library === LIGHT_NOVEL;

  return (
    <PageContentFooter>
      <div className={styles.footer}>
        <div className={styles.inputs}>
          <div className={styles.inputContainer}>
            <div className={styles.label}>
              {translate('Monitor')}
            </div>

            <MonitorBooksSelectInput
              name="monitor"
              value={monitor}
              onChange={onInputChange}
            />
          </div>

          <div className={styles.inputContainer}>
            <div className={styles.label}>
              {translate('MonitorNewItems')}
            </div>

            <MonitorNewItemsSelectInput
              name="monitorNewItems"
              value={monitorNewItems}
              onChange={onInputChange}
            />
          </div>

          <div className={styles.inputContainer}>
            <div className={styles.label}>
              {isLightNovel ? translate('EbookQualityProfile') : translate('QualityProfile')}
            </div>

            <QualityProfileSelectInputConnector
              name="qualityProfileId"
              value={qualityProfileId}
              onChange={onInputChange}
            />
          </div>

          {
            isLightNovel ?
              <div className={styles.inputContainer}>
                <div className={styles.label}>
                  {translate('AudioQualityProfile')}
                </div>

                <QualityProfileSelectInputConnector
                  name="audioQualityProfileId"
                  value={audioQualityProfileId}
                  onChange={onInputChange}
                />
              </div> :
              null
          }
        </div>

        <div className={styles.actions}>
          <label className={styles.searchLabelContainer}>
            <CheckInput
              containerClassName={styles.searchContainer}
              className={styles.searchInput}
              name="searchForMissingBooks"
              value={searchForMissingBooks}
              onChange={onSearchForMissingBooksChange}
            />

            <span>{translate('StartSearchForMissingVolumes')}</span>
          </label>

          <SpinnerButton
            kind={kinds.PRIMARY}
            isSpinning={isImporting}
            isDisabled={!importCount || isImporting}
            onPress={onImportPress}
          >
            {translate('ImportCountSeries', { count: importCount })}
          </SpinnerButton>

          {
            importResult && importResult.count ?
              <span className={styles.success}>
                {translate('LibraryImportImported', { count: importResult.count })}
                {' '}
                <Link to={libraryRoute(library)}>
                  {translate('GoToInterp', [isLightNovel ? translate('LightNovels') : translate('Manga')])}
                </Link>
              </span> :
              null
          }

          {
            importResult && importResult.notImported.length ?
              <span className={styles.error}>
                {translate('LibraryImportNotImported', { count: importResult.notImported.length })}
                <span className={styles.notImportedFolders}>
                  {importResult.notImported.join(', ')}
                </span>
              </span> :
              null
          }

          {
            importError ?
              <span className={styles.error}>
                {importError}
              </span> :
              null
          }
        </div>
      </div>
    </PageContentFooter>
  );
}

LibraryImportFooter.propTypes = {
  library: PropTypes.string.isRequired,
  monitor: PropTypes.string.isRequired,
  monitorNewItems: PropTypes.string.isRequired,
  qualityProfileId: PropTypes.number,
  audioQualityProfileId: PropTypes.number,
  searchForMissingBooks: PropTypes.bool.isRequired,
  importCount: PropTypes.number.isRequired,
  isImporting: PropTypes.bool.isRequired,
  importError: PropTypes.string,
  importResult: PropTypes.shape({
    count: PropTypes.number.isRequired,
    notImported: PropTypes.arrayOf(PropTypes.string).isRequired
  }),
  onInputChange: PropTypes.func.isRequired,
  onSearchForMissingBooksChange: PropTypes.func.isRequired,
  onImportPress: PropTypes.func.isRequired
};

export default LibraryImportFooter;
