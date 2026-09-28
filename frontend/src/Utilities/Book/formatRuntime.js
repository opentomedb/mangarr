// "9h 12m" from Audible's runtime in minutes; null when unknown.
export default function formatRuntime(minutes) {
  if (!minutes) {
    return null;
  }

  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;

  return hours ? `${hours}h ${rest}m` : `${rest}m`;
}
