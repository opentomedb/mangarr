using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.MetadataSource.Manga;

namespace NzbDrone.Core.MetadataSource
{
    // D1: rank the strict candidates instead of taking AniList's first hit. AniList puts
    // one-shots, pilots and anthologies that carry the serial's exact title (or its title as a
    // synonym) at rank 0 — the 2026-09-15 audit's Fairy Tail / Black Clover / Blue Box bindings.
    // Pure and static so the audit's cases are table tests; Rejections is what the Info log
    // shows when nothing passes.
    //
    // Kept in step with OpenTome's export/resolve_anilist.py pick(), which binds the catalogue's
    // lines by the same rules; R4-R7 were ported from OpenTome's 2026-09-24 round. OpenTome ranks
    // a line's own name, so its R4/R5 always see the term the count belongs to; Mangarr ranks the
    // entry's name, which may only be an alias or arc title of the hinted line — hence
    // termIsLineName (below), which Mangarr adds and OpenTome does not need.
    public static class AniListRanker
    {
        public const string OneShot = "ONE_SHOT";
        public const string Releasing = "RELEASING";

        // R7: one leading English article, a whole word.
        private static readonly Regex LeadingArticle = new Regex(@"^(?:the|a|an)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // R6: a trailing parenthetical (an unclosed outer one is handled in EditionStripped) and
        // what it may say for the name without it to still be the series' own name — an edition /
        // format / printing qualifier, never an arc ("chapter") or a nested series.
        private static readonly Regex TrailingParenthetical = new Regex(@"\s*\(([^()]*)\)\s*$", RegexOptions.CultureInvariant);
        private static readonly Regex EditionQualifier = new Regex(@"edition|volume list|release|version|tank[oō]bon|shins[oō]ban|vizbig|2-in-1|parution|printing", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private enum VolumeVerdict
        {
            Agree,
            TooSmall,
            OverCeiling
        }

        // Size guard against OpenTome's volume_count — ONE-SIDED. The catalogue count is the
        // English collected-edition count and AniList's is the Japanese tankoubon count, so a
        // candidate may legitimately be LARGER (Vinland Saga: 29 JP vs 15 deluxe 2-in-1 books;
        // Erased: 9 vs 5; a finished run whose English line stopped early) up to the 4x
        // omnibus-plausibility ceiling DetermineVolumeCount already uses. A candidate SMALLER
        // than the line by more than max(3, 40 %) is a pilot/anthology wearing the serial's
        // name (Pantsu Agerune: 1 volume vs 63); a RELEASING candidate gets that tolerance
        // doubled (its English line lags). Skipped when either count is unknown. The ceiling
        // does not apply to a line of 1-2 volumes (R2, OpenTome's live run 2026-09-15): a
        // one-book release or a run cut short binds the full Japanese serial (Pupa: 5 vs 1) —
        // but only against the line's OWN name (ownName); an alias retry always keeps the
        // ceiling (R3), or a spin-off's bare franchise alias binds the main serial ("Attack on
        // Titan: Harsh Mistress of the City", 2 volumes, to "Attack on Titan", 34, through
        // "Shingeki no Kyojin").
        public static bool VolumesAgree(int? candidateVolumes, string candidateStatus, int? catalogueVolumeCount, bool ownName = true)
        {
            return CheckVolumes(candidateVolumes, candidateStatus, catalogueVolumeCount, ownName) == VolumeVerdict.Agree;
        }

        // The two rejections are exclusive (a count above 4x the line is never below it), and R4
        // needs to know which one fired.
        private static VolumeVerdict CheckVolumes(int? candidateVolumes, string candidateStatus, int? catalogueVolumeCount, bool ownName)
        {
            if (!(candidateVolumes > 0) || !(catalogueVolumeCount > 0))
            {
                return VolumeVerdict.Agree;
            }

            var candidate = candidateVolumes.Value;
            var catalogue = catalogueVolumeCount.Value;

            if ((catalogue > 2 || !ownName) && candidate > catalogue * 4)
            {
                return VolumeVerdict.OverCeiling;
            }

            var tolerance = Math.Max(3.0, 0.4 * catalogue);

            if (candidateStatus == Releasing)
            {
                tolerance *= 2;
            }

            return candidate >= catalogue - tolerance ? VolumeVerdict.Agree : VolumeVerdict.TooSmall;
        }

        // R7: TitleMatcher's key after dropping one leading "the" / "a" / "an" (a whole word):
        // "The Hollow Regalia" -> "hollowregalia".
        public static string ArticleKey(string title)
        {
            return TitleMatcher.Normalize(LeadingArticle.Replace(TitleNormalizer.ForSearch(title), string.Empty, 1));
        }

        // R6: the name without a trailing edition-qualifier parenthetical, or null. "Inuyasha
        // (VizBig edition)" -> "Inuyasha"; an unclosed outer parenthetical goes too ("Ranma ½
        // (2014 English release (2-in-1 Edition)" -> "Ranma ½"). Never one naming a chapter, a
        // nested "series (", or a quoted title ("Amazing Agent Luna ("Amazing Agent Jennifer"
        // Volume list)" names another work). The catalogue names sibling editions after
        // Wikipedia's headings and AniList has one entry for the work. The caller ranks it like
        // an ALIAS (R3 keeps the ceiling, no fallback tiers): stripping can drop content — "Sailor
        // Moon (Shinsōban short stories)", 2 volumes, must not bind the 18-volume serial.
        public static string EditionStripped(string name)
        {
            var s = TitleNormalizer.ForSearch(name);
            var match = TrailingParenthetical.Match(s);

            if (!match.Success)
            {
                return null;
            }

            var head = s.Substring(0, match.Index);
            var inner = match.Groups[1].Value;

            if (head.Count(c => c == '(') > head.Count(c => c == ')'))
            {
                var open = head.LastIndexOf('(');
                inner = head.Substring(open + 1) + "(" + inner + ")";
                head = head.Substring(0, open);
            }

            head = head.Trim();
            var lower = inner.ToLowerInvariant();

            if (head.Length == 0 ||
                lower.Contains("chapter") ||
                lower.Contains("series (") ||
                inner.IndexOfAny(new[] { '"', '“', '”' }) >= 0 ||
                !EditionQualifier.IsMatch(inner))
            {
                return null;
            }

            return head;
        }

        // normalizedTitle is the search title after TitleNormalizer.ForSearch; equality is
        // TitleMatcher's (alphanumerics only, case-insensitive). Primary-title (romaji / english /
        // native) equality outranks synonym equality; ties break on popularity, then AniList's rank.
        // A synonym-only carrier never wins while ANY candidate on the page has primary-title
        // equality, even one the rules rejected (R1, OpenTome's live run 2026-09-15): a primary
        // rejected on volumes says "this is the work but the count disagrees" (Doll: "DOLL" 1 vol
        // vs 6), a same-named ONE_SHOT is that serial's pilot — either way the carrier is a
        // chapter title wearing the name, and unresolved is recoverable where a wrong bind is not.
        // ownName is false for an alias retry (R3: the volume ceiling then always holds, and no
        // fallback tier runs). termIsLineName says the term IS the catalogue line's own name (the
        // line catalogueVolumeCount came from); only R4 and R5 read it — ownName and R2 do not.
        //
        // Below exact equality, in this order (OpenTome 2026-09-24):
        //   R7 "article": the same primary-then-synonym equality after dropping one leading
        //      the / a / an from both sides (ArticleKey), R1 across tiers — never beside an exact
        //      primary-title candidate, even a rejected one. Runs on alias terms too.
        //   R5 "substring" (own name, and the term is the line's own name): no candidate on the
        //      page — rejected or ONE_SHOT included — key-equals the term, exactly or by article,
        //      and exactly ONE volume-passing, non-ONE_SHOT candidate has a title / synonym key
        //      that contains the term's key or sits inside it (shorter side >= 4) AND volumes equal
        //      to the catalogue count. The exact count is the guard, and it is only the term's
        //      count when the term names that line: R5 needs a catalogue count AND termIsLineName.
        //   R4 "ceiling" (own name, and the term is the line's own name): an exact primary /
        //      synonym match rejected SOLELY by the 4x ceiling binds (Weed: 3 English volumes,
        //      34010 "Ginga Densetsu WEED" 60) — never over an equal-titled candidate that passes
        //      the ceiling (even an R1-rejected synonym carrier — Worst's 4-volume 147044 stays),
        //      never beside an article-equal candidate, and R1 still holds inside the tier. Like
        //      R5 it needs a catalogue count AND termIsLineName.
        public static (AniListCandidate Pick, string Via, List<string> Rejections) Pick(IReadOnlyList<AniListCandidate> candidates, string normalizedTitle, int? catalogueVolumeCount, bool ownName = true, bool termIsLineName = true)
        {
            var rejections = new List<string>();
            var page = candidates ?? Array.Empty<AniListCandidate>();
            var key = TitleMatcher.Normalize(normalizedTitle);
            var articleKey = ArticleKey(normalizedTitle);

            bool PrimaryEqual(AniListCandidate c) => TitleMatcher.Matches(normalizedTitle, c.PrimaryTitles);
            bool SynonymEqual(AniListCandidate c) => TitleMatcher.Matches(normalizedTitle, c.Synonyms);
            bool ArticlePrimary(AniListCandidate c) => articleKey.Length > 0 && c.PrimaryTitles.Any(t => ArticleKey(t) == articleKey);
            bool ArticleSynonym(AniListCandidate c) => articleKey.Length > 0 && (c.Synonyms ?? new List<string>()).Any(t => ArticleKey(t) == articleKey);
            bool ContainsTerm(AniListCandidate c) => c.AllTitles
                .Select(TitleMatcher.Normalize)
                .Any(t => Math.Min(t.Length, key.Length) >= 4 && (t.Contains(key) || key.Contains(t)));

            // Page-wide readings, rejected and ONE_SHOT candidates included (R1).
            var primaryOnPage = page.Any(PrimaryEqual);
            var articleOnPage = primaryOnPage || page.Any(ArticlePrimary);
            var equalityOnPage = articleOnPage || page.Any(c => SynonymEqual(c) || ArticleSynonym(c));

            var primary = new List<(AniListCandidate Candidate, int Index)>();
            var synonym = new List<(AniListCandidate Candidate, int Index)>();
            var articlePrimary = new List<(AniListCandidate Candidate, int Index)>();
            var articleSynonym = new List<(AniListCandidate Candidate, int Index)>();
            var substring = new List<(AniListCandidate Candidate, int Index)>();
            var oversized = new List<(AniListCandidate Candidate, int Index)>();
            var carrier = false;

            // R4 and R5 read the catalogue count as the TERM's count.
            var fallbackTiers = ownName && termIsLineName;

            for (var i = 0; i < page.Count; i++)
            {
                var c = page[i];
                var label = $"{c.Id} \"{c.DisplayTitle}\"";

                if (c.Format == OneShot)
                {
                    rejections.Add(label + ": ONE_SHOT");
                    continue;
                }

                var volumes = CheckVolumes(c.Volumes, c.Status, catalogueVolumeCount, ownName);

                if (volumes != VolumeVerdict.Agree)
                {
                    rejections.Add($"{label}: {c.Volumes} vols vs catalogue {catalogueVolumeCount}");

                    // R4: rejected SOLELY by the ceiling, with exact equality, on the line's own name.
                    if (volumes == VolumeVerdict.OverCeiling && fallbackTiers && (PrimaryEqual(c) || SynonymEqual(c)))
                    {
                        oversized.Add((c, i));
                    }

                    continue;
                }

                if (PrimaryEqual(c))
                {
                    primary.Add((c, i));
                }
                else if (SynonymEqual(c))
                {
                    if (primaryOnPage)
                    {
                        rejections.Add(label + ": synonym only (a primary-title candidate is on the page)");

                        // Passed the ceiling: R4 must not outrank it either.
                        carrier = true;
                    }
                    else
                    {
                        synonym.Add((c, i));
                    }
                }
                else if (ArticlePrimary(c))
                {
                    articlePrimary.Add((c, i));
                }
                else if (ArticleSynonym(c))
                {
                    if (articleOnPage)
                    {
                        rejections.Add(label + ": synonym only (a primary-title candidate is on the page)");
                    }
                    else
                    {
                        articleSynonym.Add((c, i));
                    }
                }
                else
                {
                    rejections.Add(label + ": no title equality");

                    if (fallbackTiers && c.Volumes > 0 && c.Volumes == catalogueVolumeCount && ContainsTerm(c))
                    {
                        substring.Add((c, i));
                    }
                }
            }

            if (primary.Any())
            {
                return (Best(primary), "primary", rejections);
            }

            if (synonym.Any())
            {
                return (Best(synonym), "synonym", rejections);
            }

            var articleEqual = articlePrimary.Any() || articleSynonym.Any();

            if (articleEqual && !primaryOnPage)
            {
                return (Best(articlePrimary.Any() ? articlePrimary : articleSynonym), "article", rejections);
            }

            if (!equalityOnPage && substring.Count == 1)
            {
                return (substring[0].Candidate, "substring", rejections);
            }

            if (!carrier && !articleEqual)
            {
                var ceiling = oversized.Where(e => PrimaryEqual(e.Candidate)).ToList();

                if (!ceiling.Any() && !primaryOnPage)
                {
                    ceiling = oversized.Where(e => SynonymEqual(e.Candidate)).ToList();
                }

                if (ceiling.Any())
                {
                    return (Best(ceiling), "ceiling", rejections);
                }
            }

            return (null, null, rejections);
        }

        private static AniListCandidate Best(List<(AniListCandidate Candidate, int Index)> pool)
        {
            return pool
                .OrderByDescending(e => e.Candidate.Popularity ?? 0)
                .ThenBy(e => e.Index)
                .First()
                .Candidate;
        }
    }
}
