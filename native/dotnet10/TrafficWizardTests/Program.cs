using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using OpenBve.Native;

static class Program
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message); }
    static void Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string temp = Path.Combine(Path.GetTempPath(), "openbve-traffic-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string train = Path.Combine(temp, "Trem & teste"); Directory.CreateDirectory(train);
            File.WriteAllText(Path.Combine(train, "train.dat"), "OPENBVE\n#CAR\n40\n4\n30\n2\n20\n#ACCELERATION\n2.5,2,40,60,1\n#PERFORMANCE\n3.5 ; frein\n");
            File.WriteAllText(Path.Combine(train, "extensions.cfg"), "[exterior]\n0 = car.csv");
            var info = IntelligentTrafficPlan.ReadTrain(train, Encoding.UTF8);
            Check(info.Cars == 6 && info.Acceleration == 2.5 && info.Deceleration == 3.5, "train files supply consist and performance");
            var stops = new[] { new IntelligentTrafficPlan.Stop {Name="Sé", Position=123.5, Dwell=20, Doors="L"},
                new IntelligentTrafficPlan.Stop {Name="Luz", Position=4567.25, Dwell=30, Doors="B"} };
            var doc = IntelligentTrafficPlan.Create(train, info, stops, 8, 390, 70, 3600);
            Check(doc.Descendants("Rail").All(e => e.Value == "0"), "single-track routes never reference absent return rail 1");
            Check(doc.Descendants("FleetSize").Single().Value == "8" && doc.Descendants("SpawnInterval").Single().Value == "390", "fleet and headway preserved");
            Check(doc.Descendants("StopPosition").Last().Value == "4567.25", "metric coordinates use invariant XML numbers");
            Check(doc.Descendants("DisposeAtDestination").Single().Value == "true", "one-way trains clear the terminal after final dwell");
            Check(IntelligentTrafficPlan.Time(3661) == "01.0101", "OpenBVE time syntax");
            var encoding = Encoding.GetEncoding(1252);
            string route = Path.Combine(temp, "Rota (Sé-Luz).csv");
            byte[] original = encoding.GetBytes("With Route\r\n.Comment São Paulo\r\nWith Track\r\n100,.Station Sé\r\n");
            File.WriteAllBytes(route, original);
            string output = IntelligentTrafficPlan.Install(route, encoding, train, info, stops, 8, 390, 70, 3600);
            Check(Path.GetFileName(output) == "Rota (Sé-Luz)_IA.csv", "generated route uses original name plus _IA including accents and parentheses");
            Check(File.ReadAllBytes(route).SequenceEqual(original), "source CSV preserved byte for byte");
            Check(File.ReadAllBytes(output).Take(original.Length).SequenceEqual(original), "copy retains original encoding and relative path base");
            string xml = Directory.GetFiles(temp, "service.xml", SearchOption.AllDirectories).Single();
            Check(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(xml), XDocument.Load(xml).Descendants("Directory").Single().Value)) == train, "train relative path resolves including special characters");
            Check(encoding.GetString(File.ReadAllBytes(output)).Contains("Route.TfoXML("), "copy references generated AI definition");
            var loaded = IntelligentTrafficPlan.ReadExisting(output, encoding);
            Check(loaded.Count == 8 && loaded.Interval == 390 && loaded.TrainFolder == train && loaded.Stops[0].Name == "Sé", "reopening restores train, stops, fleet and interval");
            stops[0].Dwell = 27;
            string edited = IntelligentTrafficPlan.Install(output, encoding, train, info, stops, 3, 180, 65, 0);
            Check(edited == output && IntelligentTrafficPlan.ReadExisting(edited, encoding).Count == 3 && IntelligentTrafficPlan.ReadExisting(edited, encoding).Stops[0].Dwell == 27, "editing updates same route and keeps changed stops");
            Check(encoding.GetString(File.ReadAllBytes(edited)).Split("Route.TfoXML(").Length == 2, "editing replaces generated fleet without duplicate services");
            byte[] savedRoute = File.ReadAllBytes(edited), savedXml = File.ReadAllBytes(xml);
            int filesBefore = Directory.GetFiles(temp, "*", SearchOption.AllDirectories).Length;
            try
            {
                IntelligentTrafficPlan.Install(route, encoding, train, info, stops, 2, 120, 60, 0,
                    (_, _) => throw new InvalidDataException("simulated runtime load failure"));
                throw new Exception("validation failure reported as success");
            }
            catch (InvalidDataException)
            {
                Check(Directory.GetFiles(temp, "*", SearchOption.AllDirectories).Length == filesBefore, "failed runtime validation rolls back generated route and XML");
                Check(File.ReadAllBytes(edited).SequenceEqual(savedRoute) && File.ReadAllBytes(xml).SequenceEqual(savedXml), "failed edit restores previous configuration byte for byte");
            }
            string legacy = Path.Combine(temp, "Rota (Sé-Luz) - IA-0123456789.csv");
            File.WriteAllBytes(legacy, File.ReadAllBytes(edited));
            string migrated = IntelligentTrafficPlan.Install(legacy, encoding, train, info, stops, 4, 240, 60, 0);
            Check(migrated == output && IntelligentTrafficPlan.ReadExisting(migrated, encoding).Count == 4 && File.Exists(legacy), "legacy random-name installations migrate to _IA and preserve the old copy");
            try { IntelligentTrafficPlan.Create(train, info, stops.Reverse().ToArray(), 8, 390, 70, 0); throw new Exception("accepted invalid stops"); }
            catch (InvalidDataException) { Check(true, "unordered stops rejected"); }
            if (args.Length > 0)
            {
                var real = IntelligentTrafficPlan.ReadTrain(args[0], Encoding.UTF8);
                Check(real.Cars > 0, "bundled real train accepted: " + real.Cars + " cars");
            }
        }
        finally { Directory.Delete(temp, true); }
    }
}
