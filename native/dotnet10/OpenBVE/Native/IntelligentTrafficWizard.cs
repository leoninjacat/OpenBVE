#if WINDOWS
using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using OpenBve.Native;
using RouteManager2.Stations;

namespace OpenBve.Native
{
    internal sealed class IntelligentTrafficWizard : Form
    {
        private string route;
        private Encoding routeEncoding;
        private readonly Encoding trainEncoding;
        private RouteStation[] stations;
        private readonly TextBox routeFile = new TextBox { Dock = DockStyle.Top };
        private readonly TextBox trainFolder = new TextBox { Dock = DockStyle.Top };
        private readonly Label trainDetails = new Label { Dock = DockStyle.Top, Height = 85 };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false };
        private readonly NumericUpDown count = Number(1, 32, 4);
        private readonly NumericUpDown interval = Number(1, 7200, 300);
        private readonly NumericUpDown speed = Number(1, 300, 60);
        private readonly NumericUpDown start = Number(0, 172800, 0);
        private readonly CheckBox warmStart = new CheckBox { Text = "Iniciar com trens distribuídos pela linha", Checked = true, AutoSize = true };
        private IntelligentTrafficPlan.Settings savedSettings;
        private string settingsRoute;
        private readonly Label heading = new Label { Dock = DockStyle.Top, Height = 55 };
        private readonly Panel body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        private readonly Button next = new Button { Text = "Avançar >", AutoSize = true };
        private readonly Button back = new Button { Text = "< Voltar", AutoSize = true };
        private BindingList<IntelligentTrafficPlan.Stop> stops;
        private IntelligentTrafficPlan.TrainInfo info;
        private int page;
        internal string InstalledRoute { get; private set; }

        private static NumericUpDown Number(int min, int max, int value) => new NumericUpDown
            { Minimum = min, Maximum = max, Value = value, Width = 140 };

        internal IntelligentTrafficWizard(string route, Encoding routeEncoding, string train, Encoding trainEncoding, RouteStation[] stations)
        {
            this.route = route; this.routeEncoding = routeEncoding; this.trainEncoding = trainEncoding; this.stations = stations;
            Text = "Assistente de instalação de trens de IA — LT ARTS";
            Size = new Size(820, 580); MinimumSize = new Size(700, 500);
            StartPosition = FormStartPosition.CenterParent;
            trainFolder.Text = train;
            routeFile.Text = route;
            grid.AutoGenerateColumns = false;
            foreach (var column in new[] { ("Name", "Estação"), ("Position", "Posição (m)"), ("Dwell", "Parada (s)"), ("Doors", "Portas: L / R / B / 0") })
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = column.Item1, DataPropertyName = column.Item1, HeaderText = column.Item2 });
            grid.DataError += (_, e) =>
            {
                e.ThrowException = false;
                if (e.RowIndex >= 0 && e.RowIndex < grid.Rows.Count) grid.Rows[e.RowIndex].ErrorText = "Confira o formato do valor informado.";
            };
            var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(5) };
            var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, AutoSize = true };
            footer.Controls.Add(cancel); footer.Controls.Add(next); footer.Controls.Add(back);
            Controls.Add(body); Controls.Add(heading); Controls.Add(footer);
            CancelButton = cancel;
            back.Click += (_, _) => RenderPage(page - 1);
            next.Click += (_, _) => Advance();
            start.Value = (decimal)Math.Clamp(stations?.FirstOrDefault()?.DepartureTime ?? 0, 0, 172800);
            Render();
        }

        private void Render()
        {
            body.Controls.Clear(); back.Enabled = page > 0;
            next.Text = page == 2 ? "Instalar" : "Avançar >";
            heading.Text = $"  Etapa {page + 1} de 3 — " + new[] { "Rota e trem", "Conferir paradas", "Frota e intervalos" }[page] +
                "\r\n  " + System.IO.Path.GetFileName(route);
            if (page == 0)
            {
                var browseRoute = new Button { Text = "Escolher CSV da rota…", Dock = DockStyle.Top, Height = 32 };
                browseRoute.Click += (_, _) =>
                {
                    using var picker = new OpenFileDialog { Filter = "Rota CSV|*.csv", FileName = routeFile.Text };
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        routeFile.Text = picker.FileName;
                        try { LoadSavedSettings(); }
                        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Configuração de IA"); }
                    }
                };
                var browse = new Button { Text = "Escolher pasta do trem…", Dock = DockStyle.Top, Height = 32 };
                browse.Click += (_, _) => { using var picker = new FolderBrowserDialog { SelectedPath = trainFolder.Text }; if (picker.ShowDialog(this) == DialogResult.OK) trainFolder.Text = picker.SelectedPath; };
                var explanation = new Label { Dock = DockStyle.Fill, Text =
                    "Escolha a rota e o trem. Ao avançar, o OpenBVE lerá o CSV, seus arquivos incluídos e as estações.\r\n\r\n" +
                    "O serviço seguirá o sentido deste CSV pelo trilho do jogador (0). Não será criado um trilho 1 de retorno. " +
                    "Para uma rota com sentidos em CSVs separados, execute o assistente em cada CSV.\r\n\r\n" +
                    "As posições e portas detectadas podem ser corrigidas na próxima etapa. Cruzamentos, AMVs e retorno automático não são inferidos.\r\n\r\n" +
                    "Será criada uma cópia da rota na mesma pasta. A original e os trens permanecem preservados. Tráfego já presente no CSV será mantido." };
                body.Controls.Add(explanation); body.Controls.Add(trainDetails); body.Controls.Add(browse); body.Controls.Add(trainFolder);
                body.Controls.Add(browseRoute); body.Controls.Add(routeFile);
            }
            else if (page == 1)
            {
                body.Controls.Add(grid);
                grid.DataSource = stops;
            }
            else
            {
                var table = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
                void Row(string label, System.Windows.Forms.Control control) { int row = table.RowCount++; table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 14, 8) }, 0, row); table.Controls.Add(control, 1, row); }
                Row("Composições (1–32)", count); Row("Intervalo entre partidas (segundos)", interval);
                Row("Velocidade do plano IA (km/h)", speed); Row("Primeira partida (segundos desde 00:00)", start);
                Row("Tráfego no início da sessão", warmStart);
                var summary = new Label { Dock = DockStyle.Fill, Text =
                    $"{stops.Count} paradas • {info.Cars} carros por composição • trilho 0 • sentido do CSV\r\n\r\n" +
                    "Proteção de tráfego e espera em estações ativadas. A ocupação da via pode atrasar as partidas. " +
                    "Os trens encerram o serviço após a última parada.\r\n\r\n" +
                    "A velocidade informada é o teto do plano gerado: confira os limites da linha antes de instalar. " +
                    "Com distribuição inicial ativada, a sessão começa com tráfego adiante do jogador. Desativada, as composições partem da origem e aguardam se ela estiver ocupada.\r\n\r\nDestino: " + IntelligentTrafficPlan.OutputPath(route) +
                    "\r\n\r\nAo editar uma rota com IA, a configuração anterior é substituída, sem duplicar a frota." };
                body.Controls.Add(summary); body.Controls.Add(table);
            }
        }

        private void Advance()
        {
            try
            {
                if (page == 0)
                {
                    LoadSavedSettings();
                    route = System.IO.Path.GetFullPath(routeFile.Text);
                    if (!File.Exists(route) || !string.Equals(System.IO.Path.GetExtension(route), ".csv", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Escolha um arquivo CSV de rota existente.");
                    routeEncoding = TrafficWizardBackend.DetectEncoding(route);
                    UseWaitCursor = true; next.Enabled = back.Enabled = false;
                    try { stations = formMain.ReadTrafficRoute(route, routeEncoding); }
                    finally { UseWaitCursor = false; next.Enabled = true; }
                    string trainData = System.IO.Path.Combine(trainFolder.Text, "train.ai");
                    if (!File.Exists(trainData)) trainData = System.IO.Path.Combine(trainFolder.Text, "train.dat");
                    info = IntelligentTrafficPlan.ReadTrain(trainFolder.Text, TrafficWizardBackend.DetectEncoding(trainData));
                    stops = new BindingList<IntelligentTrafficPlan.Stop>(stations.Select(s =>
                    {
                        var point = s.Stops.FirstOrDefault(p => p.Cars == info.Cars);
                        if (!s.Stops.Any(p => p.Cars == info.Cars)) point = s.Stops.FirstOrDefault(p => p.Cars == 0);
                        if (!s.Stops.Any(p => p.Cars == info.Cars || p.Cars == 0))
                            point = s.Stops.OrderBy(p => Math.Abs(p.Cars - info.Cars)).First();
                        return new IntelligentTrafficPlan.Stop { Name = s.Name, Position = point.TrackPosition, Dwell = s.StopTime,
                            Doors = s.OpenLeftDoors ? (s.OpenRightDoors ? "B" : "L") : (s.OpenRightDoors ? "R" : "0") };
                    }).ToList());
                    if (savedSettings != null)
                    {
                        foreach (var stop in savedSettings.Stops)
                            if (stop.Name.StartsWith("Parada ")) stop.Name = stations.FirstOrDefault(s => s.Stops.Any(p => Math.Abs(p.TrackPosition - stop.Position) < 0.01))?.Name ?? stop.Name;
                        stops = new BindingList<IntelligentTrafficPlan.Stop>(savedSettings.Stops);
                    }
                    trainDetails.Text = $"Trem identificado: {info.Cars} carros. Aceleração: {info.Acceleration} km/h/s. Frenagem: {info.Deceleration} km/h/s.";
                }
                else
                {
                    if (!grid.EndEdit()) throw new InvalidDataException("Corrija o valor da célula antes de avançar.");
                    BindingContext[stops].EndCurrentEdit();
                    IntelligentTrafficPlan.Create(trainFolder.Text, info, stops, (int)count.Value, (double)interval.Value, (double)speed.Value, (double)start.Value);
                }
                if (page < 2)
                {
                    int target = page + 1;
                    RenderPage(target);
                    return;
                }
                UseWaitCursor = true; next.Enabled = back.Enabled = false;
                heading.Text = "Validando a rota completa e carregando os modelos da frota… Aguarde."; heading.Refresh();
                try
                {
                    InstalledRoute = IntelligentTrafficPlan.Install(route, routeEncoding, trainFolder.Text, info, stops,
                        (int)count.Value, (double)interval.Value, (double)speed.Value, (double)start.Value,
                        (r, x) => TrafficWizardBackend.Validate(r, x, routeEncoding, (int)count.Value), warmStart.Checked);
                }
                finally { UseWaitCursor = false; next.Enabled = back.Enabled = true; }
                MessageBox.Show(this, "Frota carregada e validada pelo OpenBVE.\r\n\r\nSelecione esta cópia da rota no simulador:\r\n" + InstalledRoute +
                    "\r\n\r\nReabra esse CSV neste aplicativo para editar a frota. " +
                    (warmStart.Checked ? "A distribuição inicial de tráfego está ativada." : "As partidas da origem aguardam se a via estiver ocupada."), "Trens de IA");
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex)
            {
                Program.FileSystem.AppendToLogFile("Traffic installer: " + ex);
                Render();
                MessageBox.Show(this, ex.Message + "\r\n\r\nA operação não foi concluída. Corrija os dados e tente novamente.", "Confira a configuração", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RenderPage(int target)
        {
            int previous = page;
            try { page = target; Render(); }
            catch { page = previous; Render(); throw; }
        }

        private void LoadSavedSettings()
        {
            if (!File.Exists(routeFile.Text)) return;
            string selected = System.IO.Path.GetFullPath(routeFile.Text);
            if (selected == settingsRoute) return;
            var settings = IntelligentTrafficPlan.ReadExisting(selected, TrafficWizardBackend.DetectEncoding(selected));
            settingsRoute = selected; savedSettings = settings;
            if (settings == null) return;
            trainFolder.Text = settings.TrainFolder;
            count.Value = (decimal)Math.Clamp(settings.Count, 1, 32);
            interval.Value = (decimal)Math.Clamp(settings.Interval, 1, 7200);
            speed.Value = (decimal)Math.Clamp(settings.Speed, 1, 300);
            start.Value = (decimal)Math.Clamp(settings.Appearance, 0, 172800);
            warmStart.Checked = settings.WarmStart;
            trainDetails.Text = "Configuração de IA encontrada. Você pode editar o trem, as paradas, a quantidade e os intervalos.";
        }

        internal void TestPages(string output)
        {
            Exception failure = null;
            Shown += (_, _) =>
            {
                try
                {
                    for (int expected = 0; expected < 3; expected++)
                    {
                        if (page != expected) throw new Exception("Wizard did not advance.");
                        using var bitmap = new System.Drawing.Bitmap(Width, Height);
                        DrawToBitmap(bitmap, new Rectangle(0, 0, Width, Height));
                        bitmap.Save(System.IO.Path.Combine(output, "traffic-wizard-" + page + ".png"));
                        if (expected < 2) next.PerformClick();
                    }
                    if (stops.Count < 2 || info.Cars < 1) throw new Exception("Missing real stations or train data.");
                }
                catch (Exception ex) { failure = ex; }
                finally { Close(); }
            };
            ShowDialog();
            if (failure != null) throw failure;
        }
    }
}
#endif
