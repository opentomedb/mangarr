using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Books;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.Clients;
using NzbDrone.Core.MediaFiles.TorrentInfo;

namespace NzbDrone.Core.Parser.Model
{
    public class RemoteBook
    {
        public ReleaseInfo Release { get; set; }
        public ParsedBookInfo ParsedBookInfo { get; set; }
        public Author Author { get; set; }
        public List<Book> Books { get; set; }

        // Light novels (2026-09): which edition of each book this release targets, from the
        // release's quality class (archive / ebook / audio). Set by ParsingService.Map, re-set by
        // DownloadDecisionMaker after its quality re-parse. Archive for every manga release.
        public MediaType MediaType { get; set; }
        public bool DownloadAllowed { get; set; }
        public TorrentSeedConfiguration SeedConfiguration { get; set; }
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public ReleaseSourceType ReleaseSource { get; set; }

        // Monthly multi-series packs: book id -> the file in the torrent that holds that volume.
        // Null for normal grabs. PackTorrent is the already-fetched .torrent so the grab never re-fetches it.
        public Dictionary<int, TorrentFileEntry> PackFiles { get; set; }
        public byte[] PackTorrent { get; set; }

        // True when history has a Grabbed row for this pack's info hash: Mangarr added the torrent before, so a
        // copy already in the client is Mangarr's to take over. False means a copy there was added by hand.
        public bool PackPreviouslyGrabbed { get; set; }

        public RemoteBook()
        {
            Books = new List<Book>();
            CustomFormats = new List<CustomFormat>();
        }

        public bool IsRecentBook()
        {
            return Books.Any(e => e.ReleaseDate >= DateTime.UtcNow.Date.AddDays(-14));
        }

        public override string ToString()
        {
            return Release.Title;
        }
    }

    public enum ReleaseSourceType
    {
        Unknown = 0,
        Rss = 1,
        Search = 2,
        UserInvokedSearch = 3,
        InteractiveSearch = 4,
        ReleasePush = 5
    }
}
