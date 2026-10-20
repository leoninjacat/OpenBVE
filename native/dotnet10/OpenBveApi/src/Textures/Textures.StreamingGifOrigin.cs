#pragma warning disable 0659,0661
using System;
using System.IO;
using OpenBveApi.Colors;
using OpenBveApi.Math;

namespace OpenBveApi.Textures
{
	/// <summary>Streaming origin for large animated GIFs</summary>
	/// <remarks>Allows OpenGL to swap the decoded file bytes of the current frame in memory using GL.TexSubImage, rather than decoding all frames</remarks>
	public class StreamingGifOrigin : TextureOrigin
	{
		private readonly string Path;
		private readonly byte[] FileBytes;
		private readonly int Width;
		private readonly int Height;
		private readonly Color32[] Palette;
		private readonly int FrameCount;
		private readonly double Interval;
		private readonly Vector2 Size;
		private int cachedFrame = -1;
		private byte[] cachedBytes;
		private readonly object sync = new object();
		private Func<int, byte[]> frameDecoder;

		/// <summary>Creates a new StreamingGifOrigin</summary>
		/// <param name="path">The on-disk path to the GIF texture</param>
		/// <param name="fileBytes">The encoded bytes</param>
		/// <param name="width">The width of the texture</param>
		/// <param name="height">The height of the texture</param>
		/// <param name="palette">The pallette of used colors</param>
		/// <param name="frameCount">The total number of frames in the GIF</param>
		/// <param name="interval">The frame interval</param>
		public StreamingGifOrigin(string path, byte[] fileBytes, int width, int height, Color32[] palette, int frameCount, double interval)
		{
			Path = path;
			FileBytes = fileBytes;
			Width = width;
			Height = height;
			Palette = palette;
			FrameCount = frameCount;
			Interval = interval;
			Size = new Vector2(width, height);
		}
		public void SetDecoder(Func<int, byte[]> decoder) => frameDecoder = decoder;

		/// <summary>Gets the number of frames in the GIF</summary>
		public int GetFrameCount() => FrameCount;

		/// <summary>Gets the frame interval for the GIF</summary>
		public double GetInterval() => Interval;

		/// <summary>Gets the texture size of the GIF</summary>
		public Vector2 GetSize() => Size;

		/// <summary>Gets the used color pallette of the GIF</summary>
		public Color32[] GetPalette() => Palette;

		/// <summary>Create a copy of this origin with a modified palette (for transparency changes)</summary>
		public StreamingGifOrigin WithModifiedPalette(Color32[] newPalette)
		{
			return new StreamingGifOrigin(Path, FileBytes, Width, Height, newPalette, FrameCount, Interval) { frameDecoder = this.frameDecoder };
		}

		/// <summary>Get bytes for specific frame – desktop on-demand decode (1 frame cached) to stop RAM naik terus</summary>
		public byte[] GetFrameBytes(int frame)
		{
			lock (sync)
			{
				if (cachedFrame == frame && cachedBytes != null) return cachedBytes;
				if (frameDecoder != null)
				{
					try
					{
						var b = frameDecoder(frame);
						if (b != null) { cachedFrame = frame; cachedBytes = b; return b; }
					}
					catch { }
				}
				// Linux-native fallback: decode the requested GIF frame.
				try
				{
                    using var image = SkiaDecoder.DecodeBitmap(FileBytes, frame);
                    if (image.Width != Width || image.Height != Height)
                        throw new InvalidDataException("GIF dimensions do not match texture.");
					byte[] bytes = new byte[checked(Width * Height)];

					for (int y = 0; y < Height; y++)
					{
						for (int x = 0; x < Width; x++)
						{
							var pixel = image.GetPixel(x, y);
							int bestIndex = 0;
							int bestDistance = int.MaxValue;

							for (int i = 0; i < Palette.Length && i < 256; i++)
							{
								int dr = pixel.Red - Palette[i].R;
								int dg = pixel.Green - Palette[i].G;
								int db = pixel.Blue - Palette[i].B;
								int da = pixel.Alpha - Palette[i].A;

								int distance =
									dr * dr + dg * dg + db * db + da * da;

								if (distance < bestDistance)
								{
									bestDistance = distance;
									bestIndex = i;
								}
							}

							bytes[y * Width + x] = (byte)bestIndex;
						}
					}

					cachedFrame = frame;
					cachedBytes = bytes;
					return bytes;
				}
				catch
				{
					return cachedBytes;
				}

			}
		}

		/// <inheritdoc/>
		public override bool GetTexture(out Texture texture)
		{
			texture = new Texture(this);
			return true;
		}

		/// <summary>Checks whether two StreamingGifOrigin are equal</summary>
		/// <param name="a">The first StreamingGifOrigin</param>
		/// <param name="b">The second StreamingGifOrigin</param>
		/// <returns>Whether the two origins are equal</returns>
		public static bool operator ==(StreamingGifOrigin a, StreamingGifOrigin b)
		{
			if (ReferenceEquals(a, b)) return true;
			if (a is null || b is null) return false;
			return a.Path == b.Path;
		}

		/// <summary>Checks whether two StreamingGifOrigin are unequal</summary>
		/// <param name="a">The first StreamingGifOrigin</param>
		/// <param name="b">The second StreamingGifOrigin</param>
		/// <returns>Whether the two origins are unequal</returns>
		public static bool operator !=(StreamingGifOrigin a, StreamingGifOrigin b) => !(a == b);
		
		/// <inheritdoc/>
		public override bool Equals(object obj) => obj is StreamingGifOrigin o && Path == o.Path;

		/// <inheritdoc/>
		public override int GetHashCode() => Path?.GetHashCode() ?? 0;
	}
}
