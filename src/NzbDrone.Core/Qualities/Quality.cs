using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Qualities
{
    public class Quality : IEmbeddedDocument, IEquatable<Quality>
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public Quality()
        {
        }

        private Quality(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString()
        {
            return Name;
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public bool Equals(Quality other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Id.Equals(other.Id);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return Equals(obj as Quality);
        }

        public static bool operator ==(Quality left, Quality right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Quality left, Quality right)
        {
            return !Equals(left, right);
        }

        public static Quality Unknown => new Quality(0, "Unknown");

        // Manga archive formats. Ids are STABLE (stored in BookFile rows) — reuse the
        // legacy ebook Ids 1-4 and the previously-vacant Id 5; only the names changed.
        public static Quality PDF => new Quality(1, "PDF");
        public static Quality CBZ => new Quality(2, "CBZ");
        public static Quality CBR => new Quality(3, "CBR");
        public static Quality ZIP => new Quality(4, "ZIP");
        public static Quality RAR => new Quality(5, "RAR");

        // Light novels (2026-09): EPUB is a NEW id. The legacy ebook ids 1-4 were reused for the
        // manga archives above and live BookFile rows carry them, so EPUB can never take id 3 back.
        public static Quality EPUB => new Quality(6, "EPUB");

        // AZW3 (2026-09): a light-novel ebook fallback, grabbed only when no EPUB release exists
        // and upgraded once one does. Id 7 was verified unused in the live DB before assignment.
        public static Quality AZW3 => new Quality(7, "AZW3");

        // LN PDF (2026-09-22): a light novel's PDF, the last-resort light-novel ebook (below AZW3),
        // offered unticked. A NEW id, not the manga PDF (1): one Quality means one class
        // everywhere (profiles, history, the UI's class map), and ".pdf" is the manga archive
        // wherever the library is unknown. Id 8 was unused before assignment (Task 6 re-checks
        // the live DB).
        public static Quality EbookPdf => new Quality(8, "Ebook PDF");

        // Audiobook qualities, original Readarr ids. Light-novel audio editions use them (2026-09).
        public static Quality MP3 => new Quality(10, "MP3");
        public static Quality FLAC => new Quality(11, "FLAC");
        public static Quality M4B => new Quality(12, "M4B");
        public static Quality UnknownAudio => new Quality(13, "Unknown Audio");

        static Quality()
        {
            All = new List<Quality>
            {
                Unknown,
                PDF,
                CBZ,
                CBR,
                ZIP,
                RAR,
                EPUB,
                AZW3,
                EbookPdf,
                UnknownAudio,
                MP3,
                M4B,
                FLAC
            };

            AllLookup = new Quality[All.Select(v => v.Id).Max() + 1];
            foreach (var quality in All)
            {
                AllLookup[quality.Id] = quality;
            }

            // Manga archive formats CBZ > CBR > ZIP > RAR > PDF, then the light-novel formats:
            // Ebook PDF < AZW3 < EPUB (the first two are fallbacks, grabbed only when nothing
            // better exists), and the audiobook ladder Unknown Audio < MP3 < FLAC < M4B. Weights
            // only order the Quality settings page; each profile ranks its own allowed set. The
            // INSERTION order here is the order QualityProfileService.GetDefaultProfile builds a
            // new profile's items in.
            // No MaxSize cap (manga volumes vary widely in size; audiobooks even more).
            DefaultQualityDefinitions = new HashSet<QualityDefinition>
            {
                new QualityDefinition(Quality.Unknown)      { Weight = 1, MinSize = 0, MaxSize = null, GroupWeight = 1 },
                new QualityDefinition(Quality.PDF)          { Weight = 5, MinSize = 0, MaxSize = null, GroupWeight = 5 },
                new QualityDefinition(Quality.RAR)          { Weight = 8, MinSize = 0, MaxSize = null, GroupWeight = 8 },
                new QualityDefinition(Quality.ZIP)          { Weight = 10, MinSize = 0, MaxSize = null, GroupWeight = 10 },
                new QualityDefinition(Quality.CBR)          { Weight = 15, MinSize = 0, MaxSize = null, GroupWeight = 15 },
                new QualityDefinition(Quality.CBZ)          { Weight = 20, MinSize = 0, MaxSize = null, GroupWeight = 20 },
                new QualityDefinition(Quality.EbookPdf)     { Weight = 22, MinSize = 0, MaxSize = null, GroupWeight = 22 },
                new QualityDefinition(Quality.AZW3)         { Weight = 23, MinSize = 0, MaxSize = null, GroupWeight = 23 },
                new QualityDefinition(Quality.EPUB)         { Weight = 25, MinSize = 0, MaxSize = null, GroupWeight = 25 },
                new QualityDefinition(Quality.UnknownAudio) { Weight = 30, MinSize = 0, MaxSize = null, GroupWeight = 30 },
                new QualityDefinition(Quality.MP3)          { Weight = 35, MinSize = 0, MaxSize = null, GroupWeight = 35 },
                new QualityDefinition(Quality.FLAC)         { Weight = 40, MinSize = 0, MaxSize = null, GroupWeight = 40 },
                new QualityDefinition(Quality.M4B)          { Weight = 45, MinSize = 0, MaxSize = null, GroupWeight = 45 },
            };
        }

        public static readonly List<Quality> All;

        public static readonly Quality[] AllLookup;

        public static readonly HashSet<QualityDefinition> DefaultQualityDefinitions;

        public static Quality FindById(int id)
        {
            if (id == 0)
            {
                return Unknown;
            }
            else if (id > AllLookup.Length)
            {
                throw new ArgumentException("ID does not match a known quality", nameof(id));
            }

            var quality = AllLookup[id];

            if (quality == null)
            {
                throw new ArgumentException("ID does not match a known quality", nameof(id));
            }

            return quality;
        }

        public static explicit operator Quality(int id)
        {
            return FindById(id);
        }

        public static explicit operator int(Quality quality)
        {
            return quality.Id;
        }
    }
}
