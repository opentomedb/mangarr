using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.PageFlip
{
    public class FlipPageOrderService : IExecute<FlipPageOrderCommand>
    {
        private readonly ICbzPageFlipper _flipper;
        private readonly IMediaFileService _mediaFileService;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public FlipPageOrderService(ICbzPageFlipper flipper,
                                    IMediaFileService mediaFileService,
                                    IDiskProvider diskProvider,
                                    Logger logger)
        {
            _flipper = flipper;
            _mediaFileService = mediaFileService;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public void Execute(FlipPageOrderCommand message)
        {
            var file = _mediaFileService.Get(message.BookFileId);
            var extension = System.IO.Path.GetExtension(file.Path ?? string.Empty).ToLowerInvariant();
            var name = System.IO.Path.GetFileNameWithoutExtension(file.Path ?? string.Empty);

            if (extension != ".cbz" && extension != ".zip")
            {
                _logger.Warn("Cannot flip page order of {0}: not a CBZ/ZIP archive", file.Path);
                message.SetResultMessage(new ServerText("{0} is not a CBZ archive; page order not flipped", name));
                return;
            }

            _flipper.Flip(file.Path);

            file.Size = _diskProvider.GetFileSize(file.Path);
            _mediaFileService.Update(file);

            _logger.Info("Flipped page order of {0}", System.IO.Path.GetFileName(file.Path));

            message.SetResultMessage(new ServerText("Page order flipped for {0}", name));
        }
    }
}
