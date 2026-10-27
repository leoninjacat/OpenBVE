using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Setup
{
    internal const string Product = "OpenBVE by LT ARTS";
    internal const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenBVE-LTARTS-Beta";
    [STAThread] private static int Main(string[] args)
    {
        AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
        AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        try
        {
#if UNINSTALL
            string root = AppDomain.CurrentDomain.BaseDirectory;
            if (MessageBox.Show("Desinstalar o " + Product + "?\n\nArquivos novos ou modificados e configurações pessoais serão preservados.", Product, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 0;
            int preserved = Remove(root);
            MessageBox.Show("Desinstalação concluída.\nArquivos pessoais ou modificados preservados: " + preserved + ".", Product);
#else
            if (args.Length == 2 && args[0] == "--verify-install")
            {
                string target = Path.GetFullPath(args[1]);
                File.WriteAllText(target + ".progress.txt", "Starting installation verification.\r\n");
                Install(target, false, false, delegate(int value, string file) { });
                Verify(target);
                File.WriteAllText(target + ".verification.txt", "INSTALL_OK: all payload hashes verified; no shortcuts or registry writes.\r\n");
                return 0;
            }
            Application.Run(new Wizard());
#endif
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            if (args.Length == 2 && args[0] == "--verify-install")
                File.WriteAllText(Path.GetFullPath(args[1]) + ".error.txt", e.ToString());
            string log = Path.Combine(Path.GetTempPath(), "OpenBVE-LTARTS-setup-error.txt");
            File.WriteAllText(log, e.ToString());
            if (args.Length == 0) MessageBox.Show(e.Message + "\n\nDetalhes: " + log, Product, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    internal static string SafePath(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (Path.IsPathRooted(relative) || !full.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("Caminho inválido no pacote.");
        if (full.Length >= 248 && !full.StartsWith(@"\\?\", StringComparison.Ordinal))
            return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full.Substring(2) : @"\\?\" + full;
        return full;
    }
    internal static string Hash(string file)
    {
        using (var sha = SHA256.Create()) using (var input = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
    }
    internal static void Verify(string root)
    {
        foreach (string line in File.ReadAllLines(Path.Combine(root, "installation.sha256")))
        {
            string file = SafePath(root, line.Substring(66));
            if (!File.Exists(file) || Hash(file) != line.Substring(0, 64)) throw new IOException("Arquivo incompleto: " + line.Substring(66));
        }
    }
    internal static void CheckTarget(string target)
    {
        if (!Environment.Is64BitOperatingSystem) throw new IOException("Esta versão requer Windows 64 bits.");
        string root = Path.GetFullPath(target).TrimEnd('\\');
        if (root == Path.GetPathRoot(root).TrimEnd('\\')) throw new IOException("Escolha uma subpasta para o aplicativo.");
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new IOException("A pasta selecionada já contém arquivos. Escolha uma pasta vazia para preservar seu conteúdo.");
        for (string current = root; current != null; current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Escolha uma pasta local sem links de diretório.");
        var drive = new DriveInfo(Path.GetPathRoot(root));
        if (drive.AvailableFreeSpace < 2048L * 1024 * 1024) throw new IOException("São necessários pelo menos 2 GB livres para instalar.");
    }
    internal static void Install(string target, bool desktop, bool register, Action<int, string> progress)
    {
        CheckTarget(target);
        if (register)
            using (var existing = Registry.CurrentUser.OpenSubKey(RegistryPath))
                if (existing != null) throw new IOException("Já existe uma instalação registrada. Desinstale-a antes de instalar esta versão.");
        var created = new List<string>();
        var shortcuts = new List<string>();
        Directory.CreateDirectory(target);
        try
        {
            using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.zip"))
            {
                if (payload == null) throw new IOException("Pacote de instalação ausente.");
                using (var zip = new ZipArchive(payload, ZipArchiveMode.Read))
                {
                    long total = zip.Entries.Sum(e => e.Length), done = 0;
                    foreach (var entry in zip.Entries)
                    {
                        string file = SafePath(target, entry.FullName);
                        if (entry.Name.Length == 0) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        using (var output = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
                        {
                            created.Add(file);
                            using (var input = entry.Open()) input.CopyTo(output);
                        }
                        done += entry.Length;
                        progress((int)(done * 90 / Math.Max(1, total)), entry.FullName);
                    }
                }
            }
            progress(92, "Conferindo a integridade dos arquivos...");
            Verify(target);
            if (register)
            {
                string group = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product + " (Beta)");
                Directory.CreateDirectory(group);
                foreach (string mode in new[] { "normal", "cco" })
                {
                    string title = mode == "cco" ? "CCO Beta" : "OpenBVE";
                    string link = Path.Combine(group, title + ".lnk");
                    Shortcut(link, target, mode); shortcuts.Add(link);
                    if (desktop)
                    {
                        link = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product + " - " + title + ".lnk");
                        Shortcut(link, target, mode); shortcuts.Add(link);
                    }
                }
                string editorLink = Path.Combine(group, "OpenBVE Route Editor.lnk");
                ShortcutFile(editorLink, Path.Combine(target, @"Editor\RouteViewer.exe"), "", target); shortcuts.Add(editorLink);
                if (desktop) {
                    editorLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "OpenBVE Route Editor.lnk");
                    ShortcutFile(editorLink, Path.Combine(target, @"Editor\RouteViewer.exe"), "", target); shortcuts.Add(editorLink);
                }
                string uninstallLink = Path.Combine(group, "Desinstalar.lnk");
                ShortcutFile(uninstallLink, Path.Combine(target, "Desinstalar.exe"), "", target); shortcuts.Add(uninstallLink);
                File.WriteAllLines(Path.Combine(target, "shortcuts.txt"), shortcuts);
                using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
                {
                    key.SetValue("DisplayName", Product + " + Route Editor"); key.SetValue("DisplayVersion", "2026.10.25.2");
                    key.SetValue("Publisher", "LT ARTS / The OpenBVE Project");
                    key.SetValue("InstallLocation", target); key.SetValue("DisplayIcon", Path.Combine(target, "OpenBVE.ico"));
                    key.SetValue("UninstallString", "\"" + Path.Combine(target, "Desinstalar.exe") + "\"");
                    key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
                }
            }
            progress(100, "Instalação concluída.");
        }
        catch
        {
            foreach (string link in shortcuts) try { File.Delete(link); } catch { }
            foreach (string file in created.AsEnumerable().Reverse()) try { File.Delete(file); } catch { }
            CleanEmptyDirectories(target);
            throw;
        }
    }
    internal static void CleanEmptyDirectories(string root)
    {
        foreach (string dir in Directory.GetDirectories(root))
        {
            string safe = SafePath(root, Path.GetFileName(dir));
            if ((File.GetAttributes(safe) & FileAttributes.ReparsePoint) != 0) continue;
            CleanEmptyDirectories(safe);
            if (!Directory.EnumerateFileSystemEntries(safe).Any()) Directory.Delete(safe);
        }
    }
    internal static void Shortcut(string link, string root, string mode)
    {
        string args = "/mode=" + mode;
        ShortcutFile(link, Path.Combine(root, @"App\OpenBve.exe"), args, root);
    }
    internal static void ShortcutFile(string link, string exe, string args, string root)
    {
        Type type = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(type);
        object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
        try
        {
            Type t = shortcut.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { exe });
            t.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { args });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { root });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { Path.Combine(root, "OpenBVE.ico") });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }
    internal static int Remove(string root)
    {
        string manifest = Path.Combine(root, "installation.sha256");
        int preserved = 0;
        foreach (string line in File.ReadAllLines(manifest))
        {
            string file = SafePath(root, line.Substring(66));
            if (!File.Exists(file)) continue;
            if (string.Equals(file, Assembly.GetExecutingAssembly().Location, StringComparison.OrdinalIgnoreCase)) continue;
            if (Hash(file) == line.Substring(0, 64)) File.Delete(file); else preserved++;
        }
        string links = Path.Combine(root, "shortcuts.txt");
        if (File.Exists(links))
        {
            string group = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product + " (Beta)");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var allowed = new[] { Path.Combine(group, "OpenBVE.lnk"), Path.Combine(group, "CCO Beta.lnk"), Path.Combine(group, "OpenBVE Route Editor.lnk"), Path.Combine(group, "Desinstalar.lnk"), Path.Combine(desktop, Product + " - OpenBVE.lnk"), Path.Combine(desktop, Product + " - CCO Beta.lnk"), Path.Combine(desktop, "OpenBVE Route Editor.lnk") };
            foreach (string link in File.ReadAllLines(links))
                if (allowed.Any(a => string.Equals(Path.GetFullPath(link), a, StringComparison.OrdinalIgnoreCase))) File.Delete(link);
            File.Delete(links);
        }
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
            if (key != null && string.Equals((string)key.GetValue("InstallLocation"), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
        File.Delete(manifest);
        CleanEmptyDirectories(root);
        return preserved;
    }
}

#if !UNINSTALL
internal sealed class Wizard : Form
{
    readonly Panel body = new Panel();
    readonly Label title = new Label();
    readonly Button back = new Button(), next = new Button(), cancel = new Button();
    readonly TextBox destination = new TextBox();
    readonly CheckBox accept = new CheckBox(), desktop = new CheckBox(), launch = new CheckBox();
    readonly ProgressBar progress = new ProgressBar();
    readonly Label status = new Label();
    int page; bool busy;
    public Wizard()
    {
        Text = "Instalação — " + Setup.Product; ClientSize = new Size(700, 480);
        Font = new Font("Segoe UI", 10); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        var header = new Panel { BackColor = Color.FromArgb(16, 51, 86), Dock = DockStyle.Top, Height = 88 };
        title.SetBounds(24, 23, 650, 50); title.ForeColor = Color.White; title.Font = new Font(Font.FontFamily, 17, FontStyle.Bold); header.Controls.Add(title);
        body.SetBounds(24, 110, 650, 290); Controls.Add(body); Controls.Add(header);
        back.Text = "< Voltar"; next.Text = "Avançar >"; cancel.Text = "Cancelar";
        back.SetBounds(340, 430, 105, 32); next.SetBounds(452, 430, 105, 32); cancel.SetBounds(565, 430, 105, 32);
        Controls.AddRange(new Control[] { back, next, cancel });
        back.Click += delegate { page--; ShowPage(); };
        next.Click += async delegate { await Advance(); };
        cancel.Click += delegate { Close(); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
        destination.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Setup.Product);
        accept.Text = "Li e aceito as condições acima."; accept.CheckedChanged += delegate { next.Enabled = accept.Checked; };
        desktop.Text = "Criar atalhos na área de trabalho"; desktop.Checked = true;
        launch.Text = "Abrir o OpenBVE ao concluir"; launch.Checked = false;
        ShowPage();
    }
    void TextAt(string text, int y, int height) { body.Controls.Add(new Label { Text = text, Left = 0, Top = y, Width = 640, Height = height }); }
    void ShowPage()
    {
        body.Controls.Clear(); back.Enabled = page > 0 && page < 4; next.Enabled = true;
        next.Text = page == 3 ? "Instalar" : page == 5 ? "Concluir" : "Avançar >";
        if (page == 0)
        {
            title.Text = "Bem-vindo ao OpenBVE by LT ARTS";
            TextAt("Este assistente instalará o OpenBVE com sua janela original, o OpenBVE Route Editor e a Mobitrens com trens de IA inteligentes.", 5, 75);
            TextAt("• Instalação para seu usuário, sem pedir administrador.\n• Aplicativos e runtime .NET incluídos.\n• Interface original de rota, trem, configurações e controles.\n• Requer Windows 10/11 de 64 bits e pelo menos 2 GB livres.", 90, 130);
            TextAt("Clique em Avançar para continuar.", 240, 35);
        }
        if (page == 1)
        {
            title.Text = "Licenças e conteúdo incluído";
            var license = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Width = 640, Height = 230, Text = "OpenBVE by LT ARTS — versão beta\r\n\r\nO motor OpenBVE e suas dependências mantêm suas licenças originais, incluídas na pasta Licenças.\r\n\r\nA rota Mobitrens e os trens mantêm a autoria e os avisos dos arquivos originais. Sua inclusão não altera a licença desses conteúdos.\r\n\r\nEsta versão inclui recursos experimentais de IA e CCO. Os XMLs utilizam caminhos fixos; a escolha automática de plataforma em Cachorros ainda não está incluída.\r\n\r\nDois anúncios de destino ainda não estão incluídos. Consulte LEIA-ME.txt para controles e limitações." };
            body.Controls.Add(license); accept.SetBounds(0, 248, 640, 30); body.Controls.Add(accept); next.Enabled = accept.Checked;
        }
        if (page == 2)
        {
            title.Text = "Escolha a pasta de destino";
            TextAt("O aplicativo, a rota e os trens serão instalados nesta pasta:", 5, 45);
            destination.SetBounds(0, 65, 530, 30); body.Controls.Add(destination);
            var browse = new Button { Text = "Procurar...", Left = 540, Top = 63, Width = 100, Height = 32 };
            browse.Click += delegate { using (var picker = new FolderBrowserDialog()) { picker.Description = "Selecione uma pasta vazia para instalar o OpenBVE"; if (picker.ShowDialog() == DialogResult.OK) destination.Text = picker.SelectedPath; } }; body.Controls.Add(browse);
            TextAt("A pasta Documentos é a opção padrão. Escolha uma pasta vazia: o instalador não sobrescreve seus projetos existentes.", 120, 80);
        }
        if (page == 3)
        {
            title.Text = "Pronto para instalar";
            TextAt("Destino:\n" + destination.Text + "\n\nSerão incluídos: OpenBVE com sua janela original, Route Editor com viewport 3D, Mobitrens, trens, IA inteligente, CCO beta e desinstalador.", 5, 145);
            desktop.SetBounds(0, 165, 640, 30); body.Controls.Add(desktop);
            TextAt("Os atalhos OpenBVE, OpenBVE Route Editor e CCO Beta serão adicionados ao menu Iniciar.", 215, 60);
        }
        if (page == 4)
        {
            title.Text = "Instalando..."; back.Enabled = next.Enabled = cancel.Enabled = false;
            TextAt("Aguarde enquanto os arquivos são extraídos e conferidos.", 5, 50);
            progress.SetBounds(0, 85, 640, 28); body.Controls.Add(progress);
            status.SetBounds(0, 135, 640, 130); status.AutoEllipsis = true; body.Controls.Add(status);
        }
        if (page == 5)
        {
            title.Text = "Instalação concluída"; back.Enabled = false; cancel.Visible = false;
            TextAt("O OpenBVE e o Route Editor foram instalados.\n\nAbra OpenBVE para escolher a rota e o trem na janela original.\nUse OpenBVE Route Editor para criar ou editar suas rotas.\n\nA Mobitrens com IA está incluída na biblioteca de rotas.", 5, 165);
            launch.SetBounds(0, 195, 640, 35); body.Controls.Add(launch);
        }
    }
    async Task Advance()
    {
        if (page == 5)
        {
            if (launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(destination.Text, "Iniciar-OpenBVE.cmd")) { UseShellExecute = true, WorkingDirectory = destination.Text });
            Close(); return;
        }
        if (page == 2) { try { Setup.CheckTarget(destination.Text); } catch (Exception e) { MessageBox.Show(e.Message, Text); return; } }
        if (page != 3) { page++; ShowPage(); return; }
        string target = Path.GetFullPath(destination.Text); bool icons = desktop.Checked;
        page = 4; busy = true; ShowPage();
        try
        {
            var report = new Progress<Tuple<int, string>>(p => { progress.Value = p.Item1; status.Text = p.Item2; });
            await Task.Run(() => Setup.Install(target, icons, true, (value, file) => ((IProgress<Tuple<int, string>>)report).Report(Tuple.Create(value, file))));
            page = 5;
        }
        catch (Exception e) { MessageBox.Show("A instalação não foi concluída.\n\n" + e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); page = 2; }
        finally { busy = false; cancel.Enabled = true; ShowPage(); }
    }
}
#endif
