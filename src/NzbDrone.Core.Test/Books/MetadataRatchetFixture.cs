using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaCover;

namespace NzbDrone.Core.Test.BookTests
{
    // UseMetadataFrom acts as a ratchet for per-volume facts: a refresh where a source comes back
    // empty must never erase a page count / release date / ISBN we already know.
    [TestFixture]
    public class MetadataRatchetFixture
    {
        [Test]
        public void edition_keeps_known_values_when_remote_is_empty()
        {
            var local = new Edition { PageCount = 192, ReleaseDate = new DateTime(2016, 7, 26), Isbn13 = "9780316315319" };
            var remote = new Edition();

            local.UseMetadataFrom(remote);

            local.PageCount.Should().Be(192);
            local.ReleaseDate.Should().Be(new DateTime(2016, 7, 26));
            local.Isbn13.Should().Be("9780316315319");
        }

        [Test]
        public void edition_adopts_remote_values_when_present()
        {
            var local = new Edition();
            var remote = new Edition { PageCount = 208, ReleaseDate = new DateTime(2020, 1, 1), Isbn13 = "9781975383661" };

            local.UseMetadataFrom(remote);

            local.PageCount.Should().Be(208);
            local.ReleaseDate.Should().Be(new DateTime(2020, 1, 1));
            local.Isbn13.Should().Be("9781975383661");
        }

        [Test]
        public void book_keeps_known_release_date_when_remote_is_null()
        {
            var local = new Book { ReleaseDate = new DateTime(2016, 7, 26) };
            var remote = new Book();

            local.UseMetadataFrom(remote);

            local.ReleaseDate.Should().Be(new DateTime(2016, 7, 26));
        }

        [Test]
        public void book_adopts_remote_release_date_when_present()
        {
            var local = new Book();
            var remote = new Book { ReleaseDate = new DateTime(2021, 3, 2) };

            local.UseMetadataFrom(remote);

            local.ReleaseDate.Should().Be(new DateTime(2021, 3, 2));
        }

        // The AniList binding (D4) ratchets the same way: a refresh that resolved nothing must
        // never un-bind an entry; a resolved id replaces the stored one.
        [Test]
        public void author_metadata_keeps_its_anilist_binding_when_remote_has_none()
        {
            var local = new AuthorMetadata { Name = "Fairy Tail", AniListId = 30598 };
            var remote = new AuthorMetadata { Name = "Fairy Tail" };

            local.UseMetadataFrom(remote);

            local.AniListId.Should().Be(30598);
        }

        [Test]
        public void author_metadata_adopts_a_remote_anilist_binding()
        {
            var local = new AuthorMetadata { Name = "Fairy Tail", AniListId = 128087 };
            var remote = new AuthorMetadata { Name = "Fairy Tail", AniListId = 30598 };

            local.UseMetadataFrom(remote);

            local.AniListId.Should().Be(30598);
        }

        // D3 (2026-09-16): the "<Series>, volume N." placeholder older builds minted is not local
        // data — a real description replaces it, an empty remote clears it, a real one is kept.
        [Test]
        public void edition_replaces_the_placeholder_with_a_real_description()
        {
            var local = new Edition { Title = "Fairy Tail Vol. 1", Overview = "Fairy Tail, volume 1." };
            var remote = new Edition { Title = "Fairy Tail Vol. 1", Overview = "Created by manga-ka Hiro Mashima of Rave Master fame, FAIRY TAIL takes place in a unique magical world." };

            local.UseMetadataFrom(remote);

            local.Overview.Should().StartWith("Created by manga-ka");
        }

        [Test]
        public void edition_clears_the_placeholder_when_remote_is_empty()
        {
            var local = new Edition { Title = "Fairy Tail Vol. 32", Overview = "Fairy Tail, volume 32." };
            var remote = new Edition { Title = "Fairy Tail Vol. 32" };

            local.UseMetadataFrom(remote);

            local.Overview.Should().BeEmpty();
        }

        [Test]
        public void edition_keeps_a_real_description_when_remote_is_empty()
        {
            var local = new Edition { Title = "Berserk Vol. 3", Overview = "Guts joins the Band of the Hawk and its charismatic leader Griffith, volume 3." };
            var remote = new Edition { Title = "Berserk Vol. 3" };

            local.UseMetadataFrom(remote);

            local.Overview.Should().Be("Guts joins the Band of the Hawk and its charismatic leader Griffith, volume 3.");
        }

        // Finding #4 (2026-09-16 review): a stored text that fails D2 — what older builds stored
        // unvalidated — is not local data either: cleared by an empty remote, replaced by a real one.
        [TestCase("**************Note: This is Notebook Not Manga Volume. Lined pages for your own ideas.")]
        [TestCase("Fire Force Notebook Cover Arts Designed Happy Birthday Gifts, 120 lined pages.")]
        [TestCase("TV・CMなどで活躍中の人気アイドル木村好珠ちゃんとのコラボ写真集。第1巻。")]
        [TestCase("Too short to be a blurb.")]
        public void edition_clears_a_stored_description_that_fails_validation_when_remote_is_empty(string stored)
        {
            var local = new Edition { Title = "Fire Force Vol. 14", Overview = stored };
            var remote = new Edition { Title = "Fire Force Vol. 14" };

            local.UseMetadataFrom(remote);

            local.Overview.Should().BeEmpty();
        }

        // Preferred Edition (2026-09-24, M6b pre-review fix): a stored blurb in the edition's own CJK
        // language is that edition's -- kept on a pass where Google returned no overview. The English
        // rule (the cases above: null language) is unchanged, and "eng" is the English rule too.
        [TestCase("jpn", "日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。")]
        [TestCase("kor", "괴수 8호의 힘을 얻은 카프카는 방위대에 입대하기 위해 다시 한번 시험에 도전하게 되는데, 그 앞에 새로운 위협이 나타난다.")]
        [TestCase("zho", "日比野卡夫卡夢想加入防衛隊，但一隻神秘的生物改變了一切。他的人生從此走上了完全不同的道路，故事就此展開。")]
        [TestCase("fra", "Dans son journal, Kafka raconte comment une étrange créature a bouleversé sa vie de nettoyeur.")]
        public void an_edition_keeps_a_stored_blurb_in_its_own_language_when_remote_is_empty(string language, string stored)
        {
            var local = new Edition { Title = "Kaiju No. 8 Tome 3", Language = language, Overview = stored };
            var remote = new Edition { Title = "Kaiju No. 8 Tome 3", Language = language };

            local.UseMetadataFrom(remote);

            local.Overview.Should().Be(stored);
        }

        [Test]
        public void an_english_edition_still_clears_a_stored_cjk_blurb_when_remote_is_empty()
        {
            var local = new Edition { Title = "Kaiju No. 8 Vol. 3", Language = "eng", Overview = "日比野カフカは防衛隊への入隊を夢見ていたが、謎の生物がすべてを変えてしまう。そして物語が始まる。" };
            var remote = new Edition { Title = "Kaiju No. 8 Vol. 3", Language = "eng" };

            local.UseMetadataFrom(remote);

            local.Overview.Should().BeEmpty();
        }

        [Test]
        public void edition_replaces_a_stored_description_that_fails_validation_with_a_real_one()
        {
            var local = new Edition { Title = "Fire Force Vol. 14", Overview = "Fire Force Notebook Cover Arts Designed Happy Birthday Gifts, 120 lined pages." };
            var remote = new Edition { Title = "Fire Force Vol. 14", Overview = "Shinra and Company 8 push deeper into the Nether as the Evangelist's plan comes into focus." };

            local.UseMetadataFrom(remote);

            local.Overview.Should().StartWith("Shinra and Company 8");
        }

        [Test]
        public void edition_keeps_an_english_description_that_passes_validation_when_remote_is_empty()
        {
            var local = new Edition { Title = "Attack on Titan Vol. 22", Overview = "The three notebooks in Eren's basement hold the truth about the Titans and the world beyond the walls." };
            var remote = new Edition { Title = "Attack on Titan Vol. 22" };

            local.UseMetadataFrom(remote);

            local.Overview.Should().StartWith("The three notebooks");
        }

        // Finding #2: the cover ratchet BookInfoProxy relies on when it mints no image for a volume
        // Google did not answer for.
        [Test]
        public void edition_keeps_its_images_when_remote_has_none()
        {
            var local = new Edition { Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://books.google.com/books/content?id=S7qCngEACAAJ&printsec=frontcover&img=1&zoom=4") } };
            var remote = new Edition();

            local.UseMetadataFrom(remote);

            local.Images.Should().ContainSingle(i => i.Url.EndsWith("zoom=4"));
        }

        [Test]
        public void edition_adopts_remote_images_when_present()
        {
            var local = new Edition { Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://s4.anilist.co/bx30598.jpg") } };
            var remote = new Edition { Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://uploads.mangadex.org/covers/227e3f72/v1.jpg") } };

            local.UseMetadataFrom(remote);

            local.Images.Should().ContainSingle(i => i.Url.Contains("mangadex"));
        }

        // D5 (2026-09-17): the ISBN record an earlier pass took the blurb from no longer passes
        // (its title does not name the series) — the stored text came from it and goes; the
        // poster the proxy minted replaces the cover it gave.
        [Test]
        public void a_rejected_remote_overview_clears_the_local_text()
        {
            var local = new Edition { Title = "Fairy Tail Vol. 24", Overview = new string('y', 200), Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://books.google.com/books/publisher/content?id=WWWQEAAAQBAJ&zoom=4") } };
            var remote = new Edition { Title = "Fairy Tail Vol. 24", Overview = null, OverviewRejected = true, Images = new List<MediaCover.MediaCover> { new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://poster/ft.jpg") } };

            local.UseMetadataFrom(remote);

            local.Overview.Should().BeEmpty();
            local.Images.Should().ContainSingle(i => i.Url == "https://poster/ft.jpg");
        }

        [Test]
        public void an_empty_remote_overview_that_is_not_rejected_keeps_the_local_text()
        {
            var local = new Edition { Title = "Fairy Tail Vol. 24", Overview = new string('y', 200) };
            var remote = new Edition { Title = "Fairy Tail Vol. 24", Overview = null, OverviewRejected = false };

            local.UseMetadataFrom(remote);

            local.Overview.Should().Be(new string('y', 200));
        }

        // The flag is never stored, so a loaded edition always carries false: it must not take part
        // in the up-to-date check, or a rejected volume would be re-written on every refresh.
        [Test]
        public void the_rejected_flag_does_not_make_an_otherwise_equal_edition_stale()
        {
            new Edition { Title = "Fairy Tail Vol. 24", Overview = string.Empty }
                .Equals(new Edition { Title = "Fairy Tail Vol. 24", Overview = string.Empty, OverviewRejected = true })
                .Should().BeTrue();
        }

        // Audiobook identity (2026-09-17, D1): the ASIN and the Audible-shaped fields are set only
        // by a pass Audible answered. A Google/ISBN-only refresh carries none of them, and must not
        // erase an identity we already hold; a pass that did answer replaces it.
        [Test]
        public void edition_keeps_its_audiobook_identity_when_remote_has_none()
        {
            var local = new Edition
            {
                Asin = "1975337182",
                AudiobookTitle = "Sword Art Online 1: Aincrad",
                AudiobookSubtitle = "Light Novel",
                RuntimeMinutes = 484,
                AudioReleaseDate = new DateTime(2020, 4, 28),
                CoveredByVolume = 1
            };
            var remote = new Edition();

            local.UseMetadataFrom(remote);

            local.Asin.Should().Be("1975337182");
            local.AudiobookTitle.Should().Be("Sword Art Online 1: Aincrad");
            local.AudiobookSubtitle.Should().Be("Light Novel");
            local.RuntimeMinutes.Should().Be(484);
            local.AudioReleaseDate.Should().Be(new DateTime(2020, 4, 28));
            local.CoveredByVolume.Should().Be(1);
        }

        [Test]
        public void edition_keeps_its_audiobook_identity_when_remote_strings_are_blank()
        {
            var local = new Edition { Asin = "1975337182", AudiobookTitle = "Sword Art Online 1: Aincrad", AudiobookSubtitle = "Light Novel" };
            var remote = new Edition { Asin = " ", AudiobookTitle = string.Empty, AudiobookSubtitle = " " };

            local.UseMetadataFrom(remote);

            local.Asin.Should().Be("1975337182");
            local.AudiobookTitle.Should().Be("Sword Art Online 1: Aincrad");
            local.AudiobookSubtitle.Should().Be("Light Novel");
        }

        [Test]
        public void edition_adopts_a_remote_audiobook_identity_when_present()
        {
            var local = new Edition
            {
                Asin = "B0OLDASIN0",
                AudiobookTitle = "old title",
                AudiobookSubtitle = "old subtitle",
                RuntimeMinutes = 1,
                AudioReleaseDate = new DateTime(2001, 1, 1),
                CoveredByVolume = 9
            };
            var remote = new Edition
            {
                Asin = "1975337182",
                AudiobookTitle = "Sword Art Online 1: Aincrad",
                AudiobookSubtitle = "Light Novel",
                RuntimeMinutes = 484,
                AudioReleaseDate = new DateTime(2020, 4, 28),
                CoveredByVolume = 1
            };

            local.UseMetadataFrom(remote);

            local.Asin.Should().Be("1975337182");
            local.AudiobookTitle.Should().Be("Sword Art Online 1: Aincrad");
            local.AudiobookSubtitle.Should().Be("Light Novel");
            local.RuntimeMinutes.Should().Be(484);
            local.AudioReleaseDate.Should().Be(new DateTime(2020, 4, 28));
            local.CoveredByVolume.Should().Be(1);
        }

        // A zero runtime is a value, not an absence: only null keeps the local one.
        [Test]
        public void edition_adopts_a_zero_remote_runtime()
        {
            var local = new Edition { RuntimeMinutes = 484 };
            var remote = new Edition { RuntimeMinutes = 0 };

            local.UseMetadataFrom(remote);

            local.RuntimeMinutes.Should().Be(0);
        }

        [Test]
        public void edition_keeps_covered_by_volume_when_remote_is_null()
        {
            var local = new Edition { CoveredByVolume = 3.5 };
            var remote = new Edition { CoveredByVolume = null };

            local.UseMetadataFrom(remote);

            local.CoveredByVolume.Should().Be(3.5);
        }

        [Test]
        public void edition_replaces_covered_by_volume_when_remote_has_one()
        {
            var local = new Edition { CoveredByVolume = 3.5 };
            var remote = new Edition { CoveredByVolume = 4 };

            local.UseMetadataFrom(remote);

            local.CoveredByVolume.Should().Be(4);
        }

        // B2 (2026-09-17, D4a): a covered mark has one owner. A pass Audible answered asserts the
        // remote's mark (or its absence) over an "audible" mark; an "import" mark is never touched
        // by a refresh. A pass Audible did not answer keeps the local mark and takes a remote one.
        [Test]
        public void audible_asserted_replaces_an_audible_mark()
        {
            var local = AudioEdition(coveredBy: 1, source: CoveredSources.Audible);
            var remote = AudioEdition(coveredBy: 3, source: CoveredSources.Audible);
            remote.CoveredAsserted = true;

            local.UseMetadataFrom(remote);

            local.CoveredByVolume.Should().Be(3);
            local.CoveredSource.Should().Be(CoveredSources.Audible);
        }

        [Test]
        public void audible_asserted_null_clears_an_audible_mark()
        {
            var local = AudioEdition(coveredBy: 1, source: CoveredSources.Audible);
            var remote = AudioEdition(coveredBy: null, source: null);
            remote.CoveredAsserted = true;

            local.UseMetadataFrom(remote);

            local.CoveredByVolume.Should().BeNull();
            local.CoveredSource.Should().BeNull();
        }

        [Test]
        public void audible_asserted_never_touches_an_import_mark()
        {
            var local = AudioEdition(coveredBy: 1, source: CoveredSources.Import);
            var remote = AudioEdition(coveredBy: null, source: null);
            remote.CoveredAsserted = true;

            local.UseMetadataFrom(remote);

            local.CoveredByVolume.Should().Be(1);
            local.CoveredSource.Should().Be(CoveredSources.Import);
        }

        [Test]
        public void not_asserted_keeps_the_local_mark_and_takes_a_remote_one()
        {
            var kept = AudioEdition(coveredBy: 1, source: CoveredSources.Import);
            kept.UseMetadataFrom(AudioEdition(coveredBy: null, source: null));
            kept.CoveredByVolume.Should().Be(1);
            kept.CoveredSource.Should().Be(CoveredSources.Import);

            var taken = AudioEdition(coveredBy: null, source: null);
            taken.UseMetadataFrom(AudioEdition(coveredBy: 2, source: null));   // a silent pass never mints one, but the rule is total
            taken.CoveredByVolume.Should().Be(2);
            taken.CoveredSource.Should().Be(CoveredSources.Audible);
        }

        // B3a (2026-09-17, D4a): a "manual" mark is the user's and is app-owned like an "import" one:
        // no refresh replaces or relabels it, and the db-field carry rides it onto the remote.
        [TestCase(null)]
        [TestCase(3.0)]
        public void audible_asserted_never_touches_a_manual_mark(double? remoteMark)
        {
            var local = AudioEdition(coveredBy: 7, source: CoveredSources.Manual);
            var remote = AudioEdition(coveredBy: remoteMark, source: null);
            remote.CoveredAsserted = true;

            local.UseMetadataFrom(remote);

            local.CoveredByVolume.Should().Be(7);
            local.CoveredSource.Should().Be(CoveredSources.Manual);
        }

        [Test]
        public void a_silent_pass_never_relabels_a_manual_mark()
        {
            var local = AudioEdition(coveredBy: 7, source: CoveredSources.Manual);
            var remote = AudioEdition(coveredBy: null, source: null);

            remote.UseDbFieldsFrom(local);                       // the refresh's carry
            remote.CoveredByVolume.Should().Be(7);
            remote.CoveredSource.Should().Be(CoveredSources.Manual);

            local.UseMetadataFrom(remote);

            local.CoveredSource.Should().Be(CoveredSources.Manual);
        }

        [Test]
        public void use_db_fields_never_carries_an_audible_mark_but_carries_manual_and_import()
        {
            foreach (var source in new[] { CoveredSources.Import, CoveredSources.Manual })
            {
                var remote = AudioEdition(coveredBy: null, source: null);
                remote.UseDbFieldsFrom(AudioEdition(coveredBy: 2, source: source));
                remote.CoveredSource.Should().Be(source);
            }

            var untouched = AudioEdition(coveredBy: null, source: null);
            untouched.UseDbFieldsFrom(AudioEdition(coveredBy: 2, source: CoveredSources.Audible));
            untouched.CoveredByVolume.Should().BeNull();
        }

        private static Edition AudioEdition(double? coveredBy, string source)
        {
            return new Edition { MediaType = MediaType.Audio, CoveredByVolume = coveredBy, CoveredSource = source };
        }

        // I1 (2026-09-17): the refresh partitions on Equals BEFORE the ratchet runs, with only the db
        // fields carried onto the remote. An "import" mark is app-owned data the refresh never
        // changes, so it rides along with Monitored: an unchanged covered row stays UpToDate instead
        // of being rewritten every pass. An "audible" mark is not carried — the remote's own answer
        // decides.
        [Test]
        public void use_db_fields_carries_an_import_mark_onto_the_remote_so_an_unchanged_row_is_up_to_date()
        {
            var local = StoredAudioEdition(coveredBy: 1, source: CoveredSources.Import);
            var remote = RemoteAudioEdition();

            remote.UseDbFieldsFrom(local);

            remote.CoveredByVolume.Should().Be(1);
            remote.CoveredSource.Should().Be(CoveredSources.Import);
            local.Equals(remote).Should().BeTrue();
        }

        [Test]
        public void use_db_fields_never_carries_an_audible_mark()
        {
            var local = StoredAudioEdition(coveredBy: 1, source: CoveredSources.Audible);
            var remote = RemoteAudioEdition();

            remote.UseDbFieldsFrom(local);

            remote.CoveredByVolume.Should().BeNull();
            remote.CoveredSource.Should().BeNull();
            local.Equals(remote).Should().BeFalse();
        }

        // The carried mark then reaches UseMetadataFrom when the row is Updated for another reason
        // (a new title here) on a pass Audible did not answer: it must come back "import", not be
        // re-labelled "audible" — that would hand the next Audible-answering pass a mark it may clear.
        [Test]
        public void a_carried_import_mark_survives_an_updated_row_on_a_silent_pass()
        {
            var local = StoredAudioEdition(coveredBy: 1, source: CoveredSources.Import);
            var remote = RemoteAudioEdition();
            remote.Title = "TBATE Vol. 2 (Audible)";

            remote.UseDbFieldsFrom(local);
            local.Equals(remote).Should().BeFalse();
            local.UseMetadataFrom(remote);

            local.Title.Should().Be("TBATE Vol. 2 (Audible)");
            local.CoveredByVolume.Should().Be(1);
            local.CoveredSource.Should().Be(CoveredSources.Import);
        }

        // B3b (2026-09-18): a pass that could not ask Google (quota) mints no image for the volume,
        // so the remote's Images is empty while the stored row has a cover: Equals failed and the
        // row was rewritten every quota-out pass (the ratchet kept the local images, so only the
        // churn was wrong). The db-field carry rides the local images onto a CoverMissed remote.
        [Test]
        public void use_db_fields_carries_the_local_images_onto_a_cover_missed_remote_so_the_row_is_up_to_date()
        {
            var local = StoredAudioEdition(coveredBy: null, source: null);
            local.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://books.google.com/books/content?id=S7qCngEACAAJ&printsec=frontcover&img=1&zoom=4"));
            var remote = RemoteAudioEdition();
            remote.CoverMissed = true;

            remote.UseDbFieldsFrom(local);

            remote.Images.Should().ContainSingle(i => i.Url.EndsWith("zoom=4"));
            local.Equals(remote).Should().BeTrue();
        }

        [Test]
        public void a_cover_missed_remote_with_its_own_image_keeps_it()
        {
            var local = StoredAudioEdition(coveredBy: null, source: null);
            local.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://books.google.com/books/content?id=S7qCngEACAAJ&printsec=frontcover&img=1&zoom=4"));
            var remote = RemoteAudioEdition();
            remote.CoverMissed = true;
            remote.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://covers.openlibrary.org/b/id/7382174-L.jpg"));

            remote.UseDbFieldsFrom(local);

            remote.Images.Should().ContainSingle(i => i.Url.Contains("openlibrary"));
        }

        // The carry is keyed on the flag alone: a remote minted without an image on a pass that DID
        // ask Google is left as minted, so the row still counts as changed.
        [Test]
        public void a_remote_without_images_that_did_not_miss_google_stays_without_them()
        {
            var local = StoredAudioEdition(coveredBy: null, source: null);
            local.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Cover, "https://books.google.com/books/content?id=S7qCngEACAAJ&printsec=frontcover&img=1&zoom=4"));
            var remote = RemoteAudioEdition();
            remote.CoverMissed = false;

            remote.UseDbFieldsFrom(local);

            remote.Images.Should().BeEmpty();
            local.Equals(remote).Should().BeFalse();
        }

        private static Edition StoredAudioEdition(double? coveredBy, string source)
        {
            var edition = RemoteAudioEdition();
            edition.Id = 7;
            edition.BookId = 3;
            edition.Monitored = true;
            edition.CoveredByVolume = coveredBy;
            edition.CoveredSource = source;
            return edition;
        }

        // What BookInfoProxy mints for a volume Audible does not cover, before the db fields are set.
        private static Edition RemoteAudioEdition()
        {
            return new Edition { MediaType = MediaType.Audio, ForeignEditionId = "local-tbate~ln-v2-audio-ed", Title = "TBATE Vol. 2", CoveredByVolume = null, CoveredSource = null };
        }

        [Test]
        public void book_keeps_its_subtitle_when_remote_has_none()
        {
            var local = new Book { Subtitle = "Aincrad" };
            var remote = new Book { Subtitle = " " };

            local.UseMetadataFrom(remote);

            local.Subtitle.Should().Be("Aincrad");
        }

        [Test]
        public void book_adopts_a_remote_subtitle_when_present()
        {
            var local = new Book { Subtitle = "Aincrad" };
            var remote = new Book { Subtitle = "Fairy Dance" };

            local.UseMetadataFrom(remote);

            local.Subtitle.Should().Be("Fairy Dance");
        }

        // Fix round 3 (2026-09-24, live finding): a subtitle stored before the junk filter existed
        // ("Jobless Reincarnation (Light Novel), Vol. 1" and the like) must not survive a refresh --
        // the pass had candidate text and rejected it, so the stored value is cleared, not kept.
        [Test]
        public void book_clears_a_junk_subtitle_the_remote_pass_rejected()
        {
            var local = new Book { Subtitle = "Jobless Reincarnation (Light Novel), Vol. 1" };
            var remote = new Book { Subtitle = null, SubtitleRejected = true };

            local.UseMetadataFrom(remote);

            local.Subtitle.Should().BeNull();
        }

        [Test]
        public void an_empty_remote_subtitle_that_is_not_rejected_keeps_the_local_text()
        {
            var local = new Book { Subtitle = "Aincrad" };
            var remote = new Book { Subtitle = null, SubtitleRejected = false };

            local.UseMetadataFrom(remote);

            local.Subtitle.Should().Be("Aincrad");
        }

        // The flag is never stored, so a loaded book always carries false: it must not take part in
        // the up-to-date check, or a rejected volume would be re-written on every refresh.
        [Test]
        public void the_subtitle_rejected_flag_does_not_make_an_otherwise_equal_book_stale()
        {
            new Book { Subtitle = null }
                .Equals(new Book { Subtitle = null, SubtitleRejected = true })
                .Should().BeTrue();
        }

        [TestCase("Fairy Tail, volume 1.", "Fairy Tail Vol. 1", true)]
        [TestCase("Kaiju No.8, volume 3.5.", "Kaiju No.8 Vol. 3.5", true)]
        [TestCase("Re:ZERO -Starting Life in Another World-, Chapter 3: Truth of Zero, volume 04.", "Re:ZERO -Starting Life in Another World-, Chapter 3: Truth of Zero Vol. 04", true)]
        [TestCase("Fairy Tail, volume 1", "Fairy Tail Vol. 1", false)]
        [TestCase("Fairy Tail, volume 2.", "Fairy Tail Vol. 1", false)]
        [TestCase("Guts joins the Band of the Hawk, volume 3.", "Berserk Vol. 3", false)]
        [TestCase("", "Berserk Vol. 3", false)]
        [TestCase("Berserk, volume 3.", "Berserk", false)]
        public void placeholder_form_is_exact(string overview, string title, bool placeholder)
        {
            Edition.IsPlaceholderOverview(overview, title).Should().Be(placeholder);
        }
    }
}
