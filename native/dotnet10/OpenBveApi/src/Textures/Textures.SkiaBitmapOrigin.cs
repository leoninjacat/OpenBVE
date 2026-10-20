using System;
using SkiaSharp;

namespace OpenBveApi.Textures
{
    /// <summary>
    /// Cross-platform bitmap texture origin backed by SkiaSharp.
    /// </summary>
    public sealed class SkiaBitmapOrigin : TextureOrigin
    {
        private readonly byte[] pixels;
        private readonly int width;
        private readonly int height;
        private readonly TextureParameters parameters;

        public SkiaBitmapOrigin(
            SKBitmap bitmap,
            TextureParameters parameters = null)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            width = bitmap.Width;
            height = bitmap.Height;

            using var rgbaBitmap = new SKBitmap(
                new SKImageInfo(
                    width,
                    height,
                    SKColorType.Rgba8888,
                    SKAlphaType.Unpremul
                )
            );

            // CopyTo may recreate the destination with the source BGRA / premultiplied
            // format. ReadPixels explicitly converts into the requested RGBA layout.
            using var sourceImage = SKImage.FromBitmap(bitmap);
            if (!sourceImage.ReadPixels(rgbaBitmap.Info, rgbaBitmap.GetPixels(), rgbaBitmap.RowBytes, 0, 0))
                throw new InvalidOperationException(
                    "Unable to convert bitmap to RGBA8888.");

            pixels = new byte[checked(width * height * 4)];

            int rowSize = checked(width * 4);
            int rowBytes = rgbaBitmap.RowBytes;
            IntPtr basePointer = rgbaBitmap.GetPixels();

            if (basePointer == IntPtr.Zero || rowBytes < rowSize)
                throw new InvalidOperationException(
                    "Invalid SkiaSharp bitmap pixel buffer.");

            for (int y = 0; y < height; y++)
            {
                IntPtr rowPointer = IntPtr.Add(
                    basePointer,
                    checked(y * rowBytes)
                );

                System.Runtime.InteropServices.Marshal.Copy(
                    rowPointer,
                    pixels,
                    y * rowSize,
                    rowSize
                );
            }

            this.parameters = parameters;
        }

        public override bool GetTexture(out Texture texture)
        {
            texture = new Texture(
                width,
                height,
                PixelFormat.RGBAlpha,
                (byte[])pixels.Clone()
            ,
                (global::OpenBveApi.Colors.Color24[])null
            );

            texture = texture.ApplyParameters(parameters);
            return true;
        }
    }
}
