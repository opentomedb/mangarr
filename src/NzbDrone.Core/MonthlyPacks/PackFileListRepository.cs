using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MonthlyPacks
{
    public interface IPackFileListRepository : IBasicRepository<PackFileList>
    {
        PackFileList FindByHash(string infoHash);
    }

    public class PackFileListRepository : BasicRepository<PackFileList>, IPackFileListRepository
    {
        public PackFileListRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public PackFileList FindByHash(string infoHash)
        {
            return Query(x => x.InfoHash == infoHash).SingleOrDefault();
        }
    }
}
