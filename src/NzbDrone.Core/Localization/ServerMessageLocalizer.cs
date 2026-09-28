using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Localization;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Localization
{
    // Server messages (UI translations v2, 2026-09-26): server-built messages in the UI language, at the API
    // boundary only. Logs, stored rows and the carriers keep their English (spec section 1).
    public interface IServerMessageLocalizer
    {
        // A message for the API. text is the carrier's template and arguments, or null for a plain string
        // (matched whole). Returns english itself for an English UI and whenever no key or no translation fits.
        string Localize(string english, ServerText text = null);

        // A stored History message: the key and the rendered placeholder texts saved beside it.
        string LocalizeStored(string english, string key, IReadOnlyList<string> args);

        // The key and rendered placeholder texts to store beside a new History message; null when no Server*
        // key has the template's skeleton.
        ServerMessageRecord Record(ServerText text);

        // Validation and provider Test failures, in place: only ReadarrErrorPipeline calls this, after the
        // English log line, on a terminal exception's failures.
        void Localize(IEnumerable<ValidationFailure> failures);
    }

    public class ServerMessageRecord
    {
        public string Key { get; set; }
        public List<string> Args { get; set; }
    }

    public class ServerMessageLocalizer : IServerMessageLocalizer
    {
        private static readonly Regex Token = new Regex(@"\{(?<token>[A-Za-z0-9]+)\}", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

        // Task 5 (2026-09-26): a FluentValidation built-in's ErrorCode is its type name, and its LanguageManager
        // message key is the same name -- except EmailAddress(), whose AspNetCoreCompatibleEmailValidator reads
        // the EmailValidator message, and IsEnumName(), whose StringEnumValidator reads the EnumValidator one
        // (FluentValidation 9.5.4, Localized(nameof(EmailValidator)) / Localized(nameof(EnumValidator))).
        private static readonly Dictionary<string, string> BuiltInMessageKeys = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "AspNetCoreCompatibleEmailValidator", "EmailValidator" },
            { "StringEnumValidator", "EnumValidator" }
        };

        // One index per loaded en dictionary: LocalizationService loads a new one after ConfigSavedEvent, so
        // the index follows it without a handler of its own.
        private readonly ConditionalWeakTable<Dictionary<string, string>, ServerMessageIndex> _indexes = new ConditionalWeakTable<Dictionary<string, string>, ServerMessageIndex>();

        private readonly ILocalizationService _localizationService;
        private readonly IConfigService _configService;

        public ServerMessageLocalizer(ILocalizationService localizationService, IConfigService configService)
        {
            _localizationService = localizationService;
            _configService = configService;
        }

        public string Localize(string english, ServerText text = null)
        {
            if (english == null || IsEnglishUi())
            {
                return english;
            }

            var context = Load();

            // Server messages (2026-09-26, task 1b review): trust text only while it still describes the
            // english being localized. CommandQueueManager.SetMessage writes Message before clearing
            // MessageText, so a concurrent read (or a mapper handed a stale pair) could otherwise localize
            // english through the wrong template. A mismatch falls back to the exact-match path, same as a
            // carrier that was never built from a template.
            return text != null && text.English == english
                ? LocalizeText(english, text, context)
                : LocalizePlain(english, context);
        }

        public string LocalizeStored(string english, string key, IReadOnlyList<string> args)
        {
            if (english == null || key == null || IsEnglishUi())
            {
                return english;
            }

            var context = Load();

            if (!Translation(key, context, out var template, out var value))
            {
                return english;
            }

            var names = ServerText.Parse(template).Holes.Select(h => h.Name).ToList();

            if (names.Count != (args?.Count ?? 0))
            {
                return english;
            }

            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var i = 0; i < names.Count; i++)
            {
                tokens.TryAdd(names[i], args[i]);
            }

            return Fill(value, tokens);
        }

        public ServerMessageRecord Record(ServerText text)
        {
            if (text == null)
            {
                return null;
            }

            var en = _localizationService.GetLocalizationDictionary("en");
            var index = _indexes.GetValue(en, d => ServerMessageIndex.Build(d));
            var parsed = ServerText.Parse(text.Template);

            if (!index.KeyBySkeleton.TryGetValue(parsed.Skeleton, out var key))
            {
                return null;
            }

            return new ServerMessageRecord
            {
                Key = key,
                Args = parsed.Holes.Select((hole, i) => Render(hole, i, text.Args, null)).ToList()
            };
        }

        public void Localize(IEnumerable<ValidationFailure> failures)
        {
            if (failures == null || IsEnglishUi())
            {
                return;
            }

            var context = Load();

            foreach (var failure in failures)
            {
                failure.ErrorMessage = LocalizeFailure(failure, context);

                if (failure is NzbDroneValidationFailure nzbDroneFailure && nzbDroneFailure.DetailedDescription != null)
                {
                    // Fix round 1 (2026-09-26): trust DetailedDescriptionText only while DetailedDescription
                    // still equals the English it produced. If something changed DetailedDescription
                    // independently since, the carrier's template no longer describes the current text.
                    nzbDroneFailure.DetailedDescription = nzbDroneFailure.DetailedDescriptionText != null && nzbDroneFailure.DetailedDescription == nzbDroneFailure.DetailedDescriptionText.English
                        ? LocalizeText(nzbDroneFailure.DetailedDescription, nzbDroneFailure.DetailedDescriptionText, context)
                        : LocalizePlain(nzbDroneFailure.DetailedDescription, context);
                }
            }
        }

        private bool IsEnglishUi()
        {
            return _configService.UILanguage == (int)Language.English;
        }

        private Context Load()
        {
            var en = _localizationService.GetLocalizationDictionary("en");

            return new Context
            {
                En = en,
                Ui = _localizationService.GetLocalizationDictionary(),
                Index = _indexes.GetValue(en, d => ServerMessageIndex.Build(d))
            };
        }

        // The UI language's own value for a key. The UI dictionary is en.json with the locale file over it,
        // so an untranslated key reads as its English: that is "no translation" (spec section 1, step 5).
        private static bool Translation(string key, Context context, out string template, out string value)
        {
            value = null;

            return context.En.TryGetValue(key, out template) && context.Ui.TryGetValue(key, out value) && value != template;
        }

        private string LocalizePlain(string english, Context context)
        {
            return context.Index.KeyByText.TryGetValue(english, out var key) && Translation(key, context, out _, out var value)
                ? Fill(value, new Dictionary<string, string>())
                : english;
        }

        private string LocalizeText(string english, ServerText text, Context context)
        {
            var parsed = ServerText.Parse(text.Template);

            if (!context.Index.KeyBySkeleton.TryGetValue(parsed.Skeleton, out var key) || !Translation(key, context, out var template, out var value))
            {
                return english;
            }

            var names = ServerText.Parse(template).Holes.Select(h => h.Name).ToList();

            if (names.Count != parsed.Holes.Count)
            {
                return english;
            }

            // The i-th placeholder of the call site fills the en template's i-th token (spec section 1, step 2).
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

            for (var i = 0; i < names.Count; i++)
            {
                if (!tokens.ContainsKey(names[i]))
                {
                    tokens[names[i]] = Render(parsed.Holes[i], i, text.Args, context);
                }
            }

            return Fill(value, tokens);
        }

        // One placeholder as the call site formatted it: the same format specifier and the same (current)
        // culture. A numbered hole reads its own argument; a named hole (NLog) reads the argument at its
        // position. A nested ServerText is localized first, or renders its English without a context (Record).
        private string Render(TemplateHole hole, int position, object[] args, Context context)
        {
            var at = hole.Index ?? position;
            var arg = at < args.Length ? args[at] : null;

            if (arg is ServerText nested)
            {
                arg = context == null ? nested.English : LocalizeText(nested.English, nested, context);
            }

            return string.Format(hole.FormatSpec, arg);
        }

        private static string Fill(string template, IReadOnlyDictionary<string, string> tokens)
        {
            return Token.Replace(template, match =>
            {
                var name = match.Groups["token"].Value;

                if (name == "appName")
                {
                    return "Mangarr";
                }

                return tokens.TryGetValue(name, out var value) ? value : match.Value;
            });
        }

        private string LocalizeFailure(ValidationFailure failure, Context context)
        {
            var english = failure.ErrorMessage;
            var nzbDroneFailure = failure as NzbDroneValidationFailure;

            if (english == null)
            {
                return null;
            }

            // Fix round 1 (2026-09-26): trust the carrier's Text only while ErrorMessage still equals the
            // English it produced. If something changed ErrorMessage independently since, the carrier's
            // template no longer describes the current text.
            if (nzbDroneFailure?.Text != null && english == nzbDroneFailure.Text.English)
            {
                return LocalizeText(english, nzbDroneFailure.Text, context);
            }

            // A rule message built from values fixed when the rule was built (Task 5, ServerRuleMessages).
            var ruleText = ServerRuleMessages.Find(english);

            if (ruleText != null)
            {
                return LocalizeText(english, ruleText, context);
            }

            var code = failure.ErrorCode ?? nzbDroneFailure?.SourceErrorCode;
            var values = failure.FormattedMessagePlaceholderValues ?? nzbDroneFailure?.SourcePlaceholderValues;

            if (values != null)
            {
                if (code != null)
                {
                    // A custom validator: ServerValidation<Name> for its ErrorCode <Name>Validator (spec section 2).
                    var custom = ServerMessageIndex.CustomValidatorKey(code);

                    if (context.En.TryGetValue(custom, out var customTemplate) && RenderValidation(customTemplate, values) == english)
                    {
                        return Translation(custom, context, out _, out var customValue) ? RenderValidation(customValue, Words(values, context)) : english;
                    }

                    // A FluentValidation built-in whose English is the built-in rendering: FluentValidation's own
                    // translation, filled from the same placeholder values. Validation itself still ran in English.
                    var languages = ValidatorOptions.Global.LanguageManager;
                    var messageKey = BuiltInMessageKeys.TryGetValue(code, out var alias) ? alias : code;
                    var builtIn = languages.GetString(messageKey, EnglishCulture);

                    if (!string.IsNullOrEmpty(builtIn) && RenderValidation(builtIn, values) == english)
                    {
                        var culture = UiCulture();
                        var translated = culture == null ? null : languages.GetString(messageKey, culture);

                        return string.IsNullOrEmpty(translated) || translated == builtIn ? english : RenderValidation(translated, values);
                    }
                }

                // A .WithMessage template: the one ServerValidation key that renders to exactly this text with
                // the failure's own placeholder values (plan ruling R6).
                foreach (var key in context.Index.ValidationTemplateKeys)
                {
                    if (RenderValidation(context.En[key], values) == english)
                    {
                        return Translation(key, context, out _, out var value) ? RenderValidation(value, Words(values, context)) : english;
                    }
                }
            }

            return LocalizePlain(english, context);
        }

        private static string RenderValidation(string template, IDictionary<string, object> values)
        {
            var formatter = new MessageFormatter();

            foreach (var pair in values)
            {
                formatter.AppendArgument(pair.Key, pair.Value);
            }

            // Fix round 1 (2026-09-26): a translated ServerValidation* value may use {appName}, same as any
            // other Server* value (Fill). FluentValidation's own MessageFormatter has no such token, so it
            // would otherwise leave "{appName}" untouched instead of "Mangarr".
            formatter.AppendArgument("appName", "Mangarr");

            return formatter.BuildMessage(template);
        }

        // A placeholder value that is itself a Server word reads in the UI language too: SystemFolderValidator
        // passes "set to" / "child of" as {relationship}. Whole-value match on token-less ServerValidation keys.
        private Dictionary<string, object> Words(IDictionary<string, object> values, Context context)
        {
            return values.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is string word &&
                        context.Index.KeyByText.TryGetValue(word, out var key) &&
                        key.StartsWith("ServerValidation", StringComparison.Ordinal) &&
                        Translation(key, context, out _, out var value)
                    ? value
                    : pair.Value);
        }

        private CultureInfo UiCulture()
        {
            var iso = IsoLanguages.Get((Language)_configService.UILanguage);

            if (iso == null)
            {
                return null;
            }

            try
            {
                return CultureInfo.GetCultureInfo(iso.CountryCode.IsNullOrWhiteSpace() ? iso.TwoLetterCode : $"{iso.TwoLetterCode}-{iso.CountryCode.ToUpperInvariant()}");
            }
            catch (CultureNotFoundException)
            {
                return null;
            }
        }

        private sealed class Context
        {
            public Dictionary<string, string> En { get; set; }
            public Dictionary<string, string> Ui { get; set; }
            public ServerMessageIndex Index { get; set; }
        }
    }

    // The Server* templates of one en dictionary, by skeleton (spec section 1, step 2: an exact dictionary
    // lookup, no shape matching).
    public class ServerMessageIndex
    {
        public static readonly string[] Prefixes = { "ServerRejection", "ServerImport", "ServerQueue", "ServerProgress", "ServerValidation", "ServerTest", "ServerApi" };

        public Dictionary<string, string> KeyBySkeleton { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public Dictionary<string, string> KeyByText { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<string> ValidationTemplateKeys { get; } = new List<string>();
        public List<string> Duplicates { get; } = new List<string>();

        // Only these prefixes: en.json already has unrelated keys named Server and ServerUrl.
        public static bool IsServerKey(string key)
        {
            return Prefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));
        }

        public static string CustomValidatorKey(string errorCode)
        {
            const string suffix = "Validator";

            return "ServerValidation" + (errorCode.EndsWith(suffix, StringComparison.Ordinal) ? errorCode.Substring(0, errorCode.Length - suffix.Length) : errorCode);
        }

        public static ServerMessageIndex Build(IReadOnlyDictionary<string, string> en)
        {
            var index = new ServerMessageIndex();

            foreach (var (key, value) in en.Where(e => IsServerKey(e.Key)).OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                var parsed = ServerText.Parse(value);

                if (index.KeyBySkeleton.TryGetValue(parsed.Skeleton, out var first))
                {
                    index.Duplicates.Add($"{first} / {key}: {parsed.Skeleton.Replace(ServerText.Marker, '…')}");
                    continue;
                }

                index.KeyBySkeleton[parsed.Skeleton] = key;

                if (parsed.Holes.Count == 0)
                {
                    index.KeyByText[value] = key;
                }
                else if (key.StartsWith("ServerValidation", StringComparison.Ordinal))
                {
                    index.ValidationTemplateKeys.Add(key);
                }
            }

            return index;
        }
    }
}
