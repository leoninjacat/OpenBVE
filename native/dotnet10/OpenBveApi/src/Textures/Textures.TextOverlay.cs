using System;
using System.Drawing;
using SkiaSharp;
using OpenBveApi.Math;

namespace OpenBveApi.Textures
{
    public static class TextOverlay
    {
        public static SKBitmap AddTextToBitmap(
            SKBitmap bitmap,
            string txt,
            string fontName,
            int fontSize,
            Color bgColor,
            Color fColor,
            Vector2 Padding)
        {
            using var typeface = SKTypeface.FromFamilyName(fontName)
                ?? SKTypeface.Default;

            using var font = new SKFont(typeface, fontSize);

            using var paint = new SKPaint
            {
                Color = new SKColor(fColor.R, fColor.G, fColor.B, fColor.A),
                IsAntialias = true
            };

            float textWidth = font.MeasureText(txt ?? "");
            var metrics = font.Metrics;
            float textHeight = metrics.Descent - metrics.Ascent;

            bool overlay = bitmap != null;

            if (!overlay)
            {
                int width = System.Math.Max(1, (int)System.Math.Ceiling(textWidth + Padding.X * 2));
                int height = System.Math.Max(1, (int)System.Math.Ceiling(textHeight + Padding.Y * 2));

                bitmap = new SKBitmap(
                    new SKImageInfo(width, height,
                        SKColorType.Rgba8888,
                        SKAlphaType.Unpremul));

                bitmap.Erase(new SKColor(
                    bgColor.R, bgColor.G, bgColor.B, bgColor.A));
            }

            using (var canvas = new SKCanvas(bitmap))
            {
                float x = (float)Padding.X;
                float y = (float)Padding.Y - metrics.Ascent;

                canvas.DrawText(txt ?? "", x, y, font, paint);
                canvas.Flush();
            }

            return bitmap;
        }

        public static bool FontAvailable(string fontName)
        {
            using var typeface = SKTypeface.FromFamilyName(fontName);
            return typeface != null &&
                   string.Equals(typeface.FamilyName, fontName,
                       StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
