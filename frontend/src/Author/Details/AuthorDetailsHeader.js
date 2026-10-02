import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import AuthorPoster from 'Author/AuthorPoster';
import { getAuthorStatusDetails } from 'Author/AuthorStatus';
import HeartRating from 'Components/HeartRating';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Link from 'Components/Link/Link';
import Marquee from 'Components/Marquee';
import Measure from 'Components/Measure';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import Popover from 'Components/Tooltip/Popover';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import QualityProfileName from 'Settings/Profiles/Quality/QualityProfileName';
import fonts from 'Styles/Variables/fonts';
import suggestCorrectionUrl from 'Utilities/Author/suggestCorrectionUrl';
import formatBytes from 'Utilities/Number/formatBytes';
import languageName from 'Utilities/String/languageName';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import AuthorAlternateTitles from './AuthorAlternateTitles';
import AuthorDetailsLinks from './AuthorDetailsLinks';
import AuthorTagsConnector from './AuthorTagsConnector';
import styles from './AuthorDetailsHeader.css';

const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

function getFanartUrl(images) {
  return images.find((x) => x.coverType === 'fanart')?.url;
}

class AuthorDetailsHeader extends Component {

  //
  // Lifecyle

  constructor(props) {
    super(props);

    this.state = {
      overviewHeight: 0,
      titleWidth: 0
    };
  }

  //
  // Listeners

  onOverviewMeasure = ({ height }) => {
    this.setState({ overviewHeight: height });
  };

  onTitleMeasure = ({ width }) => {
    this.setState({ titleWidth: width });
  };

  //
  // Render

  render() {
    const {
      id,
      width,
      authorName,
      ratings,
      path,
      statistics,
      qualityProfileId,
      qualityProfileName,
      library,
      audioQualityProfileId,
      audioQualityProfileName,
      writer,
      editionFallback,
      editionLanguage,
      monitored,
      status,
      overview,
      parentName,
      parentForeignAuthorId,
      aniListId,
      links,
      images,
      alternateTitles,
      tags,
      isSaving,
      isSmallScreen,
      onMonitorTogglePress
    } = this.props;

    const {
      bookFileCount,
      sizeOnDisk
    } = statistics;

    const {
      overviewHeight,
      titleWidth
    } = this.state;

    const statusDetails = getAuthorStatusDetails(status);

    const fanartUrl = getFanartUrl(images);
    // Small screen: toggle 40 px + the up arrow's 35 px (AuthorDetails' navigation block) + a gap,
    // and the alternate-titles icon (40 px) beside the title when there is one -- else it sat under the arrow.
    const marqueeWidth = titleWidth - (isSmallScreen ? 85 + (alternateTitles.length ? 40 : 0) : 160);

    // UI pass (2026-09-24, SD-3/SD-4): external destinations live in the Links tooltip, as in the
    // *arrs -- the AniList binding and Suggest a Correction join the entry's own links there.
    const shownLinks = [
      ...(links || []),
      ...(aniListId ? [{ name: 'AniList', url: `https://anilist.co/manga/${aniListId}` }] : []),
      { name: translate('SuggestCorrection'), url: suggestCorrectionUrl(authorName, aniListId) }
    ];
    const isLightNovel = library === 'lightNovel';

    let bookFilesCountMessage = translate('BookFilesCountMessage');

    if (bookFileCount === 1) {
      bookFilesCountMessage = translate('VolumeFilesCountOne');
    } else if (bookFileCount > 1) {
      bookFilesCountMessage = translate('VolumeFilesCount', { count: bookFileCount });
    }

    return (
      <div className={styles.header} style={{ width }} >
        <div
          className={styles.backdrop}
          style={
            fanartUrl ?
              { backgroundImage: `url(${fanartUrl})` } :
              null
          }
        >
          <div className={styles.backdropOverlay} />
        </div>

        <div className={styles.headerContent}>
          <AuthorPoster
            className={styles.poster}
            images={images}
            size={250}
            lazy={false}
          />

          <div className={styles.info}>
            <Measure
              className={styles.titleRow}
              onMeasure={this.onTitleMeasure}
            >
              <div className={styles.titleContainer}>
                <div className={styles.toggleMonitoredContainer}>
                  <MonitorToggleButton
                    className={styles.monitorToggleButton}
                    monitored={monitored}
                    isSaving={isSaving}
                    size={isSmallScreen ? 30: 40}
                    onPress={onMonitorTogglePress}
                  />
                </div>

                <div className={styles.title} style={{ width: marqueeWidth }}>
                  {
                    isSmallScreen ?
                      <div className={styles.titleWrap}>{authorName}</div> :
                      <Marquee text={authorName} />
                  }
                </div>

                {
                  !!alternateTitles.length &&
                    <div className={styles.alternateTitlesIconContainer}>
                      <Popover
                        anchor={
                          <Icon
                            name={icons.ALTERNATE_TITLES}
                            size={20}
                          />
                        }
                        title={translate('AlternateTitles')}
                        body={<AuthorAlternateTitles alternateTitles={alternateTitles} />}
                        position={tooltipPositions.BOTTOM}
                      />
                    </div>
                }
              </div>
            </Measure>

            {
              isLightNovel && writer ?
                <div className={styles.writer}>
                  {translate('ByWriter', { writer })}
                </div> :
                null
            }

            {
              editionFallback && editionLanguage ?
                <div className={styles.writer}>
                  {translate('EditionFallbackNote', { language: languageName(editionLanguage) })}
                </div> :
                null
            }

            <div className={styles.details}>
              <div>
                <HeartRating
                  rating={ratings.value}
                  iconSize={20}
                />
              </div>
            </div>

            <div className={styles.detailsLabels}>
              <Label
                className={styles.detailsLabel}
                size={sizes.LARGE}
              >
                <Icon
                  name={icons.FOLDER}
                  size={17}
                />

                <span className={styles.path}>
                  {path}
                </span>
              </Label>

              {
                parentName ?
                  <Label
                    className={styles.detailsLabel}
                    title={translate('SeriesLineTooltip')}
                    size={sizes.LARGE}
                  >
                    <Icon
                      name={icons.GROUP}
                      size={17}
                    />

                    <span className={styles.path}>
                      {translate('PartOf')} <Link to={`/collections#${parentForeignAuthorId || ''}`}>{parentName}</Link>
                    </span>
                  </Label> :
                  null
              }

              <Label
                className={styles.detailsLabel}
                title={bookFilesCountMessage}
                size={sizes.LARGE}
              >
                <Icon
                  name={icons.DRIVE}
                  size={17}
                />

                <span className={styles.sizeOnDisk}>
                  {
                    formatBytes(sizeOnDisk || 0)
                  }
                </span>
              </Label>

              <Label
                className={styles.detailsLabel}
                title={translate(isLightNovel ? 'EbookQualityProfileNamed' : 'QualityProfileNamed', { name: qualityProfileName })}
                size={sizes.LARGE}
              >
                <Icon
                  name={icons.PROFILE}
                  size={17}
                />

                <span className={styles.qualityProfileName}>
                  {isLightNovel ? `${translate('Ebook')}: ` : null}
                  <QualityProfileName
                    qualityProfileId={qualityProfileId}
                  />
                </span>
              </Label>

              {
                isLightNovel && audioQualityProfileId ?
                  <Label
                    className={styles.detailsLabel}
                    title={translate('AudioQualityProfileNamed', { name: audioQualityProfileName })}
                    size={sizes.LARGE}
                  >
                    <Icon
                      name={icons.PROFILE}
                      size={17}
                    />

                    <span className={styles.qualityProfileName}>
                      {`${translate('Audiobook')}: `}
                      <QualityProfileName
                        qualityProfileId={audioQualityProfileId}
                      />
                    </span>
                  </Label> :
                  null
              }

              <Label
                className={styles.detailsLabel}
                size={sizes.LARGE}
              >
                <Icon
                  name={monitored ? icons.MONITORED : icons.UNMONITORED}
                  size={17}
                />

                <span className={styles.qualityProfileName}>
                  {monitored ? translate('Monitored') : translate('Unmonitored')}
                </span>
              </Label>

              <Label
                className={styles.detailsLabel}
                title={statusDetails.message}
                size={sizes.LARGE}
              >
                <Icon
                  name={statusDetails.icon}
                  size={17}
                />

                <span className={styles.qualityProfileName}>
                  {statusDetails.title}
                </span>
              </Label>

              <Tooltip
                anchor={
                  <Label
                    className={styles.detailsLabel}
                    size={sizes.LARGE}
                  >
                    <Icon
                      name={icons.EXTERNAL_LINK}
                      size={17}
                    />

                    <span className={styles.links}>
                      {translate('Links')}
                    </span>
                  </Label>
                }
                tooltip={
                  <AuthorDetailsLinks
                    links={shownLinks}
                  />
                }
                kind={kinds.INVERSE}
                position={tooltipPositions.BOTTOM}
              />

              {
                !!tags.length &&
                  <Tooltip
                    anchor={
                      <Label
                        className={styles.detailsLabel}
                        size={sizes.LARGE}
                      >
                        <Icon
                          name={icons.TAGS}
                          size={17}
                        />

                        <span className={styles.tags}>
                          {translate('Tags')}
                        </span>
                      </Label>
                    }
                    tooltip={<AuthorTagsConnector authorId={id} />}
                    kind={kinds.INVERSE}
                    position={tooltipPositions.BOTTOM}
                  />

              }
            </div>
            <Measure
              onMeasure={this.onOverviewMeasure}
              className={styles.overview}
            >
              <TextTruncate
                line={Math.floor(overviewHeight / (defaultFontSize * lineHeight))}
                text={stripHtml(overview)}
              />
            </Measure>
          </div>
        </div>
      </div>
    );
  }
}

AuthorDetailsHeader.propTypes = {
  id: PropTypes.number.isRequired,
  width: PropTypes.number.isRequired,
  authorName: PropTypes.string.isRequired,
  ratings: PropTypes.object.isRequired,
  path: PropTypes.string.isRequired,
  statistics: PropTypes.object.isRequired,
  qualityProfileId: PropTypes.number.isRequired,
  qualityProfileName: PropTypes.string,
  library: PropTypes.string,
  audioQualityProfileId: PropTypes.number,
  audioQualityProfileName: PropTypes.string,
  writer: PropTypes.string,
  editionFallback: PropTypes.bool,
  editionLanguage: PropTypes.string,
  monitored: PropTypes.bool.isRequired,
  status: PropTypes.string.isRequired,
  overview: PropTypes.string,
  parentName: PropTypes.string,
  parentForeignAuthorId: PropTypes.string,
  aniListId: PropTypes.number,
  links: PropTypes.arrayOf(PropTypes.object).isRequired,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  alternateTitles: PropTypes.arrayOf(PropTypes.string).isRequired,
  tags: PropTypes.arrayOf(PropTypes.number).isRequired,
  isSaving: PropTypes.bool.isRequired,
  isSmallScreen: PropTypes.bool.isRequired,
  onMonitorTogglePress: PropTypes.func.isRequired
};

export default AuthorDetailsHeader;
