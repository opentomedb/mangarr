using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Localization
{
    public interface ILocalizationService
    {
        Dictionary<string, string> GetLocalizationDictionary();

        // Server messages (2026-09-26): a language's merged dictionary (en.json with the language file over
        // it), by file name ("en", "fr_fr", "zh_CN"), for the server-message localizer.
        Dictionary<string, string> GetLocalizationDictionary(string language);

        string GetLocalizedString(string phrase);
        string GetLocalizedString(string phrase, Dictionary<string, object> tokens);
    }

    public class LocalizationService : ILocalizationService, IHandleAsync<ConfigSavedEvent>
    {
        private const string DefaultCulture = "en";
        private static readonly Regex TokenRegex = new Regex(@"(?:\{)(?<token>[a-z0-9]+)(?:\})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private readonly ICached<Dictionary<string, string>> _cache;

        private readonly IConfigService _configService;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly Logger _logger;

        public LocalizationService(IConfigService configService,
                                   IAppFolderInfo appFolderInfo,
                                   ICacheManager cacheManager,
                                   Logger logger)
        {
            _configService = configService;
            _appFolderInfo = appFolderInfo;
            _cache = cacheManager.GetCache<Dictionary<string, string>>(typeof(Dictionary<string, string>), "localization");
            _logger = logger;
        }

        public Dictionary<string, string> GetLocalizationDictionary()
        {
            var language = GetSetLanguageFileName();

            return GetLocalizationDictionary(language);
        }

        public string GetLocalizedString(string phrase)
        {
            return GetLocalizedString(phrase, new Dictionary<string, object>());
        }

        public string GetLocalizedString(string phrase, Dictionary<string, object> tokens)
        {
            if (string.IsNullOrEmpty(phrase))
            {
                throw new ArgumentNullException(nameof(phrase));
            }

            var language = GetSetLanguageFileName();

            if (language == null)
            {
                language = DefaultCulture;
            }

            var dictionary = GetLocalizationDictionary(language);

            if (dictionary.TryGetValue(phrase, out var value))
            {
                return ReplaceTokens(value, tokens);
            }

            return phrase;
        }

        private string ReplaceTokens(string input, Dictionary<string, object> tokens)
        {
            tokens.TryAdd("appName", "Mangarr");

            return TokenRegex.Replace(input, (match) =>
            {
                var tokenName = match.Groups["token"].Value;

                tokens.TryGetValue(tokenName, out var token);

                return token?.ToString() ?? $"{{{tokenName}}}";
            });
        }

        private string GetSetLanguageFileName()
        {
            var isoLanguage = IsoLanguages.Get((Language)_configService.UILanguage) ?? IsoLanguages.Get(Language.English);
            var language = isoLanguage.TwoLetterCode;

            if (isoLanguage.CountryCode.IsNotNullOrWhiteSpace())
            {
                language = string.Format("{0}_{1}", language, isoLanguage.CountryCode);
            }

            return language;
        }

        public Dictionary<string, string> GetLocalizationDictionary(string language)
        {
            if (string.IsNullOrEmpty(language))
            {
                throw new ArgumentNullException(nameof(language));
            }

            var startupFolder = _appFolderInfo.StartUpFolder;

            var prefix = Path.Combine(startupFolder, "Localization", "Core");
            var key = prefix + language;

            // Server messages (2026-09-26): one cache entry per language. The constant key "localization"
            // handed the first language loaded to every later caller; HandleAsync(ConfigSavedEvent) still
            // clears them all.
            return _cache.Get(key, () => GetDictionary(prefix, language, DefaultCulture + ".json").GetAwaiter().GetResult());
        }

        private async Task<Dictionary<string, string>> GetDictionary(string prefix, string culture, string baseFilename)
        {
            if (string.IsNullOrEmpty(culture))
            {
                throw new ArgumentNullException(nameof(culture));
            }

            var dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            await CopyInto(dictionary, Path.Combine(prefix, baseFilename)).ConfigureAwait(false);

            // UI translations v1 (2026-09-25): French and German ask for fr_FR/de_DE (Portuguese for
            // pt_PT), which never shipped, and Readarr logged an Error for the missing regional file on
            // every cache fill. A language resolves through its base file, its regional file or both,
            // in that order; only a language with neither is an error.
            var candidates = (culture.Contains('_')
                    ? new[] { GetResourceFilename(culture.Split('_')[0]), GetResourceFilename(culture) }
                    : new[] { GetResourceFilename(culture) })
                .Select(f => Path.Combine(prefix, f))
                .Distinct()
                .ToList();

            var existing = candidates.Where(File.Exists).ToList();

            if (existing.Count == 0)
            {
                _logger.Error("Missing translation/culture resource: {0}", candidates.Last());
            }

            foreach (var path in existing)
            {
                await CopyInto(dictionary, path).ConfigureAwait(false);
            }

            return dictionary;
        }

        private async Task CopyInto(IDictionary<string, string> dictionary, string resourcePath)
        {
            if (!File.Exists(resourcePath))
            {
                _logger.Error("Missing translation/culture resource: {0}", resourcePath);
                return;
            }

            await using var fs = File.OpenRead(resourcePath);
            var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(fs);

            foreach (var key in dict.Keys)
            {
                dictionary[key] = dict[key];
            }
        }

        private static string GetResourceFilename(string culture)
        {
            var parts = culture.Split('_');

            if (parts.Length == 2)
            {
                culture = parts[0].ToLowerInvariant() + "_" + parts[1].ToUpperInvariant();
            }
            else
            {
                culture = culture.ToLowerInvariant();
            }

            return culture + ".json";
        }

        public void HandleAsync(ConfigSavedEvent message)
        {
            _cache.Clear();
        }
    }
}
