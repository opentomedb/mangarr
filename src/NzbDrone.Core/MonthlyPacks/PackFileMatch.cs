using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.TorrentInfo;

namespace NzbDrone.Core.MonthlyPacks
{
    public record PackFileMatch(TorrentFileEntry File, Author Author, Book Book);
}
