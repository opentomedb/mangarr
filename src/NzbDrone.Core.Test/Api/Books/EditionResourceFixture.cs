using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Test.Common;
using Readarr.Api.V1.Books;

namespace NzbDrone.Core.Test.Api.Books
{
    // Volumes built from chapter packs (2026-10-21): the volume page labels a chapter-built file with
    // the range its edition lists, so the edition resource carries the chapter list both ways.
    [TestFixture]
    public class EditionResourceFixture : TestBase
    {
        [Test]
        public void the_chapter_list_goes_out_and_comes_back()
        {
            var edition = new Edition { Id = 7, Chapters = new List<double> { 72, 73, 73.5, 74 } };

            var resource = edition.ToResource();

            resource.Chapters.Should().Equal(72, 73, 73.5, 74);
            resource.ToModel().Chapters.Should().Equal(72, 73, 73.5, 74);
        }

        [Test]
        public void an_unknown_chapter_list_stays_unknown()
        {
            new Edition { Id = 7 }.ToResource().Chapters.Should().BeNull();
        }
    }
}
