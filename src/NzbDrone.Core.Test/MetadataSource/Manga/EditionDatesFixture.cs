using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MetadataSource.Manga;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource.Manga
{
    // Preferred Edition (2026-09-24, D6): a coarse catalogue date becomes a date + its precision, never
    // Jan 1 (the "Jan 1 = fake" rule) -- past year -> Dec 31; the current year's deposit copy
    // ("published") -> the catalogue's build date; a planned month -> its last day.
    [TestFixture]
    public class EditionDatesFixture : CoreTest
    {
        private static readonly DateTime Today = new DateTime(2026, 9, 24);
        private static readonly DateTime Built = new DateTime(2026, 9, 20, 3, 0, 0, DateTimeKind.Utc);

        private static DateTime Local(int y, int m, int d)
        {
            return DateTime.SpecifyKind(new DateTime(y, m, d), DateTimeKind.Local).ToUniversalTime();
        }

        private static GcdVolume Coarse(string raw, string precision, string type = "published")
        {
            return new GcdVolume { VolumeNumber = 1, ReleaseDateRaw = raw, ReleaseDatePrecision = precision, ReleaseDateType = type };
        }

        [Test]
        public void a_day_date_wins_and_has_no_precision()
        {
            var (date, precision) = EditionDates.Resolve(new GcdVolume { ReleaseDate = "2019-04-02", ReleaseDateRaw = "2019-04-02", ReleaseDatePrecision = "day" }, Built, Today);

            date.Should().Be(Local(2019, 4, 2));
            precision.Should().BeNull();
        }

        [Test]
        public void a_past_year_is_december_31()
        {
            EditionDates.Resolve(Coarse("2019", "year"), Built, Today).Should().Be((Local(2019, 12, 31), EditionDates.Year));
        }

        [Test]
        public void the_current_year_published_is_the_catalogue_build_date()
        {
            EditionDates.Resolve(Coarse("2026", "year"), Built, Today).Should().Be((Local(2026, 9, 20), EditionDates.Year));
        }

        [Test]
        public void the_current_year_of_another_type_is_december_31()
        {
            EditionDates.Resolve(Coarse("2026", "year", "unknown"), Built, Today).Should().Be((Local(2026, 12, 31), EditionDates.Year));
        }

        [Test]
        public void a_projected_month_is_its_last_day()
        {
            EditionDates.Resolve(Coarse("2026-11", "month", "projected"), Built, Today).Should().Be((Local(2026, 11, 30), EditionDates.Month));
        }

        [Test]
        public void a_build_date_of_january_1_is_never_used_as_is()
        {
            EditionDates.Resolve(Coarse("2027", "year"), new DateTime(2027, 1, 1, 2, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 1))
                .Should().Be((Local(2027, 1, 2), EditionDates.Year));
        }

        [TestCase(null, null)]
        [TestCase("20xx", "year")]
        [TestCase("2019-13", "month")]
        [TestCase("2019", "decade")]
        public void anything_else_is_undated(string raw, string precision)
        {
            EditionDates.Resolve(Coarse(raw, precision), Built, Today).Should().Be(((DateTime?)null, (string)null));
        }
    }
}
