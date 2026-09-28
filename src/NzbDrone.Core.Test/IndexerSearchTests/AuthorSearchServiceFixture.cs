using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
    [TestFixture]
    public class AuthorSearchServiceFixture : CoreTest<AuthorSearchService>
    {
        private Author _author;

        [SetUp]
        public void Setup()
        {
            _author = new Author();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthor(It.IsAny<int>()))
                .Returns(_author);

            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.AuthorSearch(_author.Id, false, true, false))
                .Returns(Task.FromResult(new List<DownloadDecision>()));

            Mocker.GetMock<IProcessDownloadDecisions>()
                .Setup(s => s.ProcessDecisions(It.IsAny<List<DownloadDecision>>()))
                .Returns(Task.FromResult(new ProcessedDecisions(new List<DownloadDecision>(), new List<DownloadDecision>(), new List<DownloadDecision>())));
        }

        [Test]
        public void should_only_include_monitored_books()
        {
            _author.Books = new List<Book>
            {
                new Book { Monitored = false },
                new Book { Monitored = true }
            };

            Subject.Execute(new AuthorSearchCommand { AuthorId = _author.Id, Trigger = CommandTrigger.Manual });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(_author.Id, false, true, false),
                    Times.Exactly(_author.Books.Value.Count(s => s.Monitored)));
        }

        // Final review I1: AuthorSearchCommand.MediaType routes to the typed overload; null is the
        // untyped, every-monitored-class search as before.
        [Test]
        public void typed_author_search_command_searches_that_class_only()
        {
            Mocker.GetMock<ISearchForReleases>()
                .Setup(s => s.AuthorSearch(_author.Id, MediaType.Audio, false, false, false, true))
                .Returns(Task.FromResult(new List<DownloadDecision>()));

            Subject.Execute(new AuthorSearchCommand { AuthorId = _author.Id, MediaType = MediaType.Audio, Trigger = CommandTrigger.Scheduled });

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(_author.Id, MediaType.Audio, false, false, false, true), Times.Once());

            Mocker.GetMock<ISearchForReleases>()
                .Verify(v => v.AuthorSearch(It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never());
        }
    }
}
