using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Configuration
{
    [TestFixture]
    public class ConfigServiceFixture : TestBase<ConfigService>
    {
        [SetUp]
        public void SetUp()
        {
        }

        [Test]
        public void Add_new_value_to_database()
        {
            const string key = "RssSyncInterval";
            const int value = 12;

            Subject.RssSyncInterval = value;

            AssertUpsert(key, value);
        }

        [Test]
        public void Get_value_should_return_default_when_no_value()
        {
            Subject.RssSyncInterval.Should().Be(15);
        }

        [Test]
        public void get_value_with_persist_should_store_default_value()
        {
            var salt = Subject.HmacSalt;
            salt.Should().NotBeNullOrWhiteSpace();
            AssertUpsert("HmacSalt", salt);
        }

        [Test]
        public void get_value_with_out_persist_should_not_store_default_value()
        {
            var interval = Subject.RssSyncInterval;
            interval.Should().Be(15);
            Mocker.GetMock<IConfigRepository>().Verify(c => c.Insert(It.IsAny<Config>()), Times.Never());
        }

        // Light-novel storage (2026-09-22): nothing stored = the entry-folder homes and blank connections.
        [Test]
        public void light_novel_storage_keys_default_to_the_entry_folder_and_blank()
        {
            Subject.LightNovelEbookHome.Should().Be("entry");
            Subject.LightNovelAudioHome.Should().Be("entry");
            Subject.CalibreLibrary.Should().BeEmpty();
            Subject.CalibreUsername.Should().BeEmpty();
            Subject.CalibrePassword.Should().BeEmpty();
            Subject.CalibreRemotePath.Should().BeEmpty();
            Subject.CalibreLocalPath.Should().BeEmpty();
            Subject.AudiobookshelfRemotePath.Should().BeEmpty();
            Subject.AudiobookshelfLocalPath.Should().BeEmpty();
        }

        // Light-novel storage (2026-09-22): no install's addresses are code defaults any more
        // (migration 055 carries the dev server's over); the OpenTome link is the public contribute page.
        [Test]
        public void no_personal_defaults_remain()
        {
            Subject.CalibreContentServerUrl.Should().BeEmpty();
            Subject.AudiobookshelfUrl.Should().BeEmpty();
            Subject.AudiobookshelfLibraryId.Should().BeEmpty();
            Subject.OpenTomeUrl.Should().Be("https://opentomedb.com/contribute/");
        }

        [Test]
        public void preferred_edition_languages_default_to_english()
        {
            Subject.PreferredEditionLanguages.Should().Be("en");
        }

        // Preferred Edition (2026-09-24, final fix round Minor 3): an old tab's UI save carries no chain (null);
        // the stored chain is kept, not overwritten or cleared.
        [Test]
        public void saving_a_null_preferred_edition_chain_keeps_the_stored_one()
        {
            Mocker.GetMock<IConfigRepository>().Setup(r => r.All()).Returns(new List<Config> { new Config { Key = "preferrededitionlanguages", Value = "fr,en" } });

            Subject.SaveConfigDictionary(new Dictionary<string, object> { { "PreferredEditionLanguages", null } });

            Mocker.GetMock<IConfigRepository>().Verify(c => c.Upsert("preferrededitionlanguages", It.IsAny<string>()), Times.Never());
            Subject.PreferredEditionLanguages.Should().Be("fr,en");
        }

        private void AssertUpsert(string key, object value)
        {
            Mocker.GetMock<IConfigRepository>().Verify(c => c.Upsert(key.ToLowerInvariant(), value.ToString()));
        }

        [Test]
        [Description("This test will use reflection to ensure each config property read/writes to a unique key")]
        public void config_properties_should_write_and_read_using_same_key()
        {
            var configProvider = Subject;
            var allProperties = typeof(ConfigService).GetProperties().Where(p => p.GetSetMethod() != null).ToList();

            var keys = new List<string>();
            var values = new List<Config>();

            Mocker.GetMock<IConfigRepository>().Setup(c => c.Upsert(It.IsAny<string>(), It.IsAny<string>())).Callback<string, string>((key, value) =>
            {
                keys.Add(key);
                values.Add(new Config { Key = key, Value = value });
            });

            Mocker.GetMock<IConfigRepository>().Setup(c => c.All()).Returns(values);

            foreach (var propertyInfo in allProperties)
            {
                object value = null;

                if (propertyInfo.PropertyType == typeof(string))
                {
                    value = Guid.NewGuid().ToString();
                }
                else if (propertyInfo.PropertyType == typeof(int))
                {
                    value = DateTime.Now.Millisecond;
                }
                else if (propertyInfo.PropertyType == typeof(bool))
                {
                    value = true;
                }
                else if (propertyInfo.PropertyType.BaseType == typeof(Enum))
                {
                    value = 0;
                }

                propertyInfo.GetSetMethod().Invoke(configProvider, new[] { value });
                var returnValue = propertyInfo.GetGetMethod().Invoke(configProvider, null);

                if (propertyInfo.PropertyType.BaseType == typeof(Enum))
                {
                    returnValue = (int)returnValue;
                }

                returnValue.Should().Be(value, propertyInfo.Name);
            }

            keys.Should().OnlyHaveUniqueItems();
        }

        [Test]
        public void should_ignore_null_properties()
        {
            Mocker.GetMock<IConfigRepository>()
                  .Setup(v => v.Get("downloadedepisodesfolder"))
                  .Returns(new Config { Id = 1, Key = "DownloadedEpisodesFolder", Value = @"C:\test".AsOsAgnostic() });

            var dict = new Dictionary<string, object>();
            dict.Add("DownloadedEpisodesFolder", null);
            Subject.SaveConfigDictionary(dict);

            Mocker.GetMock<IConfigRepository>().Verify(c => c.Upsert("downloadedepisodesfolder", It.IsAny<string>()), Times.Never());
        }
    }
}
