using NzbDrone.Core.Configuration;
using Readarr.Http.REST;

namespace Readarr.Api.V1.Config
{
    public class UiConfigResource : RestResource
    {
        //Calendar
        public int FirstDayOfWeek { get; set; }
        public string CalendarWeekColumnHeader { get; set; }

        //Dates
        public string ShortDateFormat { get; set; }
        public string LongDateFormat { get; set; }
        public string TimeFormat { get; set; }
        public bool ShowRelativeDates { get; set; }

        public bool EnableColorImpairedMode { get; set; }
        public int UILanguage { get; set; }

        // Preferred Edition (2026-09-24): beside UI Language, the way Radarr puts Movie Info Language;
        // independent of it (no linking). The markets list is its own GET (api/v1/edition/markets) --
        // never a property here, since SaveConfig writes every property back.
        public string PreferredEditionLanguages { get; set; }
        public string Theme { get; set; }
    }

    public static class UiConfigResourceMapper
    {
        public static UiConfigResource ToResource(IConfigFileProvider config, IConfigService model)
        {
            return new UiConfigResource
            {
                FirstDayOfWeek = model.FirstDayOfWeek,
                CalendarWeekColumnHeader = model.CalendarWeekColumnHeader,

                ShortDateFormat = model.ShortDateFormat,
                LongDateFormat = model.LongDateFormat,
                TimeFormat = model.TimeFormat,
                ShowRelativeDates = model.ShowRelativeDates,

                EnableColorImpairedMode = model.EnableColorImpairedMode,
                UILanguage = model.UILanguage,
                PreferredEditionLanguages = model.PreferredEditionLanguages,

                Theme = config.Theme
            };
        }
    }
}
