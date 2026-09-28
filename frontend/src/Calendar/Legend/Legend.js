import PropTypes from 'prop-types';
import React from 'react';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import LegendIconItem from './LegendIconItem';
import LegendItem from './LegendItem';
import styles from './Legend.css';

function Legend(props) {
  const {
    showCutoffUnmetIcon,
    colorImpairedMode
  } = props;

  const iconsToShow = [];

  if (showCutoffUnmetIcon) {
    iconsToShow.push(
      <LegendIconItem
        name={translate('CutoffNotMet')}
        icon={icons.TRACK_FILE}
        kind={kinds.WARNING}
        tooltip={translate('QualityCutoffNotMet')}
      />
    );
  }

  return (
    <div className={styles.legend}>
      <div>
        <LegendItem
          name={translate('Downloading')}
          status="downloading"
          tooltip={translate('CalendarLegendDownloading')}
          colorImpairedMode={colorImpairedMode}
        />

        <LegendItem
          name={translate('Downloaded')}
          status="downloaded"
          tooltip={translate('CalendarLegendDownloaded')}
          colorImpairedMode={colorImpairedMode}
        />
      </div>

      <div>
        <LegendItem
          name={translate('Unreleased')}
          status="unreleased"
          tooltip={translate('CalendarLegendUnreleased')}
          colorImpairedMode={colorImpairedMode}
        />

        <LegendItem
          name={translate('Partial')}
          status="partial"
          tooltip={translate('CalendarLegendPartial')}
          colorImpairedMode={colorImpairedMode}
        />
      </div>

      <div>
        <LegendItem
          name={translate('Unmonitored')}
          status="unmonitored"
          tooltip={translate('CalendarLegendUnmonitored')}
          colorImpairedMode={colorImpairedMode}
        />

        <LegendItem
          name={translate('Missing')}
          status="missing"
          tooltip={translate('CalendarLegendMissing')}
          colorImpairedMode={colorImpairedMode}
        />
      </div>

      <div>
        {iconsToShow[0]}
      </div>

      {
        iconsToShow.length > 1 &&
          <div>
            {iconsToShow[1]}
            {iconsToShow[2]}
          </div>
      }
    </div>
  );
}

Legend.propTypes = {
  showCutoffUnmetIcon: PropTypes.bool.isRequired,
  colorImpairedMode: PropTypes.bool.isRequired
};

export default Legend;
