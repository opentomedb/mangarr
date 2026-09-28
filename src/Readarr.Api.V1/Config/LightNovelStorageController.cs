using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using Readarr.Http;

namespace Readarr.Api.V1.Config
{
    public class CalibreConnectionResource
    {
        public string Url { get; set; }
        public string Library { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string RemotePath { get; set; }
        public string LocalPath { get; set; }
    }

    public class AudiobookshelfConnectionResource
    {
        public string Url { get; set; }
        public string ApiKey { get; set; }
        public string LibraryId { get; set; }
        public string RemotePath { get; set; }
        public string LocalPath { get; set; }
    }

    // Light-novel storage (2026-09-22): Settings -> Media Management -> Light novel storage asks with
    // the values on the form (saved or not); a secret still showing the mask means the stored one.
    // Always 200 with plain-words problems -- Readarr's provider "test" answers 400 + validation
    // failures, but this form has no provider behind it and the problems are the answer.
    //   POST /api/v1/config/lightnovelstorage/calibre/test          -> { problems: [...] }
    //   POST /api/v1/config/lightnovelstorage/calibre/libraries     -> { libraries: [{id,name}], defaultLibrary, problems }
    //   POST /api/v1/config/lightnovelstorage/audiobookshelf/test   -> { problems: [...] }
    //   POST /api/v1/config/lightnovelstorage/audiobookshelf/libraries
    [V1ApiController("config/lightnovelstorage")]
    public class LightNovelStorageController : Controller
    {
        private readonly ILightNovelStorageProbe _probe;
        private readonly IConfigService _configService;

        public LightNovelStorageController(ILightNovelStorageProbe probe, IConfigService configService)
        {
            _probe = probe;
            _configService = configService;
        }

        [HttpPost("calibre/test")]
        public object TestCalibre([FromBody] CalibreConnectionResource resource)
        {
            return new { problems = _probe.TestCalibre(ToConnection(resource)) };
        }

        [HttpPost("calibre/libraries")]
        public object CalibreLibraries([FromBody] CalibreConnectionResource resource)
        {
            return _probe.CalibreLibraries(ToConnection(resource));
        }

        [HttpPost("audiobookshelf/test")]
        public object TestAudiobookshelf([FromBody] AudiobookshelfConnectionResource resource)
        {
            return new { problems = _probe.TestAudiobookshelf(ToConnection(resource)) };
        }

        [HttpPost("audiobookshelf/libraries")]
        public object AudiobookshelfLibraries([FromBody] AudiobookshelfConnectionResource resource)
        {
            return _probe.AudiobookshelfLibraries(ToConnection(resource));
        }

        private CalibreConnection ToConnection(CalibreConnectionResource resource)
        {
            var url = resource.Url?.Trim();

            return new CalibreConnection
            {
                Url = url,
                Library = resource.Library,
                Username = resource.Username,
                Password = LightNovelStorage.UnmaskForHost(resource.Password, _configService.CalibrePassword, url, _configService.CalibreContentServerUrl),
                RemotePath = resource.RemotePath,
                LocalPath = resource.LocalPath
            };
        }

        private AudiobookshelfConnection ToConnection(AudiobookshelfConnectionResource resource)
        {
            var url = resource.Url?.Trim();

            return new AudiobookshelfConnection
            {
                Url = url,
                ApiKey = LightNovelStorage.UnmaskForHost(resource.ApiKey, _configService.AudiobookshelfApiKey, url, _configService.AudiobookshelfUrl),
                LibraryId = resource.LibraryId,
                RemotePath = resource.RemotePath,
                LocalPath = resource.LocalPath
            };
        }
    }
}
