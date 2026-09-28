using System;
using System.Globalization;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource.Gcd;

namespace NzbDrone.Core.MetadataSource.Manga
{
    // Preferred Edition (2026-09-24, D6): what an edition volume's catalogue date becomes. OpenTome keeps
    // day-precision dates in release_date and coarser ones in release_date_raw + _precision + _type
    // (DNB gives German volumes a year, and a planned month for an announcement). A coarse date is
    // stored as a date plus its precision and shown as "2019" / "Nov 2026" -- never Jan 1, the value
    // this app has treated as a fake year-only date at every layer.
    public static class EditionDates
    {
        public const string Year = "year";
        public const string Month = "month";

        public static (DateTime? Date, string Precision) Resolve(GcdVolume volume, DateTime? generatedAt, DateTime today)
        {
            var day = MangaSeriesMetadataProvider.ParseDate(volume.ReleaseDate);

            if (day.HasValue)
            {
                return (day, null);
            }

            var raw = volume.ReleaseDateRaw?.Trim();

            if (raw.IsNullOrWhiteSpace())
            {
                return (null, null);
            }

            if (volume.ReleaseDatePrecision == Month &&
                DateTime.TryParseExact(raw, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var month))
            {
                return (Local(new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month))), Month);
            }

            if (volume.ReleaseDatePrecision == Year && raw.Length == 4 &&
                int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year >= 1900)
            {
                // A deposit copy of THIS year exists (DNB 'published'): wanted now, dated by the build.
                if (year == today.Year && volume.ReleaseDateType == "published" && generatedAt.HasValue)
                {
                    var built = generatedAt.Value.Date;

                    return (Local(built.Month == 1 && built.Day == 1 ? built.AddDays(1) : built), Year);
                }

                return (Local(new DateTime(year, 12, 31)), Year);
            }

            return (null, null);
        }

        // Local midnight in UTC, the value MangaSeriesMetadataProvider.ParseDate produces, so a stored row
        // compares equal to the remote on the next pass.
        private static DateTime Local(DateTime date)
        {
            return DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime();
        }
    }
}
