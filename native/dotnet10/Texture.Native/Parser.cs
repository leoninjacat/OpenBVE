using System;
using System.IO;
using System.Linq;
using System.Text;
using OpenBveApi.Textures;
using OpenBveApi.Math;
using Plugin.BMP;
using Plugin.GIF;
namespace Plugin { public partial class Plugin {
		private bool Parse(string file, out Texture texture)
		{
			/*
			 * First, check the header bytes for a known signature,
			 * then pass the file to the matching dedicated decoder.
			 */
			try
			{
				bool gifHeader = false;
				bool bmpHeader = false;
				bool pngHeader = false;
				using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read))
				{
					byte[] buffer = new byte[6];
					if (fs.Length > buffer.Length)
					{
						// ReSharper disable once MustUseReturnValue
						fs.ReadExactly(buffer);
					}
					gifHeader = buffer.SequenceEqual(GifDecoder.GIF87Header) || buffer.SequenceEqual(GifDecoder.GIF89Header);
					bmpHeader = Encoding.ASCII.GetString(buffer, 0, 2) == "BM";
					pngHeader = Encoding.ASCII.GetString(buffer, 1, 3) == "PNG";
				}

				if (gifHeader)
				{
					using (GifDecoder decoder = new GifDecoder())
					{
						decoder.Read(file);
						int frameCount = decoder.GetFrameCount();
						if (frameCount > 0)
						{
							Vector2 frameSize = decoder.GetFrameSize();
							int duration = 0;
							for (int i = 0; i < frameCount; i++) duration += decoder.GetDuration(i);
							double interval = frameCount > 0 ? ((double)duration / frameCount) / 10000000.0 : 0;
						if (frameCount >= 1)
						{
							var palette = decoder.GetPalette();
							// Only use the paletted fast path when EVERY frame decoded as indexed:
							// a mid-stream palette merge failure (>256 total colors) leaves later
							// frames as RGBA, which the fallback path below expands correctly.
							if (palette != null && decoder.IsFullyPaletted())
							{
								if (frameCount == 1)
								{
									texture = new Texture((int)frameSize.X, (int)frameSize.Y, OpenBveApi.Textures.PixelFormat.Paletted, decoder.GetIndexedFrame(0), palette);
									return true;
								}
								byte[][] frameBytes = new byte[frameCount][];
								for (int i = 0; i < frameCount; i++) frameBytes[i] = decoder.GetIndexedFrame(i);
								texture = new Texture((int)frameSize.X, (int)frameSize.Y, OpenBveApi.Textures.PixelFormat.Paletted, frameBytes, palette, interval);
								return true;
							}
						}
						// Fallback RGBA (multi-frame with >256 total colors or mixed indexed/RGBA frames; single-frame failures use the GDI+ path below)
						if (frameCount != 1)
						{
							byte[][] frameBytes = new byte[frameCount][];
								for (int i = 0; i < frameCount; i++)
								{
									int[] framePixels = decoder.GetFrame(i);
									if (framePixels == null)
									{
										// Should not happen (every decoded frame has indexed or RGBA data),
										// but never hand a null frame to Texture - it would NRE downstream.
										frameBytes[i] = new byte[(int)frameSize.X * (int)frameSize.Y * sizeof(int)];
										continue;
									}
									frameBytes[i] = new byte[framePixels.Length * sizeof(int)];
									Buffer.BlockCopy(framePixels, 0, frameBytes[i], 0, frameBytes[i].Length);
								}
								texture = new Texture((int)frameSize.X, (int)frameSize.Y, OpenBveApi.Textures.PixelFormat.RGBAlpha, frameBytes, interval);
								return true;
							}
						}
					}
				}

				if (bmpHeader)
				{
					using (BmpDecoder decoder = new BmpDecoder())
					{
						if (decoder.Read(file))
						{
							texture = new Texture(decoder.Width, decoder.Height, OpenBveApi.Textures.PixelFormat.RGBAlpha, decoder.ImageData, decoder.ColorTable);
							return true;
						}
					}
				}

				if (pngHeader)
				{
					// RGB PNGs can use a tRNS color key without a PLTE palette.
					// Skia supports this and rejects incomplete pixel data.
					texture = SkiaDecoder.Decode(file);
					return true;
				}
				texture = SkiaDecoder.Decode(file);
				return true;
			}
			catch
			{
				texture = null;
				return false;
			}
        }

} }
