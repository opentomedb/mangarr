import PropTypes from 'prop-types';
import React from 'react';
import editionName from 'Utilities/String/editionName';
import translate from 'Utilities/String/translate';
import styles from './ChangeEditionModalContent.css';

// Preferred Edition (2026-09-24, spec §4): one series in the Change Edition preview. "Same numbering" only
// when it is (neither line a collected edition); different numbering with no files is allowed but says so.
function ChangeEditionPreviewRow(props) {
  const {
    name,
    fromLanguage,
    toLanguage,
    fromVolumes,
    toVolumes,
    compatible,
    blockedReason,
    filesAffected
  } = props;

  const numbering = compatible ? translate('EditionSameNumbering') : translate('EditionDifferentNumbering');

  return (
    <div className={blockedReason ? styles.blockedRow : styles.row}>
      <span className={styles.series}>{name}</span>
      <span>{editionName(fromLanguage)} → {editionName(toLanguage)}</span>
      <span>{fromVolumes} → {toVolumes}</span>
      <span>{blockedReason || numbering}</span>
      <span>{filesAffected === 1 ? translate('EditionFilesCountOne') : translate('EditionFilesCount', { count: filesAffected })}</span>
    </div>
  );
}

ChangeEditionPreviewRow.propTypes = {
  name: PropTypes.string.isRequired,
  fromLanguage: PropTypes.string.isRequired,
  toLanguage: PropTypes.string.isRequired,
  fromVolumes: PropTypes.number.isRequired,
  toVolumes: PropTypes.number.isRequired,
  compatible: PropTypes.bool.isRequired,
  blockedReason: PropTypes.string,
  filesAffected: PropTypes.number.isRequired
};

export default ChangeEditionPreviewRow;
