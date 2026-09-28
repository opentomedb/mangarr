// Preferred Edition (2026-09-24): an edition language code as a name ("fr" -> "French"); English for none.
export default function editionName(code) {
  if (!code || code === 'en') {
    return 'English';
  }

  try {
    return new Intl.DisplayNames(['en'], { type: 'language' }).of(code) || code;
  } catch (e) {
    return code;
  }
}
