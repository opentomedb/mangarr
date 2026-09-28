using System;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Profiles.Qualities
{
    // The values the "preferred light novel format" setting accepts and the calibre OutputFormat
    // each one names -- one source of truth for the config validator that rejects anything else and
    // for ConvertLightNovelFormatService/LightNovelCalibreSettings, so the two cannot drift.
    public static class PreferredFormats
    {
        // This is the calibre delivery-conversion setting, not a grab format -- it never reorders a
        // quality profile. "any" is not offered: it always means "convert to nothing", which epub
        // already says.
        public static bool IsKnownLightNovelFormat(string format)
        {
            return format == null ||
                   string.Equals(format, "epub", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(format, "azw3", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(format, "kepub", StringComparison.OrdinalIgnoreCase);
        }

        // The calibre OutputFormat to convert the imported EPUB to on add. null/blank/epub = no
        // conversion; azw3/kepub map explicitly rather than uppercasing whatever was passed in,
        // since ForConfig() never runs this through the IsKnownLightNovelFormat validator -- a stale
        // stored value or a direct DB edit must not be able to send calibre an arbitrary OutputFormat.
        public static string LightNovelOutputFormat(string format)
        {
            if (format.IsNullOrWhiteSpace() || string.Equals(format, "epub", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.Equals(format, "azw3", StringComparison.OrdinalIgnoreCase))
            {
                return "AZW3";
            }

            if (string.Equals(format, "kepub", StringComparison.OrdinalIgnoreCase))
            {
                return "KEPUB";
            }

            return null;
        }
    }
}
