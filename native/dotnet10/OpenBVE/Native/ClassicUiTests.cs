#if WINDOWS
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace OpenBve;
internal partial class formMain
{
    internal static void RunNativeUiTest(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        using var form = new formMain();
        form.Result = new LaunchParameters();
        Exception failure = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                void Capture(string name) {
                    using var bitmap = new System.Drawing.Bitmap(form.Width,form.Height);
                    form.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height));
                    bitmap.Save(Path.Combine(output,name+".png"));
                }
                await Task.Delay(500);
                Capture("classic-start");
                form.radiobuttonOptions.Checked = true; Capture("classic-options");
                form.radiobuttonControls.Checked = true; Capture("classic-controls");
                form.radiobuttonStart.Checked = true;
                string route = Path.Combine(Program.FileSystem.InitialRouteFolder,"Mobitrens","Linha 1","Mobitrens Linha 1 (GAT-CAC) - night.csv");
                string train = Path.Combine(Program.FileSystem.InitialTrainFolder,"Serie 1000 Cofebra");
                if(!File.Exists(route) || !Directory.Exists(train)) throw new Exception("Conteúdo incluído não encontrado.");
                form.Result.RouteFile = route; form.Result.RouteEncoding = System.Text.Encoding.UTF8;
                form.Result.TrainFolder = train; form.Result.TrainEncoding = System.Text.Encoding.UTF8;
                form.checkboxTrainDefault.Checked = false;
                form.ShowTrain(false); form.ShowRoute(false);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while((form.pictureboxRouteMap.Image == null || form.previewRouteIsLoading) && timer.Elapsed.TotalSeconds < 45) await Task.Delay(150);
                if(form.pictureboxRouteMap.Image == null || form.pictureboxRouteGradient.Image == null) throw new Exception("Prévia original de mapa/altitude não carregou.");
                if(form.Result.RouteFile != route || form.Result.TrainFolder != train) throw new Exception("Seleção alterada inesperadamente.");
                Capture("classic-route-train");
                File.WriteAllText(Path.Combine(output,"result.txt"),"CLASSIC_UI_OK: original formMain, route/train previews, map/gradient, options and controls.\n");
            }
            catch(Exception ex) { failure = ex; File.WriteAllText(Path.Combine(output,"result.txt"),ex.ToString()); }
            finally {
                File.AppendAllText(Path.Combine(output,"result.txt"),"CLOSE_BEGIN\n");
                form.Close();
                File.AppendAllText(Path.Combine(output,"result.txt"),"CLOSE_RETURNED\n");
            }
        };
        form.ShowDialog();
        File.AppendAllText(Path.Combine(output,"result.txt"),"DIALOG_CLOSED\n");
        form.DisposePreviewRouteThread(); form.trainWatcher?.Dispose(); form.routeWatcher?.Dispose();
        File.AppendAllText(Path.Combine(output,"result.txt"),"CLEANUP_OK\n");
        if(failure != null) throw failure;
    }
}
#endif
