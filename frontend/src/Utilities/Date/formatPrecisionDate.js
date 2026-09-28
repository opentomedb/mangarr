import moment from 'moment';

// Preferred Edition (2026-09-24, D6): an edition's coarse release date is shown as what it is --
// "2019" for a year, "Nov 2026" for a planned month -- never as the stand-in day stored for it.
export default function formatPrecisionDate(date, precision) {
  if (!date) {
    return '';
  }

  const value = moment(date);

  if (precision === 'year') {
    return value.format('YYYY');
  }

  if (precision === 'month') {
    return value.format('MMM YYYY');
  }

  return value.format('LL');
}
