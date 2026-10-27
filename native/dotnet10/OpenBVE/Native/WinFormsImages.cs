#if WINDOWS
namespace OpenBve.Native;
internal static class WinFormsImages
{
    internal static System.Drawing.Image FromFile(string path)
    {
        using var input = System.IO.File.OpenRead(path);
        using var image = System.Drawing.Image.FromStream(input);
        return new System.Drawing.Bitmap(image);
    }
    internal static System.Drawing.Bitmap Convert(SkiaSharp.SKBitmap image)
    {
        if(image == null) return null;
        using var stream = new System.IO.MemoryStream();
        using var skImage = SkiaSharp.SKImage.FromBitmap(image);
        using var data = skImage.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream); stream.Position = 0;
        using var source = System.Drawing.Image.FromStream(stream);
        return new System.Drawing.Bitmap(source);
    }
}
internal static class WinFormsIllustrations
{
    internal static System.Drawing.Bitmap CreateRouteMap(int Width, int Height, bool inGame, out System.Collections.Generic.Dictionary<System.Guid, OpenBveApi.Math.Vector2> switchPositions, OpenBveApi.Routes.TrackFollower follower = null, int drawRadius = 500, Drawing.Font font = null)
    {
        using var map = RouteManager2.Illustrations.CreateRouteMap(Width, Height, inGame, out switchPositions, follower, drawRadius, font);
        return WinFormsImages.Convert(map);
    }
    internal static System.Drawing.Bitmap CreateRouteGradientProfile(int Width, int Height, bool inGame, Drawing.Font font = null)
    {
        using var map = RouteManager2.Illustrations.CreateRouteGradientProfile(Width, Height, inGame, font);
        return WinFormsImages.Convert(map);
    }
}
#endif
