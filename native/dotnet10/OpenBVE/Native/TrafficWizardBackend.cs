#if WINDOWS
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using OpenBveApi;
using RouteManager2.Stations;
using TrainManager.Trains;
using Path = System.IO.Path;

namespace OpenBve
{
    // Public bridge used by the independent desktop executable. The route and train
    // loaders are shared with the simulator, without displaying its launcher.
    public static class TrafficInstallerApplication
    {
        public static void Run(string[] args) => Program.Run(new[] { "--traffic-wizard" }.Concat(args).ToArray());
    }

    internal partial class formMain
    {
        internal static RouteStation[] ReadTrafficRoute(string route, Encoding encoding, bool preview = true)
        {
            using var reader = new formMain();
            if (!preview) Program.TrainManager.TFOs.Clear();
            reader.PreviewLoadRoute(new LaunchParameters { RouteFile = route, RouteEncoding = encoding }, preview);
            return Program.CurrentRoute.Stations.Where(s => s != null && !s.Dummy && s.Stops != null && s.Stops.Length > 0 &&
                s.StopMode != OpenBveApi.Runtime.StationStopMode.AllPass).ToArray();
        }
    }
}

namespace OpenBve.Native
{
    internal static class TrafficWizardBackend
    {
        private static OpenTK.GameWindow validationContext;
        internal static void Run(string[] args)
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            string test = args.FirstOrDefault(a => a.StartsWith("--traffic-test="));
            if (test != null)
            {
                RunIntegrationTest(test.Substring("--traffic-test=".Length));
                return;
            }
            using var wizard = new IntelligentTrafficWizard("", Encoding.UTF8, "", Encoding.UTF8, Array.Empty<RouteStation>());
            Application.Run(wizard);
        }

        internal static Encoding DetectEncoding(string path) => TextEncoding.GetSystemEncodingFromFile(path);

        internal static void Validate(string route, string xml, Encoding encoding, int expected)
        {
            string id = XDocument.Load(xml).Descendants("ServiceId").Single().Value;
            if (Program.Renderer.TextureManager == null)
            {
                // Full object loading needs the same graphics services as the simulator.
                // Keep its context hidden; the only visible UI is the installer app.
                validationContext = new OpenTK.GameWindow(1, 1) { Visible = false };
                validationContext.MakeCurrent();
                Program.Renderer.Initialize();
            }
            // Read the generated CSV in full, not preview mode: only a full route
            // load actually executes Route.TfoXML and constructs the AI consists.
            formMain.ReadTrafficRoute(route, encoding, false);
            var fleet = Program.TrainManager.TFOs.OfType<ScriptedTrain>().Where(t => t.ServiceId == id).ToArray();
            if (fleet.Length != expected || fleet.Any(t => t.AI == null || t.Cars.Length == 0 ||
                !t.Cars.Any(c => c.CarSections.TryGetValue(LibRender2.Trains.CarSectionType.Exterior, out var exterior) &&
                    exterior.Groups.Any(g => g.Elements.Length > 0 || g.Keyframes != null))))
                throw new InvalidDataException($"A rota não carregou a frota solicitada ({fleet.Length}/{expected}). Nenhuma instalação foi confirmada.");
            if (fleet.Any(t => t.Cars.Any(c => c.FrontAxle.Follower.TrackIndex != 0)))
                throw new InvalidDataException("A frota gerada não está no trilho do jogador.");
        }

        private static void RunIntegrationTest(string output)
        {
            output = Path.GetFullPath(output); Directory.CreateDirectory(output);
            string route = Path.Combine(Program.FileSystem.InitialRouteFolder, "Mobitrens", "Linha 1", "Mobitrens Linha 1 (GAT-CAC).csv");
            string train = Path.Combine(Program.FileSystem.InitialTrainFolder, "Serie 1000 Cofebra");
            string installed = null;
            string xml = null;
            try
            {
                var encoding = DetectEncoding(route);
                var stations = formMain.ReadTrafficRoute(route, encoding);
                using (var wizard = new IntelligentTrafficWizard(route, encoding, train, DetectEncoding(Path.Combine(train, "train.dat")), stations))
                    wizard.TestPages(output);
                var info = IntelligentTrafficPlan.ReadTrain(train, DetectEncoding(Path.Combine(train, "train.dat")));
                var stops = stations.Select(s => new IntelligentTrafficPlan.Stop { Name = s.Name,
                    Position = s.Stops.Last().TrackPosition, Dwell = s.StopTime,
                    Doors = s.OpenLeftDoors ? (s.OpenRightDoors ? "B" : "L") : (s.OpenRightDoors ? "R" : "0") }).ToArray();
                installed = IntelligentTrafficPlan.Install(route, encoding, train, info, stops, 3, 180, 60, 0,
                    (r, x) => { xml = x; Validate(r, x, encoding, 3); });
                var saved = IntelligentTrafficPlan.ReadExisting(installed, encoding);
                if (saved.Count != 3 || saved.Stops.Count != stops.Length) throw new Exception("Saved configuration did not reopen.");
                string edited = IntelligentTrafficPlan.Install(installed, encoding, train, info, saved.Stops, 2, 120, 60, 0,
                    (r, x) => Validate(r, x, encoding, 2));
                if (edited != installed || IntelligentTrafficPlan.ReadExisting(edited, encoding).Count != 2)
                    throw new Exception("Editing did not update the same route.");
                using (var editor = new IntelligentTrafficWizard(edited, encoding, "", Encoding.UTF8, Array.Empty<RouteStation>()))
                    editor.TestPages(output);
                File.WriteAllText(Path.Combine(output, "result.txt"), "TRAFFIC_INSTALL_OK: _IA naming, install and edit via real CSV full load, 2 AI consists with exterior models, restored editor settings and initial traffic enabled.\n" + installed);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(output, "result.txt"), ex.ToString());
                throw;
            }
            // Keep successful test output available for a real simulation smoke run.
        }
    }
}
#endif
