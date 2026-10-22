using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Chapters;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.BookTests.Chapters
{
    // Volumes built from chapter packs (2026-10-21): a volume is planned only when every whole
    // chapter its own edition lists is available; half chapters ride along, never required.
    [TestFixture]
    public class ChapterVolumePlannerFixture : CoreTest
    {
        private static readonly (int Volume, int First, int Last)[] Layout =
        {
            (1, 1, 6), (2, 7, 15), (3, 16, 24), (4, 25, 33), (5, 34, 42),
            (6, 43, 52), (7, 53, 61), (8, 62, 71), (9, 72, 81)
        };

        private static Book Volume(int number, List<double> chapters, MediaType mediaType = MediaType.Archive)
        {
            return new Book
            {
                Id = number,
                VolumeNumber = number,
                Editions = new List<Edition> { new Edition { MediaType = mediaType, Chapters = chapters } }
            };
        }

        private static List<double> Range(int first, int last)
        {
            return Enumerable.Range(first, last - first + 1).Select(n => (double)n).ToList();
        }

        private static List<Book> Series()
        {
            return Layout.Select(v => Volume(v.Volume, Range(v.First, v.Last))).ToList();
        }

        [Test]
        public void chapters_1_to_73_plan_the_first_eight_volumes_only()
        {
            var plan = ChapterVolumePlanner.Plan(Range(1, 73), Series(), MediaType.Archive);

            plan.Select(p => p.Book.VolumeNumber).Should().Equal(1, 2, 3, 4, 5, 6, 7, 8);
            plan[0].First.Should().Be(1);
            plan[0].Last.Should().Be(6);
            plan[7].Chapters.Should().Equal(Range(62, 71));
        }

        [Test]
        public void a_missing_whole_chapter_plans_nothing()
        {
            var plan = ChapterVolumePlanner.Plan(new List<double> { 1, 2, 3, 4, 6 }, new List<Book> { Volume(1, Range(1, 6)) }, MediaType.Archive);

            plan.Should().BeEmpty();
        }

        [Test]
        public void an_available_half_chapter_is_placed_after_its_chapter()
        {
            var available = Range(72, 81);
            available.Add(73.5);

            var plan = ChapterVolumePlanner.Plan(available, Series(), MediaType.Archive);

            plan.Should().ContainSingle();
            plan[0].Book.VolumeNumber.Should().Be(9);
            plan[0].Chapters.Should().Equal(72, 73, 73.5, 74, 75, 76, 77, 78, 79, 80, 81);
            plan[0].First.Should().Be(72);
            plan[0].Last.Should().Be(81);
        }

        [Test]
        public void a_listed_half_chapter_is_never_required()
        {
            var chapters = Range(1, 6);
            chapters.Insert(3, 3.5);

            var plan = ChapterVolumePlanner.Plan(Range(1, 6), new List<Book> { Volume(1, chapters) }, MediaType.Archive);

            plan.Should().ContainSingle();
            plan[0].Chapters.Should().Equal(Range(1, 6));
        }

        [Test]
        public void a_half_chapter_goes_to_the_volume_that_lists_it()
        {
            var second = Range(7, 12);
            second.Insert(0, 6.5);
            var books = new List<Book> { Volume(1, Range(1, 6)), Volume(2, second) };
            var available = Range(1, 12);
            available.Add(6.5);

            var plan = ChapterVolumePlanner.Plan(available, books, MediaType.Archive);

            plan[0].Chapters.Should().Equal(Range(1, 6));
            plan[1].Chapters.Should().Equal(6.5, 7, 8, 9, 10, 11, 12);
            plan[1].First.Should().Be(6.5);
        }

        // A catalogue error: Vol. 2 lists chapter 6, which Vol. 1 lists too. Neither is planned; the rest are.
        [Test]
        public void volumes_whose_whole_chapters_overlap_are_never_planned()
        {
            var series = Series();
            series[1] = Volume(2, Range(6, 15));

            ChapterVolumePlanner.Plan(Range(1, 73), series, MediaType.Archive)
                .Select(p => p.Book.VolumeNumber).Should().Equal(3, 4, 5, 6, 7, 8);
            ChapterVolumePlanner.Overlapping(series, MediaType.Archive).Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Test]
        public void a_shared_half_chapter_is_not_an_overlap()
        {
            var series = new List<Book> { Volume(1, new List<double> { 1, 2, 2.5 }), Volume(2, new List<double> { 2.5, 3, 4 }) };

            ChapterVolumePlanner.Overlapping(series, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void a_volume_with_no_chapter_list_is_never_planned()
        {
            var books = new List<Book> { Volume(1, null), Volume(2, new List<double>()) };

            ChapterVolumePlanner.Plan(Range(1, 100), books, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void only_the_edition_of_the_media_type_counts()
        {
            var books = new List<Book> { Volume(1, Range(1, 6), MediaType.Ebook) };

            ChapterVolumePlanner.Plan(Range(1, 6), books, MediaType.Archive).Should().BeEmpty();
        }

        [Test]
        public void the_edition_chapter_list_is_never_mutated()
        {
            var chapters = Range(72, 81);
            var available = Range(72, 81);
            available.Add(73.5);

            ChapterVolumePlanner.Plan(available, new List<Book> { Volume(9, chapters) }, MediaType.Archive);

            chapters.Should().Equal(Range(72, 81));
        }
    }
}
