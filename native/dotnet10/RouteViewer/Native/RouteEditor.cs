using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
namespace RouteViewer;

internal sealed class RouteEditor : IDisposable
{
    private Process process;
    private System.Threading.Channels.Channel<string> outgoing;
    private System.Threading.Tasks.Task writer;
    private RouteDocument document;
    private readonly ConcurrentQueue<string> routes = new();
    private readonly ConcurrentQueue<string> messages = new();
    internal string Layout = "float";
    internal int CanvasMode=-1,CanvasWidth,CanvasHeight;
    internal bool CanvasMatches(ViewerWindow window,int mode) => CanvasMode==mode && window.Width==CanvasWidth && window.Height==CanvasHeight && Program.Renderer.Screen.Width==CanvasWidth && Program.Renderer.Screen.Height==CanvasHeight;
    internal bool Visible=true;
    internal void ToggleVisible() {Visible=!Visible;Send("VISIBLE",Visible?"1":"0");}
    internal bool CloseApproved;
    private bool replacingRoute;
    internal bool Running => process != null && !process.HasExited;
    internal void Open(ViewerWindow window, string path)
    {
        if (Program.SmokeSeconds>0 && !Program.EditorTest && !Program.FeedbackTest && !Program.KeyboardTest) return;
        try {
            // Reloading the preview must not replace the editor's undo history.
            if (!replacingRoute && document?.Path == (File.ResolveLinkTarget(path,true)?.FullName ?? Path.GetFullPath(path)) && Running) return;
            var next=RouteDocument.Open(path,OpenBveApi.TextEncoding.GetSystemEncodingFromFile(path));
            if (!Running) {
                var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,OperatingSystem.IsWindows()?"RouteViewer.Desktop.exe":"route-editor")) {
                    UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true
                };
                start.ArgumentList.Add(window.WindowInfo.Handle.ToInt64().ToString());
                process=new Process { StartInfo=start,EnableRaisingEvents=true };
                process.OutputDataReceived+=(_,e)=>{if(e.Data!=null)messages.Enqueue(e.Data);};
                if(Program.EditorTest||Program.FeedbackTest||Program.KeyboardTest)start.ArgumentList.Add("--test");
                process.Start();process.BeginOutputReadLine();
                outgoing=System.Threading.Channels.Channel.CreateUnbounded<string>(new() {SingleReader=true,SingleWriter=true});
                var channel=outgoing;
                var input=process.StandardInput;
                writer=System.Threading.Tasks.Task.Run(async ()=> {
                    try {
                        await foreach(var line in channel.Reader.ReadAllAsync()) {
                            await input.WriteLineAsync(line);
                            await input.FlushAsync();
                        }
                    } catch(Exception error) when(error is IOException || error is ObjectDisposedException) {
                        Console.Error.WriteLine("Editor IPC: "+error.Message);
                    }
                });
            }
            document=next;replacingRoute=false;
            Send("PATH",path);Send("TEXT",document.Text);Resize(window);
        } catch(Exception error) { Program.Status="Editor: "+error.Message;Console.Error.WriteLine(Program.Status); }
    }
    internal void Send(string command,string value="")
    {
        if(!Running)return;
        // A large route must never block the render/UI thread on a pipe write.
        // Windows parenting sends synchronous window messages to that thread.
        outgoing?.Writer.TryWrite(command+"\t"+Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
    }
    internal void Resize(ViewerWindow window)=>Send("RECT",$"{window.Width} {window.Height}");
    internal void RequestRoute(string path) => routes.Enqueue(path);
    internal void Pump(ViewerWindow window)
    {
        while(routes.TryDequeue(out var route)) {if(Running)Send("ROUTE",route);else Program.PendingRoute=route;}
        while(messages.TryDequeue(out var message)) {
            int tab=message.IndexOf('\t');if(tab<0)continue;
            string command=message[..tab],value;
            try {value=Encoding.UTF8.GetString(Convert.FromBase64String(message[(tab+1)..]));}
            catch(FormatException){continue;}
            switch(command) {
                case "CANVAS":
                    var dimensions=value.Split(' ');
                    if(dimensions.Length==3&&int.TryParse(dimensions[0],out var mode)&&int.TryParse(dimensions[1],out var width)&&int.TryParse(dimensions[2],out var height)) {CanvasMode=mode;CanvasWidth=width;CanvasHeight=height;}
                    break;
                case "HOST":if(Program.EditorTest||Program.FeedbackTest)Console.WriteLine("VIEWER_EDITOR_HOST: "+value);break;
                case "THEME":window.DarkTheme=value=="dark";break;
                case "TOGGLE-EDITOR":ToggleVisible();break;
                case "TOGGLE-UI":window.ToggleInterface();break;
                case "VISIBLE":Visible=value=="1";break;
                case "OPEN":Program.OpenPicker();break;
                case "RELOAD":Program.Reload=true;break;
                case "LAYOUT":Layout=value;window.UpdateCanvas();break;
                case "ROUTE":replacingRoute=true;Program.PendingRoute=value;break;
                case "QUIT":CloseApproved=true;window.Close();break;
                case "SAVE":
                    try {document.Save(value);Send("SAVED");Program.Reload=true;}
                    catch(Exception error){Send("ERROR","Não foi possível salvar: "+error.Message);}
                    break;
            }
        }
        if(process!=null && process.HasExited && Layout!="float") {Layout="float";window.UpdateCanvas();}
    }
    public void Dispose() {Send("EXIT");outgoing?.Writer.TryComplete();if(Running)process.WaitForExit(1500);process?.Dispose();process=null;}
}
