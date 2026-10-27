using RouteViewer;

internal sealed class ObjectLibrarySelector:UserControl
{
    sealed record Choice(string Label,string Path){public override string ToString()=>Label;}
    sealed class Category
    {
        internal RouteObjectKind Kind;
        internal string Folder;
        internal TextBox Path;
        internal ComboBox Initial;
    }
    readonly List<Category> categories=new();
    internal ObjectLibrarySelector()
    {
        Dock=DockStyle.Fill;
        var tabs=new TabControl {Dock=DockStyle.Fill};Controls.Add(tabs);
        var manual=new LinkLabel {Text="Documentação oficial: objetos e comandos de rota",Dock=DockStyle.Bottom,Height=26};Controls.Add(manual);
        manual.LinkClicked+=(_,_)=>{
            try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://openbve-project.net/documentation/HTML/route_csv.html"){UseShellExecute=true});}
            catch(System.ComponentModel.Win32Exception error){MessageBox.Show(this,error.Message,"Não foi possível abrir a documentação");}
        };
        foreach(string group in new[]{"Via","Plataformas","Cenário"}) {
            var tab=new TabPage(group){AutoScroll=true};tabs.TabPages.Add(tab);
            var rows=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=1,Padding=new Padding(8)};
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tab.Controls.Add(rows);
            string description=group switch {
                "Via"=>"Escolha pastas específicas de cada categoria e o objeto inicial. Sem seleção, trilho zero e terreno básicos serão gerados. Muros, taludes e postes escolhidos iniciam no metro zero. Trilhos, terreno e laterais se repetem em blocos de 25 m.",
                "Plataformas"=>"Plataformas laterais selecionadas ocupam os primeiros 100 m (ou o trecho disponível). Coberturas precisam de plataforma no mesmo lado. Peças centrais são catalogadas para trechos entre duas vias. Isso não cria horários nem uma estação.",
                _=>"O objeto livre selecionado será colocado uma vez, 5 m à direita da via. Balizas e fendas ficam cadastradas para configurar depois no CSV. Nesta build: CSV, B3D e ANIMATED; peças centrais e fendas aceitam só estáticos."
            };
            rows.Controls.Add(new Label{Text=description,AutoSize=true,MaximumSize=new Size(780,0),Margin=new Padding(0,0,0,14)});
            foreach(var kind in RouteObjectCatalog.Kinds.Where(k=>k.Group==group)) {
                var category=new Category {Kind=kind,Path=new TextBox{ReadOnly=true,Dock=DockStyle.Fill},Initial=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Fill,DropDownWidth=700}};
                categories.Add(category);
                var box=new TableLayoutPanel {Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,Margin=new Padding(0,0,0,14)};
                box.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));box.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var label=new Label {Text=kind.Label+" — Structure."+kind.Command,AutoSize=true};box.Controls.Add(label,0,0);box.SetColumnSpan(label,3);
                var browse=new Button{Text="Escolher pasta…",AutoSize=true};var clear=new Button{Text="Limpar",AutoSize=true};
                box.Controls.Add(category.Path,0,1);box.Controls.Add(browse,1,1);box.Controls.Add(clear,2,1);
                box.Controls.Add(category.Initial,0,2);box.SetColumnSpan(category.Initial,3);
                Reset(category);
                browse.Click+=(_,_)=>Browse(category);clear.Click+=(_,_)=>Reset(category);
                rows.Controls.Add(box);
            }
        }
    }
    void Reset(Category category)
    {
        category.Folder=null;category.Path.Text="Nenhuma pasta selecionada";
        category.Initial.Items.Clear();category.Initial.Items.Add(new Choice(category.Kind.Command is "Rail" or "Ground"?"Usar o objeto básico gerado":category.Kind.Applied?"Somente cadastrar — não aplicar na via":"Somente cadastrar para uso no CSV",null));
        category.Initial.SelectedIndex=0;category.Initial.Enabled=false;
    }
    void Browse(Category category)
    {
        using var picker=new FolderBrowserDialog {Description="Pasta de "+category.Kind.Label,UseDescriptionForTitle=true,ShowNewFolderButton=false};
        if(category.Folder!=null)picker.SelectedPath=category.Folder;
        if(picker.ShowDialog(this)!=DialogResult.OK)return;
        try {SelectFolder(category,picker.SelectedPath);}
        catch(Exception error) when(error is IOException||error is UnauthorizedAccessException||error is ArgumentException) {MessageBox.Show(this,error.Message,"Biblioteca de objetos",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
    }
    void SelectFolder(Category category,string folder)
    {
        string[] files=RouteObjectCatalog.Scan(category.Kind.Command,folder);
        if(files.Length==0)throw new IOException("Esta pasta não contém objetos compatíveis com "+category.Kind.Label+".");
        Reset(category);category.Folder=folder;category.Path.Text=folder+" — "+files.Length+" objetos (incluindo subpastas)";
        foreach(string file in files)category.Initial.Items.Add(new Choice((category.Kind.Applied?"Aplicar inicialmente: ":"Priorizar no catálogo: ")+Path.GetRelativePath(folder,file),file));
        category.Initial.Enabled=true;
    }
    internal List<RouteObjectSelection> Selections()=>categories.Where(c=>c.Folder!=null).Select(c=>new RouteObjectSelection(c.Kind.Command,c.Folder,((Choice)c.Initial.SelectedItem).Path)).ToList();
    internal void SelectForTest(string command,string folder,string initial)
    {
        var category=categories.Single(c=>c.Kind.Command==command);SelectFolder(category,folder);
        if(initial!=null)category.Initial.SelectedItem=category.Initial.Items.Cast<Choice>().Single(c=>c.Path==initial);
    }
}
