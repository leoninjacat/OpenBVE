using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
namespace OpenBve
{
    // Opt-in integration diagnostics. No effect on driving controls or physics.
    internal static class NativeSmoke
    {
        private static double seconds;
        private static string capture;
        private static Stopwatch timer;
        private static bool captured;
        private static double captureAfter = 3;
        private static int frame;
        internal static void Configure(string[] args)
        {
            foreach (string arg in args) {
                if (arg.StartsWith("/native-smoke=", StringComparison.Ordinal)) {
                    if (!double.TryParse(arg.Substring(14), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds <= 0)
                        throw new ArgumentException("/native-smoke must be a positive duration in seconds.");
                }
                if (arg.StartsWith("/native-capture-after=", StringComparison.Ordinal)) {
                    if (!double.TryParse(arg.Substring(22), NumberStyles.Float, CultureInfo.InvariantCulture, out captureAfter) || captureAfter < 0)
                        throw new ArgumentException("/native-capture-after must be a nonnegative duration.");
                }
                if (arg.StartsWith("/native-capture=", StringComparison.Ordinal)) capture = Path.GetFullPath(arg.Substring(16));
            }
        }
        private static void Capture(OpenBVEGame window, string path)
        {
                int width = window.Width, height = window.Height;
                byte[] pixels = new byte[checked(width * height * 4)];
                GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
                GL.ReadBuffer(ReadBufferMode.Back);
                GL.ReadPixels(0,0,width,height,PixelFormat.Rgba,PixelType.UnsignedByte,pixels);
                using var image = new SKBitmap(new SKImageInfo(width,height,SKColorType.Rgba8888,SKAlphaType.Opaque));
                for (int y=0; y<height; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels,(height-y-1)*width*4,IntPtr.Add(image.GetPixels(), y*image.RowBytes),width*4);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var output = File.Create(path);
                using var encoded = image.Encode(SKEncodedImageFormat.Png,100);
                encoded.SaveTo(output);
        }
        internal static void Rendered(OpenBVEGame window, bool ready)
        {
            if (!ready) return;
            if (timer == null) {
                timer = Stopwatch.StartNew();
                Console.WriteLine($"NATIVE_SCENE: static={Program.Renderer.StaticObjectStates.Count}, opaque={Program.Renderer.VisibleObjects.OpaqueFaces.Count}, alpha={Program.Renderer.VisibleObjects.AlphaFaces.Count}, camera={Program.Renderer.Camera.AbsolutePosition}, direction={Program.Renderer.Camera.AbsoluteDirection}");
                Console.WriteLine("NATIVE_READY: route loaded, simulation running on " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
            }
            frame++;
            if (!captured && capture != null && timer.Elapsed.TotalSeconds >= captureAfter && frame > 3) {
                Capture(window, capture);
                captured = true;
                Console.WriteLine("NATIVE_CAPTURE: " + capture);
            }
            if (seconds > 0 && timer.Elapsed.TotalSeconds >= seconds) {
                var train = TrainManager.PlayerTrain;
                Console.WriteLine($"NATIVE_SMOKE_OK: frames={frame}, speed={train.CurrentSpeed.ToString(CultureInfo.InvariantCulture)} m/s, position={train.Cars[0].FrontAxle.Follower.TrackPosition.ToString(CultureInfo.InvariantCulture)} m");
                seconds = 0;
                window.Close();
            }
        }
    }
}
