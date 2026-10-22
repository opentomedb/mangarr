using System.IO.Compression;

namespace NzbDrone.Common.Extensions
{
    public static class ZipArchiveExtensions
    {
        // A regular file, -rw-r--r--, in the Unix-mode half of the entry's external attributes. .NET writes 0
        // there, and `unzip` then extracts files nobody can read.
        private const int ReadableFile = unchecked((int)(0x81A4u << 16));

        public static ZipArchiveEntry CreateFileEntry(this ZipArchive zip, string name, CompressionLevel level)
        {
            var entry = zip.CreateEntry(name, level);
            entry.ExternalAttributes = ReadableFile;

            return entry;
        }
    }
}
