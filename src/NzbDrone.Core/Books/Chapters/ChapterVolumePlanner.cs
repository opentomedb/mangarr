using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Books.Chapters
{
    // One volume a chapter release completes: the chapters that go into it, in reading order.
    public class PlannedVolume
    {
        public Book Book { get; set; }
        public List<double> Chapters { get; set; }
        public double First { get; set; }
        public double Last { get; set; }
    }

    // Volumes built from chapter packs (2026-10-21): which volumes one release completes on its own.
    // A volume counts only when its own edition lists its chapters and every whole one is available;
    // a half chapter (n.5) is never required: when available it goes where an edition lists it, else
    // after chapter n. Nothing is borrowed from another edition and nothing is inferred; volumes whose
    // lists share a whole chapter (a catalogue error) are never planned.
    public static class ChapterVolumePlanner
    {
        public static List<PlannedVolume> Plan(IReadOnlyCollection<double> available, IEnumerable<Book> books, MediaType mediaType)
        {
            var have = new HashSet<double>(available.Select(Key));
            var bookList = books.ToList();

            // A half chapter an edition lists goes where it is listed; only one no edition lists falls
            // back to "after chapter n".
            var listedHalves = new HashSet<double>(bookList
                .SelectMany(b => b.EditionOf(mediaType)?.Chapters ?? new List<double>())
                .Where(c => !IsWhole(c))
                .Select(Key));
            var unlistedHalves = available.Where(c => !IsWhole(c) && !listedHalves.Contains(Key(c))).ToList();
            var overlapping = Overlapping(bookList, mediaType);
            var result = new List<PlannedVolume>();

            foreach (var book in bookList)
            {
                var listed = book.EditionOf(mediaType)?.Chapters;

                if (listed == null || listed.Count == 0 || overlapping.Contains(book.Id))
                {
                    continue;
                }

                var whole = listed.Where(IsWhole).Distinct().ToList();

                if (whole.Count == 0 || !whole.All(c => have.Contains(Key(c))))
                {
                    continue;
                }

                var wholeSet = new HashSet<double>(whole);
                var chapters = whole
                    .Concat(listed.Where(c => !IsWhole(c) && have.Contains(Key(c))))
                    .Concat(unlistedHalves.Where(h => wholeSet.Contains(Math.Floor(h))))
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();

                result.Add(new PlannedVolume
                {
                    Book = book,
                    Chapters = chapters,
                    First = chapters.First(),
                    Last = chapters.Last()
                });
            }

            return result;
        }

        // The chapters a release's range makes available: every whole chapter in [start, end], plus
        // start / end themselves when fractional. "c001-073" says nothing about 73.5.
        public static List<double> Available(double start, double end)
        {
            var result = new List<double>();

            for (var c = Math.Ceiling(start); c <= end; c++)
            {
                result.Add(c);
            }

            if (!IsWhole(start))
            {
                result.Add(start);
            }

            if (!IsWhole(end) && end != start)
            {
                result.Add(end);
            }

            return result;
        }

        // Volumes (book ids) whose edition lists a whole chapter another volume's edition also lists: a
        // catalogue error, and which volume the chapter belongs to would be a guess. A shared half chapter
        // is not one (it is placed where listed, never required).
        public static HashSet<int> Overlapping(IEnumerable<Book> books, MediaType mediaType)
        {
            return books
                .SelectMany(b => (b.EditionOf(mediaType)?.Chapters ?? new List<double>())
                    .Where(IsWhole)
                    .Select(Key)
                    .Distinct()
                    .Select(c => (Book: b.Id, Chapter: c)))
                .GroupBy(x => x.Chapter)
                .Where(g => g.Select(x => x.Book).Distinct().Count() > 1)
                .SelectMany(g => g.Select(x => x.Book))
                .ToHashSet();
        }

        public static bool IsEnabled(Author author, IConfigService config)
        {
            return author != null &&
                   author.Library == LibraryType.Manga &&
                   config.BuildVolumesFromChapters &&
                   author.BuildFromChapters;
        }

        private static bool IsWhole(double chapter)
        {
            return chapter == Math.Floor(chapter);
        }

        private static double Key(double chapter)
        {
            return Math.Round(chapter, 2);
        }
    }
}
