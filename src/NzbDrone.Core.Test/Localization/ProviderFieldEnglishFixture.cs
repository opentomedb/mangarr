using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using NzbDrone.Core.Annotations;
using NzbDrone.Test.Common;
using Readarr.Http.ClientSchema;

namespace NzbDrone.Core.Test.Localization
{
    // UI translations v1 (2026-09-25): every [FieldDefinition] Label/HelpText/HelpTextWarning as the API
    // renders it in English -- SchemaBuilder.ToSchema itself, over the real en.json -- captured before the
    // literals become keys. ToSchema, not GetFieldMappings: Task 10 moves the lookup into ToSchema, and
    // the rewrite must leave every line of this as it was.
    [TestFixture]
    public class ProviderFieldEnglishFixture : TestBase
    {
        [SetUp]
        public void Setup()
        {
            // SchemaBuilder's localization service is static: set it here, never inherit another fixture's.
            Mocker.SetConstant(EnglishLocalization.Create());
            SchemaBuilder.Initialize(Mocker.Container);
        }

        // Every settings type the API can render: concrete, with at least one [FieldDefinition] property
        // (declared or inherited). An abstract base renders through its subclasses.
        private static List<Type> SettingsTypes()
        {
            return typeof(FieldDefinitionAttribute).Assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters)
                .Where(t => t.GetProperties().Any(p => p.GetCustomAttribute<FieldDefinitionAttribute>(false) != null))
                .OrderBy(t => t.FullName, StringComparer.Ordinal)
                .ToList();
        }

        [Test]
        public void provider_field_english_is_pinned()
        {
            var fields = new SortedDictionary<string, object>(StringComparer.Ordinal);

            foreach (var type in SettingsTypes())
            {
                // A type that will not construct or render is a hole in the golden: fail, never skip.
                var model = Activator.CreateInstance(type);

                foreach (var field in SchemaBuilder.ToSchema(model))
                {
                    fields.Add($"{type.FullName}.{field.Name}", new
                    {
                        field.Label,
                        field.HelpText,
                        field.HelpTextWarning
                    });
                }
            }

            LocaleGolden.Pin("provider-fields", fields);
        }
    }
}
