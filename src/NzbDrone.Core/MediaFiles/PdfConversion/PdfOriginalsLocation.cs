using System.IO;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MediaFiles.PdfConversion
{
    // The one place that knows where converted PDFs' originals are kept: the folder the user chose in
    // Media Management, or pdf-originals in the app data folder (today's behavior) when it is empty.
    public static class PdfOriginalsLocation
    {
        // Written into a folder Mangarr owns. Retention only ever deletes inside a marked (or default) folder,
        // so a folder the user pointed at that already holds their own PDFs is never touched.
        public const string MarkerFileName = ".mangarr-pdf-originals";

        public static bool IsDefault(IConfigService configService) => string.IsNullOrWhiteSpace(configService.PdfOriginalsFolder);

        public static string Root(IConfigService configService, IAppFolderInfo appFolderInfo)
        {
            var configured = configService.PdfOriginalsFolder;

            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(appFolderInfo.AppDataFolder, "pdf-originals")
                : configured;
        }
    }
}
