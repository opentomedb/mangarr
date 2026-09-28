using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Core.Backup;
using NzbDrone.Core.Books.Calibre;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Download;
using NzbDrone.Core.Extras.Metadata.ComicInfo;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Housekeeping;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.MetadataSource.Gcd;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.PdfConversion;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Jobs
{
    public interface ITaskManager
    {
        IList<ScheduledTask> GetPending();
        List<ScheduledTask> GetAll();
        DateTime GetNextExecution(Type type);
    }

    public class TaskManager : ITaskManager, IHandle<ApplicationStartedEvent>, IHandle<CommandExecutedEvent>, IHandleAsync<ConfigSavedEvent>
    {
        private readonly IScheduledTaskRepository _scheduledTaskRepository;
        private readonly IConfigService _configService;
        private readonly IGcdMetadataService _gcdMetadataService;
        private readonly Logger _logger;
        private readonly ICached<ScheduledTask> _cache;

        public TaskManager(IScheduledTaskRepository scheduledTaskRepository, IConfigService configService, IGcdMetadataService gcdMetadataService, ICacheManager cacheManager, Logger logger)
        {
            _scheduledTaskRepository = scheduledTaskRepository;
            _configService = configService;
            _gcdMetadataService = gcdMetadataService;
            _cache = cacheManager.GetCache<ScheduledTask>(GetType());
            _logger = logger;
        }

        public IList<ScheduledTask> GetPending()
        {
            return _cache.Values
                         .Where(c => c.Interval > 0 && c.LastExecution.AddMinutes(c.Interval) < DateTime.UtcNow)
                         .ToList();
        }

        public List<ScheduledTask> GetAll()
        {
            return _cache.Values.ToList();
        }

        public DateTime GetNextExecution(Type type)
        {
            var scheduledTask = _cache.Find(type.FullName);

            return scheduledTask.LastExecution.AddMinutes(scheduledTask.Interval);
        }

        public void Handle(ApplicationStartedEvent message)
        {
            var defaultTasks = new List<ScheduledTask>
                {
                    new ScheduledTask
                    {
                        Interval = 1,
                        TypeName = typeof(RefreshMonitoredDownloadsCommand).FullName,
                        Priority = CommandPriority.High
                    },

                    new ScheduledTask
                    {
                        Interval = 5,
                        TypeName = typeof(MessagingCleanupCommand).FullName
                    },


                    new ScheduledTask
                    {
                        Interval = 6 * 60,
                        TypeName = typeof(CheckHealthCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 24 * 60,
                        TypeName = typeof(RefreshAuthorCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 24 * 60,
                        TypeName = typeof(RescanFoldersCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 24 * 60,
                        TypeName = typeof(HousekeepingCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = GetBackupInterval(),
                        TypeName = typeof(BackupCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 5,
                        TypeName = typeof(ImportListSyncCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = GetRssSyncInterval(),
                        TypeName = typeof(RssSyncCommand).FullName
                    },

                    // Manga: recurring automatic search for monitored, missing volumes. Stock
                    // Readarr ships no scheduled missing search, so ongoing series never auto-filled
                    // gaps (RSS only matches what's already in the feed). 0 disables.
                    new ScheduledTask
                    {
                        Interval = GetBookSearchInterval(),
                        TypeName = typeof(MissingBookSearchCommand).FullName
                    },

                    // Cutoff-unmet upgrades (2026-09-20): stock Readarr never scheduled this search (Sonarr and
                    // Lidarr do), so an owned volume below its profile's cutoff was only ever upgraded by RSS luck.
                    // Weekly, not the missing-search interval (final review I1): an upgrade hunt over volumes we
                    // already hold earns far less indexer load than the missing search, and the two maturing on the
                    // same tick doubled the fan-out. Disabling the missing search (0) disables this one too.
                    new ScheduledTask
                    {
                        Interval = GetBookSearchInterval() == 0 ? 0 : 7 * 24 * 60,
                        TypeName = typeof(CutoffUnmetBookSearchCommand).FullName
                    },

                    // Daily check for a newer GCD metadata artifact on the public GitHub Release; hourly
                    // while no catalogue is loaded (beta readiness F1, GetMetadataUpdateInterval).
                    new ScheduledTask
                    {
                        Interval = GetMetadataUpdateInterval(),
                        TypeName = typeof(MetadataUpdateCommand).FullName
                    },

                    // Manga: daily whole-library PDF->CBZ conversion safety net. The import-time
                    // hook (PdfImportConversionService) converts grabs as they land; this catches
                    // anything that slipped past it. No-op when the library holds no PDFs. Beta
                    // readiness (2026-09-28, F3): daily only with PdfToCbzSweep on (an existing
                    // library, migration 059); otherwise manual only, like the tasks below.
                    new ScheduledTask
                    {
                        Interval = GetPdfSweepInterval(),
                        TypeName = typeof(ConvertPdfToCbzCommand).FullName
                    },

                    // Settings tidy (2026-09-23): library-wide light-novel maintenance, manual only
                    // (0 = never scheduled; GetPending ignores it) -- run from System -> Tasks. Each
                    // service already refuses/no-ops when it does not apply (EPUB delivery or ebooks
                    // in the entry folder for the convert task; a home that is not calibre/ABS for
                    // the sync task).
                    new ScheduledTask
                    {
                        Interval = 0,
                        TypeName = typeof(ConvertLightNovelFormatCommand).FullName
                    },

                    new ScheduledTask
                    {
                        Interval = 0,
                        TypeName = typeof(SyncLightNovelTitlesCommand).FullName
                    },

                    // Tasks follow-up (2026-09-23): the library-wide adopt-existing job, same
                    // manual-only shape as the two above. ImportExistingLightNovelsService.Execute
                    // already runs every light-novel entry when AuthorId is null and only ever
                    // queues a search when SearchAfter is true, which a task-created command
                    // (default constructor) never sets.
                    new ScheduledTask
                    {
                        Interval = 0,
                        TypeName = typeof(ImportExistingLightNovelsCommand).FullName
                    },

                    // UI pass (2026-09-24, decision 1): the library-wide "Embed Metadata" (ComicInfo.xml into
                    // every CBZ) left the Library toolbar for System -> Tasks, manual only like the three above.
                    new ScheduledTask
                    {
                        Interval = 0,
                        TypeName = typeof(WriteComicInfoCommand).FullName
                    }
                };

            var currentTasks = _scheduledTaskRepository.All().ToList();

            _logger.Trace("Initializing jobs. Available: {0} Existing: {1}", defaultTasks.Count, currentTasks.Count);

            foreach (var job in currentTasks)
            {
                if (!defaultTasks.Any(c => c.TypeName == job.TypeName))
                {
                    _logger.Trace("Removing job from database '{0}'", job.TypeName);
                    _scheduledTaskRepository.Delete(job.Id);
                }
            }

            foreach (var defaultTask in defaultTasks)
            {
                var currentDefinition = currentTasks.SingleOrDefault(c => c.TypeName == defaultTask.TypeName) ?? defaultTask;

                currentDefinition.Interval = defaultTask.Interval;

                if (currentDefinition.Id == 0)
                {
                    currentDefinition.LastExecution = DateTime.UtcNow;
                }

                currentDefinition.Priority = defaultTask.Priority;

                _cache.Set(currentDefinition.TypeName, currentDefinition);
                _scheduledTaskRepository.Upsert(currentDefinition);
            }
        }

        private int GetBackupInterval()
        {
            var interval = _configService.BackupInterval;

            if (interval < 1)
            {
                interval = 1;
            }

            return interval * 60 * 24;
        }

        private int GetRssSyncInterval()
        {
            var interval = _configService.RssSyncInterval;

            if (interval > 0 && interval < 10)
            {
                return 10;
            }

            if (interval < 0)
            {
                return 0;
            }

            return interval;
        }

        // Beta readiness (2026-09-28, F1): a new install has no catalogue until the first check fetches it,
        // and light-novel adds are refused until then, so a failed first fetch retries hourly instead of
        // waiting a day. Re-evaluated after every run (Handle(CommandExecutedEvent)).
        private int GetMetadataUpdateInterval()
        {
            return _gcdMetadataService.Available ? 24 * 60 : 60;
        }

        private int GetPdfSweepInterval()
        {
            return _configService.PdfToCbzSweep ? 24 * 60 : 0;
        }

        private int GetBookSearchInterval()
        {
            var interval = _configService.BookSearchInterval;

            // Floor at 30 min when enabled so a misconfiguration can't hammer indexers; <= 0 disables.
            if (interval > 0 && interval < 30)
            {
                return 30;
            }

            if (interval < 0)
            {
                return 0;
            }

            return interval;
        }

        public void Handle(CommandExecutedEvent message)
        {
            var scheduledTask = _scheduledTaskRepository.All().SingleOrDefault(c => c.TypeName == message.Command.Body.GetType().FullName);

            if (scheduledTask != null && message.Command.Body.UpdateScheduledTask)
            {
                _logger.Trace("Updating last run time for: {0}", scheduledTask.TypeName);

                var lastExecution = DateTime.UtcNow;
                var startTime = message.Command.StartedAt.Value;

                _scheduledTaskRepository.SetLastExecutionTime(scheduledTask.Id, lastExecution, startTime);

                var cached = _cache.Find(scheduledTask.TypeName);

                cached.LastExecution = lastExecution;
                cached.LastStartTime = startTime;
            }

            if (message.Command.Body is MetadataUpdateCommand)
            {
                SetInterval(typeof(MetadataUpdateCommand), GetMetadataUpdateInterval());
            }
        }

        private void SetInterval(Type type, int interval)
        {
            var cached = _cache.Find(type.FullName);

            if (cached == null || cached.Interval == interval)
            {
                return;
            }

            var definition = _scheduledTaskRepository.GetDefinition(type);
            definition.Interval = interval;
            _scheduledTaskRepository.UpdateMany(new List<ScheduledTask> { definition });

            cached.Interval = interval;
        }

        public void HandleAsync(ConfigSavedEvent message)
        {
            var rss = _scheduledTaskRepository.GetDefinition(typeof(RssSyncCommand));
            rss.Interval = GetRssSyncInterval();

            var backup = _scheduledTaskRepository.GetDefinition(typeof(BackupCommand));
            backup.Interval = GetBackupInterval();

            _scheduledTaskRepository.UpdateMany(new List<ScheduledTask> { rss, backup });

            _cache.Find(rss.TypeName).Interval = rss.Interval;
            _cache.Find(backup.TypeName).Interval = backup.Interval;

            SetInterval(typeof(ConvertPdfToCbzCommand), GetPdfSweepInterval());
        }
    }
}
