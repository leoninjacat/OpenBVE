using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace OpenBve.Native;

// Independent of the desktop toolkit: route syntax is resolved by the route plugin.
internal static class IntelligentTrafficPlan
{
    public sealed class Stop
    {
        public string Name { get; set; }
        public double Position { get; set; }
        public double Dwell { get; set; }
        public string Doors { get; set; }
    }

    internal sealed class TrainInfo
    {
        public int Cars;
        public double Acceleration;
        public double Deceleration;
    }

    internal sealed class Settings
    {
        public string TrainFolder;
        public List<Stop> Stops;
        public int Count;
        public double Interval, Speed, Appearance;
        public bool WarmStart;
    }

    private static readonly Regex ManagedBlock = new Regex(@"\r?\n; Assistente de trens de IA LT ARTS\r?\nRoute\.TfoXML\(([^)]+)\)\r?\n?", RegexOptions.IgnoreCase);

    internal static string OutputPath(string route)
    {
        string name = Path.GetFileNameWithoutExtension(route);
        name = Regex.Replace(name, @"(?: - IA-[0-9a-f]{10}|_IA)+$", "", RegexOptions.IgnoreCase);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(route)), name + "_IA.csv");
    }

    internal static Settings ReadExisting(string route, Encoding encoding)
    {
        var match = ManagedBlock.Matches(encoding.GetString(File.ReadAllBytes(route))).Cast<Match>().LastOrDefault();
        if (match == null) return null;
        string xml = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(route)), match.Groups[1].Value.Replace('/', Path.DirectorySeparatorChar)));
        var doc = XDocument.Load(xml);
        var definition = doc.Descendants("Definition").Single();
        double Value(string key, double fallback) => double.TryParse(definition.Element(key)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : fallback;
        double Seconds(string text)
        {
            string[] parts = text.Split('.');
            string tail = parts.Length > 1 ? parts[1].PadRight(4, '0') : "0000";
            return int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600 + int.Parse(tail.Substring(0, 2), CultureInfo.InvariantCulture) * 60 + int.Parse(tail.Substring(2, 2), CultureInfo.InvariantCulture);
        }
        var names = doc.Descendants("StationName").Select(n => n.Value).ToArray();
        var stops = doc.Descendants("Stops").Single().Elements("Stop").Select((s, i) => new Stop
        {
            Name = i < names.Length ? names[i] : "Parada " + (i + 1),
            Position = double.Parse(s.Element("StopPosition").Value, CultureInfo.InvariantCulture),
            Dwell = Seconds(s.Element("StopTime")?.Value ?? "00.0015"), Doors = s.Element("Doors")?.Value ?? "0"
        }).ToList();
        return new Settings { TrainFolder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(xml), doc.Descendants("Directory").Single().Value)),
            Stops = stops, Count = (int)Value("FleetSize", 1), Interval = Value("SpawnInterval", 300),
            Speed = double.Parse(doc.Descendants("TargetSpeed").First().Value, CultureInfo.InvariantCulture),
            Appearance = Seconds(definition.Element("AppearanceTime")?.Value ?? "00.0000"),
            WarmStart = definition.Element("WarmStart")?.Value != "false" };
    }

    internal static TrainInfo ReadTrain(string folder, Encoding encoding)
    {
        string data = Path.Combine(folder, "train.ai");
        if (!File.Exists(data)) data = Path.Combine(folder, "train.dat");
        if (!File.Exists(data) || !File.Exists(Path.Combine(folder, "extensions.cfg")))
            throw new InvalidDataException("O trem precisa de train.dat (ou train.ai) e extensions.cfg para o modelo externo da IA.");
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        List<string> current = null;
        foreach (string raw in File.ReadAllLines(data, encoding))
        {
            string line = raw.Split(';')[0].Trim();
            if (line.StartsWith("#")) sections[line] = current = new List<string>();
            else if (line.Length > 0 && current != null) current.Add(line);
        }
        double Number(string section, int row, int column = 0)
        {
            if (!sections.TryGetValue(section, out var rows) || row >= rows.Count)
                throw new InvalidDataException("Dados incompletos do trem: " + section);
            string[] values = rows[row].Split(',');
            if (column >= values.Length || !double.TryParse(values[column], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n))
                throw new InvalidDataException("Valor inválido no trem: " + section);
            return n;
        }
        double cars = Number("#CAR", 1) + Number("#CAR", 3);
        double acceleration = Number("#ACCELERATION", 0);
        double deceleration = Number("#PERFORMANCE", 0);
        if (cars < 1 || cars > 128 || cars != Math.Truncate(cars) || acceleration <= 0 || deceleration <= 0)
            throw new InvalidDataException("Quantidade de carros ou desempenho inválido no trem.");
        return new TrainInfo { Cars = (int)cars, Acceleration = acceleration, Deceleration = deceleration };
    }

    internal static string Time(double seconds)
    {
        int total = (int)Math.Ceiling(seconds);
        return $"{total / 3600:00}.{total / 60 % 60:00}{total % 60:00}";
    }

    internal static XDocument Create(string trainPath, TrainInfo train, IList<Stop> stops, int count, double interval, double speed, double appearance, bool warmStart = true)
    {
        if (count < 1 || count > 32 || !double.IsFinite(interval) || interval < 1 || !double.IsFinite(speed) || speed <= 0 || !double.IsFinite(appearance) || appearance < 0)
            throw new ArgumentException("Confira a quantidade, o intervalo, o horário e a velocidade.");
        if (stops.Count < 2) throw new InvalidDataException("São necessárias pelo menos duas paradas.");
        for (int i = 0; i < stops.Count; i++)
        {
            var s = stops[i];
            if (!double.IsFinite(s.Position) || !double.IsFinite(s.Dwell) || s.Dwell < 0 || s.Dwell > 3600 ||
                (i > 0 && s.Position <= stops[i - 1].Position) || !new[] { "L", "R", "B", "0" }.Contains(s.Doors))
                throw new InvalidDataException("Paradas fora de ordem ou com posição, tempo ou portas inválidos.");
        }
        // Services run in the CSV's direction on its player track (always index 0).
        var definition = new XElement("Definition",
            new XElement("AppearanceTime", Time(appearance)),
            new XElement("LeaveTime", "48.0000"), new XElement("DisposeAtDestination", true),
            new XElement("SpawnInterval", interval), new XElement("FleetSize", count),
            new XElement("WarmStart", warmStart),
            new XElement("BirthClearance", 50), new XElement("ProtectedTraffic", true),
            new XElement("PreferStationWaiting", true));
        var points = new XElement("Stops", stops.Select(s => new XElement("Stop",
            new XElement("StopPosition", s.Position), new XElement("StopTime", Time(s.Dwell)),
            new XElement("Doors", s.Doors), new XElement("Rail", 0), new XElement("Direction", 1),
            new XElement("Accelerate", train.Acceleration), new XElement("Decelerate", train.Deceleration),
            new XElement("TargetSpeed", speed))));
        return new XDocument(new XElement("openBVE", new XElement("TrackFollowingObject", definition,
            new XElement("Train", new XElement("Directory", trainPath)), points)));
    }

    internal static string Install(string route, Encoding encoding, string train, TrainInfo info, IList<Stop> stops,
        int count, double interval, double speed, double appearance, Action<string, string> validate = null, bool warmStart = true)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(route));
        string output = OutputPath(route);
        string id = "IA-" + Path.GetFileNameWithoutExtension(output);
        // CSV command arguments cannot safely contain parentheses or separators.
        string storageName = Regex.Replace(Path.GetFileNameWithoutExtension(output), @"[^\p{L}\p{N}_-]", "_");
        string xmlFolder = Path.Combine(folder, storageName + ".files");
        string xml = Path.Combine(xmlFolder, "service.xml");
        byte[] previousRoute = File.Exists(output) ? File.ReadAllBytes(output) : null;
        byte[] previousXml = File.Exists(xml) ? File.ReadAllBytes(xml) : null;
        if (previousRoute != null && ReadExisting(output, encoding) == null)
            throw new IOException("O arquivo de destino já existe e não foi criado pelo assistente: " + output);
        if (previousXml != null && XDocument.Load(xml).Root.Element("TrafficInstaller") == null)
            throw new IOException("A pasta de destino já contém uma definição não gerenciada pelo assistente.");
        var doc = Create(Path.GetRelativePath(xmlFolder, Path.GetFullPath(train)), info, stops, count, interval, speed, appearance, warmStart);
        doc.Descendants("Definition").Single().Add(new XElement("ServiceId", id));
        doc.Root.Add(new XElement("TrafficInstaller", new XElement("Version", 1),
            new XElement("StationNames", stops.Select(s => new XElement("StationName", s.Name)))));
        byte[] original = File.ReadAllBytes(route);
        string text = encoding.GetString(original);
        if (ManagedBlock.IsMatch(text)) original = encoding.GetBytes(ManagedBlock.Replace(text, ""));
        string relativeXml = Path.GetRelativePath(folder, xml).Replace('\\', '/');
        byte[] suffix = encoding.GetBytes("\r\n; Assistente de trens de IA LT ARTS\r\nRoute.TfoXML(" + relativeXml + ")\r\n");
        Directory.CreateDirectory(xmlFolder);
        try
        {
            doc.Save(xml);
            using (var stream = new FileStream(output, FileMode.Create))
            {
                stream.Write(original, 0, original.Length);
                stream.Write(suffix, 0, suffix.Length);
            }
            validate?.Invoke(output, xml);
        }
        catch
        {
            if (previousRoute != null) File.WriteAllBytes(output, previousRoute);
            else if (File.Exists(output)) File.Delete(output);
            if (previousXml != null) File.WriteAllBytes(xml, previousXml);
            else if (File.Exists(xml)) File.Delete(xml);
            if (!Directory.EnumerateFileSystemEntries(xmlFolder).Any()) Directory.Delete(xmlFolder);
            throw;
        }
        return output;
    }
}
