using System;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles
{
    // Light-novel storage (2026-09-22): one prefix swap per service, both directions -- "path as
    // calibre/Audiobookshelf sees it" <-> "path as Mangarr sees it". Both blank = the same path
    // (identity; a half-set pair is refused by the settings validator and treated as blank here).
    // Roots match with or without their trailing slash. A path outside the source root, or a result
    // that climbs out of the target root through "..", is refused (null). Only a path that carries a
    // "." or ".." segment is normalised, so every other path comes back as the exact string --
    // stored rows are compared by path.
    public static class LightNovelPathMap
    {
        public static bool IsSet(string fromRoot, string toRoot)
        {
            return fromRoot.IsNotNullOrWhiteSpace() && toRoot.IsNotNullOrWhiteSpace();
        }

        public static string Map(string path, string fromRoot, string toRoot)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return null;
            }

            if (!IsSet(fromRoot, toRoot))
            {
                return path;
            }

            var from = WithSlash(fromRoot);
            var to = WithSlash(toRoot);

            if (path == WithoutSlash(from))
            {
                return WithoutSlash(to);
            }

            if (!path.StartsWith(from, StringComparison.Ordinal))
            {
                return null;
            }

            var mapped = to + path.Substring(from.Length);

            if (!mapped.Split('/').Any(s => s == "." || s == ".."))
            {
                return mapped;
            }

            var full = Path.GetFullPath(mapped);

            return full.StartsWith(to, StringComparison.Ordinal) || full == WithoutSlash(to) ? full : null;
        }

        // Equal to the root, or below it (a sibling that only shares the prefix is not).
        public static bool IsUnder(string path, string root)
        {
            if (path.IsNullOrWhiteSpace() || root.IsNullOrWhiteSpace())
            {
                return false;
            }

            return WithSlash(path).StartsWith(WithSlash(root), StringComparison.Ordinal);
        }

        public static string WithoutSlash(string path)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return null;
            }

            path = path.Trim();

            return path.Length > 1 ? path.TrimEnd('/') : path;
        }

        private static string WithSlash(string path)
        {
            path = path.Trim();

            return path.EndsWith("/") ? path : path + "/";
        }
    }
}
