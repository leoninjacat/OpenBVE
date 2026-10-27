using RouteViewer;

internal sealed class NewRouteWizard:Form
{
    readonly TextBox name=new(){Text="Minha rota",Dock=DockStyle.Fill};
    readonly TextBox parent=new(){Dock=DockStyle.Fill,ReadOnly=true};
    readonly ObjectLibrarySelector libraries=new();
    readonly NumericUpDown length=new(){Minimum=25,Maximum=100000,Increment=25,Value=1000,Dock=DockStyle.Fill};
    readonly NumericUpDown gauge=new(){Minimum=600,Maximum=2000,Value=1600,Dock=DockStyle.Fill};
    readonly Panel page=new(){Dock=DockStyle.Fill,Padding=new Padding(18)};
    readonly Label heading=new(){Dock=DockStyle.Top,Height=48,Font=new Font(SystemFonts.MessageBoxFont.FontFamily,15),Padding=new Padding(16,10,0,0)};
    readonly Button back=new(){Text="Voltar",AutoSize=true};
    readonly Button next=new(){Text="Próximo",AutoSize=true};
    int step;
    internal string CreatedRoute {get;private set;}
    string ProjectFolder=>Path.Combine(parent.Text,RouteProject.FolderName(name.Text));
    string ObjectsFolder=>Path.Combine(ProjectFolder,"Railway","Object");
    internal NewRouteWizard()
    {
        Text="Nova rota — OpenBVE by LT ARTS";ClientSize=new Size(900,650);MinimumSize=new Size(840,600);StartPosition=FormStartPosition.CenterParent;
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(10)};
        var cancel=new Button {Text="Cancelar",DialogResult=DialogResult.Cancel,AutoSize=true};
        buttons.Controls.AddRange(new Control[]{cancel,next,back});
        Controls.Add(page);Controls.Add(heading);Controls.Add(buttons);CancelButton=cancel;AcceptButton=next;
        next.Click+=(_,_)=>Advance();back.Click+=(_,_)=>{step--;ShowStep();};ShowStep();
    }
    void Browse(TextBox target,string title)
    {
        using var picker=new FolderBrowserDialog {Description=title,UseDescriptionForTitle=true,ShowNewFolderButton=true};
        if(Directory.Exists(target.Text))picker.SelectedPath=target.Text;
        if(picker.ShowDialog(this)==DialogResult.OK)target.Text=picker.SelectedPath;
    }
    void ShowStep()
    {
        // Fields are reused when moving back: remove them before disposing a page.
        foreach(var control in new Control[]{name,parent,libraries,length,gauge})control.Parent=null;
        while(page.Controls.Count>0)page.Controls[0].Dispose();
        back.Enabled=step>0;next.Text=step==2?"Criar e abrir":"Próximo";
        heading.Text=step switch{0=>"1. Nome e destino",1=>"2. Biblioteca de objetos",_=>"3. Conferir e criar"};
        if(step==1){page.Controls.Add(libraries);return;}
        var grid=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,AutoScroll=true};
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));page.Controls.Add(grid);
        void Row(Control control,bool full=true){int row=grid.RowCount++;grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));grid.Controls.Add(control,0,row);if(full)grid.SetColumnSpan(control,2);control.Margin=new Padding(0,5,8,7);}
        void Label(string text)=>Row(new Label {Text=text,AutoSize=true,MaximumSize=new Size(610,0)});
        void Folder(TextBox field,string caption){Label(caption);Row(field,false);var choose=new Button {Text="Escolher pasta…",AutoSize=true};grid.Controls.Add(choose,1,grid.RowCount-1);choose.Click+=(_,_)=>Browse(field,caption);}
        if(step==0) {
            Label("Nome da rota");Row(name);Folder(parent,"Salvar o projeto dentro de:");
            Label("Comprimento inicial (metros, múltiplos de 25)");Row(length);
            Label("Bitola (milímetros)");Row(gauge);
        } else {
            Label("Projeto: "+ProjectFolder);Label("CSV: Railway/Route/"+RouteProject.FolderName(name.Text)+".csv");
            Label("Objetos: "+ObjectsFolder);Label($"Trecho reto: {length.Value} m · Bitola: {gauge.Value} mm");
            foreach(var selection in libraries.Selections())Label(selection.Command+": "+selection.Folder+(selection.InitialFile==null?" (somente catálogo)":" — inicial: "+Path.GetFileName(selection.InitialFile)));
            Label("As bibliotecas escolhidas serão referenciadas por caminhos relativos, sem copiar ou alterar modelos e texturas. Ao compartilhar, preserve essas pastas. O catálogo JSON lista os índices por categoria.");
            Label("Também serão criadas as pastas Railway/Object e Railway/Sound. A rota abrirá no editor e no viewport 3D.");
        }
    }
    void Advance()
    {
        try {
            if(step==0){RouteProject.FolderName(name.Text);if(!Directory.Exists(parent.Text))throw new IOException("Escolha a pasta onde salvar o projeto.");if(length.Value%25!=0)throw new IOException("Use um comprimento múltiplo de 25 m.");}
            if(step==1)RouteObjectCatalog.Build(Path.Combine(ProjectFolder,"Railway","Object"),(int)length.Value,libraries.Selections());
            if(step<2){step++;ShowStep();return;}
            CreatedRoute=RouteProject.Create(name.Text,ProjectFolder,ObjectsFolder,(int)length.Value,(int)gauge.Value,libraries.Selections());
            DialogResult=DialogResult.OK;Close();
        }catch(Exception error) when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException) {MessageBox.Show(this,error.Message,"Não foi possível criar a rota",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    internal string RunSelfTest(string root)
    {
        Directory.CreateDirectory(root);parent.Text=root;name.Text="Teste do assistente";
        string folder=Path.Combine(root,"Biblioteca de teste");Directory.CreateDirectory(folder);
        string asset=Path.Combine(folder,"rail.csv");File.WriteAllText(asset,"CreateMeshBuilder\nAddVertex,0,0,0\nAddVertex,1,0,0\nAddVertex,0,0,25\nAddFace2,0,1,2\nSetColor,150,150,150\n");
        libraries.SelectForTest("Rail",folder,asset);
        Show();Application.DoEvents();
        Advance();if(step!=1)throw new Exception("Wizard did not advance to objects");
        PerformLayout();Application.DoEvents();
        using(var preview=new Bitmap(Width,Height)){DrawToBitmap(preview,new Rectangle(Point.Empty,Size));preview.Save(Path.Combine(root,"wizard-categories.png"),System.Drawing.Imaging.ImageFormat.Png);}
        step=0;ShowStep();if(name.Text!="Teste do assistente")throw new Exception("Back navigation lost form values");
        Advance();Advance();if(step!=2)throw new Exception("Wizard did not show summary");
        if(libraries.Selections().Single().InitialFile!=asset)throw new Exception("Wizard lost category selection on back navigation");
        Advance();if(CreatedRoute==null||!File.Exists(CreatedRoute))throw new Exception("Wizard did not create route");
        return CreatedRoute;
    }
}
