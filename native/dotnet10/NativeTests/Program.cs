using System;
using System.Drawing;
using System.IO;
using OpenBve.Native.Drawing;
using OpenBve.Native.Drawing.Imaging;
using OpenBveApi.Textures;
using SkiaSharp;
using Bitmap = OpenBve.Native.Drawing.Bitmap;
using Graphics = OpenBve.Native.Drawing.Graphics;
using Font = OpenBve.Native.Drawing.Font;
using FontFamily = OpenBve.Native.Drawing.FontFamily;
using GraphicsUnit = OpenBve.Native.Drawing.GraphicsUnit;
using PixelFormat = OpenBve.Native.Drawing.Imaging.PixelFormat;
class Tests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Main()
    {
        Check(OpenBveApi.Hosts.HostInterface.Win32PluginHostReady != null &&
              OpenBveApi.Hosts.HostInterface.Win32PluginHostStopped != null,
              "Host static initialization on Linux (no named Windows events)");
        using var bitmap = new Bitmap(3,2);
        bitmap.SetPixel(1,0,Color.FromArgb(128,240,20,60));
        var locked = bitmap.LockBits(new Rectangle(0,0,3,2),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
        Check(locked.Stride == 12, "BGR alignment");
        byte[] data = new byte[24];
        System.Runtime.InteropServices.Marshal.Copy(locked.Scan0,data,0,data.Length);
        Check(Math.Abs(data[3]-60) <= 1 && Math.Abs(data[5]-240) <= 1, "BGR channel order");
        bitmap.UnlockBits(locked);
        Texture source = new Texture(bitmap);
        Check(source.Origin.GetTexture(out var decoded), "Skia texture origin");
        Check(Math.Abs(decoded.GetPixel(1).R-240) <= 1 && Math.Abs(decoded.GetAlpha(1)-128) <= 1, "RGBA texture / alpha");
        var copied = new Texture(decoded);
        Check(copied.Origin.GetTexture(out var raw) && ReferenceEquals(raw,decoded), "Raw texture registration");
        using var font = new Font(FontFamily.GenericSansSerif,16,GraphicsUnit.Pixel);
        using var graphics = Graphics.FromImage(bitmap);
        Check(graphics.MeasureString("OpenBVE",font).Width > 10, "Font measurement");
        string path = Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".png");
        try {
            using(var stream = File.Create(path)) bitmap.Save(stream,ImageFormat.Png);
            var plugin = new Plugin.Plugin();
            plugin.Load(new TestHost(), null, new TestOptions());
            Check(plugin.QueryTextureDimensions(path,out int width,out int height) && width==3 && height==2,"Texture dimensions");
            Check(plugin.LoadTexture(path,out var texture) && texture.GetAlpha(1)==128,"Texture plugin alpha");
        } finally { File.Delete(path); }
        using (var nativeBitmap = new SKBitmap(new SKImageInfo(2, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul))) {
            nativeBitmap.SetPixel(0, 0, new SKColor(240, 20, 60, 128));
            using var image = SKImage.FromBitmap(nativeBitmap);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            var decodedPng = SkiaDecoder.Decode(png.ToArray());
            Check(decodedPng.GetPixel(0).R == 240 && decodedPng.GetAlpha(0) == 128, "Skia fallback RGBA");
            using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95);
            Check(SkiaDecoder.Decode(jpeg.ToArray()).Width == 2, "JPEG fallback");
        }
        var gifBytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQICgAAACwAAAAAAgABAAAIBQABAAgIACH5BAgKAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
        var palette = new[] { new OpenBveApi.Colors.Color32(255,0,0,255), new OpenBveApi.Colors.Color32(0,0,255,255) };
        var gif = new StreamingGifOrigin("test.gif", gifBytes, 2, 1, palette, 2, 0.1);
        Check(gif.GetFrameBytes(1)[0] == 1, "GIF second frame");
        Check(gif.GetFrameBytes(0)[0] == 0, "GIF first frame after seek");
        Check(gif.GetFrameBytes(1)[0] == 1, "GIF repeat seek");
        try { SkiaDecoder.Decode(new byte[] { 0, 1, 2 }); throw new Exception("Invalid image accepted"); }
        catch (InvalidDataException) { }
        Console.WriteLine("Native graphics/texture tests passed.");
    }
}

sealed class TestOptions : OpenBveApi.BaseOptions { public override void Save(string path) => throw new NotSupportedException(); }
sealed class TestHost : OpenBveApi.Hosts.HostInterface {
    public TestHost() : base(OpenBveApi.Hosts.HostApplication.OpenBve) { }
    public override OpenBveApi.Trains.AbstractTrain ParseTrackFollowingObject(string file, string folder) => throw new NotSupportedException();
}
