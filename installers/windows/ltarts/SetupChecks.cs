using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class SetupChecks
{
    [STAThread] static int Main(string[] args)
    {
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
        AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        Application.EnableVisualStyles();
        string output = args[0]; Directory.CreateDirectory(output);
        try
        {
            bool blocked = false;
            try { Setup.SafePath(output, @"..\outside.txt"); } catch (IOException) { blocked = true; }
            if (!blocked) throw new Exception("Path traversal was accepted");
            string longFile = Setup.SafePath(output, new string('a', 100) + "\\" + new string('b', 100) + "\\teste.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(longFile));
            File.WriteAllText(longFile, "long path");
            if (File.ReadAllText(longFile) != "long path") throw new Exception("Long paths are not supported");
            string fixture = Path.Combine(output, "uninstall-fixture"); Directory.CreateDirectory(fixture);
            string original = Path.Combine(fixture, "original.txt"), edited = Path.Combine(fixture, "edited.txt");
            File.WriteAllText(original, "original"); File.WriteAllText(edited, "before edit");
            File.WriteAllLines(Path.Combine(fixture, "installation.sha256"), new[] { Setup.Hash(original) + "  original.txt", Setup.Hash(edited) + "  edited.txt" });
            File.WriteAllText(edited, "my edited route"); File.WriteAllText(Path.Combine(fixture, "personal.txt"), "personal content");
            if (Setup.Remove(fixture) != 1 || File.Exists(original) || File.ReadAllText(edited) != "my edited route" || !File.Exists(Path.Combine(fixture, "personal.txt"))) throw new Exception("Uninstall did not preserve user files");
            Setup.Shortcut(Path.Combine(output, "CCO Beta.lnk"), @"C:\Teste com espaços\OpenBVE", "cco");
            if (!File.Exists(Path.Combine(output, "CCO Beta.lnk"))) throw new Exception("Shortcut was not created");
            using (var wizard = new Wizard())
            {
                wizard.Show(); Application.DoEvents();
                var page = typeof(Wizard).GetField("page", BindingFlags.Instance | BindingFlags.NonPublic);
                var show = typeof(Wizard).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic);
                var next = (Button)typeof(Wizard).GetField("next", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wizard);
                var accept = (CheckBox)typeof(Wizard).GetField("accept", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wizard);
                page.SetValue(wizard, 1); show.Invoke(wizard, null);
                if (next.Enabled) throw new Exception("License acceptance is not enforced");
                accept.Checked = true;
                if (!next.Enabled) throw new Exception("License acceptance did not unlock navigation");
                foreach (int i in new[] { 0, 1, 2, 3, 4, 5 })
                {
                    page.SetValue(wizard, i); show.Invoke(wizard, null);
                    Application.DoEvents();
                    using (var image = new Bitmap(wizard.Width, wizard.Height))
                    {
                        wizard.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        image.Save(Path.Combine(output, "wizard-" + i + ".png"));
                    }
                }
            }
            File.WriteAllText(Path.Combine(output,"checks.txt"), "PASS: path traversal rejected; uninstall preserves edited and new files; CCO shortcut created; license gate enforced; six wizard screens rendered.\r\n");
            return 0;
        }
        catch (Exception e) { File.WriteAllText(Path.Combine(output,"checks.txt"), e.ToString()); return 1; }
    }
}
