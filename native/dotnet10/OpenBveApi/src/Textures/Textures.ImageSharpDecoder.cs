using System;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OpenBveApi.Textures
{
    /// <summary>
    /// Decodes image files into native OpenBVE RGBA textures.
    /// This implementation does not depend on System.Drawing.
    /// </summary>
    public static class ImageSharpDecoder
    {
        public static Texture Decode(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(
                    "Image path cannot be empty.", nameof(path));
            }

            using Image<Rgba32> image =
                Image.Load<Rgba32>(path);

            return Convert(image);
        }

        public static Texture Decode(byte[] encodedBytes)
        {
            if (encodedBytes == null)
            {
                throw new ArgumentNullException(nameof(encodedBytes));
            }

            using Image<Rgba32> image =
                Image.Load<Rgba32>(encodedBytes);

            return Convert(image);
        }

        private static Texture Convert(Image<Rgba32> image)
        {
            int width = image.Width;
            int height = image.Height;

            byte[] pixels = new byte[
                checked(width * height * 4)
            ];

            image.CopyPixelDataTo(pixels);

            return new Texture(
                width,
                height,
                PixelFormat.RGBAlpha,
                pixels
            ,
                (global::OpenBveApi.Colors.Color24[])null
            );
        }
    }
}
