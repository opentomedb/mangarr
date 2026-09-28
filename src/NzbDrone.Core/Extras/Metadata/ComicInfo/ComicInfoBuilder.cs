using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Extras.Metadata.ComicInfo
{
    // The metadata Mangarr owns for a single volume, mapped to ComicInfo.xml element names.
    // Null/blank fields are left untouched in an existing ComicInfo.xml rather than cleared,
    // so a value another writer (e.g. the mangadex-meta enricher) supplied is never erased.
    public class ComicInfoFields
    {
        public string Series { get; set; }
        public string Title { get; set; }
        public string Number { get; set; }       // volume number, canonical form ("6", "3.5")
        public string Summary { get; set; }
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? PageCount { get; set; }
        public string Publisher { get; set; }
        public string LanguageIso { get; set; }
        public int? Count { get; set; }           // total volumes, only when the series has ended
        public string Manga { get; set; }         // reading direction, e.g. "YesAndRightToLeft"
    }

    // Produces / updates a ComicInfo.xml document (the metadata Komga and other readers read from
    // inside a CBZ). MERGE semantics: an existing ComicInfo.xml is preserved element-for-element and
    // only the fields Mangarr owns are set — critically, the reading-direction <Manga> element and
    // any other foreign element survive untouched. Pure and I/O-free so it is fully unit-testable;
    // the archive write lives in ComicInfoEmbedder.
    public static class ComicInfoBuilder
    {
        // Elements Mangarr authoritatively owns. Only these are added/overwritten; everything else
        // in an existing document is carried through verbatim.
        private static readonly string[] OwnedElements =
        {
            "Series", "Title", "Number", "Summary", "Year", "Month", "Day", "PageCount", "Publisher", "LanguageISO", "Count"
        };

        public static string Merge(string existingXml, ComicInfoFields fields)
        {
            XDocument doc;
            XElement root;

            if (existingXml.IsNotNullOrWhiteSpace() && TryParse(existingXml, out doc) && doc.Root?.Name.LocalName == "ComicInfo")
            {
                root = doc.Root;
            }
            else
            {
                // Fresh document, matching the ComicInfo schema declarations Komga and the wider
                // ecosystem expect on the root element.
                XNamespace xsi = "http://www.w3.org/2001/XMLSchema-instance";
                XNamespace xsd = "http://www.w3.org/2001/XMLSchema";
                root = new XElement("ComicInfo",
                    new XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
                    new XAttribute(XNamespace.Xmlns + "xsd", xsd.NamespaceName));
                doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
            }

            Set(root, "Series", fields.Series);
            Set(root, "Title", fields.Title);
            Set(root, "Number", fields.Number);
            Set(root, "Summary", fields.Summary);
            Set(root, "Year", fields.Year);
            Set(root, "Month", fields.Month);
            Set(root, "Day", fields.Day);
            Set(root, "PageCount", fields.PageCount is > 0 ? fields.PageCount : null);
            Set(root, "Publisher", fields.Publisher);
            Set(root, "LanguageISO", fields.LanguageIso);
            Set(root, "Count", fields.Count is > 0 ? fields.Count : null);

            // Reading direction is set only when absent — never override a value another tool
            // curated (mangadex-meta writes it), and it's content the user might have corrected.
            SetIfAbsent(root, "Manga", fields.Manga);

            return Serialize(doc);
        }

        // True when the merged result would differ from the input — lets the embedder skip rewriting
        // (and re-hashing) an archive whose ComicInfo is already current. Compares normalized XML.
        public static bool WouldChange(string existingXml, ComicInfoFields fields)
        {
            var merged = Merge(existingXml, fields);

            if (existingXml.IsNullOrWhiteSpace())
            {
                return true;
            }

            return !TryParse(existingXml, out var doc) || Serialize(doc) != merged;
        }

        private static void Set(XElement root, string name, string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return; // never clear a field we simply don't have
            }

            var existing = root.Elements().FirstOrDefault(e => e.Name.LocalName == name);
            if (existing != null)
            {
                existing.Value = value;
            }
            else
            {
                root.Add(new XElement(name, value));
            }
        }

        private static void Set(XElement root, string name, int? value)
        {
            if (value.HasValue)
            {
                Set(root, name, value.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void SetIfAbsent(XElement root, string name, string value)
        {
            if (value.IsNullOrWhiteSpace() || root.Elements().Any(e => e.Name.LocalName == name))
            {
                return;
            }

            root.Add(new XElement(name, value));
        }

        private static bool TryParse(string xml, out XDocument doc)
        {
            try
            {
                doc = XDocument.Parse(xml);
                return true;
            }
            catch (System.Xml.XmlException)
            {
                doc = null;
                return false;
            }
        }

        private static string Serialize(XDocument doc)
        {
            // Match the encoding declaration ecosystem tools emit; XDocument.ToString() drops the
            // declaration, so build it explicitly.
            var body = doc.ToString(SaveOptions.None);
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + body;
        }
    }
}
