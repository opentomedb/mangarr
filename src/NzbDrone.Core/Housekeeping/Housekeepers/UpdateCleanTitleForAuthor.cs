using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class UpdateCleanTitleForAuthor : IHousekeepingTask
    {
        private readonly IAuthorRepository _authorRepository;
        private readonly Logger _logger;

        public UpdateCleanTitleForAuthor(IAuthorRepository authorRepository, Logger logger)
        {
            _authorRepository = authorRepository;
            _logger = logger;
        }

        public void Clean()
        {
            var authors = _authorRepository.All().ToList();
            var usedNames = new HashSet<string>(authors.Select(x => x.CleanName));

            authors.ForEach(s =>
            {
                var cleanName = LibraryTypes.CleanNameFor(s.Name.CleanAuthorName(), s.Library);
                if (s.CleanName != cleanName)
                {
                    // IX_Authors_CleanName is UNIQUE: skip renames that would land on a name
                    // another author (or an earlier rename in this run) already holds. If the
                    // holder itself renames away later in this run, the skipped rename simply
                    // self-heals on the next housekeeping pass.
                    if (usedNames.Contains(cleanName))
                    {
                        _logger.Warn("Not updating CleanName for {0}: '{1}' is already in use", s, cleanName);
                        return;
                    }

                    usedNames.Remove(s.CleanName);
                    usedNames.Add(cleanName);

                    s.CleanName = cleanName;
                    _authorRepository.Update(s);
                }
            });
        }
    }
}
