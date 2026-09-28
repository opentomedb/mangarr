import translate from 'Utilities/String/translate';

function formatAge(age, ageHours, ageMinutes) {
  age = Math.round(age);
  ageHours = parseFloat(ageHours);
  ageMinutes = ageMinutes && parseFloat(ageMinutes);

  if (age < 2 && ageHours) {
    if (ageHours < 2 && !!ageMinutes) {
      // UI translations v1 (2026-09-25): the singular still keys on ageHours, as Readarr's did.
      return ageHours === 1 ?
        translate('FormatAgeMinute', { age: ageMinutes.toFixed(0) }) :
        translate('FormatAgeMinutes', { age: ageMinutes.toFixed(0) });
    }

    return ageHours === 1 ?
      translate('FormatAgeHour', { age: ageHours.toFixed(1) }) :
      translate('FormatAgeHours', { age: ageHours.toFixed(1) });
  }

  return age === 1 ?
    translate('FormatAgeDay', { age }) :
    translate('FormatAgeDays', { age });
}

export default formatAge;
