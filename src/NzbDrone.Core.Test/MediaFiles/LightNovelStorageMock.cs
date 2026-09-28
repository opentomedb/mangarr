using Moq;
using NzbDrone.Core.MediaFiles;

namespace NzbDrone.Core.Test.MediaFiles
{
    // Light-novel storage (2026-09-22): one storage double for every fixture that routes light-novel
    // files -- calibre + Audiobookshelf homes by default, the calibre pair the identity, and the
    // Audiobookshelf pair "/audiobooks/" <-> "/srv/audiobooks/" through the real mapper.
    public static class LightNovelStorageMock
    {
        public const string AbsRemote = "/audiobooks/";
        public const string AbsLocal = "/srv/audiobooks/";
        public const string AbsRoot = "/srv/audiobooks";
        public const string CalibreUrl = "http://calibre.test:8081";

        public static void Setup(Mock<ILightNovelStorage> storage, string ebookHome = LightNovelHome.Calibre, string audioHome = LightNovelHome.Audiobookshelf)
        {
            storage.SetupGet(s => s.EbookHome).Returns(ebookHome);
            storage.SetupGet(s => s.AudioHome).Returns(audioHome);
            storage.SetupGet(s => s.Calibre).Returns(new CalibreConnection { Url = CalibreUrl, Library = "books", RemotePath = string.Empty, LocalPath = string.Empty });
            storage.SetupGet(s => s.Audiobookshelf).Returns(new AudiobookshelfConnection { Url = "http://abs.test", ApiKey = "abs-key", LibraryId = "lib-1234", RemotePath = AbsRemote, LocalPath = AbsLocal });
            storage.Setup(s => s.MapFromCalibre(It.IsAny<string>())).Returns<string>(p => p);
            storage.Setup(s => s.MapToCalibre(It.IsAny<string>())).Returns<string>(p => p);
            storage.Setup(s => s.MapFromAudiobookshelf(It.IsAny<string>())).Returns<string>(p => LightNovelPathMap.Map(p, AbsRemote, AbsLocal));
            storage.Setup(s => s.MapToAudiobookshelf(It.IsAny<string>())).Returns<string>(p => LightNovelPathMap.Map(p, AbsLocal, AbsRemote));
            storage.Setup(s => s.AudiobookshelfRoot()).Returns(AbsRoot);
        }
    }
}
