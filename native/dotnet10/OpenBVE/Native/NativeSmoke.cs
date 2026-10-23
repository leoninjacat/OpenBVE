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
        private static bool testCco;
        private static bool testCabin;
        private static bool testMap;
        private static bool testJump;
        internal static void Configure(string[] args)
        {
            foreach (string arg in args) {
                if (arg == "/native-cco-test") testCco = true;
                if (arg == "/native-cabin-test") testCabin = true;
                if (arg == "/native-map-test") testMap = true;
                if (arg == "/native-jump-test") testJump = true;
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
                if (testJump) NativeStationJumpTests.Run();
                if (testCabin) NativeCabinTests.Run();
                if (testMap && CcoMode.View == CcoMode.TrafficView.Hidden) CcoMode.CycleTrafficView();
                if (testCco) {
                    if (!CcoMode.Enabled || TrainManager.PlayerTrain.AI == null)
                        throw new InvalidOperationException("CCO must start with AI.");
                    var train = TrainManager.PlayerTrain;
                    double position = train.Cars[0].FrontAxle.Follower.TrackPosition;
                    double time = Program.CurrentRoute.SecondsSinceMidnight;
                    CcoMode.SelectStation(0);
                    Program.Renderer.Camera.Alignment.Yaw = 0.42;
                    CcoMode.Cycle(1);
                    CcoMode.Cycle(-1);
                    if (Math.Abs(Program.Renderer.Camera.Alignment.Yaw - 0.42) > 0.000001)
                        throw new InvalidOperationException("Station camera state was not restored.");
                    CcoMode.SelectStation(Program.CurrentRoute.Stations.Length - 1);
                    double stationPosition = Program.Renderer.Camera.Alignment.TrackPosition;
                    string stationName = CcoMode.StationName;
                    Program.Renderer.Camera.Alignment.Position = new OpenBveApi.Math.Vector3(30, 20, 10);
                    Program.Renderer.Camera.Alignment.TrackPosition += 250;
                    Program.Renderer.Camera.Alignment.Yaw = 1.2;
                    Program.Renderer.Camera.Alignment.Zoom = 0.5;
                    if (!CcoMode.ResetCamera() ||
                        Program.Renderer.Camera.Alignment.TrackPosition != stationPosition ||
                        Program.Renderer.Camera.Alignment.Position.X != 6 ||
                        Program.Renderer.Camera.Alignment.Position.Y != 4 ||
                        Program.Renderer.Camera.Alignment.Position.Z != 0 ||
                        Program.Renderer.Camera.Alignment.Yaw != -0.35 ||
                        Program.Renderer.Camera.Alignment.Zoom != 0 ||
                        CcoMode.StationName != stationName)
                        throw new InvalidOperationException("Reset did not restore the selected station camera.");
                    CcoMode.Cycle(1);
                    CcoMode.Cycle(-1);
                    if (Program.Renderer.Camera.Alignment.TrackPosition != stationPosition ||
                        Program.Renderer.Camera.Alignment.Yaw != -0.35)
                        throw new InvalidOperationException("Reset camera was not preserved after cycling.");
                    Console.WriteLine("CCO_RESET_OK: selected station default restored and retained after cycling.");
                    if (train.Cars[0].FrontAxle.Follower.TrackPosition != position ||
                        Program.CurrentRoute.SecondsSinceMidnight != time || train.AI == null)
                        throw new InvalidOperationException("Camera selection changed train or clock.");
                    CcoMode.Cycle(1);
                    Console.WriteLine("CCO_TEST_OK: AI active, independent station cameras, cycle, train and clock preserved.");
                    var viewKey = new OpenTK.Input.KeyboardKeyEventArgs();
                    typeof(OpenTK.Input.KeyboardKeyEventArgs).GetProperty("Key").SetValue(viewKey, OpenTK.Input.Key.F7);
                    if (CcoMode.View != CcoMode.TrafficView.Curves)
                        throw new InvalidOperationException("CCO must start with the geographical map.");
                    MainLoop.KeyDownEvent(window, viewKey);
                    MainLoop.KeyDownEvent(window, viewKey);
                    if (CcoMode.View != CcoMode.TrafficView.Diagram)
                        throw new InvalidOperationException("F7 must advance once, including when held.");
                    MainLoop.KeyUpEvent(window, viewKey);
                    MainLoop.KeyDownEvent(window, viewKey);
                    MainLoop.KeyUpEvent(window, viewKey);
                    if (CcoMode.View != CcoMode.TrafficView.Hidden)
                        throw new InvalidOperationException("Second F7 must hide the CCO overlays.");
                    MainLoop.KeyDownEvent(window, viewKey);
                    MainLoop.KeyUpEvent(window, viewKey);
                    if (CcoMode.View != CcoMode.TrafficView.Curves || !CcoMode.Enabled ||
                        train.AI == null || train.Cars[0].FrontAxle.Follower.TrackPosition != position ||
                        Program.CurrentRoute.SecondsSinceMidnight != time)
                        throw new InvalidOperationException("F7 must wrap to the map without changing the simulation.");
                    Console.WriteLine("CCO_VIEWS_OK: F7 cycles map, diagram, hidden; held key advances once; simulation preserved.");
                    double savedSpeed = train.CurrentSpeed;
                    try {
                        train.CurrentSpeed = 0;
                        if (Graphics.Renderers.Overlays.CcoTrainColor(train) != OpenBveApi.Colors.Color128.Red)
                            throw new InvalidOperationException("Stopped CCO indicator must be red.");
                        foreach (double speed in new[] { -1.0, 1.0 }) {
                            train.CurrentSpeed = speed;
                            if (Graphics.Renderers.Overlays.CcoTrainColor(train) == OpenBveApi.Colors.Color128.Red)
                                throw new InvalidOperationException("Moving CCO indicator must regain its train color.");
                        }
                    } finally { train.CurrentSpeed = savedSpeed; }
                    foreach (var stop in Program.CurrentRoute.Stations) {
                        if (!string.Equals(stop.Name?.Trim(), "Cachorros", StringComparison.OrdinalIgnoreCase) ||
                            !Program.CurrentRoute.Tracks.ContainsKey(4) || !Program.CurrentRoute.Tracks.ContainsKey(50)) continue;
                        double terminal = stop.GetStopPosition(train);
                        if (CcoMode.TrafficRail(4, terminal - 200) != 1 || CcoMode.TrafficRail(50, terminal) != 1 ||
                            CcoMode.TrafficRail(4, terminal - 600) != -1 || CcoMode.TrafficRail(50, terminal + 100) != -1 ||
                            CcoMode.TrafficRail(2, terminal) != -1 || CcoMode.TrafficRail(3, terminal) != -1)
                            throw new InvalidOperationException("Cachorros must retain terminal connections without admitting Linha 2.");
                        Console.WriteLine("CCO_TERMINAL_OK: Cachorros rails 4/50 retained on via 1; unrelated rails and positions excluded.");
                        break;
                    }
                    Console.WriteLine("CCO_STOPPED_OK: stopped trains red; moving in either direction restores normal color.");
                }
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
                Console.WriteLine($"CABIN_STATS: stations={CabinAnnouncements.StationClipsPlayed}, radio={CabinAnnouncements.RadioClipsPlayed}, enabled={CabinAnnouncements.RadioEnabled}");
                seconds = 0;
                window.Close();
            }
        }
    }
}
