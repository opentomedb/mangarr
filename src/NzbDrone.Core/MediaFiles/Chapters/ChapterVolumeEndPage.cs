using System;
using System.IO;
using System.Linq;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace NzbDrone.Core.MediaFiles.Chapters
{
    // The closing page of a volume built from chapters: plain white, dark grey text, centred, in Roboto
    // Regular (embedded; Apache-2.0, see Resources/Fonts/Roboto-LICENSE.txt).
    public static class ChapterVolumeEndPage
    {
        public const int FallbackWidth = 1200;
        public const int FallbackHeight = 1800;

        private static readonly Lazy<FontFamily> Roboto = new Lazy<FontFamily>(LoadRoboto);

        // Whether Roboto has a glyph for every character of the text (a ja/ko/zh translation does not).
        public static bool CanDraw(string text)
        {
            var font = Roboto.Value.CreateFont(12);

            return text.EnumerateRunes()
                .Where(r => !System.Text.Rune.IsWhiteSpace(r))
                .All(r => font.TryGetGlyphs(new CodePoint(r.Value), ColorFontSupport.None, out var glyphs) && glyphs.Count > 0 && glyphs.All(g => g.GlyphMetrics.GlyphType != GlyphType.Fallback));
        }

        public static byte[] Render(int width, int height, string[] lines)
        {
            var font = Roboto.Value.CreateFont(width / 20f);
            var options = new RichTextOptions(font)
            {
                Origin = new PointF(width / 2f, height / 2f),
                WrappingLength = width * 0.8f,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            using var image = new Image<Rgba32>(width, height, Color.White);

            image.Mutate(c => c.DrawText(options, string.Join("\n\n", lines), Color.ParseHex("#333333")));

            using var output = new MemoryStream();
            image.Save(output, new PngEncoder());

            return output.ToArray();
        }

        private static FontFamily LoadRoboto()
        {
            var assembly = typeof(ChapterVolumeEndPage).Assembly;
            var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("Roboto-Regular.ttf", StringComparison.Ordinal));

            using var stream = assembly.GetManifestResourceStream(name);
            var collection = new FontCollection();

            return collection.Add(stream);
        }
    }
}
