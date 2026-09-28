import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import AuthorPoster from 'Author/AuthorPoster';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import { icons, kinds } from 'Helpers/Props';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import styles from './FixMatchCandidate.css';

// One AniList candidate in the Fix Match modal: the add-search result card
// (Search/Author/AddNewAuthorSearchResult) shrunk to a list row. Everything shown is the
// MatchCandidateResource the backend already ranked; nothing is fetched or decided here.
class FixMatchCandidate extends Component {

  //
  // Listeners

  onSelectPress = () => {
    this.props.onSelectPress(this.props.aniListId);
  };

  //
  // Render

  render() {
    const {
      aniListId,
      title,
      format,
      year,
      volumes,
      popularity,
      coverUrl,
      description,
      isCurrent,
      isSaving
    } = this.props;

    // AuthorImage rewrites `<coverType>.jpg` into a sized name; an AniList URL never
    // contains that token, so it passes through untouched (the placeholder covers a null).
    const images = coverUrl ? [{ coverType: 'poster', url: coverUrl }] : [];

    const details = [
      format,
      year,
      volumes == null ? null : translate('FixMatchVolumes', { volumes }),
      popularity == null ? null : translate('FixMatchPopularity', { popularity: popularity.toLocaleString() })
    ].filter((x) => x != null).join(' · ');

    return (
      <div className={styles.candidate}>
        <AuthorPoster
          className={styles.poster}
          images={images}
          size={100}
          overflow={true}
          lazy={false}
        />

        <div className={styles.content}>
          <div className={styles.titleRow}>
            <div className={styles.title}>
              {title}
            </div>

            <span className={styles.id}>
              #{aniListId}
            </span>
          </div>

          <div className={styles.details}>
            {details}
          </div>

          <div className={styles.overview}>
            <TextTruncate
              truncateText="…"
              line={3}
              text={stripHtml(description || '')}
            />
          </div>
        </div>

        <div className={styles.actions}>
          {
            isCurrent ?
              <Icon
                className={styles.currentIcon}
                name={icons.CHECK_CIRCLE}
                size={28}
                title={translate('MatchedTo', { id: aniListId })}
              /> :
              <Button
                kind={kinds.PRIMARY}
                isDisabled={isSaving}
                onPress={this.onSelectPress}
              >
                {translate('Select')}
              </Button>
          }
        </div>
      </div>
    );
  }
}

FixMatchCandidate.propTypes = {
  aniListId: PropTypes.number.isRequired,
  title: PropTypes.string.isRequired,
  format: PropTypes.string,
  year: PropTypes.number,
  volumes: PropTypes.number,
  popularity: PropTypes.number,
  coverUrl: PropTypes.string,
  description: PropTypes.string,
  isCurrent: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  onSelectPress: PropTypes.func.isRequired
};

export default FixMatchCandidate;
