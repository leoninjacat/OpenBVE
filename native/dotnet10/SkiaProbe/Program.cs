using SkiaSharp;
using System.Runtime.InteropServices;

using var bitmap = new SKBitmap(
    new SKImageInfo(
        2,
        2,
        SKColorType.Rgba8888,
        SKAlphaType.Unpremul
    )
);

bitmap.SetPixel(0, 0, SKColors.Red);
bitmap.SetPixel(1, 0, SKColors.Green);
bitmap.SetPixel(0, 1, SKColors.Blue);
bitmap.SetPixel(1, 1, SKColors.Transparent);

Console.WriteLine($"Dimensões: {bitmap.Width}x{bitmap.Height}");
Console.WriteLine($"RowBytes: {bitmap.RowBytes}");
Console.WriteLine($"ColorType: {bitmap.ColorType}");
Console.WriteLine($"AlphaType: {bitmap.AlphaType}");

byte[] pixels = new byte[bitmap.RowBytes * bitmap.Height];

Marshal.Copy(
    bitmap.GetPixels(),
    pixels,
    0,
    pixels.Length
);

for (int y = 0; y < bitmap.Height; y++)
{
    for (int x = 0; x < bitmap.Width; x++)
    {
        int offset = y * bitmap.RowBytes + x * 4;

        Console.WriteLine(
            $"Pixel ({x},{y}): " +
            $"R={pixels[offset]} " +
            $"G={pixels[offset + 1]} " +
            $"B={pixels[offset + 2]} " +
            $"A={pixels[offset + 3]}"
        );
    }
}
