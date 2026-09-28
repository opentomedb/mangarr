using System;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Update
{
    // Parses the changelog.json that deploy.sh bakes into the image (/app/bin/changelog.json)
    // into the single "what's in this build" UpdatePackage the Updates page shows: the running
    // version, stamped with the bake time, fix* commit subjects under Fixed and the rest under
    // New. Anything unusable (missing, malformed, no commits) yields null and the page shows
    // nothing — never an error.
    public static class BuildChangelog
    {
        public static UpdatePackage Parse(string json, Version currentVersion, string branch)
        {
            if (json.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                var root = JObject.Parse(json);
                var commits = root["commits"] as JArray;

                if (commits == null || commits.Count == 0)
                {
                    return null;
                }

                var changes = new UpdateChanges();

                foreach (var commit in commits)
                {
                    var subject = commit["subject"]?.ToString();

                    if (subject.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    if (subject.StartsWith("fix", StringComparison.OrdinalIgnoreCase))
                    {
                        changes.Fixed.Add(subject);
                    }
                    else
                    {
                        changes.New.Add(subject);
                    }
                }

                if (changes.New.Count == 0 && changes.Fixed.Count == 0)
                {
                    return null;
                }

                var generated = root["generated"]?.Value<DateTime?>() ?? DateTime.UtcNow;

                return new UpdatePackage
                {
                    Version = currentVersion,
                    Branch = branch,
                    ReleaseDate = generated.ToUniversalTime(),
                    FileName = "docker-image",
                    Url = string.Empty,
                    Hash = commits[0]["hash"]?.ToString(),
                    Changes = changes
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
