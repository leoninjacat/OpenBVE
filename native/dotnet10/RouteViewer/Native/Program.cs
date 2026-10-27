using System;
using System.IO;
using System.Text;
using OpenBve.Native.Desktop;
using OpenBveApi;
using OpenBveApi.Routes;
using OpenBveApi.FileSystem;
using OpenBveApi.Interface;
using OpenBveApi.Math;
using OpenTK;
using OpenTK.Graphics;
using RouteManager2;

namespace RouteViewer;
internal static class Program
{
    internal static FileSystem FileSystem;
    internal static Host CurrentHost;
    internal static NewRenderer Renderer;
    internal static CurrentRoute CurrentRoute;
    internal static TrainManager TrainManager;
    internal static Sounds Sounds;
    internal static string CurrentRouteFile;
    internal static bool CurrentlyLoading;
    internal static CameraRig Camera = new CameraRig();
    internal static volatile string Status = "Abra uma rota CSV ou RW para explorar.";
    internal static volatile string PendingRoute;
    internal static volatile bool Picking;
    internal static bool Reload;
    internal static string CapturePath;
    internal static string MessageLogPath;
    internal static double SmokeSeconds;
    internal static bool CameraSmoke;
    internal static bool ReloadSmoke;
    internal static string SwitchSmoke;
    internal static int LoadCount;
    internal static bool UiTest;
    internal static bool EditorTest;
    internal static bool FeedbackTest;
    internal static bool KeyboardTest;
    internal static bool TextureTest;
    internal static bool CreateOnStart;
    internal static int WindowWidth = 1280, WindowHeight = 800;
    static string loadedRoute;
    [STAThread]
    static int Main(string[] args)
    {
        try {
            if (Array.IndexOf(args, "--help") >= 0) {
                Console.WriteLine("OpenBVE Route Editor By LT ARTS\nRouteViewer [FILE.csv|FILE.rw] [--smoke=SECONDS] [--capture=FILE] [--camera-test] [--reload-test] [--switch=FILE] [--size=WIDTHxHEIGHT]");
                return 0;
            }
            NativeLibraries.Configure();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            foreach (string arg in args) {
                if (arg.StartsWith("--smoke=")) SmokeSeconds = double.Parse(arg.Substring(8), System.Globalization.CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--capture=")) CapturePath = System.IO.Path.GetFullPath(arg.Substring(10));
                else if (arg.StartsWith("--message-log=")) MessageLogPath = System.IO.Path.GetFullPath(arg.Substring(14));
                else if (arg == "--feedback-test") FeedbackTest = true;
                else if (arg == "--editor-test") EditorTest = true;
                else if (arg == "--keyboard-test") KeyboardTest = true;
                else if (arg == "--texture-test") TextureTest = true;
                else if (arg == "--new-route") CreateOnStart = true;
                else if (arg == "--ui-test") UiTest = true;
                else if (arg == "--camera-test") CameraSmoke = true;
                else if (arg == "--reload-test") ReloadSmoke = true;
                else if (arg.StartsWith("--switch=")) SwitchSmoke = ResolvePath(arg.Substring(9));
                else if (arg.StartsWith("--size=")) {
                    var size = arg.Substring(7).Split('x');
                    WindowWidth = Math.Clamp(int.Parse(size[0]), 640, 7680);
                    WindowHeight = Math.Clamp(int.Parse(size[1]), 480, 4320);
                }
                else if (!arg.StartsWith("--")) CurrentRouteFile = ResolvePath(arg);
            }
            if (ReloadSmoke) CameraSmoke = true;
            if (!OperatingSystem.IsLinux() && (UiTest || FeedbackTest))
                throw new PlatformNotSupportedException("Os testes de eventos --ui-test e --feedback-test requerem X11/Linux.");
            if(KeyboardTest&&!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("--keyboard-test requer Windows.");
            CurrentHost = new Host();
            FileSystem = FileSystem.FromCommandLineArgs(Array.Empty<string>(), CurrentHost);
            FileSystem.SettingsFolder = File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory,"portable.flag"))
                ? System.IO.Path.Combine(AppContext.BaseDirectory,"UserData","Settings")
                : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RouteViewer-Native", "Settings");
            FileSystem.CreateFileSystem();
            FileSystem.AppendToLogFile("OpenBVE Route Editor By LT ARTS .NET 10");
            Options.LoadOptions();
            Interface.CurrentOptions.ViewingDistance = 1000;
            Toolkit.Init(OperatingSystem.IsLinux()
                ? new ToolkitOptions { Backend = PlatformBackend.PreferX11 }
                : new ToolkitOptions());
            Renderer = new NewRenderer(CurrentHost, Interface.CurrentOptions, FileSystem);
            CurrentRoute = new CurrentRoute(CurrentHost, Renderer);
            TrainManager = new TrainManager(CurrentHost, Renderer, Interface.CurrentOptions, FileSystem);
            Sounds = new Sounds(CurrentHost);
            if (!CurrentHost.LoadPlugins(FileSystem, Interface.CurrentOptions, out string error, TrainManager, Renderer)) throw new InvalidOperationException(error);
            Translations.LoadLanguageFiles(FileSystem.GetDataFolder("Languages"));
            Renderer.CameraTrackFollower = new TrackFollower(CurrentHost);
            using var window = new ViewerWindow();
            Renderer.GameWindow = window;
            window.Run(60,60);
            Sounds.DeInitialize();
            return Environment.ExitCode;
        } catch (Exception error) {
            Console.Error.WriteLine(error);
            try { File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"routeviewer-error.log"),error.ToString()); } catch { }
            try { FileSystem?.AppendToLogFile(error.ToString()); } catch { }
            return 1;
        }
    }

    internal static bool LoadRoute()
    {
        CurrentlyLoading = true;
        RouteInfo.Failure=null;
        try {
            ((ViewerWindow)Renderer.GameWindow).Editor.Open((ViewerWindow)Renderer.GameWindow,CurrentRouteFile);
            Loading.Load(CurrentRouteFile, TextEncoding.GetSystemEncodingFromFile(CurrentRouteFile), null);
            if (!Loading.Complete || Loading.Cancel) throw new InvalidOperationException("Carregamento cancelado.");
            Renderer.Camera.QuadTreeLeaf = null;
            Renderer.Lighting.Initialize();
            Renderer.InitializeVisibility();
            Renderer.CurrentInterface = LibRender2.Screens.InterfaceType.Normal;
            if (loadedRoute == CurrentRouteFile) { Camera.Invalidate(); Camera.Apply(); }
            else Camera.ResetFromTrack();
            loadedRoute = CurrentRouteFile;
            LoadCount++;
            Status = System.IO.Path.GetFileName(CurrentRouteFile);
            Renderer.GameWindow.Title = Status + " — OpenBVE Route Editor By LT ARTS";
            Console.WriteLine($"VIEWER_READY: objects={Renderer.StaticObjectStates.Count}, stations={CurrentRoute.Stations.Length}");
            return true;
        } catch (Exception error) {
            Status = "Falha ao abrir rota: " + error.Message;
            RouteInfo.Failure=Status;
            Console.Error.WriteLine(error);
            FileSystem.AppendToLogFile(error.ToString());
            Environment.ExitCode = SmokeSeconds > 0 ? 1 : 0;
            if (Renderer.GameWindow is not ViewerWindow viewer || !viewer.Editor.Running) CurrentRouteFile = null;
            return false;
        } finally { CurrentlyLoading = false;RouteInfo.Refresh(); Renderer.GameWindow.TargetRenderFrequency=60; }
    }

    internal static string ResolvePath(string path)
    {
        // Use the same portal resolution as the simulator's native GTK picker.
        if (!OperatingSystem.IsLinux() || !path.StartsWith(System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "/run/user/" + Environment.UserName, "doc") + "/")) return System.IO.Path.GetFullPath(path);
        return RunPicker(path);
    }
    static string RunPicker(string path = null,bool create=false)
    {
        var start = new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "RouteViewer.Desktop.exe" : "route-picker")) {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (OperatingSystem.IsWindows()) start.ArgumentList.Add(create?"--new-route":"--pick");
        if (path != null) { start.ArgumentList.Add("--resolve"); start.ArgumentList.Add(path); }
        using var process = System.Diagnostics.Process.Start(start);
        var stderr = process.StandardError.ReadToEndAsync();
        string result = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode > 1) throw new IOException(stderr.GetAwaiter().GetResult());
        return process.ExitCode == 0 && result.Length > 0 ? result : null;
    }
    internal static async void OpenPicker()
    {
        if (Picking || CurrentlyLoading) return;
        Picking = true;
        try { string path = await System.Threading.Tasks.Task.Run(() => RunPicker()); if (path != null) ((ViewerWindow)Renderer.GameWindow).Editor.RequestRoute(path); }
        catch (Exception error) { Status = "Não foi possível abrir a rota: " + error.Message; }
        finally { Picking = false; }
    }
    internal static async void NewRoute()
    {
        if(!OperatingSystem.IsWindows()||Picking||CurrentlyLoading)return;
        var editor=((ViewerWindow)Renderer.GameWindow).Editor;
        if(editor.Running){editor.Send("NEW");return;}
        Picking=true;
        try {string path=await System.Threading.Tasks.Task.Run(()=>RunPicker(create:true));if(path!=null)editor.RequestRoute(path);}
        catch(Exception error){Status="Nova rota: "+error.Message;}
        finally{Picking=false;}
    }
}
