using System;
using System.IO;
using SkiaSharp;

namespace OpenBveApi.Textures
{
    /// <summary>Decodes native textures with the same Skia backend used for drawing.</summary>
    public static class SkiaDecoder
    {
        public static Texture Decode(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Image path cannot be empty.", nameof(path));
            return Decode(File.ReadAllBytes(path));
        }

        public static Texture Decode(byte[] encodedBytes)
        {
            using var bitmap = DecodeBitmap(encodedBytes);
            new SkiaBitmapOrigin(bitmap).GetTexture(out var texture);
            return texture;
        }

        internal static SKBitmap DecodeBitmap(byte[] encodedBytes, int frame = 0)
        {
            ArgumentNullException.ThrowIfNull(encodedBytes);
            using var data = SKData.CreateCopy(encodedBytes);
            using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Unsupported image format.");
            int target = System.Math.Clamp(frame, 0, System.Math.Max(0, codec.FrameCount - 1));
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bitmap = new SKBitmap(info);
            var result = codec.GetPixels(info, bitmap.GetPixels(), new SKCodecOptions(target));
            if (result != SKCodecResult.Success)
            {
                bitmap.Dispose();
                throw new InvalidDataException("Unable to decode image: " + result);
            }
            return bitmap;
        }
    }
}
