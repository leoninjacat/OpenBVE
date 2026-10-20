// Portable implementations of the small drawing surface used by the game.
// Uses System.Drawing.Primitives only; no GDI+, WinForms or Mono dependency.
using System;
using System.Drawing;
using System.IO;
using SkiaSharp;
namespace OpenBve.Native.Drawing
{
    public enum FontStyle { Regular, Bold, Italic }
    public enum GraphicsUnit { Pixel, Point }
    public enum StringAlignment { Near, Center, Far }
    public sealed class FontFamily
    {
        public string Name { get; }
        public FontFamily(string name) { Name = name; }
        public static FontFamily GenericSansSerif => new FontFamily("sans-serif");
    }
    public sealed class Font : IDisposable
    {
        public FontFamily FontFamily { get; }
        public string Name => FontFamily.Name;
        public float Size { get; }
        internal SKTypeface Typeface { get; }
        internal SKFont Native { get; }
        public Font(FontFamily family, float size, GraphicsUnit unit) : this(family, size, FontStyle.Regular, unit) {}
        public Font(string family, float size) : this(new FontFamily(family), size, FontStyle.Regular, GraphicsUnit.Pixel) {}
        public Font(FontFamily family, float size, FontStyle style, GraphicsUnit unit)
        {
            FontFamily = family; Size = size;
            Typeface = SKTypeface.FromFamilyName(family.Name, style == FontStyle.Bold ? SKFontStyle.Bold : SKFontStyle.Normal);
            Native = new SKFont(Typeface, size) { Edging = SKFontEdging.Antialias };
            // The NoDependencies Skia build can have no system font manager.
            // Resolve an installed font file through fontconfig in that case.
            if (Native.MeasureText("M") <= 0) {
                Native.Dispose(); Typeface?.Dispose();
                Typeface = SKTypeface.FromFile(FontFiles.Resolve(family.Name, style));
                Native = new SKFont(Typeface, size) { Edging = SKFontEdging.Antialias };
                if (Native.MeasureText("M") <= 0) throw new InvalidOperationException("No usable font found. Install fontconfig and a sans-serif font.");
            }
        }
        public void Dispose() { Native.Dispose(); Typeface.Dispose(); }
    }
    internal static class FontFiles
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string,string> cache = new();
        internal static string Resolve(string family, FontStyle style) => cache.GetOrAdd(family + ":style=" + style, pattern => {
            var start = new System.Diagnostics.ProcessStartInfo("fc-match") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-f"); start.ArgumentList.Add("%{file}"); start.ArgumentList.Add("--"); start.ArgumentList.Add(pattern);
            using var process = System.Diagnostics.Process.Start(start);
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(3000)) { process.Kill(true); throw new InvalidOperationException("fontconfig resolution timed out."); }
            string path = output.GetAwaiter().GetResult().Trim();
            if (process.ExitCode != 0 || !File.Exists(path)) throw new InvalidOperationException("Cannot resolve system font: " + pattern);
            return path;
        });
    }
    public sealed class StringFormat : IDisposable
    {
        public StringAlignment Alignment { get; set; }
        public StringAlignment LineAlignment { get; set; }
        public static StringFormat GenericDefault => new StringFormat();
        public static StringFormat GenericTypographic => new StringFormat();
        public void Dispose() {}
    }
    public class Brush : IDisposable
    {
        internal Color Color;
        public Brush(Color color) { Color = color; }
        public void Dispose() {}
    }
    public sealed class SolidBrush : Brush { public SolidBrush(Color color) : base(color) {} }
    public sealed class Pen : IDisposable
    {
        internal Color Color;
        internal float Width;
        public Pen(Color color, float width = 1) { Color = color; Width = width; }
        public void Dispose() {}
    }
    public class Bitmap : SKBitmap
    {
        public Imaging.PixelFormat PixelFormat { get; }
        public Bitmap(int width, int height) : this(width, height, Imaging.PixelFormat.Format32bppArgb) {}
        public Bitmap(int width, int height, Imaging.PixelFormat format) : base(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul)) { PixelFormat = format; Erase(SKColors.Transparent); }
        public Bitmap(string path) : this(Decode(path) ?? throw new InvalidDataException("Cannot decode image: " + path), true) {}
        public Bitmap(Stream stream) : this(Decode(stream) ?? throw new InvalidDataException("Cannot decode image stream"), true) {}
        public Bitmap(SKBitmap source) : this(source, false) {}
        private Bitmap(SKBitmap source, bool dispose) : this(source.Width, source.Height)
        {
            using var canvas = new SKCanvas(this); canvas.DrawBitmap(source, 0, 0);
            if (dispose) source.Dispose();
        }
        public static Bitmap FromFile(string path) => new Bitmap(path);
        public new Color GetPixel(int x, int y) { var c = base.GetPixel(x,y); return Color.FromArgb(c.Alpha,c.Red,c.Green,c.Blue); }
        public void SetPixel(int x, int y, Color color) => base.SetPixel(x,y,new SKColor(color.R,color.G,color.B,color.A));
        public void Save(Stream stream, Imaging.ImageFormat format)
        {
            using var image = SKImage.FromBitmap(this);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100); data.SaveTo(stream);
        }
        public Imaging.BitmapData LockBits(Rectangle bounds, Imaging.ImageLockMode mode, Imaging.PixelFormat format)
        {
            if (bounds != new Rectangle(0, 0, Width, Height)) throw new NotSupportedException("Partial bitmap locks are not supported.");
            // GDI callers requesting 24-bit data need packed BGR rows.
            int channels = format == Imaging.PixelFormat.Format24bppRgb ? 3 : 4;
            int stride = (Width * channels + 3) & ~3;
            byte[] bytes = new byte[stride * Height];
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) {
                SKColor c = base.GetPixel(x,y); int i = y * stride + x * channels;
                bytes[i] = c.Blue; bytes[i+1] = c.Green; bytes[i+2] = c.Red;
                if (channels == 4) bytes[i+3] = c.Alpha;
            }
            return new Imaging.BitmapData(bytes, Width, Height, stride, channels, mode);
        }
        public void UnlockBits(Imaging.BitmapData data)
        {
            if (data.Mode != Imaging.ImageLockMode.ReadOnly)
                for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++) {
                    int i = y * data.Stride + x * data.Channels;
                    base.SetPixel(x,y,new SKColor(data.Bytes[i+2], data.Bytes[i+1], data.Bytes[i], data.Channels == 4 ? data.Bytes[i+3] : (byte)255));
                }
            data.Dispose();
        }
    }
    public sealed class Graphics : IDisposable
    {
        private readonly SKCanvas canvas;
        private Graphics(Bitmap bitmap) { canvas = new SKCanvas(bitmap); }
        public static Graphics FromImage(Bitmap bitmap) => new Graphics(bitmap);
        public Drawing2D.SmoothingMode SmoothingMode { get; set; }
        public Text.TextRenderingHint TextRenderingHint { get; set; }
        private static SKColor ColorOf(Color c) => new SKColor(c.R,c.G,c.B,c.A);
        private static SKPaint Paint(Brush b) => new SKPaint { Color = ColorOf(b.Color), IsAntialias = true };
        private static SKPaint Paint(Pen p) => new SKPaint { Color = ColorOf(p.Color), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = p.Width };
        public void Clear(Color c) => canvas.Clear(ColorOf(c));
        public SizeF MeasureString(string text, Font font, int width = int.MaxValue, StringFormat format = null)
        {
            float w = 0; string[] lines = text.Split('\n');
            foreach (string line in lines) w = Math.Max(w, font.Native.MeasureText(line));
            return new SizeF(w, lines.Length * font.Native.Spacing);
        }
        public void DrawString(string text, Font font, Brush brush, PointF point) => DrawString(text,font,brush,point.X,point.Y);
        public void DrawString(string text, Font font, Brush brush, float x, float y)
        {
            using var paint = Paint(brush);
            foreach (string line in text.Split('\n')) {
                canvas.DrawText(line.TrimEnd('\r'), x, y - font.Native.Metrics.Ascent, font.Native, paint);
                y += font.Native.Spacing;
            }
        }
        public void FillRectangle(Brush b, RectangleF r) => FillRectangle(b,r.X,r.Y,r.Width,r.Height);
        public void DrawString(string t, Font f, Brush b, RectangleF r) => DrawString(t,f,b,r.X,r.Y);
        public void FillEllipse(Brush b, RectangleF r) => FillEllipse(b,r.X,r.Y,r.Width,r.Height);
        public void DrawEllipse(Pen p, RectangleF r) => DrawEllipse(p,r.X,r.Y,r.Width,r.Height);
        public void DrawLine(Pen p, PointF a, PointF b) => DrawLine(p,a.X,a.Y,b.X,b.Y);
        public void DrawString(string text, Font font, Brush brush, PointF point, StringFormat format) => DrawString(text,font,brush,point.X,point.Y,format);
        public void DrawString(string text, Font font, Brush brush, float x, float y, StringFormat format)
        {
            var size = MeasureString(text,font);
            x -= format.Alignment == StringAlignment.Center ? size.Width / 2 : format.Alignment == StringAlignment.Far ? size.Width : 0;
            y -= format.LineAlignment == StringAlignment.Center ? size.Height / 2 : format.LineAlignment == StringAlignment.Far ? size.Height : 0;
            DrawString(text,font,brush,x,y);
        }
        public void DrawCurve(Pen pen, PointF[] points, int offset, int segments)
        {
            using var path = new SKPath();
            if (segments < 1 || points.Length < 2) return;
            path.MoveTo(points[offset].X,points[offset].Y);
            for (int i=offset; i<offset+segments && i+1<points.Length; i++) {
                PointF a=points[Math.Max(0,i-1)], b=points[i], c=points[i+1], d=points[Math.Min(points.Length-1,i+2)];
                path.CubicTo(b.X+(c.X-a.X)/6,b.Y+(c.Y-a.Y)/6,c.X-(d.X-b.X)/6,c.Y-(d.Y-b.Y)/6,c.X,c.Y);
            }
            using var paint = Paint(pen); canvas.DrawPath(path,paint);
        }
        public void DrawLine(Pen pen, float x1,float y1,float x2,float y2) { using var p = Paint(pen); canvas.DrawLine(x1,y1,x2,y2,p); }
        public void DrawRectangle(Pen pen,float x,float y,float w,float h) { using var p = Paint(pen); canvas.DrawRect(x,y,w,h,p); }
        public void FillRectangle(Brush brush,float x,float y,float w,float h) { using var p = Paint(brush); canvas.DrawRect(x,y,w,h,p); }
        public void DrawEllipse(Pen pen,float x,float y,float w,float h) { using var p = Paint(pen); canvas.DrawOval(new SKRect(x,y,x+w,y+h),p); }
        public void FillEllipse(Brush brush,float x,float y,float w,float h) { using var p = Paint(brush); canvas.DrawOval(new SKRect(x,y,x+w,y+h),p); }
        public void FillPolygon(Brush brush, PointF[] points) { using var path = new SKPath(); if (points.Length == 0) return; path.MoveTo(points[0].X,points[0].Y); foreach(var point in points) path.LineTo(point.X,point.Y); path.Close(); using var p = Paint(brush); canvas.DrawPath(path,p); }
        public void DrawCurve(Pen pen, PointF[] points) => DrawCurve(pen,points,0,points.Length-1);
        public void DrawImage(Bitmap image,float x,float y,float w,float h) => canvas.DrawBitmap(image,new SKRect(x,y,x+w,y+h));
        public void DrawImage(Bitmap image,float x,float y) => canvas.DrawBitmap(image,x,y);
        public void TranslateTransform(float x,float y, Drawing2D.MatrixOrder order = Drawing2D.MatrixOrder.Prepend) => canvas.Translate(x,y);
        public void RotateTransform(float angle) => canvas.RotateDegrees(angle);
        public void ResetTransform() => canvas.ResetMatrix();
        public void Dispose() => canvas.Dispose();
    }
}
namespace OpenBve.Native.Drawing.Imaging
{
    public enum PixelFormat { Format32bppArgb, Format32bppPArgb, Format24bppRgb }
    public enum ImageLockMode { ReadOnly, ReadWrite, WriteOnly }
    public sealed class ImageFormat { public static ImageFormat Png = new ImageFormat(); }
    public sealed class BitmapData : IDisposable
    {
        internal byte[] Bytes; internal int Channels; internal ImageLockMode Mode;
        private System.Runtime.InteropServices.GCHandle pin;
        public int Width { get; } public int Height { get; } public int Stride { get; }
        public IntPtr Scan0 => pin.AddrOfPinnedObject();
        internal BitmapData(byte[] bytes,int width,int height,int stride,int channels,ImageLockMode mode) { Bytes=bytes; Width=width; Height=height; Stride=stride; Channels=channels; Mode=mode; pin=System.Runtime.InteropServices.GCHandle.Alloc(bytes,System.Runtime.InteropServices.GCHandleType.Pinned); }
        public void Dispose() { if(pin.IsAllocated) pin.Free(); }
    }
}
namespace OpenBve.Native.Drawing.Text { public enum TextRenderingHint { ClearTypeGridFit, AntiAlias } }
namespace OpenBve.Native.Drawing.Drawing2D { public enum SmoothingMode { AntiAlias } public enum MatrixOrder { Append, Prepend } }
