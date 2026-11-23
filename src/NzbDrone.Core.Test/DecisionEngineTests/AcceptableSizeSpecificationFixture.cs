using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // The size floor (2026-11-14): a release is measured against its quality definition's
    // MinSize / MaxSize (MB, the Quality Definitions sliders). A 5 MB "volume" is a post with
    // nothing in it, never a book.
    [TestFixture]
    public class AcceptableSizeSpecificationFixture : CoreTest<AcceptableSizeSpecification>
    {
        private RemoteBook _remoteBook;

        [SetUp]
        public void Setup()
        {
            _remoteBook = new RemoteBook
            {
                Release = new ReleaseInfo(),
                ParsedBookInfo = new ParsedBookInfo { Quality = new QualityModel(Quality.CBZ) }
            };
        }

        private void WithDefinition(double? minSize, double? maxSize)
        {
            Mocker.GetMock<IQualityDefinitionService>()
                  .Setup(s => s.Get(Quality.CBZ))
                  .Returns(new QualityDefinition(Quality.CBZ) { MinSize = minSize, MaxSize = maxSize });
        }

        private void WithSizeMb(double megabytes)
        {
            _remoteBook.Release.Size = (long)(megabytes * 1024 * 1024);
        }

        [Test]
        public void should_accept_when_the_size_is_unknown()
        {
            WithDefinition(10, null);
            WithSizeMb(0);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_no_minimum_is_set()
        {
            WithDefinition(null, null);
            WithSizeMb(1);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_the_minimum_is_zero()
        {
            WithDefinition(0, null);
            WithSizeMb(1);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_release_under_the_minimum()
        {
            WithDefinition(10, null);
            WithSizeMb(5);

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("smaller than the minimum");
        }

        [Test]
        public void should_accept_a_release_at_the_minimum()
        {
            WithDefinition(10, null);
            WithSizeMb(10);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_release_above_the_minimum()
        {
            WithDefinition(10, null);
            WithSizeMb(227);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_release_over_the_maximum()
        {
            WithDefinition(10, 500);
            WithSizeMb(501);

            var decision = Subject.IsSatisfiedBy(_remoteBook, null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Contain("larger than the maximum");
        }

        [Test]
        public void should_accept_a_release_at_the_maximum()
        {
            WithDefinition(10, 500);
            WithSizeMb(500);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_treat_a_zero_maximum_as_unlimited()
        {
            WithDefinition(10, 0);
            WithSizeMb(5000);

            Subject.IsSatisfiedBy(_remoteBook, null).Accepted.Should().BeTrue();
        }
    }
}
