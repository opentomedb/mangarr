import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import AuthorPoster from 'Author/AuthorPoster';
import HeartRating from 'Components/HeartRating';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import { icons, kinds, sizes } from 'Helpers/Props';
import dimensions from 'Styles/Variables/dimensions';
import fonts from 'Styles/Variables/fonts';
import languageName from 'Utilities/String/languageName';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import AddNewAuthorModal from './AddNewAuthorModal';
import styles from './AddNewAuthorSearchResult.css';

const columnPadding = parseInt(dimensions.authorIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(dimensions.authorIndexColumnPaddingSmallScreen);
const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

function calculateHeight(rowHeight, isSmallScreen) {
  let height = rowHeight - 45;

  if (isSmallScreen) {
    height -= columnPaddingSmallScreen;
  } else {
    height -= columnPadding;
  }

  return height;
}

class AddNewAuthorSearchResult extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isNewAddAuthorModalOpen: false
    };
  }

  componentDidUpdate(prevProps) {
    if (!prevProps.isExistingAuthor && this.props.isExistingAuthor) {
      this.onAddAuthorModalClose();
    }
  }

  //
  // Listeners

  onPress = () => {
    this.setState({ isNewAddAuthorModalOpen: true });
  };

  onAddAuthorModalClose = () => {
    this.setState({ isNewAddAuthorModalOpen: false });
  };

  onAniListLinkPress = (event) => {
    event.stopPropagation();
  };

  //
  // Render

  render() {
    const {
      foreignAuthorId,
      titleSlug,
      authorName,
      year,
      disambiguation,
      status,
      overview,
      ratings,
      folder,
      images,
      library,
      totalVolumes,
      aniListId,
      editionFallback,
      editionLanguage,
      editionOptions,
      catalogueLine,
      isExistingAuthor,
      isSmallScreen
    } = this.props;

    const {
      isNewAddAuthorModalOpen
    } = this.state;

    const linkProps = isExistingAuthor ? { to: `/author/${titleSlug}` } : { onPress: this.onPress };

    const endedString = translate('StatusEndedEnded');

    const height = calculateHeight(230, isSmallScreen);

    // Line safety (2026-09-28): one short line under the title for a catalogue-backed result.
    const lineFacts = catalogueLine ?
      [
        editionFallback && editionLanguage ? translate('EditionFallbackNote', { language: languageName(editionLanguage) }) : null,
        catalogueLine.volumeCount ? translate('CountVolumes', { count: catalogueLine.volumeCount }) : null,
        catalogueLine.publisher || null,
        catalogueLine.spinOffOf ? translate('SpinOffOf', { name: catalogueLine.spinOffOf }) : null
      ].filter(Boolean).join(' · ') :
      null;

    return (
      <div className={styles.searchResult}>
        <Link
          className={styles.underlay}
          {...linkProps}
        />

        <div className={styles.overlay}>
          {
            isSmallScreen ?
              null :
              <AuthorPoster
                className={styles.poster}
                images={images}
                size={250}
                overflow={true}
                lazy={false}
              />
          }

          <div className={styles.content}>
            <div className={styles.nameRow}>
              <div className={styles.nameContainer}>
                <div className={styles.name}>
                  {authorName}

                  {
                    !authorName.contains(year) && year ?
                      <span className={styles.year}>
                        ({year})
                      </span> :
                      null
                  }
                  {
                    !!disambiguation &&
                      <span className={styles.year}>({disambiguation})</span>
                  }
                </div>
              </div>

              <div className={styles.icons}>
                {
                  isExistingAuthor ?
                    <Icon
                      className={styles.alreadyExistsIcon}
                      name={icons.CHECK_CIRCLE}
                      size={36}
                      title={translate('AlreadyInYourLibrary')}
                    /> :
                    null
                }

                {/* UI pass (2026-09-24, AN-4): the metadata link a Sonarr result carries (TVDB). */}
                {
                  aniListId ?
                    <Link
                      className={styles.aniListLink}
                      to={`https://anilist.co/manga/${aniListId}`}
                      title="AniList"
                      onPress={this.onAniListLinkPress}
                    >
                      <Icon
                        className={styles.aniListLinkIcon}
                        name={icons.EXTERNAL_LINK}
                        size={28}
                      />
                    </Link> :
                    null
                }
              </div>
            </div>

            {
              lineFacts ?
                <div
                  className={styles.lineFacts}
                  title={lineFacts}
                >
                  {lineFacts}
                </div> :
                null
            }

            <div>
              {
                ratings.votes > 0 ?
                  <Label size={sizes.LARGE}>
                    <HeartRating
                      rating={ratings.value}
                      iconSize={13}
                    />
                  </Label> :
                  null
              }

              {
                status === 'ended' ?
                  <Label
                    kind={kinds.DANGER}
                    size={sizes.LARGE}
                  >
                    {endedString}
                  </Label> :
                  null
              }

              {
                library === 'lightNovel' ?
                  <Label size={sizes.LARGE}>
                    {totalVolumes ? translate('LightNovelVolumes', { count: totalVolumes }) : translate('LightNovel')}
                  </Label> :
                  null
              }
            </div>

            <div
              className={styles.overview}
              style={{
                maxHeight: `${height}px`
              }}
            >
              <TextTruncate
                truncateText="…"
                line={Math.floor(height / (defaultFontSize * lineHeight))}
                text={stripHtml(overview)}
              />
            </div>
          </div>
        </div>

        <AddNewAuthorModal
          isOpen={isNewAddAuthorModalOpen && !isExistingAuthor}
          foreignAuthorId={foreignAuthorId}
          authorName={authorName}
          disambiguation={disambiguation}
          year={year}
          overview={overview}
          folder={folder}
          images={images}
          library={library}
          editionLanguage={editionLanguage}
          editionOptions={editionOptions}
          onModalClose={this.onAddAuthorModalClose}
        />
      </div>
    );
  }
}

AddNewAuthorSearchResult.propTypes = {
  foreignAuthorId: PropTypes.string.isRequired,
  titleSlug: PropTypes.string.isRequired,
  authorName: PropTypes.string.isRequired,
  year: PropTypes.number,
  disambiguation: PropTypes.string,
  status: PropTypes.string.isRequired,
  overview: PropTypes.string,
  ratings: PropTypes.object.isRequired,
  folder: PropTypes.string.isRequired,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  library: PropTypes.string,
  totalVolumes: PropTypes.number,
  aniListId: PropTypes.number,
  editionFallback: PropTypes.bool,
  editionLanguage: PropTypes.string,
  editionOptions: PropTypes.arrayOf(PropTypes.object),
  catalogueLine: PropTypes.object,
  isExistingAuthor: PropTypes.bool.isRequired,
  isSmallScreen: PropTypes.bool.isRequired
};

export default AddNewAuthorSearchResult;
