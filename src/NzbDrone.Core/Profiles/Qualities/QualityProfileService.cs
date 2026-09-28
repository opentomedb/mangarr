using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Books;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.CustomFormats.Events;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.RootFolders;

namespace NzbDrone.Core.Profiles.Qualities
{
    public interface IQualityProfileService
    {
        QualityProfile Add(QualityProfile profile);
        void Update(QualityProfile profile);
        void Delete(int id);
        List<QualityProfile> All();
        QualityProfile Get(int id);
        bool Exists(int id);
        QualityProfile GetDefaultProfile(string name, Quality cutoff = null, params Quality[] allowed);
    }

    public class QualityProfileService : IQualityProfileService,
                                         IHandle<ApplicationStartedEvent>,
                                         IHandle<CustomFormatAddedEvent>,
                                         IHandle<CustomFormatDeletedEvent>
    {
        private readonly IProfileRepository _profileRepository;
        private readonly IAuthorService _authorService;
        private readonly IImportListFactory _importListFactory;
        private readonly ICustomFormatService _formatService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public QualityProfileService(IProfileRepository profileRepository,
                                     IAuthorService authorService,
                                     IImportListFactory importListFactory,
                                     ICustomFormatService formatService,
                                     IRootFolderService rootFolderService,
                                     IConfigService configService,
                                     Logger logger)
        {
            _configService = configService;
            _profileRepository = profileRepository;
            _authorService = authorService;
            _importListFactory = importListFactory;
            _rootFolderService = rootFolderService;
            _formatService = formatService;
            _logger = logger;
        }

        public QualityProfile Add(QualityProfile profile)
        {
            return _profileRepository.Insert(profile);
        }

        public void Update(QualityProfile profile)
        {
            _profileRepository.Update(profile);
        }

        public void Delete(int id)
        {
            if (_authorService.GetAllAuthors().Any(c => c.QualityProfileId == id) ||
                _importListFactory.All().Any(c => c.ProfileId == id) ||
                _rootFolderService.All().Any(c => c.DefaultQualityProfileId == id))
            {
                var profile = _profileRepository.Get(id);
                throw new QualityProfileInUseException(profile.Name);
            }

            _profileRepository.Delete(id);
        }

        public List<QualityProfile> All()
        {
            return _profileRepository.All().ToList();
        }

        public QualityProfile Get(int id)
        {
            return _profileRepository.Get(id);
        }

        public bool Exists(int id)
        {
            return _profileRepository.Exists(id);
        }

        public void Handle(ApplicationStartedEvent message)
        {
            if (!All().Any())
            {
                _logger.Info("Setting up default quality profiles");

                AddDefaultProfile("Manga",
                    Quality.CBZ,
                    Quality.CBZ,
                    Quality.CBR,
                    Quality.ZIP,
                    Quality.RAR,
                    Quality.PDF);
            }

            // Light novels (2026-09): one profile per format, added to every install, upgraded ones
            // included. Beta readiness (2026-09-28, F7): once. It used to run on every start, matched by
            // name, so a deleted or renamed profile came back; now the first start that runs it sets
            // LightNovelProfilesSeeded. An existing install (which already has both, from the last start)
            // sets the flag the first time it starts this build, and nothing is added.
            if (!_configService.LightNovelProfilesSeeded)
            {
                EnsureDefaultProfile("Light Novel EPUB", Quality.EPUB, Quality.EPUB, Quality.AZW3);
                EnsureDefaultProfile("Light Novel Audio", Quality.M4B, Quality.M4B, Quality.FLAC, Quality.MP3, Quality.UnknownAudio);

                _configService.LightNovelProfilesSeeded = true;
            }
        }

        private void EnsureDefaultProfile(string name, Quality cutoff, params Quality[] allowed)
        {
            if (All().Any(p => p.Name == name))
            {
                return;
            }

            _logger.Info("Adding quality profile {0}", name);

            var profile = GetDefaultProfile(name, cutoff, allowed);
            profile.UpgradeAllowed = true;
            Add(profile);
        }

        public void Handle(CustomFormatAddedEvent message)
        {
            var all = All();

            foreach (var profile in all)
            {
                profile.FormatItems.Insert(0, new ProfileFormatItem
                {
                    Score = 0,
                    Format = message.CustomFormat
                });

                Update(profile);
            }
        }

        public void Handle(CustomFormatDeletedEvent message)
        {
            var all = All();
            foreach (var profile in all)
            {
                profile.FormatItems = profile.FormatItems.Where(c => c.Format.Id != message.CustomFormat.Id).ToList();

                if (profile.FormatItems.Empty())
                {
                    profile.MinFormatScore = 0;
                    profile.CutoffFormatScore = 0;
                }

                Update(profile);
            }
        }

        public QualityProfile GetDefaultProfile(string name, Quality cutoff = null, params Quality[] allowed)
        {
            var groupedQualites = Quality.DefaultQualityDefinitions.GroupBy(q => q.GroupWeight);
            var items = new List<QualityProfileQualityItem>();
            var groupId = 1000;
            var profileCutoff = cutoff == null ? Quality.Unknown.Id : cutoff.Id;

            foreach (var group in groupedQualites)
            {
                if (group.Count() == 1)
                {
                    var quality = group.First().Quality;
                    items.Add(new QualityProfileQualityItem { Quality = quality, Allowed = allowed.Contains(quality) });
                    continue;
                }

                var groupAllowed = group.Any(g => allowed.Contains(g.Quality));

                items.Add(new QualityProfileQualityItem
                {
                    Id = groupId,
                    Name = group.First().GroupName,
                    Items = group.Select(g => new QualityProfileQualityItem
                    {
                        Quality = g.Quality,
                        Allowed = groupAllowed
                    }).ToList(),
                    Allowed = groupAllowed
                });

                if (group.Any(s => s.Quality.Id == profileCutoff))
                {
                    profileCutoff = groupId;
                }

                groupId++;
            }

            var formatItems = _formatService.All().Select(format => new ProfileFormatItem
            {
                Score = 0,
                Format = format
            }).ToList();

            var qualityProfile = new QualityProfile
            {
                Name = name,
                Cutoff = profileCutoff,
                Items = items,
                MinFormatScore = 0,
                CutoffFormatScore = 0,
                FormatItems = formatItems
            };

            return qualityProfile;
        }

        private QualityProfile AddDefaultProfile(string name, Quality cutoff, params Quality[] allowed)
        {
            var profile = GetDefaultProfile(name, cutoff, allowed);

            return Add(profile);
        }
    }
}
