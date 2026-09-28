import PropTypes from 'prop-types';
import React from 'react';
import Label from 'Components/Label';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

function getKind(seeders) {
  if (seeders > 50) {
    return kinds.PRIMARY;
  }

  if (seeders > 10) {
    return kinds.INFO;
  }

  if (seeders > 0) {
    return kinds.WARNING;
  }

  return kinds.DANGER;
}

// UI translations v1 (2026-09-25): whole phrases per unit, not `${peers} ${peersUnit}s` -- a
// translation can't pluralize an English noun by adding "s".
function getSeedersTooltipPart(seeders) {
  if (seeders == null) {
    return translate('UnknownSeeders');
  }

  if (seeders === 1) {
    return translate('OneSeeder');
  }

  return translate('SeedersCount', { count: seeders });
}

function getLeechersTooltipPart(leechers) {
  if (leechers == null) {
    return translate('UnknownLeechers');
  }

  if (leechers === 1) {
    return translate('OneLeecher');
  }

  return translate('LeechersCount', { count: leechers });
}

function Peers(props) {
  const {
    seeders,
    leechers
  } = props;

  const kind = getKind(seeders);

  return (
    <Label
      kind={kind}
      title={`${getSeedersTooltipPart(seeders)}, ${getLeechersTooltipPart(leechers)}`}
    >
      {seeders == null ? '-' : seeders} / {leechers == null ? '-' : leechers}
    </Label>
  );
}

Peers.propTypes = {
  seeders: PropTypes.number,
  leechers: PropTypes.number
};

export default Peers;
