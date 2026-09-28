import moment from 'moment';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import TextTruncate from 'react-text-truncate';
import getQueueItemState from 'Activity/Queue/getQueueItemState';
import AuthorNameLink from 'Author/AuthorNameLink';
import BookCover from 'Book/BookCover';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Marquee from 'Components/Marquee';
import Measure from 'Components/Measure';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, sizes, tooltipPositions } from 'Helpers/Props';
import fonts from 'Styles/Variables/fonts';
import suggestCorrectionUrl from 'Utilities/Author/suggestCorrectionUrl';
import formatRuntime from 'Utilities/Book/formatRuntime';
import { AUDIO } from 'Utilities/Book/mediaTypes';
import formatPrecisionDate from 'Utilities/Date/formatPrecisionDate';
import formatBytes from 'Utilities/Number/formatBytes';
import stripHtml from 'Utilities/String/stripHtml';
import translate from 'Utilities/String/translate';
import BookDetailsLinks from './BookDetailsLinks';
import styles from './BookDetailsHeader.css';

const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

function getFanartUrl(images) {
  return images.find((x) => x.coverType === 'fanart')?.url;
}

class BookDetailsHeader extends Component {

  //
  // Lifecycle

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
      width,
      titleSlug,
      title,
      displayTitle,
      seriesTitle,
      pageCount,
      overview,
      statistics = {},
      monitored,
      releaseDate,
      releaseDatePrecision,
      images,
      links,
      isSaving,
      shortDateFormat,
      author,
      isSmallScreen,
      selectedMediaType,
      selectedEdition,
      selectedEditionSize,
      selectedEditionUnreleased,
      queueItem,
      onMonitorTogglePress
    } = this.props;

    const {
      overviewHeight,
      titleWidth
    } = this.state;

    const fanartUrl = getFanartUrl(author.images);
    // Small screen: toggle 40 px + the up arrow's 35 px (BookDetails' navigation block) + a gap.
    const marqueeWidth = titleWidth - (isSmallScreen ? 85 : 160);

    // Per-edition header (2026-09-21): the Audiobook tab of a light novel shows the audiobook's own
    // facts -- runtime, release date (grey while unreleased), the M4B's size, an Audible link --
    // instead of the print edition's pages and date. The title is the one display title on both
    // tabs (UI pass 2026-09-24, V1): the same "Series: Subtitle (Vol. N)" calibre and
    // Audiobookshelf carry, never Audible's product title.
    const audioTab = author.library === 'lightNovel' && selectedMediaType === AUDIO && !!selectedEdition;
    const shownTitle = displayTitle || title;
    const runtime = audioTab ? formatRuntime(selectedEdition.runtimeMinutes) : null;
    const shownDate = audioTab ? selectedEdition.audioReleaseDate : releaseDate;
    const unreleased = audioTab && selectedEditionUnreleased;
    // Preferred Edition (2026-09-24, D6, final fix round Minor 1): an edition's year/month date reads "2019" /
    // "Nov 2026". The audiobook's own date keeps its day.
    const shownDateText = shownDate && !audioTab && releaseDatePrecision ?
      formatPrecisionDate(shownDate, releaseDatePrecision) :
      shownDate && moment(shownDate.slice(0, 10)).format(shortDateFormat);
    let dateTitle = null;

    if (unreleased && shownDate) {
      dateTitle = translate('AudioUnreleasedHelpText', { date: moment(shownDate.slice(0, 10)).format(shortDateFormat) });
    } else if (unreleased) {
      dateTitle = translate('AudioUnlistedHelpText');
    }

    let durationText = null;

    if (audioTab) {
      durationText = runtime;
    } else if (pageCount) {
      durationText = translate('CountPages', { count: pageCount });
    }
    // Series page round (2026-09-24): "Downloading N%" or the import state while the volume (the
    // selected edition, on a light novel) has a download that will fill it.
    const queueState = queueItem ? getQueueItemState(queueItem) : null;
    const shownSize = selectedEdition && selectedEditionSize != null ? selectedEditionSize : statistics.sizeOnDisk;
    // UI pass (2026-09-24, SD-3): Suggest a Correction joins the Links, pre-filled with the volume's
    // canonical "<series> Vol. N" title (BookInfoProxy mints it that way).
    const shownLinks = [
      ...(links || []),
      ...(audioTab && selectedEdition.asin ? [{ name: 'Audible', url: `https://www.audible.com/pd/${selectedEdition.asin}` }] : []),
      { name: translate('SuggestCorrection'), url: suggestCorrectionUrl(title, author.aniListId) }
    ];

    return (
      <div className={styles.header} style={{ width }}>
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
          <BookCover
            className={styles.cover}
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
                    size={isSmallScreen ? 30 : 40}
                    onPress={onMonitorTogglePress}
                  />
                </div>

                <div className={styles.title} style={{ width: marqueeWidth }}>
                  {
                    isSmallScreen ?
                      <div className={styles.titleWrap}>{shownTitle}</div> :
                      <Marquee text={shownTitle} />
                  }
                </div>

              </div>
            </Measure>

            <div className={styles.details}>
              <div>
                {seriesTitle}
              </div>

              <div>
                <AuthorNameLink
                  className={styles.authorLink}
                  titleSlug={author.titleSlug}
                  authorName={author.authorName}
                />

                {
                  durationText ?
                    <span className={styles.duration}>
                      {durationText}
                    </span> :
                    null
                }

              </div>
            </div>

            <div className={styles.detailsLabels}>
              {
                (shownDate || unreleased) &&
                  <Label
                    className={styles.detailsLabel}
                    size={sizes.LARGE}
                    kind={unreleased ? kinds.DISABLED : kinds.DEFAULT}
                    title={dateTitle}
                  >
                    <Icon
                      name={icons.CALENDAR}
                      size={17}
                    />

                    <span className={styles.sizeOnDisk}>
                      {
                        shownDate ?
                          shownDateText :
                          translate('ComingSoon')
                      }
                    </span>
                  </Label>
              }

              <Label
                className={styles.detailsLabel}
                size={sizes.LARGE}
              >
                <Icon
                  name={icons.DRIVE}
                  size={17}
                />

                <span className={styles.sizeOnDisk}>
                  {
                    formatBytes(shownSize)
                  }
                </span>
              </Label>

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

              {
                queueState ?
                  <Label
                    className={styles.detailsLabel}
                    kind={queueState.kind === kinds.DANGER || queueState.kind === kinds.WARNING ? queueState.kind : kinds.DEFAULT}
                    title={queueState.title}
                    size={sizes.LARGE}
                  >
                    <Icon
                      name={queueState.iconName}
                      size={17}
                    />

                    <span className={styles.qualityProfileName}>
                      {queueState.label}
                    </span>
                  </Label> :
                  null
              }

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
                  <BookDetailsLinks
                    titleSlug={titleSlug}
                    links={shownLinks}
                  />
                }
                kind={kinds.INVERSE}
                position={tooltipPositions.BOTTOM}
              />

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

BookDetailsHeader.propTypes = {
  id: PropTypes.number.isRequired,
  width: PropTypes.number.isRequired,
  titleSlug: PropTypes.string.isRequired,
  title: PropTypes.string.isRequired,
  displayTitle: PropTypes.string,
  seriesTitle: PropTypes.string.isRequired,
  pageCount: PropTypes.number,
  overview: PropTypes.string,
  statistics: PropTypes.object.isRequired,
  releaseDate: PropTypes.string.isRequired,
  releaseDatePrecision: PropTypes.string,
  images: PropTypes.arrayOf(PropTypes.object).isRequired,
  links: PropTypes.arrayOf(PropTypes.object).isRequired,
  monitored: PropTypes.bool.isRequired,
  shortDateFormat: PropTypes.string.isRequired,
  isSaving: PropTypes.bool.isRequired,
  author: PropTypes.object,
  isSmallScreen: PropTypes.bool.isRequired,
  selectedMediaType: PropTypes.string,
  selectedEdition: PropTypes.object,
  selectedEditionSize: PropTypes.number,
  selectedEditionUnreleased: PropTypes.bool,
  queueItem: PropTypes.object,
  onMonitorTogglePress: PropTypes.func.isRequired
};

BookDetailsHeader.defaultProps = {
  isSaving: false
};

export default BookDetailsHeader;
