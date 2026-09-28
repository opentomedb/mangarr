using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.BookImport
{
    public class ImportResult
    {
        public ImportDecision<LocalBook> ImportDecision { get; private set; }
        public List<string> Errors { get; private set; }

        // Server messages (2026-09-26, plan ruling R2): the template behind each entry of Errors (null where
        // plain). CompletedDownloadService hands them to the queue's per-file status messages. Not serialized:
        // ImportResult itself never reaches the API.
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public List<ServerText> ErrorTexts { get; private set; }

        public ImportResultType Result
        {
            get
            {
                if (Errors.Any())
                {
                    if (ImportDecision.Approved)
                    {
                        return ImportResultType.Skipped;
                    }

                    return ImportResultType.Rejected;
                }

                return ImportResultType.Imported;
            }
        }

        public ImportResult(ImportDecision<LocalBook> importDecision, params string[] errors)
        {
            Ensure.That(importDecision, () => importDecision).IsNotNull();

            ImportDecision = importDecision;
            Errors = errors.ToList();
            ErrorTexts = errors.Select(e => (ServerText)null).ToList();
        }

        public ImportResult(ImportDecision<LocalBook> importDecision, ServerText error)
            : this(importDecision, error.English)
        {
            ErrorTexts = new List<ServerText> { error };
        }

        // A rejected decision: one error per rejection, each keeping its template.
        public static ImportResult Rejected(ImportDecision<LocalBook> importDecision)
        {
            return new ImportResult(importDecision, importDecision.Rejections.Select(r => r.Reason).ToArray())
            {
                ErrorTexts = importDecision.Rejections.Select(r => r.Text).ToList()
            };
        }
    }
}
