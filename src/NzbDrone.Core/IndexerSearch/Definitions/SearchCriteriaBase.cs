using System.Collections.Generic;
using System.Text.RegularExpressions;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.IndexerSearch.Definitions
{
    public abstract class SearchCriteriaBase
    {
        private static readonly Regex NonWord = new Regex(@"[^\w`'’]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BeginningThe = new Regex(@"^the\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public virtual bool MonitoredBooksOnly { get; set; }
        public virtual bool UserInvokedSearch { get; set; }
        public virtual bool InteractiveSearch { get; set; }

        public Author Author { get; set; }
        public List<Book> Books { get; set; }

        // Light novels (2026-09): which edition class this search is for -- Archive for every manga
        // search, Ebook or Audio for a light-novel edition search (B3 sets it). Edition is the one
        // searched edition when the search is for a single volume, else null.
        public MediaType MediaType { get; set; }
        public Edition Edition { get; set; }

        // Final review I1 (2026-09-20): whether this search stamps Book.LastSearchTime on its
        // criteria set. Every search does except the SCHEDULED cutoff-unmet run: the stamp is
        // per book but the two scheduled searches want different EDITIONS of it, so a cutoff run
        // stamping a volume whose other edition is missing would make the missing search's 24h
        // back-off skip that volume's missing edition for good.
        public bool StampLastSearch { get; set; } = true;

        public string AuthorQuery => GetQueryTitle(Author.Name);

        public static string GetQueryTitle(string title)
        {
            Ensure.That(title, () => title).IsNotNullOrWhiteSpace();

            // Most VA books are listed as VA, not Various Authors
            // TODO: Needed in Mangarr??
            if (title == "Various Authors")
            {
                title = "VA";
            }

            var cleanTitle = BeginningThe.Replace(title, string.Empty);

            cleanTitle = cleanTitle.Replace(" & ", " ");
            cleanTitle = cleanTitle.Replace(".", " ");
            cleanTitle = NonWord.Replace(cleanTitle, "+");

            //remove any repeating +s
            cleanTitle = Regex.Replace(cleanTitle, @"\+{2,}", "+");
            cleanTitle = cleanTitle.RemoveAccent();
            cleanTitle = cleanTitle.Trim('+', ' ');

            return cleanTitle.Length == 0 ? title : cleanTitle;
        }
    }
}
