using System.Runtime.InteropServices;
using System.Text;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        try { return Run(args); }
        catch(Exception error) {
            Console.Error.WriteLine(error);
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"desktop-error.log"),error.ToString());
            return 1;
        }
    }
    static int Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Console.InputEncoding=Console.OutputEncoding=new UTF8Encoding(false);
        string wizardTest=args.FirstOrDefault(a=>a.StartsWith("--wizard-test="));
        if(wizardTest!=null) {
            using var wizard=new NewRouteWizard();
            Console.WriteLine("WIZARD_TEST_OK: "+wizard.RunSelfTest(wizardTest["--wizard-test=".Length..]));return 0;
        }
        if(args.Contains("--new-route")) {
            using var wizard=new NewRouteWizard();
            if(wizard.ShowDialog()!=DialogResult.OK)return 1;
            Console.Write(wizard.CreatedRoute);return 0;
        }
        if(args.Contains("--pick")) {
            using var picker=new OpenFileDialog {Filter="Rotas OpenBVE (*.csv;*.rw)|*.csv;*.rw",CheckFileExists=true};
            if(picker.ShowDialog()!=DialogResult.OK)return 1;
            Console.Write(picker.FileName);return 0;
        }
        if(args.Contains("--self-test")) {
            using var test=new EditorWindow(IntPtr.Zero);
            Console.Error.WriteLine("DESKTOP_CONSTRUCTOR_OK");return 0;
        }
        if(args.Length==0||!long.TryParse(args[0],out var handle))return 2;
        Application.Run(new EditorWindow(new IntPtr(handle),args.Contains("--test")));
        return 0;
    }
}

// The engine retains ownership of encoding, conflict detection and disk writes.
// This desktop host uses the same UTF-8/base64 protocol as the GTK editor.
internal sealed class EditorWindow : Form
{
    readonly IntPtr viewer;
    readonly SplitContainer split=new() {Dock=DockStyle.Fill};
    readonly RichTextBox text=new() {Dock=DockStyle.Fill,Font=new Font("Consolas",11),WordWrap=false,AcceptsTab=true,DetectUrls=false,HideSelection=false};
    readonly ToolStrip tools=new() {Dock=DockStyle.Top};
    readonly ToolStripLabel status=new();
    readonly ComboBox layout=new() {DropDownStyle=ComboBoxStyle.DropDownList,Width=180};
    readonly Panel editor=new() {Dock=DockStyle.Fill};
    readonly Form floating=new() {Text="Editor de rotas",Width=650,Height=800};
    readonly System.Windows.Forms.Timer timer=new() {Interval=100};
    bool changing,dirty,waiting,exiting,visible=true;
    Action pending;
    int mode;
    string route="";
    readonly int originalStyle;
    readonly bool testing;

    internal EditorWindow(IntPtr viewer,bool testing=false)
    {
        this.testing=testing;
        this.viewer=viewer;originalStyle=GetWindowLong(viewer,-16);
        Text="OpenBVE Route Editor — LT ARTS";Width=1400;Height=850;
        split.Size=new Size(1400,850);split.SplitterDistance=900;split.Panel2MinSize=200;
        Controls.Add(split);editor.Controls.Add(text);editor.Controls.Add(tools);
        split.Panel2.Controls.Add(editor);
        void Button(string label,Action action){var b=tools.Items.Add(label);b.Click+=(_,_)=>action();}
        Button("Nova rota",CreateRoute);Button("Abrir",()=>Emit("OPEN"));Button("Salvar",Save);Button("Recarregar",()=>Emit("RELOAD"));
        layout.Items.AddRange(new object[]{"Lateral","Abaixo","Janela independente"});
        tools.Items.Add(new ToolStripControlHost(layout));tools.Items.Add(status);
        layout.SelectedIndexChanged+=(_,_)=>Arrange(layout.SelectedIndex);
        text.TextChanged+=(_,_)=>{if(!changing){dirty=true;UpdateTitle();}};
        floating.FormClosing+=(_,e)=>{if(!exiting){e.Cancel=true;visible=false;floating.Hide();Emit("VISIBLE","0");}};
        FormClosing+=(_,e)=>{if(!exiting){e.Cancel=true;Confirm(()=>Emit("QUIT"));}};
        KeyPreview=true;KeyDown+=(_,e)=>{
            if(e.Control&&e.KeyCode==Keys.S){Save();e.SuppressKeyPress=true;}
            if(e.Control&&e.KeyCode==Keys.O){Emit("OPEN");e.SuppressKeyPress=true;}
            if(e.Control&&e.KeyCode==Keys.N){CreateRoute();e.SuppressKeyPress=true;}
        };
        Shown+=(_,_)=>{
            _=Task.Run(ReadCommands);
            layout.SelectedIndex=0;
            timer.Tick+=(_,_)=>PlaceViewer();timer.Start();
        };
        FormClosed+=(_,_)=>{timer.Dispose();floating.Dispose();};
    }
    void Emit(string command,string value="")
    {
        Console.WriteLine(command+"\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));Console.Out.Flush();
    }
    async Task ReadCommands()
    {
        string line;
        while((line=await Console.In.ReadLineAsync())!=null) {
            int tab=line.IndexOf('\t');if(tab<0)continue;
            string value;
            try{value=Encoding.UTF8.GetString(Convert.FromBase64String(line[(tab+1)..]));}catch(FormatException){continue;}
            string command=line[..tab];
            if(IsDisposed)return;
            try{BeginInvoke(new Action(()=>Receive(command,value)));}catch(InvalidOperationException){return;}
        }
        if(!IsDisposed)try{BeginInvoke(new Action(Shutdown));}catch(InvalidOperationException){}
    }
    void Receive(string command,string value)
    {
        switch(command) {
            case "PATH":route=value;UpdateTitle();break;
            case "TEXT":changing=true;text.Text=value;changing=false;dirty=false;UpdateTitle();break;
            case "SAVED":waiting=false;text.ReadOnly=false;dirty=false;UpdateTitle();var next=pending;pending=null;next?.Invoke();break;
            case "ERROR":waiting=false;text.ReadOnly=false;pending=null;MessageBox.Show(this,value,"Erro ao salvar");break;
            case "QUIT":Confirm(()=>Emit("QUIT"));break;
            case "ROUTE":Confirm(()=>Emit("ROUTE",value));break;
            case "VISIBLE":visible=value=="1";Arrange(mode);break;
            case "EXIT":Shutdown();break;
            case "NEW":CreateRoute();break;
            case "TEST-LAYOUT" when testing:layout.SelectedIndex=int.Parse(value);break;
            case "TEST-EDIT" when testing:text.AppendText(value);Save();break;
            case "TEST-FOCUS" when testing:
                SetForegroundWindow(Handle);
                if(value=="viewport")SetFocus(viewer);else text.Focus();
                break;
        }
    }
    void UpdateTitle(){Text=(dirty?"* ":"")+Path.GetFileName(route)+" — Route Viewer by LT ARTS";status.Text=dirty?"Alterações não salvas":"Salvo";}
    void Save(){if(waiting)return;waiting=true;text.ReadOnly=true;Emit("SAVE",text.Text);}
    void CreateRoute()=>Confirm(()=>{
        using var wizard=new NewRouteWizard();
        if(wizard.ShowDialog(this)==DialogResult.OK)Emit("ROUTE",wizard.CreatedRoute);
    });
    void Confirm(Action action)
    {
        if(waiting)return;
        if(!dirty){action();return;}
        var result=MessageBox.Show(this,"Salvar as alterações da rota antes de continuar?","Editor de rotas",MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question);
        if(result==DialogResult.No)action();
        else if(result==DialogResult.Yes){pending=action;Save();}
    }
    void Arrange(int selected)
    {
        mode=selected;
        editor.Parent=mode==2?floating:split.Panel2;
        split.Panel2Collapsed=mode==2||!visible;
        if(mode!=2) {
            split.Orientation=mode==0?Orientation.Vertical:Orientation.Horizontal;
            split.SplitterDistance=Math.Max(1,(mode==0?split.Width:split.Height)*2/3);
        }
        if(mode==2&&visible)floating.Show(this);else floating.Hide();
        Emit("LAYOUT",mode==0?"side":mode==1?"bottom":"float");PlaceViewer();
    }
    void PlaceViewer()
    {
        if(exiting)return;
        if(!IsWindow(viewer)){Shutdown();return;}
        var panel=split.Panel1;
        if(GetParent(viewer)!=panel.Handle) {
            SetWindowLong(viewer,-16,(originalStyle&~unchecked((int)0x80CF0000))|0x40000000);
            SetParent(viewer,panel.Handle);
        }
        MoveWindow(viewer,0,0,Math.Max(1,panel.Width),Math.Max(1,panel.Height),true);
        Emit("CANVAS",$"{mode} {panel.Width} {panel.Height}");
    }
    void Shutdown()
    {
        if(exiting)return;exiting=true;timer.Stop();
        // Detach before disposing the host: destroying a parent destroys its HWND children.
        SetParent(viewer,IntPtr.Zero);SetWindowLong(viewer,-16,originalStyle);
        Close();
    }
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr window,int index,int value);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr window,int x,int y,int width,int height,bool repaint);
    [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
}
