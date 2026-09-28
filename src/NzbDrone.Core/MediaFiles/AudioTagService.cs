using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles.BookImport.Existing;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using TagLib;

namespace NzbDrone.Core.MediaFiles
{
    public interface IAudioTagService
    {
        ParsedTrackInfo ReadTags(string file);
        void WriteTags(BookFile trackfile, bool newDownload, bool force = false);
        void SyncTags(List<Edition> tracks);
        List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId);
        List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId);
        void RetagFiles(RetagFilesCommand message);
        void RetagAuthor(RetagAuthorCommand message);
    }

    public class AudioTagService : IAudioTagService
    {
        private readonly IConfigService _configService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRootFolderWatchingService _rootFolderWatchingService;
        private readonly IAuthorService _authorService;
        private readonly IMapCoversToLocal _mediaCoverService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IAdoptedAudioSyncService _adoptedSync;
        private readonly Logger _logger;

        public AudioTagService(IConfigService configService,
                               IMediaFileService mediaFileService,
                               IDiskProvider diskProvider,
                               IRootFolderWatchingService rootFolderWatchingService,
                               IAuthorService authorService,
                               IMapCoversToLocal mediaCoverService,
                               IEventAggregator eventAggregator,
                               IAdoptedAudioSyncService adoptedSync,
                               Logger logger)
        {
            _configService = configService;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _rootFolderWatchingService = rootFolderWatchingService;
            _authorService = authorService;
            _mediaCoverService = mediaCoverService;
            _eventAggregator = eventAggregator;
            _adoptedSync = adoptedSync;
            _logger = logger;
        }

        public AudioTag ReadAudioTag(string path)
        {
            return new AudioTag(path);
        }

        public ParsedTrackInfo ReadTags(string path)
        {
            return new AudioTag(path);
        }

        public AudioTag GetTrackMetadata(BookFile trackfile)
        {
            var edition = trackfile.Edition.Value;
            var book = edition.Book.Value;
            var author = book.Author.Value;
            var partCount = edition.BookFiles.Value.Count;

            var fileTags = ReadAudioTag(trackfile.Path);

            // Light-novel audio (2026-09-18): tag the way Audiobookshelf reads it -- the album is
            // the book title in ABS, so it carries the Audible product title (falling back to the
            // edition title); the per-file title is the same value (equal to the album = not a
            // chapter title); SERIES / SERIES-PART carry the volume. Manga and ebooks keep
            // Readarr's tags byte-identical.
            if (author.Library == LibraryType.LightNovel && edition.MediaType == MediaType.Audio)
            {
                // Audible splits some product names into title + subtitle ("Mushoku Tensei" / "Jobless
                // Reincarnation (Light Novel), Vol. 1"; "Sword Art Online 6" / "Phantom Bullet"); the
                // audiobook's name is both, as the product page shows it. A subtitle that only names the
                // series or an edition label ("Light Novel") adds nothing, and one the title already
                // carries is not repeated.
                var audiobookTitle = edition.AudiobookTitle.IsNotNullOrWhiteSpace() ? edition.AudiobookTitle : edition.Title;
                var subtitle = edition.AudiobookSubtitle;
                if (edition.AudiobookTitle.IsNotNullOrWhiteSpace()
                    && subtitle.IsNotNullOrWhiteSpace()
                    && !Subtitles.NamesSeries(subtitle, author.Name, author.Metadata.Value.Aliases)
                    && !audiobookTitle.Contains(subtitle, StringComparison.OrdinalIgnoreCase))
                {
                    audiobookTitle = $"{audiobookTitle}: {subtitle}";
                }

                // The edition's file count at both moments a tag is written: the DB list is right
                // on a sync / retag of stored files but empty at import (WriteTags runs before the
                // batch is inserted), and the batch count on the BookFile is right at import but
                // not mapped (0 on a DB-loaded file) -- so whichever is set. Kept off partCount:
                // the full write's TrackCount stays upstream's DB count.
                var fileCount = Math.Max(partCount, trackfile.PartCount);

                // One copy each (2026-09-20): Mangarr-grabbed audio carries the series' Writer as
                // performers / album artists; an adopted original keeps its own (its tags are
                // written only on the entry's opt-in, and never its artist).
                var writer = author.Metadata.Value.Writer;

                // Additive (D4): only the four fields below are written (plus the writer, above);
                // every other field carries the file's own value so whatever reads this tag sees
                // what is on disk (the artist is not the series name, the label and cover are not
                // cleared). No cover lookup: the file's picture is never touched.
                return new AudioTag
                {
                    Additive = true,
                    AdditiveAuthors = !trackfile.Adopted && writer.IsNotNullOrWhiteSpace() ? new[] { writer } : null,

                    // a multi-part audiobook keeps its per-file titles (the chapter titles ABS
                    // reads); the title is the album's only on a single-file edition. (Read falls
                    // back to the sort title for a file with no title; both diff sides share that
                    // fallback, so it never drives a write of its own.)
                    Title = fileCount == 1 ? audiobookTitle : fileTags.Title,
                    Book = audiobookTitle,
                    Series = author.Name,

                    // an unnumbered row gets no part rather than sorting as #0
                    SeriesPart = book.VolumeNumber > 0 ? MangaVolumeParser.Format(book.VolumeNumber) : null,
                    Performers = fileTags.Performers,
                    BookAuthors = fileTags.BookAuthors,
                    Track = fileTags.Track,
                    TrackCount = fileTags.TrackCount,
                    Disc = fileTags.Disc,
                    DiscCount = fileTags.DiscCount,
                    Media = fileTags.Media,
                    Date = fileTags.Date,
                    Year = fileTags.Year,
                    OriginalReleaseDate = fileTags.OriginalReleaseDate,
                    OriginalYear = fileTags.OriginalYear,
                    Publisher = fileTags.Publisher,
                    Genres = fileTags.Genres,
                    ImageFile = null,
                    ImageSize = fileTags.ImageSize,
                };
            }

            var cover = edition.Images.FirstOrDefault(x => x.CoverType == MediaCoverTypes.Cover);
            string imageFile = null;
            long imageSize = 0;
            if (cover != null)
            {
                imageFile = _mediaCoverService.GetCoverPath(book.Id, MediaCoverEntity.Book, cover.CoverType, cover.Extension, null);
                _logger.Trace($"Embedding: {imageFile}");
                var fileInfo = _diskProvider.GetFileInfo(imageFile);
                if (fileInfo.Exists)
                {
                    imageSize = fileInfo.Length;
                }
                else
                {
                    imageFile = null;
                }
            }

            return new AudioTag
            {
                Title = edition.Title,
                Performers = new[] { author.Name },
                BookAuthors = new[] { author.Name },
                Track = (uint)trackfile.Part,
                TrackCount = (uint)partCount,
                Book = book.Title,
                Disc = fileTags.Disc,
                DiscCount = fileTags.DiscCount,

                // We may have omitted media so index in the list isn't the same as medium number
                Media = fileTags.Media,
                Date = edition.ReleaseDate,
                Year = (uint)(edition.ReleaseDate?.Year ?? 0),
                OriginalReleaseDate = book.ReleaseDate,
                OriginalYear = (uint)(book.ReleaseDate?.Year ?? 0),
                Publisher = edition.Publisher,
                Genres = new string[0],
                ImageFile = imageFile,
                ImageSize = imageSize,
            };
        }

        private void UpdateTrackfileSizeAndModified(BookFile trackfile, string path)
        {
            // update the saved file size so that the importer doesn't get confused on the next scan
            var fileInfo = _diskProvider.GetFileInfo(path);
            trackfile.Size = fileInfo.Length;
            trackfile.Modified = fileInfo.LastWriteTimeUtc;

            if (trackfile.Id > 0)
            {
                _mediaFileService.Update(trackfile);
            }
        }

        public void RemoveAllTags(string path)
        {
            TagLib.File file = null;
            try
            {
                file = TagLib.File.Create(path);
                file.RemoveTags(TagLib.TagTypes.AllTags);
                file.Save();
            }
            catch (CorruptFileException ex)
            {
                _logger.Warn(ex, $"Tag removal failed for {path}.  File is corrupt");
            }
            catch (Exception ex)
            {
                _logger.ForWarnEvent()
                    .Exception(ex)
                    .Message($"Tag removal failed for {path}")
                    .WriteSentryWarn("Tag removal failed")
                    .Log();
            }
            finally
            {
                file?.Dispose();
            }
        }

        public void WriteTags(BookFile trackfile, bool newDownload, bool force = false)
        {
            if (!force)
            {
                if (_configService.WriteAudioTags == WriteAudioTagsType.No ||
                    (_configService.WriteAudioTags == WriteAudioTagsType.NewFiles && !newDownload))
                {
                    return;
                }
            }

            // One copy each (2026-09-20): an adopted original is never written into unless the
            // entry opts in (AdoptedTagWrite); a retag's force does not override that.
            if (trackfile.Adopted && !trackfile.Author.Value.AdoptedTagWrite)
            {
                _logger.Debug("{0}: adopted original, tags left alone", trackfile);
                return;
            }

            var newTags = GetTrackMetadata(trackfile);
            var path = trackfile.Path;

            var diff = ReadAudioTag(path).Diff(newTags);

            if (!diff.Any())
            {
                _logger.Debug("No tags update for {0} due to no difference", trackfile);
                return;
            }

            _rootFolderWatchingService.ReportFileSystemChangeBeginning(path);

            // a scrub never applies to an additive write: it would take the very fields the
            // additive write leaves alone
            var scrub = _configService.ScrubAudioTags && !newTags.Additive;

            if (scrub)
            {
                _logger.Debug($"Scrubbing tags for {trackfile}");
                RemoveAllTags(path);
            }
            else if (_configService.ScrubAudioTags)
            {
                _logger.Debug("{0}: additive tag write, scrub skipped", trackfile);
            }

            _logger.Debug($"Writing tags for {trackfile}");

            newTags.Write(path);

            UpdateTrackfileSizeAndModified(trackfile, path);

            _eventAggregator.PublishEvent(new BookFileRetaggedEvent(trackfile.Author.Value, trackfile, diff, scrub));
        }

        public void SyncTags(List<Edition> editions)
        {
            if (_configService.WriteAudioTags != WriteAudioTagsType.Sync)
            {
                return;
            }

            var toSync = new List<BookFile>();

            // get the tracks to update
            foreach (var edition in editions)
            {
                var bookFiles = edition.BookFiles.Value;

                _logger.Debug($"Syncing audio tags for {bookFiles.Count} files");

                foreach (var file in bookFiles.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))))
                {
                    // populate tracks (which should also have release/book/author set) because
                    // not all of the updates will have been committed to the database yet
                    file.Edition = edition;

                    // ABS titles match calibre (2026-09-23): every Audiobooks-homed row -- adopted or
                    // grabbed -- goes to the ABS sync; only an adopted original skips the file write,
                    // since Mangarr never writes into it.
                    if (file.Home == FileHome.Audiobooks)
                    {
                        toSync.Add(file);
                    }

                    if (file.Adopted)
                    {
                        continue;
                    }

                    WriteTags(file, false);
                }
            }

            SyncAdopted(toSync);
        }

        // One copy each (2026-09-20); ABS titles match calibre (2026-09-23): the Audiobooks-homed
        // rows of one entry go to the ABS sync together (one library read per entry), adopted and
        // grabbed alike. A retag of one is the ABS sync; with the entry's opt-in (AdoptedTagWrite) an
        // adopted row is written too, on top of it -- ABS reads its own metadata, not the file's
        // tags, so the sync is never skipped for the write.
        private void SyncAdopted(List<BookFile> toSync)
        {
            foreach (var group in toSync.GroupBy(f => f.Edition.Value.Book.Value.Author.Value.Id))
            {
                _adoptedSync.Sync(group.First().Edition.Value.Book.Value.Author.Value, group.ToList(), out _);
            }
        }

        private void RetagOrSync(Author author, List<BookFile> audioFiles)
        {
            var toSync = audioFiles.Where(x => x.Home == FileHome.Audiobooks).ToList();

            if (toSync.Any())
            {
                _adoptedSync.Sync(author, toSync, out _);
            }

            foreach (var file in audioFiles.Where(x => !x.Adopted || author.AdoptedTagWrite))
            {
                WriteTags(file, false, force: true);
            }
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByAuthor(int authorId)
        {
            var files = _mediaFileService.GetFilesByAuthor(authorId);

            return GetPreviews(files).OrderBy(b => b.BookId).ThenBy(b => b.Path).ToList();
        }

        public List<RetagBookFilePreview> GetRetagPreviewsByBook(int bookId)
        {
            var files = _mediaFileService.GetFilesByBook(bookId);

            return GetPreviews(files).OrderBy(b => b.BookId).ThenBy(b => b.Path).ToList();
        }

        private IEnumerable<RetagBookFilePreview> GetPreviews(List<BookFile> files)
        {
            foreach (var f in files.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))).OrderBy(x => x.Edition.Value.Title))
            {
                var file = f;

                if (f.Edition.Value == null)
                {
                    _logger.Warn($"File {f} is not linked to any books");
                    continue;
                }

                var oldTags = ReadAudioTag(f.Path);
                var newTags = GetTrackMetadata(f);
                var diff = oldTags.Diff(newTags);

                if (diff.Any())
                {
                    yield return new RetagBookFilePreview
                    {
                        AuthorId = file.Author.Value.Id,
                        BookId = file.Edition.Value.Id,
                        BookFileId = file.Id,
                        Path = file.Path,
                        Changes = diff
                    };
                }
            }
        }

        public void RetagFiles(RetagFilesCommand message)
        {
            var author = _authorService.GetAuthor(message.AuthorId);
            var bookFiles = _mediaFileService.Get(message.Files);
            var audioFiles = bookFiles.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))).ToList();

            _logger.ProgressInfo("Re-tagging {0} audio files for {1}", audioFiles.Count, author.Name);
            RetagOrSync(author, audioFiles);

            _logger.ProgressInfo("Selected audio files re-tagged for {0}", author.Name);
        }

        public void RetagAuthor(RetagAuthorCommand message)
        {
            _logger.Debug("Re-tagging all audio files for selected authors");
            var authorToRename = _authorService.GetAuthors(message.AuthorIds);

            foreach (var author in authorToRename)
            {
                var bookFiles = _mediaFileService.GetFilesByAuthor(author.Id);
                var audioFiles = bookFiles.Where(x => MediaFileExtensions.AudioExtensions.Contains(Path.GetExtension(x.Path))).ToList();

                _logger.ProgressInfo("Re-tagging all audio files for series: {0}", author.Name);
                RetagOrSync(author, audioFiles);

                _logger.ProgressInfo("All audio files re-tagged for {0}", author.Name);
            }
        }
    }
}
