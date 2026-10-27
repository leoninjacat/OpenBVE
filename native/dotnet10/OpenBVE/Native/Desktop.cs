using System;
using System.Runtime.InteropServices;
namespace OpenBve.Native.Desktop
{
    // The native engine has no desktop dialogs. Errors go to stderr and its log.
    internal static class Application
    {
        public static string StartupPath => AppContext.BaseDirectory;
        public static string ExecutablePath => Environment.ProcessPath;
        public static string ProductName => "OpenBVE Native";
        public static string ProductVersion => typeof(OpenBve.Program).Assembly.GetName().Version.ToString();
    }
    internal enum MessageBoxButtons { OK }
    internal enum MessageBoxIcon { Hand, Stop }
    internal static class MessageBox
    {
        public static void Show(string text, string title = "OpenBVE", MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.Hand) => Console.Error.WriteLine($"{title}: {text}");
    }
    internal static class NativeLibraries
    {
        public static void Configure()
        {
            System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) => {
                string file = System.IO.Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
                return System.IO.File.Exists(file) ? context.LoadFromAssemblyPath(file) : null;
            };
            if (!OperatingSystem.IsLinux()) return;
            NativeLibrary.SetDllImportResolver(typeof(OpenTK.Toolkit).Assembly, (name, assembly, path) => {
                string mapped = name switch {
                    "openal32.dll" => "libopenal.so.1", "SDL2.dll" => "libSDL2-2.0.so.0",
                    "libSDL2.so" => "libSDL2-2.0.so.0", "libGL.so" => "libGL.so.1",
                    "libX11" => "libX11.so.6", "libXi" => "libXi.so.6",
                    "libXrandr" => "libXrandr.so.2", _ => name
                };
                return NativeLibrary.TryLoad(mapped, assembly, path, out var handle) ? handle : IntPtr.Zero;
            });
        }
    }
}
