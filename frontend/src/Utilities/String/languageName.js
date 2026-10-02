// KR/CN consumer (2026-09-29): a language code's display name in the browser's locale ("ja" -> "Japanese").
export default function languageName(code) {
  if (!code) {
    return '';
  }

  try {
    return new Intl.DisplayNames(undefined, { type: 'language' }).of(code) || code;
  } catch {
    return code;
  }
}
