#if WINDOWS
using System;
using System.IO;
namespace OpenBve;
internal static class ClassicWindows
{
    internal static void ConfigureFolders()
    {
        for(var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
            if(Directory.Exists(Path.Combine(folder.FullName,"Railway","Route")))
            {
                Program.FileSystem.InitialRouteFolder = Path.Combine(folder.FullName,"Railway","Route");
                Program.FileSystem.InitialTrainFolder = Path.Combine(folder.FullName,"Train");
                Program.FileSystem.RouteInstallationDirectory = Path.Combine(folder.FullName,"Railway");
                Program.FileSystem.TrainInstallationDirectory = Program.FileSystem.InitialTrainFolder;
                break;
            }
    }
}
#endif
