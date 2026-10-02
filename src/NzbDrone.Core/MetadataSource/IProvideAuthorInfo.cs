using System;
using System.Collections.Generic;
using NzbDrone.Core.Books;

namespace NzbDrone.Core.MetadataSource
{
    public interface IProvideAuthorInfo
    {
        Author GetAuthorInfo(string readarrId, bool useCache = true, bool resolveVolumeDetails = true);

        // Preferred Edition (2026-09-24): the Add form's chosen edition. The three-argument call stays
        // every other caller's (and every mock's) -- an overload, never an optional parameter.
        Author GetAuthorInfo(string readarrId, bool useCache, bool resolveVolumeDetails, string editionLanguage);

        // Staging fix S1 (2026-10-01, spec §3.2): an add of a fallback candidate (a work with no line in the chain's
        // languages) also carries the line the search chose, so the resolve binds it by id. An overload, as above.
        Author GetAuthorInfo(string readarrId, bool useCache, bool resolveVolumeDetails, string editionLanguage, string tomeLineId);

        HashSet<string> GetChangedAuthors(DateTime startTime);
    }
}
